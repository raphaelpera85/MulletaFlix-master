using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Providers.Books.OpenLibrary;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Providers.Tests.Books;

public class OpenLibraryProviderTests
{
    [Fact]
    public async Task GetImages_WorkWithCovers_ReturnsCoverById()
    {
        var provider = CreateProvider(
            new[]
            {
                new Uri("https://openlibrary.org/works/OL26415696W.json"),
            },
            """
            {
              "key": "/works/OL26415696W",
              "title": "20 Mil Leguas Submarinas",
              "covers": [123456]
            }
            """);

        var book = new Book();
        book.SetProviderId("OpenLibrary", "OL26415696W");

        var images = (await provider.GetImages(book, CancellationToken.None)).ToList();

        Assert.Single(images);
        Assert.Equal(ImageType.Primary, images[0].Type);
        Assert.Equal("https://covers.openlibrary.org/b/id/123456-L.jpg", images[0].Url);
    }

    [Fact]
    public async Task GetImages_WorkWithoutCovers_ReturnsEditionCoverById()
    {
        var provider = CreateProvider(new Dictionary<Uri, string>
        {
            [new Uri("https://openlibrary.org/works/OL26415696W.json")] = """
            {
              "key": "/works/OL26415696W",
              "title": "20 Mil Leguas Submarinas"
            }
            """,
            [new Uri("https://openlibrary.org/works/OL26415696W/editions.json?limit=25")] = """
            {
              "entries": [
                {
                  "key": "/books/OL123M",
                  "title": "20 Mil Leguas Submarinas",
                  "covers": [987654],
                  "isbn_13": ["9780000000000"]
                }
              ]
            }
            """
        });

        var book = new Book();
        book.SetProviderId("OpenLibrary", "OL26415696W");

        var images = (await provider.GetImages(book, CancellationToken.None)).ToList();

        Assert.Single(images);
        Assert.Equal(ImageType.Primary, images[0].Type);
        Assert.Equal("https://covers.openlibrary.org/b/id/987654-L.jpg", images[0].Url);
    }

    [Fact]
    public async Task GetImages_WorkWithoutAnyCovers_DoesNotReturnInvalidWorkOlidCover()
    {
        var provider = CreateProvider(new Dictionary<Uri, string>
        {
            [new Uri("https://openlibrary.org/works/OL26415696W.json")] = """
            {
              "key": "/works/OL26415696W",
              "title": "20 Mil Leguas Submarinas"
            }
            """,
            [new Uri("https://openlibrary.org/works/OL26415696W/editions.json?limit=25")] = """
            {
              "entries": [
                {
                  "key": "/books/OL123M",
                  "title": "20 Mil Leguas Submarinas"
                }
              ]
            }
            """
        });

        var book = new Book();
        book.SetProviderId("OpenLibrary", "OL26415696W");

        var images = (await provider.GetImages(book, CancellationToken.None)).ToList();

        Assert.Empty(images);
    }

    [Fact]
    public async Task GetImages_IsbnWithoutCovers_ReturnsOpenLibraryFallbackCover()
    {
        var provider = CreateProvider(
            new[]
            {
                new Uri("https://openlibrary.org/api/books?bibkeys=ISBN:9788571641304&format=json&jscmd=data"),
            },
            """
            {
              "ISBN:9788571641304": {
                "title": "1984",
                "identifiers": {
                  "isbn_13": ["9788571641304"]
                }
              }
            }
            """);

        var book = new Book();
        book.SetProviderId("ISBN", "9788571641304");

        var images = (await provider.GetImages(book, CancellationToken.None)).ToList();

        Assert.Single(images);
        Assert.Equal(ImageType.Primary, images[0].Type);
        Assert.Equal("https://covers.openlibrary.org/b/isbn/9788571641304-L.jpg?default=false", images[0].Url);
    }

    private static OpenLibraryProvider CreateProvider(Uri[] expectedUris, string responseBody)
        => CreateProvider(expectedUris.ToDictionary(uri => uri, _ => responseBody));

    private static OpenLibraryProvider CreateProvider(IReadOnlyDictionary<Uri, string> responses)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                Assert.NotNull(request.RequestUri);
                Assert.True(responses.TryGetValue(request.RequestUri!, out var responseBody), $"Unexpected request URI: {request.RequestUri}");
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent(responseBody)
                });
            });

        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler.Object));

        return new OpenLibraryProvider(httpClientFactory.Object, NullLogger<OpenLibraryProvider>.Instance);
    }
}
