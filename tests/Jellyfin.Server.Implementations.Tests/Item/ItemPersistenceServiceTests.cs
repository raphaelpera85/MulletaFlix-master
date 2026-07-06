using System;
using System.Linq;
using MediaBrowser.Model.Entities;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Server.Implementations.Item;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Item;

public class ItemPersistenceServiceTests
{
    [Fact]
    public void ItemValueKeyComparer_TreatsValuesAsCaseInsensitiveWithinSameType()
    {
        var values = new[]
        {
            (ItemValueType.Genre, "Magic"),
            (ItemValueType.Genre, "magic"),
            (ItemValueType.Studios, "Magic")
        };

        var distinctValues = values.Distinct(ItemPersistenceService.ItemValueKeyComparer).ToArray();

        Assert.Equal(2, distinctValues.Length);
    }

    [Fact]
    public void CreateItemValueLookup_UsesCleanValueForKeys()
    {
        var lookup = ItemPersistenceService.CreateItemValueLookup(
            new[]
            {
                new ItemValue
                {
                    ItemValueId = Guid.NewGuid(),
                    Type = ItemValueType.Studios,
                    Value = "Pathé",
                    CleanValue = "pathe"
                }
            });

        Assert.True(lookup.ContainsKey((ItemValueType.Studios, "Pathe")));
    }

    [Fact]
    public void ClearTrackedNavigationProperties_RemovesTrackedCollectionsBeforeAttach()
    {
        var entity = new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = "Movie"
        };

        entity.Provider = new[]
        {
            new BaseItemProvider
            {
                ItemId = entity.Id,
                Item = entity,
                ProviderId = "tmdb",
                ProviderValue = "1234"
            }
        };

        entity.LockedFields = new[]
        {
            new BaseItemMetadataField
            {
                Id = 1,
                ItemId = entity.Id,
                Item = entity
            }
        };

        entity.Images = new[]
        {
            new BaseItemImageInfo
            {
                Id = Guid.NewGuid(),
                Path = "poster.jpg",
                ImageType = ImageInfoImageType.Primary,
                Width = 100,
                Height = 200,
                ItemId = entity.Id,
                Item = entity
            }
        };

        entity.TrailerTypes = new[]
        {
            new BaseItemTrailerType
            {
                Id = 1,
                ItemId = entity.Id,
                Item = entity
            }
        };

        ItemPersistenceService.ClearTrackedNavigationProperties(entity);

        Assert.Equal("Movie", entity.Type);
        Assert.Null(entity.Provider);
        Assert.Null(entity.LockedFields);
        Assert.Null(entity.Images);
        Assert.Null(entity.TrailerTypes);
    }
}
