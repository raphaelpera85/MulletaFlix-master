using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Providers.Plugins.MyDramaList
{
    public class MyDramaListExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "MyDramaList";

        /// <inheritdoc />
        public string Key => "MyDramaList";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => null;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Series;
    }
}
