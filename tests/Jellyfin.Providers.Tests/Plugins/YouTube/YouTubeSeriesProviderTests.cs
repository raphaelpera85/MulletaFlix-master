using System;
using System.Collections.Generic;
using System.IO;
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
using MediaBrowser.Providers.Plugins.YouTube;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Providers.Tests.Plugins.YouTube
{
    public class YouTubeSeriesProviderTests
    {
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
        private readonly Mock<ILogger<YouTubeSeriesProvider>> _loggerMock;
        private readonly Mock<ILogger<YouTubeSeriesImageProvider>> _imageLoggerMock;

        public YouTubeSeriesProviderTests()
        {
            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _loggerMock = new Mock<ILogger<YouTubeSeriesProvider>>();
            _imageLoggerMock = new Mock<ILogger<YouTubeSeriesImageProvider>>();
        }

        [Fact]
        public void ExtractIdFromPath_WithValidPlaylistId_ReturnsId()
        {
            // Arrange
            var path = @"D:\Media\TVShows\Minha Novela [youtube-PL12345]";

            // Act
            var dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var match = System.Text.RegularExpressions.Regex.Match(dirName, @"\[youtube-(PL[a-zA-Z0-9_-]+|UC[a-zA-Z0-9_-]{22})\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Assert
            Assert.True(match.Success);
            Assert.Equal("PL12345", match.Groups[1].Value);
        }

        [Fact]
        public async Task GetMetadata_WithPlaylistId_ReturnsParsedMetadata()
        {
            // Arrange
            var jsonResponse = @"{
                ""metadata"": {
                    ""playlistMetadataRenderer"": {
                        ""title"": ""Minha Novela Coreana"",
                        ""description"": ""Esta é uma novela de teste.""
                    }
                }
            }";

            var httpClient = CreateMockHttpClient(jsonResponse);
            _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var provider = new YouTubeSeriesProvider(_httpClientFactoryMock.Object, _loggerMock.Object);
            var info = new SeriesInfo
            {
                Name = "Minha Novela",
                Path = @"D:\Media\TVShows\Minha Novela [youtube-PL12345]"
            };

            // Act
            var result = await provider.GetMetadata(info, CancellationToken.None);

            // Assert
            Assert.True(result.HasMetadata);
            Assert.Equal("Minha Novela Coreana", result.Item.Name);
            Assert.Equal("Esta é uma novela de teste.", result.Item.Overview);
            Assert.Equal("PL12345", result.Item.GetProviderId("YouTube"));
        }

        [Fact]
        public async Task GetMetadata_WithStrmFileAndOpenGraph_ReturnsParsedMetadata()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var strmFile = Path.Combine(tempDir, "episode1.strm");
            File.WriteAllText(strmFile, "https://www.reelshort.com/show/12345");

            var htmlResponse = @"<html>
<head>
  <meta property=""og:title"" content=""ReelShort Test Show"" />
  <meta property=""og:description"" content=""This is a ReelShort test show description."" />
  <meta property=""og:image"" content=""https://images.reelshort.com/poster.jpg"" />
</head>
</html>";

            var httpClient = CreateMockHttpClient(htmlResponse);
            _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var provider = new YouTubeSeriesProvider(_httpClientFactoryMock.Object, _loggerMock.Object);
            var info = new SeriesInfo
            {
                Name = "Short Series",
                Path = tempDir
            };

            try
            {
                // Act
                var result = await provider.GetMetadata(info, CancellationToken.None);

                // Assert
                Assert.True(result.HasMetadata);
                Assert.Equal("ReelShort Test Show", result.Item.Name);
                Assert.Equal("This is a ReelShort test show description.", result.Item.Overview);
                Assert.Equal("https://www.reelshort.com/show/12345", result.Item.GetProviderId("YouTube"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task GetImages_WithChannelId_ReturnsAvatarAndBanner()
        {
            // Arrange
            var jsonResponse = @"{
                ""header"": {
                    ""c4TabbedHeaderRenderer"": {
                        ""avatar"": {
                            ""thumbnails"": [
                                { ""url"": ""https://yt3.ggpht.com/avatar_high.jpg"" }
                            ]
                        },
                        ""banner"": {
                            ""thumbnails"": [
                                { ""url"": ""https://yt3.ggpht.com/banner_high.jpg"" }
                            ]
                        }
                    }
                }
            }";

            var httpClient = CreateMockHttpClient(jsonResponse);
            _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var provider = new YouTubeSeriesImageProvider(_httpClientFactoryMock.Object, _imageLoggerMock.Object);
            var series = new Series
            {
                Path = @"D:\Media\TVShows\Canal Novelas [youtube-UC1234567890123456789012]"
            };
            series.SetProviderId("YouTube", "UC1234567890123456789012");

            // Act
            var result = await provider.GetImages(series, CancellationToken.None);

            // Assert
            var imagesList = result.ToList();
            Assert.Equal(2, imagesList.Count);

            var primaryImg = imagesList.FirstOrDefault(i => i.Type == ImageType.Primary);
            var backdropImg = imagesList.FirstOrDefault(i => i.Type == ImageType.Backdrop);

            Assert.NotNull(primaryImg);
            Assert.Equal("https://yt3.ggpht.com/avatar_high.jpg", primaryImg.Url);

            Assert.NotNull(backdropImg);
            Assert.Equal("https://yt3.ggpht.com/banner_high.jpg", backdropImg.Url);
        }

        [Fact]
        public async Task GetMetadata_WithStrmFilePointingToMediaUrl_ReturnsNoMetadata()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var strmFile = Path.Combine(tempDir, "episode1.strm");
            File.WriteAllText(strmFile, "http://po17.eu:80/series/5513988112/27330511464/72052.mkv");

            var provider = new YouTubeSeriesProvider(_httpClientFactoryMock.Object, _loggerMock.Object);
            var info = new SeriesInfo
            {
                Name = "Short Series",
                Path = tempDir
            };

            try
            {
                // Act
                var result = await provider.GetMetadata(info, CancellationToken.None);

                // Assert
                Assert.False(result.HasMetadata);
                Assert.Null(result.Item);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
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
