using System.Linq;
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
}
