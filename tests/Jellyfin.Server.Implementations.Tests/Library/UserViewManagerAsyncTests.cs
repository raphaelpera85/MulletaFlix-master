using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Library;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Library;

public class UserViewManagerAsyncTests
{
    [Fact]
    public async Task GetUserViewsAsync_IncludesExternalContentWithoutBlocking()
    {
        var user = new User("Test User", "auth-provider", "reset-provider")
        {
            Id = Guid.NewGuid()
        };
        var childFolder = new TestFolder("Library Folder");
        var channelFolder = new Channel { Id = Guid.NewGuid(), Name = "Channel Folder", ForcedSortName = "Channel Folder" };
        var liveTvFolder = new Folder { Id = Guid.NewGuid(), Name = "Live TV Folder", ForcedSortName = "Live TV Folder" };
        var rootFolder = new TestRootFolder(childFolder);

        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetUserRootFolder()).Returns(rootFolder);
        libraryManager.Setup(x => x.Sort(It.IsAny<IEnumerable<BaseItem>>(), user, It.IsAny<IEnumerable<ItemSortBy>>(), SortOrder.Ascending))
            .Returns((IEnumerable<BaseItem> items, User? _, IEnumerable<ItemSortBy> _, SortOrder _) => items);

        var channelManager = new Mock<IChannelManager>();
        channelManager.Setup(x => x.GetChannelsInternalAsync(It.Is<ChannelQuery>(q => q.UserId.Equals(user.Id))))
            .ReturnsAsync(new QueryResult<Channel>(new[] { channelFolder }));

        var liveTvManager = new Mock<ILiveTvManager>();
        liveTvManager.Setup(x => x.GetEnabledUsers()).Returns([user]);
        liveTvManager.Setup(x => x.GetInternalLiveTvFolder(It.IsAny<CancellationToken>())).Returns(liveTvFolder);

        var localizationManager = new Mock<MediaBrowser.Model.Globalization.ILocalizationManager>();
        localizationManager.Setup(x => x.GetLocalizedString(It.IsAny<string>())).Returns<string>(phrase => phrase);

        var config = new Mock<IServerConfigurationManager>();
        config.Setup(x => x.Configuration).Returns(new ServerConfiguration { EnableFolderView = false });

        var manager = new UserViewManager(
            libraryManager.Object,
            localizationManager.Object,
            channelManager.Object,
            liveTvManager.Object,
            config.Object);

        var result = await manager.GetUserViewsAsync(new UserViewQuery
        {
            User = user,
            IncludeExternalContent = true,
            IncludeHidden = true
        });

