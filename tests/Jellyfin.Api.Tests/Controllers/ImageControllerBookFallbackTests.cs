using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Api.Controllers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MulletaFlix.Api.Tests.Controllers;

public class ImageControllerBookFallbackTests
{
    private readonly ImageController _subject;
    private readonly Mock<IProviderManager> _providerManager;

    public ImageControllerBookFallbackTests()
    {
        _providerManager = new Mock<IProviderManager>(MockBehavior.Strict);

        _subject = new ImageController(
            Mock.Of<IUserManager>(),
            Mock.Of<ILibraryManager>(),
            _providerManager.Object,
            Mock.Of<IImageProcessor>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ILogger<ImageController>>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<IApplicationPaths>());
    }

    [Fact]
    public async Task TryRecoverMissingBookPrimaryImageAsync_DownloadsRemoteCover()
    {
        var book = new Book { Name = "20 Mil Leguas Submarinas" };
        book.SetProviderId("OpenLibrary", "OL26415696W");

        var remoteImage = new RemoteImageInfo
        {
            Type = ImageType.Primary,
            Url = "https://covers.openlibrary.org/b/id/123-L.jpg"
        };

        _providerManager
            .Setup(pm => pm.GetAvailableRemoteImages(
                book,
                It.Is<RemoteImageQuery>(query =>
                    query.ImageType == ImageType.Primary &&
                    query.IncludeAllLanguages &&
                    query.IncludeDisabledProviders &&
                    query.ProviderName == string.Empty),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { remoteImage });

        _providerManager
            .Setup(pm => pm.SaveImage(
                book,
                remoteImage.Url,
                ImageType.Primary,
                0,
                It.IsAny<CancellationToken>()))
            .Callback<BaseItem, string, ImageType, int?, CancellationToken>((item, _, type, _, _) =>
            {
                item.AddImage(new ItemImageInfo
                {
                    Path = remoteImage.Url,
                    Type = type
                });
            })
            .Returns(Task.CompletedTask);

        var recovered = await _subject.TryRecoverMissingBookPrimaryImageAsync(book, ImageType.Primary, 0, CancellationToken.None);

        Assert.True(recovered);
        Assert.NotNull(book.GetImageInfo(ImageType.Primary, 0));
        Assert.Equal(remoteImage.Url, book.GetImagePath(ImageType.Primary, 0));

        _providerManager.VerifyAll();
    }
}
