using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.DbConfiguration;
using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;

namespace MulletaFlix.Database.Implementations;

/// <summary>
/// Defines the type and extension points for multi database support.
/// </summary>
public interface IMulletaFlixDatabaseProvider
{
    /// <summary>
    /// Initialises MulletaFlixs EFCore database access.
    /// </summary>
    /// <param name="options">The EFCore database options.</param>
    /// <param name="databaseConfiguration">The MulletaFlix database options.</param>
    void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions databaseConfiguration);

    /// <summary>
    /// Will be invoked when EFCore wants to build its model.
    /// </summary>
    /// <param name="modelBuilder">The ModelBuilder from EFCore.</param>
    void OnModelCreating(ModelBuilder modelBuilder);

    /// <summary>
    /// Will be invoked when EFCore wants to configure its model.
    /// </summary>
    /// <param name="configurationBuilder">The ModelConfigurationBuilder from EFCore.</param>
    void ConfigureConventions(ModelConfigurationBuilder configurationBuilder);

    /// <summary>
    /// If supported this should run any periodic maintaince tasks.
    /// </summary>
    /// <param name="cancellationToken">The token to abort the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task RunScheduledOptimisation(CancellationToken cancellationToken);

    /// <summary>
    /// If supported this should perform any actions that are required on stopping the MulletaFlix server.
    /// </summary>
    /// <param name="cancellationToken">The token that will be used to abort the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task RunShutdownTask(CancellationToken cancellationToken);

    /// <summary>
    /// Runs a full Database backup that can later be restored to.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A key to identify the backup.</returns>
    /// <exception cref="NotImplementedException">May throw an NotImplementException if this operation is not supported for this database.</exception>
    Task<string> MigrationBackupFast(CancellationToken cancellationToken);

    /// <summary>
    /// Restores a backup that has been previously created by <see cref="MigrationBackupFast(CancellationToken)"/>.
    /// </summary>
    /// <param name="key">The key to the backup from which the current database should be restored from.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task RestoreBackupFast(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a backup that has been previously created by <see cref="MigrationBackupFast(CancellationToken)"/>.
    /// </summary>
    /// <param name="key">The key to the backup which should be cleaned up.</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task DeleteBackup(string key);

    /// <summary>
    /// Runs a full-text search against BaseItems.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="searchTerm">The search term.</param>
    /// <param name="userId">Optional user ID for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queryable of BaseItemEntity matching the search term.</returns>
    IQueryable<BaseItemEntity> FullTextSearch(
        MulletaFlixDbContext context,
        string searchTerm,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges all data from the database.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    /// <param name="tableNames">The names of the tables to purge.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task PurgeDatabase(MulletaFlixDbContext dbContext, IEnumerable<string>? tableNames);
}
