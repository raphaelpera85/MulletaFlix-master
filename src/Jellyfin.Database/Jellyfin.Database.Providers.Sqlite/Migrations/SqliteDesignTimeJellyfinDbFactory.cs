using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace MulletaFlix.Database.Providers.Sqlite.Migrations
{
    /// <summary>
    /// The design time factory for <see cref="MulletaFlixDbContext"/>.
    /// This is only used for the creation of migrations and not during runtime.
    /// </summary>
    internal sealed class SqliteDesignTimeMulletaFlixDbFactory : IDesignTimeDbContextFactory<MulletaFlixDbContext>
    {
        public MulletaFlixDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<MulletaFlixDbContext>();
            optionsBuilder.UseSqlite("Data Source=MulletaFlix.db", f => f.MigrationsAssembly(GetType().Assembly));

            return new MulletaFlixDbContext(
                optionsBuilder.Options,
                NullLogger<MulletaFlixDbContext>.Instance,
                new SqliteDatabaseProvider(null!, NullLogger<SqliteDatabaseProvider>.Instance),
                new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
        }
    }
}

