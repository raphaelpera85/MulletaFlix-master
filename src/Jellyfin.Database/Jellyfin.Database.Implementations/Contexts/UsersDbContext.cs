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

public class UsersDbContext(
    DbContextOptions<UsersDbContext> options,
    ILogger<UsersDbContext> logger,
    IMulletaFlixDatabaseProvider? mulletaFlixDatabaseProvider = null) : DbContext(options)
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
    public DbSet<PricingPlan> PricingPlans => Set<PricingPlan>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentGatewayConfig> PaymentGatewayConfigs => Set<PaymentGatewayConfig>();
    public DbSet<DiscountCoupon> DiscountCoupons => Set<DiscountCoupon>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        HandleConcurrencyToken();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        HandleConcurrencyToken();
        return base.SaveChanges(acceptAllChangesOnSuccess);
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
        mulletaFlixDatabaseProvider?.OnModelCreating(modelBuilder);
        base.OnModelCreating(modelBuilder);

        // Apply configurations for entities managed by UsersDbContext
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.UserConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.UserLicenseConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.DisplayPreferencesConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.PermissionConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.PreferenceConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.DeviceConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.ActivityLogConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.CustomItemDisplayPreferencesConfiguration());

        // New billing configurations
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.PricingPlanConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.PaymentTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.PaymentGatewayConfigConfiguration());
        modelBuilder.ApplyConfiguration(new MulletaFlix.Database.Implementations.ModelConfiguration.DiscountCouponConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        mulletaFlixDatabaseProvider?.ConfigureConventions(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);
    }
}
