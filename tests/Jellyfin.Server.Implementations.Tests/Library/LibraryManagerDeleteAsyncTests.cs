using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using Emby.Server.Implementations.Library;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Library;

public class LibraryManagerDeleteAsyncTests
{
    private readonly Emby.Server.Implementations.Library.LibraryManager _libraryManager;
    private readonly Mock<IChannelManager> _channelManagerMock;
    private readonly Mock<IItemPersistenceService> _persistenceServiceMock;
    private readonly Mock<ILibraryManager> _baseLibraryManagerMock;

    public LibraryManagerDeleteAsyncTests()
    {
        var fixture = new Fixture().Customize(new AutoMoqCustomization());

        var configMock = fixture.Freeze<Mock<IServerConfigurationManager>>();
        configMock.Setup(c => c.Configuration).Returns(new ServerConfiguration { CacheSize = 100 });

        var linkedChildrenServiceMock = fixture.Freeze<Mock<ILinkedChildrenService>>();
        linkedChildrenServiceMock
            .Setup(s => s.GetLinkedChildrenIds(It.IsAny<Guid>(), It.IsAny<int?>()))
            .Returns(Array.Empty<Guid>());

        _channelManagerMock = new Mock<IChannelManager>();
        _channelManagerMock.Setup(c => c.DeleteItem(It.IsAny<BaseItem>())).Returns(Task.CompletedTask);
        _channelManagerMock
            .Setup(c => c.GetStaticMediaSources(It.IsAny<BaseItem>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<MediaSourceInfo>());

        _persistenceServiceMock = fixture.Freeze<Mock<IItemPersistenceService>>();
        _baseLibraryManagerMock = new Mock<ILibraryManager>();

        _libraryManager = fixture.Build<Emby.Server.Implementations.Library.LibraryManager>()
            .Do(s => s.AddParts(
                fixture.Create<IEnumerable<IResolverIgnoreRule>>(),
                Enumerable.Empty<IItemResolver>(),
                fixture.Create<IEnumerable<IIntroProvider>>(),
                fixture.Create<IEnumerable<IBaseItemComparer>>(),
                fixture.Create<IEnumerable<ILibraryPostScanTask>>()))
            .Create();

        BaseItem.ChannelManager = _channelManagerMock.Object;
        var recordingsManagerMock = new Mock<IRecordingsManager>();
        recordingsManagerMock.Setup(r => r.GetActiveRecordingInfo(It.IsAny<string>())).Returns(() => null);
        Video.RecordingsManager = recordingsManagerMock.Object;

        configMock
            .Setup(c => c.ApplicationPaths.ProgramDataPath)
            .Returns("C:\\data");
        configMock
            .Setup(c => c.ApplicationPaths.InternalMetadataPath)
            .Returns("C:\\data\\metadata");

        _baseLibraryManagerMock
            .Setup(m => m.GetLinkedAlternateVersions(It.IsAny<Video>()))
            .Returns(Enumerable.Empty<Video>());

        var mediaSegmentManagerMock = new Mock<IMediaSegmentManager>();
        mediaSegmentManagerMock.Setup(m => m.IsTypeSupported(It.IsAny<BaseItem>())).Returns(false);
        mediaSegmentManagerMock.Setup(m => m.HasSegments(It.IsAny<Guid>())).Returns(false);

        BaseItem.ConfigurationManager = configMock.Object;
        BaseItem.FileSystem ??= fixture.Create<IFileSystem>();
        BaseItem.MediaSourceManager ??= fixture.Create<IMediaSourceManager>();
        BaseItem.MediaSegmentManager = mediaSegmentManagerMock.Object;
        BaseItem.LibraryManager = _baseLibraryManagerMock.Object;
    }

    [Fact]
    public async Task DeleteItemAsync_WithChannelSourceType_CallsChannelManagerDeleteItem()
    {
        var item = new Video
        {
            ChannelId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            Name = "Test Channel Item"
        };

        var parent = new Folder { Id = Guid.NewGuid() };

        await _libraryManager.DeleteItemAsync(
            item,
            new DeleteOptions { DeleteFromExternalProvider = true },
            parent,
            false);

        _channelManagerMock.Verify(c => c.DeleteItem(item), Times.Once);
    }

    [Fact]
    public async Task DeleteItemAsync_DelegatesCorrectNumberOfParams()
    {
        var parent = new Folder { Id = Guid.NewGuid() };
        var item = new Video
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            Name = "Test Item"
        };

        _baseLibraryManagerMock
            .Setup(m => m.GetItemById(parent.Id))
            .Returns(parent);

        await _libraryManager.DeleteItemAsync(item, new DeleteOptions());

        _persistenceServiceMock.Verify(
            p => p.DeleteItem(It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0].Equals(item.Id))),
            Times.Once);
    }
}
