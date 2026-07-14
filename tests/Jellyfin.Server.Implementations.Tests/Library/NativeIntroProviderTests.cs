using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emby.Server.Implementations.Library;
using MulletaFlix.Database.Implementations.Entities;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Branding;
using Moq;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Library;

public class NativeIntroProviderTests
{
    [Theory]
    [InlineData(".strm")]
    [InlineData(".mkv")]
    public async Task GetIntros_ReturnsConfiguredIntro_ForStrmAndNativeVideo(string itemExtension)
    {
        var introPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(introPath, string.Empty);

        try
        {
            var configManager = new Mock<IServerConfigurationManager>();
            configManager.Setup(m => m.GetConfiguration("branding")).Returns(new BrandingOptions
            {
                IntroEnabled = true,
                IntroPath = introPath
            });

            var prebufferManager = new Mock<IStrmPrebufferManager>();
            prebufferManager.Setup(m => m.PrepareAsync(It.IsAny<BaseItem>())).Returns(Task.CompletedTask);

            var provider = new NativeIntroProvider(
                configManager.Object,
                prebufferManager.Object,
                Mock.Of<Microsoft.Extensions.Logging.ILogger<NativeIntroProvider>>());

            var item = new Video
            {
                Id = Guid.NewGuid(),
                Path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{itemExtension}")
            };
            var user = new User("test", "test", "test")
            {
                Id = Guid.NewGuid()
            };

            var intros = await provider.GetIntros(item, user);

            Assert.Single(intros);
            Assert.Equal(introPath, intros.First().Path);
            prebufferManager.Verify(m => m.PrepareAsync(It.IsAny<BaseItem>()), Times.Once);
        }
        finally
        {
            File.Delete(introPath);
        }
    }
}
