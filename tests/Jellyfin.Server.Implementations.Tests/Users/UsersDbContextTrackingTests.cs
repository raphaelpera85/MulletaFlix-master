using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MulletaFlix.Database.Implementations.Contexts;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Users;

public class UsersDbContextTrackingTests
{
    [Fact]
    public void Constructor_KeepsAutomaticChangeDetectionEnabled()
    {
        var options = new DbContextOptionsBuilder<UsersDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_KeepsAutomaticChangeDetectionEnabled))
            .Options;
        using var context = new UsersDbContext(options, NullLogger<UsersDbContext>.Instance);

        Assert.True(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
