using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Providers.Books.OpenLibrary
{
    public class OpenLibraryExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "Open Library";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item.TryGetProviderId("OpenLibrary", out var externalId))
            {
                if (item is Book)
                {
                    yield return $"https://openlibrary.org/books/{externalId}";
                }
            }
        }
    }
}
