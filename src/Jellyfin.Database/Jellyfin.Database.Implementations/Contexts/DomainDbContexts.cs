using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Entities.Books;
using MulletaFlix.Database.Implementations.Entities.Channels;
using MulletaFlix.Database.Implementations.Entities.Movies;
using MulletaFlix.Database.Implementations.Entities.Security;
using MulletaFlix.Database.Implementations.Entities.Series;
using MulletaFlix.Database.Implementations.Locking;
using MulletaFlix.Database.Implementations.ModelConfiguration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Database.Implementations.Contexts;

public class MoviesDbContext : DbContext
{
    public MoviesDbContext(DbContextOptions<MoviesDbContext> options, ILogger<MoviesDbContext> logger) : base(options)
    {
    }
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<MovieMetadata> MovieMetadata => Set<MovieMetadata>();
    public DbSet<MovieUserData> MovieUserData => Set<MovieUserData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new MovieConfiguration());
        modelBuilder.ApplyConfiguration(new MovieMetadataConfiguration());
        modelBuilder.ApplyConfiguration(new MovieUserDataConfiguration());
    }
}

public class SeriesDbContext : DbContext
{
    public SeriesDbContext(DbContextOptions<SeriesDbContext> options, ILogger<SeriesDbContext> logger) : base(options)
    {
    }
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<SeriesUserData> SeriesUserData => Set<SeriesUserData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new SeriesConfiguration());
        modelBuilder.ApplyConfiguration(new SeasonConfiguration());
        modelBuilder.ApplyConfiguration(new EpisodeConfiguration());
        modelBuilder.ApplyConfiguration(new SeriesUserDataConfiguration());
    }
}

public class ChannelsDbContext : DbContext
{
    public ChannelsDbContext(DbContextOptions<ChannelsDbContext> options, ILogger<ChannelsDbContext> logger) : base(options)
    {
    }
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Program> Programs => Set<Program>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ChannelConfiguration());
        modelBuilder.ApplyConfiguration(new ProgramConfiguration());
    }
}

public class BooksDbContext : DbContext
{
    public BooksDbContext(DbContextOptions<BooksDbContext> options, ILogger<BooksDbContext> logger) : base(options)
    {
    }
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookUserData> BookUserData => Set<BookUserData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new BookConfiguration());
        modelBuilder.ApplyConfiguration(new BookUserDataConfiguration());
    }
}

public class SystemDbContext : DbContext
{
    public SystemDbContext(DbContextOptions<SystemDbContext> options, ILogger<SystemDbContext> logger) : base(options)
    {
    }
    public DbSet<DeviceOptions> DeviceOptions => Set<DeviceOptions>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
