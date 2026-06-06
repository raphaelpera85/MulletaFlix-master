using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Database.Providers.Sqlite;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Item;

public sealed class NextUpQueryOptimizationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly JellyfinDbContext _context;
    private readonly Mock<IDbContextFactory<JellyfinDbContext>> _dbProviderMock;
    private readonly Mock<IItemTypeLookup> _itemTypeLookupMock;
    private readonly Mock<IItemQueryHelpers> _queryHelpersMock;
    private readonly NextUpService _service;

    public NextUpQueryOptimizationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite(_connection)
            .Options;

        var dbProvider = new Mock<IJellyfinDatabaseProvider>();
        dbProvider.Setup(p => p.OnModelCreating(It.IsAny<ModelBuilder>()))
            .Callback<ModelBuilder>(static mb => mb.SetDefaultDateTimeKind(DateTimeKind.Utc));

        var lockingBehavior = new Mock<IEntityFrameworkCoreLockingBehavior>();
        lockingBehavior.Setup(l => l.OnSaveChanges(It.IsAny<JellyfinDbContext>(), It.IsAny<Action>()))
            .Callback<JellyfinDbContext, Action>(static (_, action) => action());
        lockingBehavior.Setup(l => l.OnSaveChangesAsync(It.IsAny<JellyfinDbContext>(), It.IsAny<Func<Task>>()))
            .Callback<JellyfinDbContext, Func<Task>>(static (_, func) => func());

        _context = new JellyfinDbContext(
            options,
            NullLogger<JellyfinDbContext>.Instance,
            dbProvider.Object,
            lockingBehavior.Object);

        _context.Database.EnsureCreated();

        _dbProviderMock = new Mock<IDbContextFactory<JellyfinDbContext>>();
        _dbProviderMock.Setup(f => f.CreateDbContext()).Returns(_context);

        _itemTypeLookupMock = new Mock<IItemTypeLookup>();
        _itemTypeLookupMock.Setup(l => l.BaseItemKindNames)
            .Returns(new Dictionary<BaseItemKind, string> { { BaseItemKind.Episode, "Episode" } });

        _queryHelpersMock = new Mock<IItemQueryHelpers>();

        _service = new NextUpService(
            _dbProviderMock.Object,
            _itemTypeLookupMock.Object,
            _queryHelpersMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void GetNextUpSeriesKeys_ReturnsProjectedKeys()
    {
        var userId = Guid.NewGuid();
        var seriesAKey = "SeriesA";
        var seriesBKey = "SeriesB";
        var topParentId = Guid.NewGuid();

        var episodeA = new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = "Episode",
            SeriesPresentationUniqueKey = seriesAKey,
            TopParentId = topParentId,
            ParentIndexNumber = 1,
            IndexNumber = 1
        };
        var episodeB = new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = "Episode",
            SeriesPresentationUniqueKey = seriesBKey,
            TopParentId = topParentId,
            ParentIndexNumber = 1,
            IndexNumber = 1
        };

        var userEntity = new Jellyfin.Database.Implementations.Entities.User("testuser", "auth", "auth")
        {
            Id = userId
        };
        _context.Users.Add(userEntity);

        _context.BaseItems.AddRange(episodeA, episodeB);
        _context.UserData.AddRange(
            new UserData
            {
                UserId = userId,
                ItemId = episodeA.Id,
                Item = episodeA,
                User = userEntity,
                CustomDataKey = string.Empty,
                LastPlayedDate = DateTime.UtcNow.AddDays(-1),
                Played = true
            },
            new UserData
            {
                UserId = userId,
                ItemId = episodeB.Id,
                Item = episodeB,
                User = userEntity,
                CustomDataKey = string.Empty,
                LastPlayedDate = DateTime.UtcNow.AddDays(-2),
                Played = true
            });

        _context.SaveChanges();

        var query = new InternalItemsQuery
        {
            User = new User("testuser", "auth", "auth") { Id = userId },
            TopParentIds = [topParentId]
        };

        var result = _service.GetNextUpSeriesKeys(query, DateTime.UtcNow.AddDays(-3));

        Assert.Equal(2, result.Count);
        Assert.Equal(seriesAKey, result[0]);
        Assert.Equal(seriesBKey, result[1]);
    }

    [Fact]
    public void GetNextUpSeriesKeys_AppliesLimit()
    {
        var userId = Guid.NewGuid();
        var seriesAKey = "SeriesA";
        var seriesBKey = "SeriesB";
        var topParentId = Guid.NewGuid();

        var episodeA = new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = "Episode",
            SeriesPresentationUniqueKey = seriesAKey,
            TopParentId = topParentId,
            ParentIndexNumber = 1,
            IndexNumber = 1
        };
        var episodeB = new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = "Episode",
            SeriesPresentationUniqueKey = seriesBKey,
            TopParentId = topParentId,
            ParentIndexNumber = 1,
            IndexNumber = 1
        };

        var userEntity = new Jellyfin.Database.Implementations.Entities.User("testuser", "auth", "auth")
        {
            Id = userId
        };
        _context.Users.Add(userEntity);

        _context.BaseItems.AddRange(episodeA, episodeB);
        _context.UserData.AddRange(
            new UserData
            {
                UserId = userId,
                ItemId = episodeA.Id,
                Item = episodeA,
                User = userEntity,
                CustomDataKey = string.Empty,
                LastPlayedDate = DateTime.UtcNow.AddDays(-1),
                Played = true
            },
            new UserData
            {
                UserId = userId,
                ItemId = episodeB.Id,
                Item = episodeB,
                User = userEntity,
                CustomDataKey = string.Empty,
                LastPlayedDate = DateTime.UtcNow.AddDays(-2),
                Played = true
            });

        _context.SaveChanges();

        var query = new InternalItemsQuery
        {
            User = new User("testuser", "auth", "auth") { Id = userId },
            TopParentIds = [topParentId],
            Limit = 1
        };

        var result = _service.GetNextUpSeriesKeys(query, DateTime.UtcNow.AddDays(-3));

        Assert.Single(result);
        Assert.Equal(seriesAKey, result[0]);
    }
}
