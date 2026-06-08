using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Emby.Server.Implementations.Dto;
using MulletaFlix.Database.Implementations.Entities;
using MediaBrowser.Common;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Dto;

public class DtoServiceAsyncTests
{
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly Mock<ITrickplayManager> _trickplayManagerMock;
    private readonly DtoService _dtoService;

    public DtoServiceAsyncTests()
    {
        _libraryManagerMock = new Mock<ILibraryManager>();

        var imageProcessor = new Mock<IImageProcessor>();
        imageProcessor
            .Setup(x => x.GetImageCacheTag(It.IsAny<BaseItem>(), It.IsAny<ItemImageInfo>()))
            .Returns((BaseItem _, ItemImageInfo image) => "tag:" + image.Path);

        var appHost = new Mock<IApplicationHost>();
        appHost.Setup(x => x.SystemId).Returns("test-server");

        Video.RecordingsManager = new Mock<IRecordingsManager>().Object;
        _trickplayManagerMock = new Mock<ITrickplayManager>();

        _dtoService = new DtoService(
            NullLogger<DtoService>.Instance,
            _libraryManagerMock.Object,
            new Mock<IUserDataManager>().Object,
            imageProcessor.Object,
            new Mock<IProviderManager>().Object,
            new Mock<IRecordingsManager>().Object,
            appHost.Object,
            new Mock<IMediaSourceManager>().Object,
            new Lazy<ILiveTvManager>(() => new Mock<ILiveTvManager>().Object),
            _trickplayManagerMock.Object,
            new Mock<IChapterManager>().Object);

        BaseItem.LibraryManager = _libraryManagerMock.Object;
    }

    [Fact]
    public async Task GetBaseItemDtoAsync_PreferEpisodeParentPoster_PrefersSeasonPosterOverEpisodeAndSeries()
    {
        var (episode, season, series) = BuildEpisode(seasonHasPoster: true);
        var options = new DtoOptions(false) { PreferEpisodeParentPoster = true };

        var dto = await _dtoService.GetBaseItemDtoAsync(episode, options);

        Assert.False(dto.ImageTags is not null && dto.ImageTags.ContainsKey(ImageType.Primary));
        Assert.Null(dto.SeriesPrimaryImageTag);
        Assert.Equal(season.Id, dto.ParentPrimaryImageItemId);
        Assert.Equal("tag:" + season.GetImageInfo(ImageType.Primary, 0)!.Path, dto.ParentPrimaryImageTag);
        Assert.Equal(season.GetDefaultPrimaryImageAspectRatio(), dto.PrimaryImageAspectRatio);
    }

    [Fact]
    public async Task GetBaseItemDtoAsync_PreferEpisodeParentPoster_FallsBackToSeriesWhenSeasonHasNoPoster()
    {
        var (episode, _, series) = BuildEpisode(seasonHasPoster: false);
        var options = new DtoOptions(false) { PreferEpisodeParentPoster = true };

        var dto = await _dtoService.GetBaseItemDtoAsync(episode, options);

        Assert.False(dto.ImageTags is not null && dto.ImageTags.ContainsKey(ImageType.Primary));
        Assert.Null(dto.SeriesPrimaryImageTag);
        Assert.Equal(series.Id, dto.ParentPrimaryImageItemId);
        Assert.Equal("tag:" + series.GetImageInfo(ImageType.Primary, 0)!.Path, dto.ParentPrimaryImageTag);
    }

    [Fact]
    public async Task GetBaseItemDtoAsync_WithoutPreferEpisodeParentPoster_KeepsEpisodePrimary()
    {
        var (episode, _, _) = BuildEpisode(seasonHasPoster: true);
        var options = new DtoOptions(false);

        var dto = await _dtoService.GetBaseItemDtoAsync(episode, options);

        Assert.NotNull(dto.ImageTags);
        Assert.True(dto.ImageTags.ContainsKey(ImageType.Primary));
        Assert.NotNull(dto.SeriesPrimaryImageTag);
        Assert.Null(dto.ParentPrimaryImageItemId);
    }

