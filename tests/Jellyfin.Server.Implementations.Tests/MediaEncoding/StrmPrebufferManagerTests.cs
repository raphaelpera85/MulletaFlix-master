using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.MediaEncoding;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Branding;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.MediaEncoding;

public class StrmPrebufferManagerTests
{
    [Fact]
    public async Task CopyToAsync_Should_KeepPrebufferAvailable_ForRepeatedReads()
    {
        var tempStrm = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.strm");
        await File.WriteAllTextAsync(tempStrm, "https://example.test/video.mp4");

        try
        {
            var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("abcdef", System.Text.Encoding.UTF8, "video/mp4")
                });

            var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));

            var configurationManager = new Mock<IServerConfigurationManager>();
            configurationManager.Setup(x => x.GetConfiguration("branding")).Returns(new BrandingOptions
            {
                PrebufferEnabled = true,
                PrebufferSizeMb = 1
            });

            var applicationHost = new Mock<IServerApplicationHost>();
            applicationHost.Setup(x => x.GetSmartApiUrl("localhost")).Returns("http://localhost");

            var manager = new StrmPrebufferManager(
                httpClientFactory.Object,
                configurationManager.Object,
                applicationHost.Object,
                NullLogger<StrmPrebufferManager>.Instance);

            var item = new Video
            {
                Id = Guid.NewGuid(),
                Path = tempStrm
            };

            await manager.PrepareAsync(item);

            Assert.True(manager.TryGetProxyUrl(item.Id, out var proxyUrl));
            Assert.Equal("http://localhost/Videos/" + item.Id.ToString("N") + "/Prebuffer", proxyUrl);

            await using (var first = new MemoryStream())
            {
                await manager.CopyToAsync(item.Id, first, CancellationToken.None);
                Assert.Equal("abcdef", System.Text.Encoding.UTF8.GetString(first.ToArray()));
            }

            await using (var second = new MemoryStream())
            {
                await manager.CopyToAsync(item.Id, second, CancellationToken.None);
                Assert.Equal("abcdef", System.Text.Encoding.UTF8.GetString(second.ToArray()));
            }
        }
        finally
        {
            File.Delete(tempStrm);
        }
    }
}
