using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Providers.Books.OpenLibrary
{
    public class OpenLibraryExternalId : IExternalId
    {
        public string ProviderName => "Open Library";

        public string Key => "OpenLibrary";

        public ExternalIdMediaType? Type => null;

        public bool Supports(IHasProviderIds item) => item is Book;
    }
}
