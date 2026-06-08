using System;
using System.Linq;
using MulletaFlix.Api.Caching;
using MulletaFlix.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Caching;

public class HomeDtoCacheTests
{
    [Fact]
    public void BuildCacheKey_DifferentQueries_ProducesDifferentKeys()
    {
        var queryA = new InternalItemsQuery
        {
            User = new("user", "auth", "auth") { Id = Guid.NewGuid() },
            Recursive = true,
            Limit = 50,
            StartIndex = 0,
            IncludeItemTypes = [BaseItemKind.Movie],
            IsPlayed = false,
            IsFavorite = null,
            MediaTypes = [],
            ParentId = Guid.Empty
        };

        var queryB = new InternalItemsQuery
        {
            User = new("user", "auth", "auth") { Id = Guid.NewGuid() },
            Recursive = true,
            Limit = 50,
            StartIndex = 0,
            IncludeItemTypes = [BaseItemKind.Series],
            IsPlayed = false,
            IsFavorite = null,
            MediaTypes = [],
            ParentId = Guid.Empty
        };

        var keyA = ItemsResponseCache.BuildCacheKey(queryA);
        var keyB = ItemsResponseCache.BuildCacheKey(queryB);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void IsCacheable_FirstPageQuery_ReturnsTrue()
    {
        var query = new InternalItemsQuery
        {
            EnableTotalRecordCount = true,
            Limit = 20,
            StartIndex = 0
        };

        Assert.True(ItemsResponseCache.IsCacheable(query));
    }

    [Fact]
    public void IsCacheable_HasSearchTerm_ReturnsFalse()
    {
        var query = new InternalItemsQuery
        {
            EnableTotalRecordCount = true,
            Limit = 20,
            StartIndex = 0,
            SearchTerm = "test"
        };

        Assert.False(ItemsResponseCache.IsCacheable(query));
    }

    [Fact]
    public void TryGetSet_RoundTrip_ReturnsCachedValue()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new ItemsResponseCache(memoryCache);

        var key = ItemsResponseCache.BuildCacheKey(new InternalItemsQuery
        {
            Limit = 10,
            StartIndex = 0
        });

        var expected = new QueryResult<BaseItemDto>
        {
            Items = [new BaseItemDto { Id = Guid.NewGuid(), Name = "Test" }],
            TotalRecordCount = 1
        };

        cache.Set(key, expected);

        var found = cache.TryGet(key, out var actual);

        Assert.True(found);
        Assert.NotNull(actual);
        Assert.Equal(expected.TotalRecordCount, actual.TotalRecordCount);
        Assert.Equal(expected.Items[0].Id, actual.Items[0].Id);
        Assert.Equal(expected.Items[0].Name, actual.Items[0].Name);
    }
}

