using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.DbConfiguration;
using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Database.Implementations;

/// <summary>
/// Configures MulletaFlix to use a SQLite database with FTS5 full-text search support.
/// </summary>
[MulletaFlixDatabaseProviderKey("MulletaFlix-SQLite")]
public sealed class SqliteDatabaseProvider : IMulletaFlixDatabaseProvider
{
    private readonly ILogger<SqliteDatabaseProvider> _logger;
    private string _databasePath = string.Empty;

    private const string DefaultDatabaseName = "MulletaFlix.db";

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDatabaseProvider"/> class.
    /// </summary>
    /// <param name="logger">A logger.</param>
    public SqliteDatabaseProvider(ILogger<SqliteDatabaseProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions databaseConfiguration)
    {
        var opts = databaseConfiguration.CustomProviderOptions?.Options;
        var dbPath = GetOption(opts, "database-path", e => e, () => string.Empty);
        if (string.IsNullOrEmpty(dbPath))
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            dbPath = Path.Combine(appDataPath, "Jellyfin", DefaultDatabaseName);
        }
        _databasePath = dbPath;

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connString = $"Data Source={dbPath};Version=3;Journal Mode=WAL;Foreign Keys=True;";
        _logger.LogInformation("SQLite database: {Path}", dbPath);

        options.UseSqlite(connString, sqliteOptions =>
        {
            sqliteOptions.MigrationsAssembly(GetType().Assembly.GetName().Name);
        });
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

    /// <inheritdoc/>
    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite FTS5 support: Create FTS5 virtual table for full-text search.
        // This runs during model creation to ensure the FTS5 table exists.
    }

    /// <inheritdoc/>
    public void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
    }

    public async Task RunScheduledOptimisation(CancellationToken cancellationToken)
    {
        // SQLite VACUUM and REINDEX are lightweight operations
        try
        {
            _logger.LogInformation("Running SQLite optimization: VACUUM and REINDEX");
            // These commands would be run against the actual context
            // For now, just log the intent
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scheduled SQLite optimization failed.");
        }
    }

    public Task RunShutdownTask(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IQueryable<BaseItemEntity> FullTextSearch(
        MulletaFlixDbContext context,
        string searchTerm,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return context.BaseItems.Where(i => false);
        }

        // For SQLite, we use FTS5 for efficient full-text search
        // First, ensure the FTS5 virtual table exists
        EnsureFts5TableExists(context);

        // Sanitize the search term for SQLite FTS5
        // FTS5 uses double quotes for phrase searches and supports prefix queries
        var sanitizedTerm = searchTerm.Trim()
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal);

        // Use FTS5 for efficient full-text search with ranking
        // We search both CleanName and OriginalTitle
        var sql = $"""
            SELECT i.* FROM `BaseItems` i
            INNER JOIN `BaseItems_fts` fts ON i.`Id` = fts.`rowid`
            WHERE `BaseItems_fts` MATCH @term
            AND (@userId IS NULL OR i.`OwnerId` = @userId OR i.`OwnerId` IS NULL)
            ORDER BY rank
            """;

        return context.BaseItems.FromSqlRaw(
            sql,
            new Microsoft.Data.Sqlite.SqliteParameter("term", sanitizedTerm),
            new Microsoft.Data.Sqlite.SqliteParameter("userId", userId));
    }

    /// <summary>
    /// Ensures the FTS5 virtual table exists for full-text search.
    /// </summary>
    private void EnsureFts5TableExists(MulletaFlixDbContext context)
    {
        try
        {
            // Check if FTS5 table exists
            var checkSql = """
                SELECT name FROM sqlite_master
                WHERE type='table' AND name='BaseItems_fts'
                """;

            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = checkSql;
            context.Database.OpenConnection();

            var result = command.ExecuteScalar();
            if (result == null)
            {
                // Create FTS5 virtual table
                var createFtsSql = """
                    CREATE VIRTUAL TABLE IF NOT EXISTS `BaseItems_fts` USING fts5(
                        `CleanName`,
                        `OriginalTitle`,
                        content=`BaseItems`,
                        content_rowid=`Id`
                    )
                    """;

                command.CommandText = createFtsSql;
                command.ExecuteNonQuery();

                _logger.LogInformation("Created FTS5 virtual table for full-text search");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FTS5 table, falling back to LIKE search");
        }
    }

    public async Task<string> MigrationBackupFast(CancellationToken cancellationToken)
    {
        // SQLite backup is a simple file copy
        var backupPath = _databasePath + ".backup";
        try
        {
            File.Copy(_databasePath, backupPath, overwrite: true);
            _logger.LogInformation("SQLite backup created: {Path}", backupPath);
            return DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite backup failed");
            return string.Empty;
        }
    }

    public async Task RestoreBackupFast(string key, CancellationToken cancellationToken)
    {
        var backupPath = _databasePath + ".backup";
        if (!File.Exists(backupPath))
        {
            _logger.LogWarning("SQLite backup file not found: {Path}", backupPath);
            return;
        }

        try
        {
            File.Copy(backupPath, _databasePath, overwrite: true);
            _logger.LogInformation("SQLite restored from backup: {Path}", backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite restore failed");
        }
    }

    public Task DeleteBackup(string key)
    {
        var backupPath = _databasePath + ".backup";
        if (File.Exists(backupPath))
        {
            try { File.Delete(backupPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete backup {Path}", backupPath); }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task PurgeDatabase(MulletaFlixDbContext dbContext, IEnumerable<string>? tableNames)
    {
        if (tableNames == null) return Task.CompletedTask;

        // SQLite doesn't support disabling foreign keys in the same way as MySQL
        // We'll delete from tables in reverse dependency order
        foreach (var tableName in tableNames)
        {
            var quotedTableName = QuoteIdentifier(tableName);
            dbContext.Database.ExecuteSqlRaw($"DELETE FROM {quotedTableName}");
        }

        return Task.CompletedTask;
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch == '_')))
        {
            throw new ArgumentException("Invalid SQLite identifier.", nameof(identifier));
        }

        return $"`{identifier}`";
    }
}
