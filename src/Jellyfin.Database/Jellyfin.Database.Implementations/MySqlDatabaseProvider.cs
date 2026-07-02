using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.DbConfiguration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Database.Implementations;

/// <summary>
/// Configures MulletaFlix to use a MySQL/MariaDB database.
/// </summary>
[MulletaFlixDatabaseProviderKey("MulletaFlix-MySQL")]
public sealed class MySqlDatabaseProvider : IMulletaFlixDatabaseProvider
{
    private readonly ILogger<MySqlDatabaseProvider> _logger;
    private string? _toolsDir;
    private string _backupDir = string.Empty;
    private string _server = "localhost";
    private int _port = 3306;
    private string _user = "root";
    private string _password = string.Empty;

    private const string BackupFolderName = "MySQLBackups";

    private static readonly string DefaultConnectionString =
        "Server=localhost;Port=3306;User ID=root;Password=;CharSet=utf8mb4;";

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlDatabaseProvider"/> class.
    /// </summary>
    /// <param name="logger">A logger.</param>
    public MySqlDatabaseProvider(ILogger<MySqlDatabaseProvider> logger)
    {
        _logger = logger;
    }

    private string ToolsDir => _toolsDir ?? string.Empty;
    private string MysqldumpPath => string.IsNullOrEmpty(ToolsDir) ? "mysqldump" : Path.Combine(ToolsDir, "mysqldump.exe");
    private string MysqlPath => string.IsNullOrEmpty(ToolsDir) ? "mysql" : Path.Combine(ToolsDir, "mysql.exe");

