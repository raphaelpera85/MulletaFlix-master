using System.Collections.Generic;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Providers.MediaInfo;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MulletaFlix.Providers.Tests.MediaInfo;

public class LyricResolverTests
{
    private readonly LyricResolver _lyricResolver;

    public LyricResolverTests()
    {
        Video.RecordingsManager = Mock.Of<IRecordingsManager>();

        var applicationPaths = new Mock<IServerApplicationPaths>().Object;
        var serverConfig = new Mock<IServerConfigurationManager>();
        serverConfig.Setup(c => c.ApplicationPaths)
            .Returns(applicationPaths);
        BaseItem.ConfigurationManager = serverConfig.Object;

        var mediaEncoder = new Mock<IMediaEncoder>(MockBehavior.Strict);
        mediaEncoder.Setup(me => me.GetMediaInfo(It.IsAny<MediaInfoRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new MediaBrowser.Model.MediaInfo.MediaInfo
            {
                MediaStreams = new List<MediaStream>
                {
                    new()
                    {
                        Type = MediaStreamType.Lyric
                    }
                }
            });

        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsRegex(MediaInfoResolverTests.VideoDirectoryRegex)))
            .Returns(true);
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsRegex(MediaInfoResolverTests.MetadataDirectoryRegex)))
            .Returns(true);

        _lyricResolver = new LyricResolver(Mock.Of<ILogger<LyricResolver>>(), Mock.Of<ILocalizationManager>(), mediaEncoder.Object, fileSystem.Object, new Emby.Naming.Common.NamingOptions());
    }

    [Theory]
    [InlineData("My.Video.lrc", true)]
    [InlineData("My.Video.mp3", false)]
    [InlineData("My.Video.txt", true)]
    public async System.Threading.Tasks.Task GetExternalStreams_MixedFilenames_PicksLyrics(string file, bool matches)
    {
        BaseItem.MediaSourceManager = Mock.Of<IMediaSourceManager>();

        var audio = new MediaBrowser.Controller.Entities.Audio.Audio
        {
            Path = MediaInfoResolverTests.VideoDirectoryPath + "/My.Video.mp3"
        };

        var directoryService = MediaInfoResolverTests.GetDirectoryServiceForExternalFile(file);
        var streams = _lyricResolver.GetExternalStreams(audio, 0, directoryService, false);

        if (matches)
        {
            Assert.Single(streams);
            Assert.Equal(MediaStreamType.Lyric, streams[0].Type);
            Assert.Equal(MediaInfoResolverTests.VideoDirectoryPath + "/" + file, streams[0].Path);
        }
        else
        {
            Assert.Empty(streams);
        }
    }
}
