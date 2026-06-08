using MulletaFlix.Database.Providers.Sqlite.Migrations;
using MulletaFlix.Server.Implementations.Migrations;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.EfMigrations;

public class EfMigrationTests
{
    [Fact]
    public void CheckForUnappliedMigrations_SqLite()
    {
        var dbDesignContext = new SqliteDesignTimeMulletaFlixDbFactory();
        var context = dbDesignContext.CreateDbContext([]);
        Assert.False(context.Database.HasPendingModelChanges(), "There are unapplied changes to the EFCore model for SQLite. Please create a Migration.");
    }
}

