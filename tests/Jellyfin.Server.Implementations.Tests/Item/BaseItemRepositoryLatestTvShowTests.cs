using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MulletaFlix.Data.Enums;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Locking;
using MulletaFlix.Server.Implementations.Item;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Item;

public sealed class BaseItemRepositoryLatestTvShowTests : IDisposable
{
    private readonly MulletaFlixDbContext _context;
    private readonly BaseItemRepository _repository;

    public BaseItemRepositoryLatestTvShowTests()
    {
        var options = new DbContextOptionsBuilder<MulletaFlixDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var dbProvider = new Mock<IMulletaFlixDatabaseProvider>();
        dbProvider.Setup(p => p.OnModelCreating(It.IsAny<ModelBuilder>()));

        var lockingBehavior = new Mock<IEntityFrameworkCoreLockingBehavior>();
        lockingBehavior.Setup(l => l.OnSaveChanges(It.IsAny<MulletaFlixDbContext>(), It.IsAny<Action>()))
            .Callback<MulletaFlixDbContext, Action>(static (_, action) => action());
        lockingBehavior.Setup(l => l.OnSaveChangesAsync(It.IsAny<MulletaFlixDbContext>(), It.IsAny<Func<Task>>()))
            .Callback<MulletaFlixDbContext, Func<Task>>(static (_, func) => func());

        _context = new MulletaFlixDbContext(
            options,
            NullLogger<MulletaFlixDbContext>.Instance,
            dbProvider.Object,
            lockingBehavior.Object);

        _context.Database.EnsureCreated();

        var dbFactory = new Mock<IDbContextFactory<MulletaFlixDbContext>>();
        dbFactory.Setup(f => f.CreateDbContext()).Returns(_context);
        dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_context);

        var itemTypeLookup = new Mock<IItemTypeLookup>();
        itemTypeLookup.Setup(l => l.BaseItemKindNames)
            .Returns(new Dictionary<BaseItemKind, string>
            {
                { BaseItemKind.Episode, typeof(Episode).ToString() },
                { BaseItemKind.Season, typeof(Season).ToString() },
                { BaseItemKind.Series, typeof(Series).ToString() }
            });

        _repository = new BaseItemRepository(
            dbFactory.Object,
            Mock.Of<IServerApplicationHost>(),
            itemTypeLookup.Object,
            Mock.Of<MediaBrowser.Controller.Configuration.IServerConfigurationManager>(manager =>
                manager.Configuration == new ServerConfiguration()),
            NullLogger<BaseItemRepository>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public void GetLatestItemList_TvShows_ReturnsSeriesContainersInLatestOrder()
    {
        var now = new DateTime(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc);

        var seriesA = CreateSeries("Series A");
        var seasonA1 = CreateSeason(seriesA, "Series A - Season 1");
        var seasonA2 = CreateSeason(seriesA, "Series A - Season 2");
        var seriesAEpisodes = new[]
        {
            CreateEpisode("Series A", seriesA, seasonA1, now.AddHours(-1)),
            CreateEpisode("Series A", seriesA, seasonA1, now.AddHours(-2)),
            CreateEpisode("Series A", seriesA, seasonA1, now.AddDays(-3))
        };

        var seriesB = CreateSeries("Series B");
        var seasonB1 = CreateSeason(seriesB, "Series B - Season 1");
        var seriesBEpisodes = new[]
        {
            CreateEpisode("Series B", seriesB, seasonB1, now.AddHours(-3)),
            CreateEpisode("Series B", seriesB, seasonB1, now.AddHours(-4)),
            CreateEpisode("Series B", seriesB, seasonB1, now.AddDays(-3))
        };

        var seriesC = CreateSeries("Series C");
        var seasonC1 = CreateSeason(seriesC, "Series C - Season 1");
        var seriesCEpisodes = new[]
        {
            CreateEpisode("Series C", seriesC, seasonC1, now.AddHours(-5)),
            CreateEpisode("Series C", seriesC, seasonC1, now.AddDays(-3)),
            CreateEpisode("Series C", seriesC, seasonC1, now.AddDays(-4))
        };

        _context.BaseItems.AddRange(
            seriesA,
            seasonA1,
            seasonA2,
            seriesAEpisodes[0],
            seriesAEpisodes[1],
            seriesAEpisodes[2],
            seriesB,
            seasonB1,
            seriesBEpisodes[0],
            seriesBEpisodes[1],
            seriesBEpisodes[2],
            seriesC,
            seasonC1,
            seriesCEpisodes[0],
            seriesCEpisodes[1],
            seriesCEpisodes[2]);
        _context.SaveChanges();

        var result = _repository.GetLatestItemList(
            new InternalItemsQuery
            {
                Limit = 3
            },
            CollectionType.tvshows);

        Assert.Equal(3, result.Count);
        Assert.IsType<Series>(result[1]);
        Assert.IsType<Series>(result[0]);
        Assert.IsType<Series>(result[2]);
        Assert.Equal(seriesA.Id, result[0].Id);
        Assert.Equal(seriesB.Id, result[1].Id);
        Assert.Equal(seriesC.Id, result[2].Id);
    }

    [Fact]
    public void GetLatestItemList_TvShows_DoesNotMergeDifferentSeriesWithTheSameName()
    {
        var now = new DateTime(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc);

        var sharedSeriesName = "Shared Series";
        var seriesA = CreateSeries(sharedSeriesName);
        var seasonA = CreateSeason(seriesA, $"{sharedSeriesName} - Season 1");
        var episodeA = CreateEpisode(sharedSeriesName, seriesA, seasonA, now.AddHours(-1));

        var seriesB = CreateSeries(sharedSeriesName);
        var seasonB = CreateSeason(seriesB, $"{sharedSeriesName} - Season 1");
        var episodeB = CreateEpisode(sharedSeriesName, seriesB, seasonB, now.AddHours(-2));

        _context.BaseItems.AddRange(
            seriesA,
            seasonA,
            episodeA,
            seriesB,
            seasonB,
            episodeB);
        _context.SaveChanges();

        var result = _repository.GetLatestItemList(
            new InternalItemsQuery
            {
                Limit = 2
            },
            CollectionType.tvshows);

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.IsType<Series>(item));
        Assert.Contains(result, item => item.Id == seriesA.Id);
        Assert.Contains(result, item => item.Id == seriesB.Id);
    }

    private static BaseItemEntity CreateSeries(string name)
    {
        return new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = typeof(Series).ToString(),
            Name = name,
            IsFolder = true,
            IsSeries = true
        };
    }

    private static BaseItemEntity CreateSeason(BaseItemEntity series, string name)
    {
        return new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = typeof(Season).ToString(),
            Name = name,
            SeriesId = series.Id,
            ParentId = series.Id,
            IsFolder = true,
            DateCreated = series.DateCreated
        };
    }

    private static BaseItemEntity CreateEpisode(string seriesName, BaseItemEntity series, BaseItemEntity season, DateTime dateCreated)
    {
        return new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = typeof(Episode).ToString(),
            Name = $"{seriesName} Episode",
            SeriesName = seriesName,
            SeriesId = series.Id,
            SeasonId = season.Id,
            ParentId = season.Id,
            DateCreated = dateCreated,
            IsFolder = false
        };
    }
}
