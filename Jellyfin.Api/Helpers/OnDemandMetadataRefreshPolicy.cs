using System;
using MulletaFlix.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace MulletaFlix.Api.Helpers;

internal static class OnDemandMetadataRefreshPolicy
{
    private static readonly TimeSpan RefreshStalenessThreshold = TimeSpan.FromDays(3);

    public static bool ShouldRefresh(BaseItem item, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(item);

        var hasMetadataAndArtwork = !string.IsNullOrWhiteSpace(item.Overview)
            && item.HasImage(ImageType.Primary);
        if (hasMetadataAndArtwork)
        {
            return false;
        }

        if (item.DateLastRefreshed == DateTime.MinValue)
        {
            return true;
        }

        return utcNow - item.DateLastRefreshed >= RefreshStalenessThreshold;
    }
}
