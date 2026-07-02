using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.XbmcMetadata;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace MulletaFlix.XbmcMetadata.Tests
{
    public sealed class NfoUserDataSaverTests
    {
        [Fact]
        public async Task UserDataSaved_DoesNotTriggerImmediateNfoWrite()
        {
            var configManager = new Mock<IConfigurationManager>();
            configManager.Setup(x => x.GetConfiguration("xbmcmetadata"))
                .Returns(new XbmcMetadataOptions
                {
                    UserId = Guid.NewGuid().ToString("D")
                });

            var userDataManager = new Mock<IUserDataManager>();
            var providerManager = new Mock<IProviderManager>(MockBehavior.Strict);

            var saver = new NfoUserDataSaver(
                NullLogger<NfoUserDataSaver>.Instance,
                configManager.Object,
                userDataManager.Object,
                providerManager.Object);

            await saver.StartAsync(CancellationToken.None);

            var item = new Folder
            {
                Name = "Test Folder"
            };

            userDataManager.Raise(
                x => x.UserDataSaved += null,
                new UserDataSaveEventArgs
                {
                    UserId = Guid.NewGuid(),
                    Item = item,
                    SaveReason = UserDataSaveReason.UpdateUserRating,
                    UserData = new UserItemData
                    {
                        Key = "test"
                    }
                });

            await saver.StopAsync(CancellationToken.None);
        }
    }
}
