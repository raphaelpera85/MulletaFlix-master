using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    public void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions databaseConfiguration)
    {
        // Default Connection String matching portable MariaDB setup
        var connectionString = "Server=localhost;Port=3306;User ID=root;Password=;Database=mulletaflix_users;CharSet=utf8mb4;";
        _logger.LogInformation("MySQL Connection String: {ConnectionString}", connectionString);

        var serverVersion = new MariaDbServerVersion(new Version(11, 4, 2));

        options.UseMySql(connectionString, serverVersion, mySqlOptions =>
        {
            mySqlOptions.MigrationsAssembly(GetType().Assembly.GetName().Name);
            mySqlOptions.SchemaBehavior(Pomelo.EntityFrameworkCore.MySql.Infrastructure.MySqlSchemaBehavior.Translate, (schema, table) => $"{schema}.{table}");
        });
    }

    /// <inheritdoc/>
    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var typeName = entity.ClrType.Name;
            var schema = GetSchemaForType(typeName);
            entity.SetSchema(schema);
        }
    }

    private string GetSchemaForType(string typeName)
    {
        return typeName switch
        {
            // Users and Licensing
            "AccessSchedule" or "ActivityLog" or "Device" or "DisplayPreferences" or "ItemDisplayPreferences" or
            "CustomItemDisplayPreferences" or "Permission" or "Preference" or "User" or "UserLicense" or
            "PricingPlan" or "PaymentTransaction" or "PaymentGatewayConfig" or "DiscountCoupon" or "UserData"
                => "mulletaflix_users",

            // Movies
            "Movie" or "MovieMetadata" or "BaseItemEntity" or "Chapter" or "LinkedChildEntity"
                => "mulletaflix_movies",

            // Series
            "Series" or "Season" or "Episode" or "SeriesMetadata" or "SeasonMetadata" or "EpisodeMetadata"
                => "mulletaflix_series",

            // Channels (IPTV)
            "Channel" or "Program"
                => "mulletaflix_channels",

            // Books
            "Book" or "BookMetadata"
                => "mulletaflix_books",

            // System Default / Core Configuration
            _ => "mulletaflix_system"
        };
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
