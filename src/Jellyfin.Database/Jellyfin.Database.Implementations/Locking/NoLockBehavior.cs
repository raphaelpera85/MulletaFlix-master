using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Database.Implementations.Locking;

/// <summary>
/// Default lock behavior. Defines no explicit application locking behavior.
/// </summary>
public class NoLockBehavior : IEntityFrameworkCoreLockingBehavior
{
    private readonly ILogger<NoLockBehavior> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NoLockBehavior"/> class.
    /// </summary>
    /// <param name="logger">The Application logger.</param>
    public NoLockBehavior(ILogger<NoLockBehavior> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void OnSaveChanges(MulletaFlixDbContext context, Action saveChanges)
    {
        saveChanges();
    }

    /// <inheritdoc/>
    public void Initialise(DbContextOptionsBuilder optionsBuilder)
    {
        _logger.LogInformation("The database locking mode has been set to: NoLock.");
    }

    /// <inheritdoc/>
    public async Task OnSaveChangesAsync(MulletaFlixDbContext context, Func<Task> saveChanges)
    {
        await saveChanges().ConfigureAwait(false);
    }
}

