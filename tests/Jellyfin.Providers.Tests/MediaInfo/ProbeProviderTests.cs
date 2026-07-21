using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Moq;
using MediaBrowser.Providers.MediaInfo;
using Xunit;

namespace MulletaFlix.Providers.Tests.MediaInfo;

public class ProbeProviderTests
{
    [Fact]
    public async Task FetchAsync_InvalidStrmShortcut_SkipsProbe()
    {
        var mediaEncoder = new Mock<IMediaEncoder>(MockBehavior.Strict);
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger>());

        BaseItem.MediaSourceManager = Mock.Of<IMediaSourceManager>();
        Video.RecordingsManager = Mock.Of<IRecordingsManager>();

        IFixture fixture = new Fixture().Customize(new AutoMoqCustomization { ConfigureMembers = true });
        fixture.Inject(mediaEncoder.Object);
        fixture.Inject(loggerFactory.Object);

        var provider = fixture.Create<ProbeProvider>();
        var strmPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.strm");

        try
        {
            await File.WriteAllTextAsync(strmPath, @"C:\Media\episode.mkv", TestContext.Current.CancellationToken);

            var result = await provider.FetchAsync(
                new Movie
                {
                    Path = strmPath,
                    IsShortcut = true
                },
                new MetadataRefreshOptions(Mock.Of<IDirectoryService>()),
                CancellationToken.None);

            Assert.Equal(ItemUpdateType.None, result);
        }
        finally
        {
            if (File.Exists(strmPath))
            {
                File.Delete(strmPath);
            }
        }
    }
}
