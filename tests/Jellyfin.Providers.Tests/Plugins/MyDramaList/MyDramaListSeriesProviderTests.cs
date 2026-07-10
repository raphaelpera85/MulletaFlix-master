using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Providers.Plugins.MyDramaList;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Providers.Tests.Plugins.MyDramaList
{
    public class MyDramaListSeriesProviderTests
    {
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
        private readonly Mock<ILogger<MyDramaListSeriesProvider>> _loggerMock;
        private readonly Mock<ILogger<MyDramaListSeriesImageProvider>> _imageLoggerMock;

        public MyDramaListSeriesProviderTests()
        {
            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _loggerMock = new Mock<ILogger<MyDramaListSeriesProvider>>();
            _imageLoggerMock = new Mock<ILogger<MyDramaListSeriesImageProvider>>();
        }

        [Fact]
        public async Task GetSearchResults_WithMockHtml_ReturnsDramas()
        {
            // Arrange
            var mockHtml = @"
<div class=""col-xs-3 row-cell film-cover cover"">
  <div class=""item"">
    <a class=""block"" href=""/772321-cang-zhu"">
      <img class=""img-responsive cover lazy"" data-src=""https://i.mydramalist.com/DkmXYy_4s.jpg"" alt=""Hidden Love""/>
    </a>
  </div>
</div>";

            var httpClient = CreateMockHttpClient(mockHtml);
            _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var provider = new MyDramaListSeriesProvider(_httpClientFactoryMock.Object, _loggerMock.Object);
            var searchInfo = new SeriesInfo { Name = "Hidden Love" };

            // Act
            var result = await provider.GetSearchResults(searchInfo, CancellationToken.None);

            // Assert
            var resultList = result.ToList();
            Assert.NotEmpty(resultList);
            Assert.Equal("Hidden Love", resultList.First().Name);
            Assert.Equal("772321-cang-zhu", resultList.First().GetProviderId("MyDramaList"));
            Assert.Equal("https://i.mydramalist.com/DkmXYy_4s.jpg", resultList.First().ImageUrl);
        }

        [Fact]
        public async Task GetMetadata_WithJsonLd_ReturnsParsedMetadata()
        {
            // Arrange
            var mockHtml = @"
<html>
<head>
<script type=""application/ld+json"">
{""@context"":""https://schema.org"",""@type"":""TVSeries"",""@id"":""https://mydramalist.com/772321-cang-zhu"",""url"":""https://mydramalist.com/772321-cang-zhu"",""name"":""Hidden Love"",""image"":""https://i.mydramalist.com/DkmXYy_4f.jpg"",""description"":""This is a story about hidden love."",""genre"":[""Historical"",""Romance""],""datePublished"":""2025-05-02"",""aggregateRating"":{""@type"":""AggregateRating"",""ratingValue"":7.3}}
</script>
</head>
</html>";

            var httpClient = CreateMockHttpClient(mockHtml);
            _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var provider = new MyDramaListSeriesProvider(_httpClientFactoryMock.Object, _loggerMock.Object);
            var info = new SeriesInfo
            {
                Name = "Hidden Love"
            };
            info.SetProviderId("MyDramaList", "772321-cang-zhu");

            // Act
            var result = await provider.GetMetadata(info, CancellationToken.None);

            // Assert
            Assert.True(result.HasMetadata);
            Assert.Equal("Hidden Love", result.Item.Name);
            Assert.Equal("This is a story about hidden love.", result.Item.Overview);
            Assert.Equal(7.3f, result.Item.CommunityRating);
            Assert.Equal(2025, result.Item.ProductionYear);
            Assert.Contains("Historical", result.Item.Genres);
            Assert.Contains("Romance", result.Item.Genres);
        }

        [Fact]
        public async Task GetImages_WithJsonLd_ReturnsCover()
        {
            // Arrange
            var mockHtml = @"
<html>
<head>
<script type=""application/ld+json"">
{""@context"":""https://schema.org"",""@type"":""TVSeries"",""image"":""https://i.mydramalist.com/DkmXYy_4f.jpg""}
</script>
</head>
</html>";

            var httpClient = CreateMockHttpClient(mockHtml);
            _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var provider = new MyDramaListSeriesImageProvider(_httpClientFactoryMock.Object, _imageLoggerMock.Object);
            var series = new Series();
            series.SetProviderId("MyDramaList", "772321-cang-zhu");

            // Act
            var result = await provider.GetImages(series, CancellationToken.None);

            // Assert
            var imagesList = result.ToList();
            Assert.Equal(2, imagesList.Count);

            var primaryImg = imagesList.FirstOrDefault(i => i.Type == ImageType.Primary);
            Assert.NotNull(primaryImg);
            Assert.Equal("https://i.mydramalist.com/DkmXYy_4f.jpg", primaryImg.Url);
        }

        private static HttpClient CreateMockHttpClient(string responseContent)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
                });

            return new HttpClient(handlerMock.Object);
        }
    }
}
