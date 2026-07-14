using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Jellyfin.Database.Implementations.Migrations;
using MulletaFlix.Database.Implementations.Migrations;
using Xunit;
using System;

namespace MulletaFlix.Server.Implementations.Tests.EfMigrations;

public class EfMigrationTests
{
    private sealed class ExposedFixBaseItemRedundantRelations : FixBaseItemRedundantRelations
    {
        public void Apply(MigrationBuilder migrationBuilder)
        {
            base.Up(migrationBuilder);
        }
    }

    [Fact]
    public void CheckForUnappliedMigrations_MySql()
    {
        var dbDesignContext = new MySqlDesignTimeMulletaFlixDbFactory();
        var context = dbDesignContext.CreateDbContext([]);
        Assert.False(context.Database.HasPendingModelChanges(), "There are unapplied changes to the EFCore model for MySQL. Run: dotnet ef migrations add --context MulletaFlixDbContext --project src/Jellyfin.Database/Jellyfin.Database.Implementations");
    }

    [Fact]
    public void FixBaseItemRedundantRelations_DropsDuplicateFullTextIndexBeforeRecreatingIt()
    {
        var migration = new ExposedFixBaseItemRedundantRelations();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");

        migration.Apply(builder);

        var operations = builder.Operations;
        var dropIndex = Assert.IsType<SqlOperation>(operations[1]);
        Assert.Contains("DROP INDEX IF EXISTS IX_BaseItems_FullTextSearch ON BaseItems;", dropIndex.Sql, StringComparison.Ordinal);

        var createIndex = Assert.IsType<CreateIndexOperation>(operations[2]);
        Assert.Equal("IX_BaseItems_FullTextSearch", createIndex.Name);
    }
}