    [Fact]
    public async Task GetBaseItemDtosAsync_PreferEpisodeParentPoster_PrefersSeasonPosterOverEpisodeAndSeries()
    {
        var (episode, season, series) = BuildEpisode(seasonHasPoster: true);
        var options = new DtoOptions(false) { PreferEpisodeParentPoster = true };

        var dtos = await _dtoService.GetBaseItemDtosAsync(new[] { episode }, options);

        var dto = dtos.Single();
        Assert.False(dto.ImageTags is not null && dto.ImageTags.ContainsKey(ImageType.Primary));
        Assert.Null(dto.SeriesPrimaryImageTag);
        Assert.Equal(season.Id, dto.ParentPrimaryImageItemId);
        Assert.Equal("tag:" + season.GetImageInfo(ImageType.Primary, 0)!.Path, dto.ParentPrimaryImageTag);
        Assert.Equal(season.GetDefaultPrimaryImageAspectRatio(), dto.PrimaryImageAspectRatio);
    }

    [Fact]
    public async Task GetBaseItemDtosAsync_PreferEpisodeParentPoster_FallsBackToSeriesWhenSeasonHasNoPoster()
    {
        var (episode, _, series) = BuildEpisode(seasonHasPoster: false);
        var options = new DtoOptions(false) { PreferEpisodeParentPoster = true };

        var dtos = await _dtoService.GetBaseItemDtosAsync(new[] { episode }, options);

        var dto = dtos.Single();
        Assert.False(dto.ImageTags is not null && dto.ImageTags.ContainsKey(ImageType.Primary));
        Assert.Null(dto.SeriesPrimaryImageTag);
        Assert.Equal(series.Id, dto.ParentPrimaryImageItemId);
        Assert.Equal("tag:" + series.GetImageInfo(ImageType.Primary, 0)!.Path, dto.ParentPrimaryImageTag);
    }

    [Fact]
    public async Task GetBaseItemDtosAsync_WithoutPreferEpisodeParentPoster_KeepsEpisodePrimary()
    {
        var (episode, _, _) = BuildEpisode(seasonHasPoster: true);
        var options = new DtoOptions(false);

        var dtos = await _dtoService.GetBaseItemDtosAsync(new[] { episode }, options);

        var dto = dtos.Single();
        Assert.NotNull(dto.ImageTags);
        Assert.True(dto.ImageTags.ContainsKey(ImageType.Primary));
        Assert.NotNull(dto.SeriesPrimaryImageTag);
        Assert.Null(dto.ParentPrimaryImageItemId);
    }

    [Fact]
    public async Task GetBaseItemDtoAsync_WithTrickplayField_LoadsManifestAsynchronously()
    {
        var video = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        var options = new DtoOptions(false) { Fields = [ItemFields.Trickplay] };
        _trickplayManagerMock
            .Setup(x => x.GetTrickplayManifest(video))
            .ReturnsAsync(new Dictionary<string, Dictionary<int, TrickplayInfo>>
            {
                ["main"] = new()
                {
                    [320] = new TrickplayInfo
                    {
                        ItemId = video.Id,
                        Width = 320,
                        Height = 180,
                        TileWidth = 10,
                        TileHeight = 10,
                        ThumbnailCount = 100,
                        Interval = 10000,
                        Bandwidth = 12345
                    }
                }
            });

        var dto = await _dtoService.GetBaseItemDtoAsync(video, options);

        Assert.NotNull(dto.Trickplay);
        Assert.True(dto.Trickplay.ContainsKey("main"));
        Assert.True(dto.Trickplay["main"].ContainsKey(320));
        _trickplayManagerMock.Verify(x => x.GetTrickplayManifest(video), Times.Once);
    }

    private (Episode Episode, Season Season, Series Series) BuildEpisode(bool seasonHasPoster)
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Series" };
        series.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = "http://test/series.jpg" }, 0);

        var season = new Season { Id = Guid.NewGuid(), Name = "Season", SeriesId = series.Id };
        if (seasonHasPoster)
        {
            season.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = "http://test/season.jpg" }, 0);
        }

        var episode = new Episode
        {
            Id = Guid.NewGuid(),
            Name = "Episode",
            SeasonId = season.Id,
            SeriesId = series.Id
        };
        episode.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = "http://test/episode.jpg" }, 0);

        _libraryManagerMock.Setup(x => x.GetItemById(season.Id)).Returns(season);
        _libraryManagerMock.Setup(x => x.GetItemById(series.Id)).Returns(series);

        return (episode, season, series);
    }
}

