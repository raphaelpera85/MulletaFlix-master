using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Entities.Security;
using MulletaFlix.Database.Implementations.Interfaces;
using MulletaFlix.Database.Implementations.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Database.Implementations.Contexts;

public class UsersDbContext(DbContextOptions<UsersDbContext> options, ILogger<UsersDbContext> logger, IEntityFrameworkCoreLockingBehavior entityFrameworkCoreLocking) : DbContext(options)
{
    public DbSet<AccessSchedule> AccessSchedules => Set<AccessSchedule>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DisplayPreferences> DisplayPreferences => Set<DisplayPreferences>();
    public DbSet<ItemDisplayPreferences> ItemDisplayPreferences => Set<ItemDisplayPreferences>();
    public DbSet<CustomItemDisplayPreferences> CustomItemDisplayPreferences => Set<CustomItemDisplayPreferences>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Preference> Preferences => Set<Preference>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserLicense> UserLicenses => Set<UserLicense>();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        HandleConcurrencyToken();
        var result = -1;
        await entityFrameworkCoreLocking.OnSaveChangesAsync(this, async () =>
        {
            result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return result;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        HandleConcurrencyToken();
        var result = -1;
        entityFrameworkCoreLocking.OnSaveChanges(this, () =>
        {
            result = base.SaveChanges(acceptAllChangesOnSuccess);
        });
        return result;
    }

    private void HandleConcurrencyToken()
    {
        foreach (var saveEntity in ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Modified)
                     .Select(entry => entry.Entity)
                     .OfType<IHasConcurrencyToken>())
        {
            saveEntity.OnSavingChanges();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // modelBuilder.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly);
    }
}
