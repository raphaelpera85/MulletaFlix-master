using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Data;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Enums;
using MulletaFlix.Server.Implementations.Users;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Users;

public sealed class UserLicenseManagerTests : IDisposable
{
    private readonly DbContextOptions<UsersDbContext> _dbOptions;
    private readonly UserLicenseManager _licenseManager;

    public UserLicenseManagerTests()
    {
        _dbOptions = new DbContextOptionsBuilder<UsersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var context = CreateDbContext();
        context.Database.EnsureCreated();

        var factory = new Mock<IDbContextFactory<UsersDbContext>>();
        factory.Setup(f => f.CreateDbContext()).Returns(CreateDbContext);
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDbContext);

        _licenseManager = new UserLicenseManager(
            factory.Object,
            Mock.Of<IUserManager>(),
            Mock.Of<ISessionManager>(),
            NullLogger<UserLicenseManager>.Instance);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task SetLicenseAsync_AdminUser_IsAlwaysUnlimited()
    {
        var user = await CreateUserAsync(isAdmin: true);

        var dto = await _licenseManager.SetLicenseAsync(user.Id, 730, "admin", Guid.NewGuid());

        Assert.True(dto.IsUnlimited);
        Assert.Null(dto.DurationHours);
        Assert.Equal(DateTime.MaxValue, dto.ExpirationDate);

        using var context = CreateDbContext();
        var license = await context.UserLicenses.AsNoTracking().SingleAsync(l => l.UserId == user.Id);
        Assert.True(license.IsUnlimited);
        Assert.Null(license.DurationHours);
        Assert.Null(license.ExpirationDate);
    }

    [Fact]
    public async Task SetLicenseAsync_WhenLicenseIsActive_RenewsFromCurrentExpiration()
    {
        var user = await CreateUserAsync();
        var now = DateTime.UtcNow;
        var originalExpiration = now.AddDays(2);

        using (var context = CreateDbContext())
        {
            context.UserLicenses.Add(new UserLicense
            {
                UserId = user.Id,
                StartDate = now.AddDays(-3),
                DurationHours = 24,
                ExpirationDate = originalExpiration,
                IsUnlimited = false,
                CreatedAt = now.AddDays(-3),
                UpdatedAt = now.AddDays(-3)
            });

            await context.SaveChangesAsync();
        }

        var dto = await _licenseManager.SetLicenseAsync(user.Id, 24, "renew", Guid.NewGuid());

        Assert.False(dto.IsUnlimited);
        Assert.Equal(originalExpiration, dto.StartDate);
        Assert.Equal(originalExpiration.AddHours(24), dto.ExpirationDate);

        using var verificationContext = CreateDbContext();
        var renewed = await verificationContext.UserLicenses.AsNoTracking().SingleAsync(l => l.UserId == user.Id);
        Assert.Equal(originalExpiration, renewed.StartDate);
        Assert.Equal(originalExpiration.AddHours(24), renewed.ExpirationDate);
    }

    [Fact]
    public async Task SetLicenseAsync_WhenLicenseIsExpired_RenewsFromNow()
    {
        var user = await CreateUserAsync();
        var now = DateTime.UtcNow;
        var expiredAt = now.AddHours(-2);

        using (var context = CreateDbContext())
        {
            context.UserLicenses.Add(new UserLicense
            {
                UserId = user.Id,
                StartDate = now.AddDays(-5),
                DurationHours = 24,
                ExpirationDate = expiredAt,
                IsUnlimited = false,
                CreatedAt = now.AddDays(-5),
                UpdatedAt = now.AddDays(-5)
            });

            await context.SaveChangesAsync();
        }

        var before = DateTime.UtcNow;
        var dto = await _licenseManager.SetLicenseAsync(user.Id, 24, "renew", Guid.NewGuid());
        var after = DateTime.UtcNow;

        Assert.False(dto.IsUnlimited);
        Assert.InRange(dto.StartDate, before, after);
        Assert.Equal(dto.StartDate.AddHours(24), dto.ExpirationDate);
    }

    [Fact]
    public async Task ExpireOutdatedLicensesAsync_DisablesOnlyExpiredNonAdminUsers()
    {
        var expiredUser = await CreateUserAsync();
        var adminUser = await CreateUserAsync(isAdmin: true);
        var now = DateTime.UtcNow;

        using (var context = CreateDbContext())
        {
            context.UserLicenses.AddRange(
                new UserLicense
                {
                    UserId = expiredUser.Id,
                    StartDate = now.AddDays(-2),
                    DurationHours = 24,
                    ExpirationDate = now.AddHours(-1),
                    IsUnlimited = false,
                    CreatedAt = now.AddDays(-2),
                    UpdatedAt = now.AddDays(-2)
                },
                new UserLicense
                {
                    UserId = adminUser.Id,
                    StartDate = now.AddDays(-2),
                    DurationHours = null,
                    ExpirationDate = null,
                    IsUnlimited = true,
                    CreatedAt = now.AddDays(-2),
                    UpdatedAt = now.AddDays(-2)
                });

            await context.SaveChangesAsync();
        }

        var disabledCount = await _licenseManager.ExpireOutdatedLicensesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, disabledCount);

        using var verificationContext = CreateDbContext();
        var expiredUserPermissions = await verificationContext.Users
            .Include(u => u.Permissions)
            .SingleAsync(u => u.Id == expiredUser.Id);
        var adminPermissions = await verificationContext.Users
            .Include(u => u.Permissions)
            .SingleAsync(u => u.Id == adminUser.Id);

        Assert.True(expiredUserPermissions.HasPermission(PermissionKind.IsDisabled));
        Assert.False(adminPermissions.HasPermission(PermissionKind.IsDisabled));
    }

    private UsersDbContext CreateDbContext()
    {
        return new UsersDbContext(_dbOptions, NullLogger<UsersDbContext>.Instance);
    }

    private async Task<User> CreateUserAsync(bool isAdmin = false)
    {
        using var context = CreateDbContext();
        var user = new User(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        if (isAdmin)
        {
            user.SetPermission(PermissionKind.IsAdministrator, true);
        }

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}
