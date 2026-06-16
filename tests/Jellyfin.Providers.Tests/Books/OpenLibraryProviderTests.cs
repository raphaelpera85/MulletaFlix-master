using System;
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
    public async Task GetImages_WorkWithoutCovers_ReturnsOpenLibraryFallbackCover()
    {
        var provider = CreateProvider(
            new[]
            {
                new Uri("https://openlibrary.org/works/OL26415696W.json"),
            },
            """
            {
              "key": "/works/OL26415696W",
              "title": "20 Mil Leguas Submarinas"
            }
            """);

        var book = new Book();
        book.SetProviderId("OpenLibrary", "OL26415696W");

        var images = (await provider.GetImages(book, CancellationToken.None)).ToList();

        Assert.Single(images);
        Assert.Equal(ImageType.Primary, images[0].Type);
        Assert.Equal("https://covers.openlibrary.org/b/olid/OL26415696W-L.jpg?default=false", images[0].Url);
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
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                Assert.Contains(expectedUris, expected => expected == request.RequestUri);
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
