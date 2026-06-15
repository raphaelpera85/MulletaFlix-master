using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Database.Implementations.Contexts;

public class MoviesDbContext(DbContextOptions<MoviesDbContext> options, ILogger<MoviesDbContext> logger, IEntityFrameworkCoreLockingBehavior entityFrameworkCoreLocking) : DbContext(options)
{
    // Em Jellyfin puro tudo era BaseItems. Agora abstrairemos as tabelas específicas.
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<MovieMetadata> MovieMetadata => Set<MovieMetadata>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<UserData> MovieUserData => Set<UserData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}

public class SeriesDbContext(DbContextOptions<SeriesDbContext> options, ILogger<SeriesDbContext> logger, IEntityFrameworkCoreLockingBehavior entityFrameworkCoreLocking) : DbContext(options)
{
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<SeriesMetadata> SeriesMetadata => Set<SeriesMetadata>();
    public DbSet<SeasonMetadata> SeasonMetadata => Set<SeasonMetadata>();
    public DbSet<EpisodeMetadata> EpisodeMetadata => Set<EpisodeMetadata>();
    public DbSet<UserData> SeriesUserData => Set<UserData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}

public class ChannelsDbContext(DbContextOptions<ChannelsDbContext> options, ILogger<ChannelsDbContext> logger, IEntityFrameworkCoreLockingBehavior entityFrameworkCoreLocking) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Program> Programs => Set<Program>(); // EPG

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}

public class BooksDbContext(DbContextOptions<BooksDbContext> options, ILogger<BooksDbContext> logger, IEntityFrameworkCoreLockingBehavior entityFrameworkCoreLocking) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookMetadata> BookMetadata => Set<BookMetadata>();
    public DbSet<UserData> BookUserData => Set<UserData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}

public class SystemDbContext(DbContextOptions<SystemDbContext> options, ILogger<SystemDbContext> logger, IEntityFrameworkCoreLockingBehavior entityFrameworkCoreLocking) : DbContext(options)
{
    public DbSet<DeviceOptions> DeviceOptions => Set<DeviceOptions>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ProviderMapping> ProviderMappings => Set<ProviderMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}

public class Movie {}
public class MovieMetadata {}
public class Series {}
public class Season {}
public class Episode {}
public class SeriesMetadata {}
public class SeasonMetadata {}
public class EpisodeMetadata {}
public class Channel {}
public class Program {}
public class Book {}
public class BookMetadata {}