        Assert.Equal(3, result.Length);
        Assert.Contains(result, folder => folder.Name == "Library Folder");
        Assert.Contains(result, folder => folder.Name == "Channel Folder");
        Assert.Contains(result, folder => folder.Name == "Live TV Folder");
    }

    [Fact]
    public async Task GetLatestItemsAsync_WhenParentIsChannel_UsesAsyncChannelQuery()
    {
        var user = new User("Test User", "auth-provider", "reset-provider")
        {
            Id = Guid.NewGuid()
        };
        var channelParent = new Channel { Id = Guid.NewGuid(), Name = "Channel Parent" };
        var firstItem = new Folder { Id = Guid.NewGuid(), Name = "First Item" };
        var secondItem = new Folder { Id = Guid.NewGuid(), Name = "Second Item" };

        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetItemById(channelParent.Id)).Returns(channelParent);

        var channelManager = new Mock<IChannelManager>();
        channelManager.Setup(x => x.GetLatestChannelItemsInternal(
                It.Is<InternalItemsQuery>(q => q.ChannelIds.SequenceEqual(new[] { channelParent.Id }) && q.User == user),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult<BaseItem>(new BaseItem[] { firstItem, secondItem }));

        var localizationManager = new Mock<MediaBrowser.Model.Globalization.ILocalizationManager>();
        localizationManager.Setup(x => x.GetLocalizedString(It.IsAny<string>())).Returns<string>(phrase => phrase);

        var manager = new UserViewManager(
            libraryManager.Object,
            localizationManager.Object,
            channelManager.Object,
            new Mock<ILiveTvManager>().Object,
            new Mock<IServerConfigurationManager>().Object);

        var result = await manager.GetLatestItemsAsync(
            new LatestItemsQuery
            {
                ParentId = channelParent.Id,
                User = user,
                Limit = 10,
                IncludeItemTypes = Array.Empty<BaseItemKind>()
            },
            new DtoOptions());

        Assert.Equal(2, result.Count);
        Assert.Equal(firstItem, result[0].Item2[0]);
        Assert.Equal(secondItem, result[1].Item2[0]);
    }

    [Fact]
    public async Task GetUserViewsAsync_WhenFolderIsUserSpecific_UsesAsyncNamedView()
    {
        var user = new User("Test User", "auth-provider", "reset-provider")
        {
            Id = Guid.NewGuid()
        };
        var childFolder = new UserSpecificCollectionFolder("Playlists");
        var userView = new UserView { Id = Guid.NewGuid(), Name = "Playlists", ForcedSortName = "Playlists" };
        var rootFolder = new TestRootFolder(childFolder);

        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetUserRootFolder()).Returns(rootFolder);
        libraryManager.Setup(x => x.Sort(It.IsAny<IEnumerable<BaseItem>>(), user, It.IsAny<IEnumerable<ItemSortBy>>(), SortOrder.Ascending))
            .Returns((IEnumerable<BaseItem> items, User? _, IEnumerable<ItemSortBy> _, SortOrder _) => items);
        libraryManager.Setup(x => x.GetNamedViewAsync(user, childFolder.Name, childFolder.Id, childFolder.CollectionType, It.IsAny<string>()))
            .ReturnsAsync(userView);
        libraryManager.Setup(x => x.GetNamedView(user, childFolder.Name, childFolder.Id, childFolder.CollectionType, It.IsAny<string>()))
            .Throws(new InvalidOperationException("Sync named view should not be used by GetUserViewsAsync"));

        var localizationManager = new Mock<MediaBrowser.Model.Globalization.ILocalizationManager>();
        localizationManager.Setup(x => x.GetLocalizedString(It.IsAny<string>())).Returns<string>(phrase => phrase);

        var config = new Mock<IServerConfigurationManager>();
        config.Setup(x => x.Configuration).Returns(new ServerConfiguration { EnableFolderView = false });

        var manager = new UserViewManager(
            libraryManager.Object,
            localizationManager.Object,
            new Mock<IChannelManager>().Object,
            new Mock<ILiveTvManager>().Object,
            config.Object);

        var result = await manager.GetUserViewsAsync(new UserViewQuery
        {
            User = user,
            IncludeExternalContent = false,
            IncludeHidden = true
        });

        Assert.Single(result);
        Assert.Equal(userView, result[0]);
        libraryManager.Verify(x => x.GetNamedViewAsync(user, childFolder.Name, childFolder.Id, childFolder.CollectionType, It.IsAny<string>()), Times.Once);
    }

    private sealed class TestRootFolder : Folder
    {
        private readonly IReadOnlyList<BaseItem> _children;

        public TestRootFolder(params BaseItem[] children)
        {
            _children = children;
        }

        public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery? query = null)
        {
            return _children;
        }
    }

    private sealed class TestFolder : Folder
    {
        public TestFolder(string name)
        {
            Name = name;
            ForcedSortName = name;
            Id = Guid.NewGuid();
        }
    }

    private sealed class UserSpecificCollectionFolder : Folder, ICollectionFolder, ISupportsUserSpecificView
    {
        public UserSpecificCollectionFolder(string name)
        {
            Name = name;
            ForcedSortName = name;
            Id = Guid.NewGuid();
        }

        public bool EnableUserSpecificView => true;

        public CollectionType? CollectionType => Jellyfin.Data.Enums.CollectionType.music;

        string[] ICollectionFolder.PhysicalLocations => Array.Empty<string>();
    }
}
