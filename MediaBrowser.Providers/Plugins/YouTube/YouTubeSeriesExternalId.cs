using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Providers.Plugins.YouTube
{
    public class YouTubeSeriesExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "YouTube";

        /// <inheritdoc />
        public string Key => "YouTube";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => null;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Series;
    }
}
