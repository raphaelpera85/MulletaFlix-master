using System;
using MulletaFlix.Api.Helpers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Xunit;

namespace MulletaFlix.Api.Tests.Helpers;

public static class OnDemandMetadataRefreshPolicyTests
{
    [Fact]
    public static void ShouldRefresh_ReturnsTrue_WhenItemHasNoMetadataOrPrimaryImageAndHasNeverRefreshed()
    {
        var item = new Movie
        {
            DateLastRefreshed = DateTime.MinValue
        };

        Assert.True(OnDemandMetadataRefreshPolicy.ShouldRefresh(item, DateTime.UtcNow));
    }

    [Fact]
    public static void ShouldRefresh_ReturnsFalse_WhenItemAlreadyHasOverviewAndPrimaryImage()
    {
        var item = new Movie
        {
            DateLastRefreshed = DateTime.UtcNow.AddDays(-30),
            Overview = "Overview"
        };
        item.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = "http://example.com/poster.jpg" }, 0);

        Assert.False(OnDemandMetadataRefreshPolicy.ShouldRefresh(item, DateTime.UtcNow));
    }

    [Fact]
    public static void ShouldRefresh_ReturnsFalse_WhenItemIsTooRecent()
    {
        var item = new Movie
        {
            DateLastRefreshed = DateTime.UtcNow.AddDays(-1)
        };

        Assert.False(OnDemandMetadataRefreshPolicy.ShouldRefresh(item, DateTime.UtcNow));
    }
}
