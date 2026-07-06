using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Moq;
using Moq.Protected;
using Xunit;

namespace MediaBrowser.Providers.Plugins.Omdb;

public sealed class OmdbItemProviderTests
{
    [Fact]
    public async Task GetSearchResults_MovieWithoutImdbId_UsesImdbTitleSearch()
    {
        await AssertSearchResolvesImdbIdAsync(
            new MovieInfo
            {
                Name = "The Matrix",
                Year = 1999
            },
            "feature",
            "The Matrix 1999",
            "tt0133093");
    }

    [Fact]
    public async Task GetSearchResults_SeriesWithoutImdbId_UsesTvSeriesSearch()
    {
        await AssertSearchResolvesImdbIdAsync(
            new SeriesInfo
            {
                Name = "Breaking Bad",
                Year = 2008
            },
            "tv_series",
            "Breaking Bad 2008",
            "tt0903747");
    }

    [Fact]
    public async Task GetSearchResults_EpisodeWithoutSeriesImdbId_UsesTvEpisodeSearch()
    {
        await AssertSearchResolvesImdbIdAsync(
            new EpisodeInfo
            {
                Name = "Pilot",
                Year = 2008
            },
            "tv_episode",
            "Pilot 2008",
            "tt0959621");
    }

    private static async Task AssertSearchResolvesImdbIdAsync<T>(
        T lookupInfo,
        string expectedTitleType,
        string expectedSearchTitle,
        string expectedImdbId)
        where T : ItemLookupInfo
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");

                if (uri.Host.Contains("imdb.com", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.True(uri.AbsolutePath.Contains("/search/title/", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains($"title={WebUtility.UrlEncode(expectedSearchTitle)}", uri.Query, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains($"title_type={expectedTitleType}", uri.Query, StringComparison.OrdinalIgnoreCase);

                    return Task.FromResult(new HttpResponseMessage
                    {
                        StatusCode = System.Net.HttpStatusCode.OK,
                        Content = new StringContent($"<html><body><a href=\"/title/{expectedImdbId}/\">Match</a></body></html>")
                    });
                }

                Assert.True(uri.Query.Contains("plot=full", StringComparison.OrdinalIgnoreCase));
                Assert.True(uri.Query.Contains($"i={expectedImdbId}", StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent($"{{\"Title\":\"{lookupInfo.Name}\",\"Year\":\"{lookupInfo.Year}\",\"imdbID\":\"{expectedImdbId}\",\"Response\":\"True\"}}")
                });
            });

        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler.Object));

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager.Setup(x => x.ParseName(lookupInfo.Name))
            .Returns(new ItemLookupInfo { Name = lookupInfo.Name, Year = lookupInfo.Year });

        var provider = new OmdbItemProvider(
            httpClientFactory.Object,
            libraryManager.Object,
            Mock.Of<IFileSystem>(),
            Mock.Of<IServerConfigurationManager>());

        var results = lookupInfo switch
        {
            MovieInfo movieInfo => await provider.GetSearchResults(movieInfo, CancellationToken.None),
            SeriesInfo seriesInfo => await provider.GetSearchResults(seriesInfo, CancellationToken.None),
            EpisodeInfo episodeInfo => await provider.GetSearchResults(episodeInfo, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(lookupInfo))
        };
        var result = Assert.Single(results);

        Assert.Equal(expectedImdbId, result.GetProviderId(MetadataProvider.Imdb));
    }
}
