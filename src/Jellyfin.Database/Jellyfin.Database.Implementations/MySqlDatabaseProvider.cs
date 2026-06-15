using System;
using System.Collections.Generic;
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

    /// <inheritdoc/>
    public IDbContextFactory<MulletaFlixDbContext>? DbContextFactory { get; set; }

    /// <inheritdoc/>
    public void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions databaseConfiguration, string schemaName = "")
    {
        var connString = databaseConfiguration.CustomProviderOptions?.Options is { } opts
            ? BuildConnectionString(opts)
            : DefaultConnectionString;

        var schema = !string.IsNullOrEmpty(schemaName) ? schemaName : "mulletaflix_users";
        connString = ApplySchema(connString, schema);
        _logger.LogInformation("MySQL: {Schema}", schema);

        var versionStr = databaseConfiguration.CustomProviderOptions?.Options is { } cfg
            ? GetOption(cfg, "server-version", e => e, () => "11.4.2")
            : "11.4.2";

        var serverVersion = new MariaDbServerVersion(new Version(versionStr));

        options.UseMySql(connString, serverVersion, mySqlOptions =>
        {
            mySqlOptions.MigrationsAssembly(GetType().Assembly.GetName().Name);
            mySqlOptions.SchemaBehavior(Pomelo.EntityFrameworkCore.MySql.Infrastructure.MySqlSchemaBehavior.Translate, (schema, table) => table);
        });
    }

    private static string BuildConnectionString(ICollection<CustomDatabaseOption> opts)
    {
        var server = GetOption(opts, "server", e => e, () => "localhost");
        var port = GetOption(opts, "port", e => e, () => "3306");
        var user = GetOption(opts, "user", e => e, () => "root");
        var password = GetOption(opts, "password", e => e, () => "");
        return $"Server={server};Port={port};User ID={user};Password={password};CharSet=utf8mb4;";
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

    /// <inheritdoc/>
    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Per-schema entity configuration done by each DbContext's OnModelCreating
    }

    /// <inheritdoc/>
    public void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
    }

    /// <inheritdoc/>
    public Task RunScheduledOptimisation(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RunShutdownTask(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<string> MigrationBackupFast(CancellationToken cancellationToken)
    {
        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc/>
    public Task RestoreBackupFast(string key, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteBackup(string key)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task PurgeDatabase(MulletaFlixDbContext dbContext, IEnumerable<string>? tableNames)
    {
        if (tableNames == null) return;

        await dbContext.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;").ConfigureAwait(false);
        foreach (var tableName in tableNames)
        {
            await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM `{tableName}`;").ConfigureAwait(false);
        }
        await dbContext.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;").ConfigureAwait(false);
    }
}