    /// <inheritdoc/>
    public void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions databaseConfiguration)
    {
        var opts = databaseConfiguration.CustomProviderOptions?.Options;
        _server = GetOption(opts, "server", e => e, () => "localhost");
        _port = int.TryParse(GetOption(opts, "port", e => e, () => "3306"), out var p) ? p : 3306;
        _user = GetOption(opts, "user", e => e, () => "root");
        _password = GetOption(opts, "password", e => e, () => "");
        _toolsDir = GetOption<string?>(opts, "mysql-tools-dir", e => e, () => null);
        if (string.IsNullOrEmpty(_toolsDir))
        {
            // Auto-detect mariadb/bin relative to this assembly's location
            var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (assemblyDir is not null)
            {
                var candidate = Path.Combine(assemblyDir, "mariadb", "bin");
                if (Directory.Exists(candidate))
                {
                    _toolsDir = candidate;
                    _logger.LogInformation("Auto-detected MariaDB tools at {Path}", candidate);
                }
            }
        }
        _backupDir = GetOption(opts, "backup-dir", e => e, () => string.Empty);

        var connString = opts is not null
            ? $"Server={_server};Port={_port};User ID={_user};Password={_password};CharSet=utf8mb4;Pooling=True;Minimum Pool Size=10;Maximum Pool Size=200;Connection Idle Timeout=300;Connection Lifetime=1800;Default Command Timeout=120;"
            : DefaultConnectionString;

        connString = ApplySchema(connString, DatabaseNames.Main);
        _logger.LogInformation("MySQL database: {Database}", DatabaseNames.Main);

        var versionStr = GetOption(opts, "server-version", e => e, () => "11.4.2");
        var serverVersion = new MariaDbServerVersion(new Version(versionStr));

        options.UseMySql(connString, serverVersion, mySqlOptions =>
        {
            mySqlOptions.MigrationsAssembly(GetType().Assembly.GetName().Name);
            mySqlOptions.SchemaBehavior(Pomelo.EntityFrameworkCore.MySql.Infrastructure.MySqlSchemaBehavior.Translate, (schema, table) => schema ?? DatabaseNames.Main);
        });
    }

    private static string ApplySchema(string connString, string schema)
    {
        var parts = connString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = $"Database={schema}";
                return string.Join(";", parts) + ";";
            }
        }
        return $"{connString.TrimEnd(';')};Database={schema};";
    }

    public static T GetOption<T>(ICollection<CustomDatabaseOption>? options, string key, Func<string, T> converter, Func<T>? defaultValue = null)
    {
        if (options is null) return defaultValue is not null ? defaultValue() : default!;
        foreach (var opt in options)
        {
            if (opt.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return converter(opt.Value);
        }
        return defaultValue is not null ? defaultValue() : default!;
    }

    private string BuildUserCredentialArguments()
    {
        var userArgument = $"-u {_user}";
        if (string.IsNullOrWhiteSpace(_password))
        {
            return userArgument;
        }

        return $"{userArgument} -p{_password}";
    }

    private async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _logger.LogError("{Tool} failed (exit {Code}): {Error}", fileName, process.ExitCode, error);
        }

        return output;
    }

    private async Task<string> RunProcessWithInputFileAsync(string fileName, string arguments, string inputFile, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        await using (var input = File.OpenRead(inputFile))
        {
            await input.CopyToAsync(process.StandardInput.BaseStream, cancellationToken).ConfigureAwait(false);
        }

        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _logger.LogError("{Tool} failed (exit {Code}): {Error}", fileName, process.ExitCode, error);
        }

        return output;
    }

    /// <inheritdoc/>
    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Per-schema entity configuration done by each DbContext's OnModelCreating
    }

    /// <inheritdoc/>
    public void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
    }

    public async Task RunScheduledOptimisation(CancellationToken cancellationToken)
    {
        if (!CheckToolsAvailable()) return;

        try
        {
            var result = await RunProcessAsync(MysqlPath,
                $"-h {_server} -P {_port} {BuildUserCredentialArguments()} {DatabaseNames.Main} -e \"ANALYZE TABLE `{DatabaseNames.Main}`.`Movies`;\"",
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Optimization result for {Database}: {Result}",
                DatabaseNames.Main,
                string.IsNullOrEmpty(result) ? "OK" : result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scheduled MySQL optimization failed.");
        }
    }

    public Task RunShutdownTask(CancellationToken cancellationToken)
    {
        // FLUSH not needed for embedded MariaDB — process kill handles it
        return Task.CompletedTask;
    }

    private bool CheckToolsAvailable()
    {
        var mysqldump = MysqldumpPath;
        var mysql = MysqlPath;

        if (string.IsNullOrEmpty(ToolsDir)) return true; // rely on PATH

        if (!File.Exists(mysqldump))
        {
            _logger.LogWarning("mysqldump not found at {Path}. Backup/restore unavailable.", mysqldump);
            return false;
        }

        if (!File.Exists(mysql))
        {
            _logger.LogWarning("mysql CLI not found at {Path}. Backup/restore unavailable.", mysql);
            return false;
        }

        return true;
    }

    public async Task<string> MigrationBackupFast(CancellationToken cancellationToken)
    {
        if (!CheckToolsAvailable()) return string.Empty;

        var key = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var backupFolder = GetBackupFolder();
        Directory.CreateDirectory(backupFolder);

        var outputFile = Path.Combine(backupFolder, $"{key}_{DatabaseNames.Main}.sql");
        _logger.LogInformation("Backing up {Database} to {File}", DatabaseNames.Main, outputFile);

        await RunProcessAsync(MysqldumpPath,
            $"-h {_server} -P {_port} {BuildUserCredentialArguments()} --databases {DatabaseNames.Main} --routines --triggers --single-transaction --quick --result-file=\"{outputFile}\"",
            cancellationToken).ConfigureAwait(false);

        return key;
    }

    public async Task RestoreBackupFast(string key, CancellationToken cancellationToken)
    {
        var backupFolder = GetBackupFolder();

        var inputFile = Path.Combine(backupFolder, $"{key}_{DatabaseNames.Main}.sql");
        if (!File.Exists(inputFile))
        {
            _logger.LogWarning("Backup file not found for {Database}: {File}", DatabaseNames.Main, inputFile);
            return;
        }

        _logger.LogInformation("Restoring {Database} from {File}", DatabaseNames.Main, inputFile);

        await RunProcessWithInputFileAsync(MysqlPath,
            $"-h {_server} -P {_port} {BuildUserCredentialArguments()} {DatabaseNames.Main}",
            inputFile,
            cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteBackup(string key)
    {
        var backupFolder = GetBackupFolder();
        if (!Directory.Exists(backupFolder)) return Task.CompletedTask;

        var file = Path.Combine(backupFolder, $"{key}_{DatabaseNames.Main}.sql");
        if (File.Exists(file))
        {
            try { File.Delete(file); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete backup {File}", file); }
        }

        return Task.CompletedTask;
    }

    private string GetBackupFolder()
    {
        return !string.IsNullOrEmpty(_backupDir)
            ? Path.Combine(_backupDir, BackupFolderName)
            : Path.Combine(Path.GetTempPath(), "MulletaFlix", BackupFolderName);
    }

    /// <inheritdoc/>
    public async Task PurgeDatabase(MulletaFlixDbContext dbContext, IEnumerable<string>? tableNames)
    {
        if (tableNames == null) return;

        await dbContext.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;").ConfigureAwait(false);
        try
        {
            foreach (var tableName in tableNames)
            {
                var quotedTableName = string.Join('.', tableName.Split('.').Select(QuoteIdentifier));
                await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM {quotedTableName};").ConfigureAwait(false);
            }
        }
        finally
        {
            await dbContext.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;").ConfigureAwait(false);
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch == '_')))
        {
            throw new ArgumentException("Invalid MySQL identifier.", nameof(identifier));
        }

        return $"`{identifier}`";
    }
}
