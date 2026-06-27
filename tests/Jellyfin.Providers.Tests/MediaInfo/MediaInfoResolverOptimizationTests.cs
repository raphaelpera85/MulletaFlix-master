using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Providers.MediaInfo;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MulletaFlix.Providers.Tests.MediaInfo;

public class MediaInfoResolverOptimizationTests
{
    [Fact]
    public async Task GetExternalStreamsAsync_DeduplicatesAcrossDirectories_AndProbesEncoderOnce()
    {
        Video.RecordingsManager = Mock.Of<IRecordingsManager>();

        var applicationPaths = new Mock<IServerApplicationPaths>().Object;
        var serverConfig = new Mock<IServerConfigurationManager>();
        serverConfig.Setup(c => c.ApplicationPaths)
            .Returns(applicationPaths);
        BaseItem.ConfigurationManager = serverConfig.Object;

        var mediaSourceManager = new Mock<IMediaSourceManager>();
        mediaSourceManager.Setup(m => m.GetPathProtocol(It.IsAny<string>()))
            .Returns(MediaProtocol.File);
        BaseItem.MediaSourceManager = mediaSourceManager.Object;

        var video = new Movie
        {
            Path = MediaInfoResolverTests.VideoDirectoryPath + "/My.Video.mkv"
        };

        var matchingFile = MediaInfoResolverTests.VideoDirectoryPath + "/My.Video.en.srt";
        var unsupportedFile = MediaInfoResolverTests.VideoDirectoryPath + "/My.Video.txt";
        var wrongPrefixFile = MediaInfoResolverTests.VideoDirectoryPath + "/Other.Movie.en.srt";

        var directoryService = new Mock<IDirectoryService>(MockBehavior.Strict);
        directoryService.Setup(ds => ds.GetFilePaths(It.IsRegex(MediaInfoResolverTests.VideoDirectoryRegex), It.IsAny<bool>()))
            .Returns(new[] { matchingFile, unsupportedFile, wrongPrefixFile });
        directoryService.Setup(ds => ds.GetFilePaths(It.IsRegex(MediaInfoResolverTests.MetadataDirectoryRegex), It.IsAny<bool>()))
            .Returns(Array.Empty<string>());

        var mediaEncoder = new Mock<IMediaEncoder>(MockBehavior.Strict);
        mediaEncoder.Setup(me => me.GetMediaInfo(It.IsAny<MediaInfoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaBrowser.Model.MediaInfo.MediaInfo
            {
                MediaStreams = new List<MediaStream>
                {
                    new()
                    {
                        Type = MediaStreamType.Subtitle
                    }
                }
            });

        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsRegex(MediaInfoResolverTests.VideoDirectoryRegex)))
            .Returns(true);
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsRegex(MediaInfoResolverTests.MetadataDirectoryRegex)))
            .Returns(true);

        var resolver = new SubtitleResolver(
            Mock.Of<ILogger<SubtitleResolver>>(),
            Mock.Of<MediaBrowser.Model.Globalization.ILocalizationManager>(),
            mediaEncoder.Object,
            fileSystem.Object,
            new Emby.Naming.Common.NamingOptions());

        var streams = await resolver.GetExternalStreamsAsync(video, 0, directoryService.Object, false, CancellationToken.None);

        Assert.Single(streams);
        Assert.Equal(matchingFile, streams[0].Path);
        mediaEncoder.Verify(me => me.GetMediaInfo(It.IsAny<MediaInfoRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        directoryService.Verify(ds => ds.GetFilePaths(It.IsRegex(MediaInfoResolverTests.VideoDirectoryRegex), It.IsAny<bool>()), Times.Once);
        directoryService.Verify(ds => ds.GetFilePaths(It.IsRegex(MediaInfoResolverTests.MetadataDirectoryRegex), It.IsAny<bool>()), Times.Once);
    }
}
