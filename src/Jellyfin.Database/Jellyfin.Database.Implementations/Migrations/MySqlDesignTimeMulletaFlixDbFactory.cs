using System;
using MulletaFlix.Database.Implementations.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace MulletaFlix.Database.Implementations.Migrations;

public sealed class MySqlDesignTimeMulletaFlixDbFactory : IDesignTimeDbContextFactory<MulletaFlixDbContext>
{
    public MulletaFlixDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MulletaFlixDbContext>();

        var connString = $"Server=localhost;Port=3306;User ID=root;Password=;Database={DatabaseNames.Main};CharSet=utf8mb4;Default Command Timeout=120;";
        var serverVersion = new MariaDbServerVersion(new Version(11, 4, 2));

        optionsBuilder.UseMySql(connString, serverVersion, mySqlOptions =>
        {
            mySqlOptions.MigrationsAssembly(GetType().Assembly.GetName().Name);
            mySqlOptions.SchemaBehavior(Pomelo.EntityFrameworkCore.MySql.Infrastructure.MySqlSchemaBehavior.Translate, (schema, table) => table);
        });

        return new MulletaFlixDbContext(
            optionsBuilder.Options,
            NullLogger<MulletaFlixDbContext>.Instance,
            new MySqlDatabaseProvider(NullLogger<MySqlDatabaseProvider>.Instance),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
    }
}
