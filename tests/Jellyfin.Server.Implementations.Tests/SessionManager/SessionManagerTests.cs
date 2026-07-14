using System;
using System.Linq;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Entities.Security;
using MulletaFlix.Data.Dtos;
using MulletaFlix.Data.Queries;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.SessionManager;

public class SessionManagerTests
{
    [Theory]
    [InlineData("", typeof(ArgumentException))]
    [InlineData(null, typeof(ArgumentNullException))]
    public async Task GetAuthorizationToken_Should_ThrowException(string? deviceId, Type exceptionType)
    {
        await using var sessionManager = new Emby.Server.Implementations.Session.SessionManager(
            NullLogger<Emby.Server.Implementations.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            Mock.Of<IUserDataManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ILibraryManager>(),
            Mock.Of<IUserManager>(),
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IDeviceManager>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>());

        await Assert.ThrowsAsync(exceptionType, () => sessionManager.GetAuthorizationToken(
            new User("test", "default", "default"),
            deviceId,
            "app_name",
            "0.0.0",
            "device_name"));
    }

    [Theory]
    [MemberData(nameof(AuthenticateNewSessionInternal_Exception_TestData))]
    public async Task AuthenticateNewSessionInternal_Should_ThrowException(AuthenticationRequest authenticationRequest, Type exceptionType)
    {
        await using var sessionManager = new Emby.Server.Implementations.Session.SessionManager(
            NullLogger<Emby.Server.Implementations.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            Mock.Of<IUserDataManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ILibraryManager>(),
            Mock.Of<IUserManager>(),
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IDeviceManager>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>());

        await Assert.ThrowsAsync(exceptionType, () => sessionManager.AuthenticateNewSessionInternal(authenticationRequest, false));
    }

    [Fact]
    public async Task AuthenticateNewSessionInternal_Should_KeepSessions_Isolated_ForDifferentUsers_OnSameDevice()
    {
        var userOne = new User("user-one", "default", "default");
        var userTwo = new User("user-two", "default", "default");
        var userManager = new Mock<IUserManager>();
        userManager.Setup(x => x.GetUserByName("user-one")).Returns(userOne);
        userManager.Setup(x => x.GetUserByName("user-two")).Returns(userTwo);
        userManager.Setup(x => x.GetUserDto(It.IsAny<User>(), It.IsAny<string?>())).Returns(new MediaBrowser.Model.Dto.UserDto());

        var deviceManager = new Mock<IDeviceManager>();
        deviceManager.Setup(x => x.CanAccessDevice(It.IsAny<User>(), It.IsAny<string>())).Returns(true);
        deviceManager.Setup(x => x.GetDevices(It.IsAny<DeviceQuery>())).Returns(new QueryResult<Device>
        {
            Items = Array.Empty<Device>(),
            TotalRecordCount = 0
        });
        deviceManager.Setup(x => x.CreateDevice(It.IsAny<Device>())).ReturnsAsync((Device device) => device);
        deviceManager.Setup(x => x.DeleteDevice(It.IsAny<Device>())).Returns(Task.CompletedTask);
        deviceManager.Setup(x => x.GetDeviceOptions(It.IsAny<string>())).Returns((DeviceOptionsDto?)null);

        var eventManager = new Mock<IEventManager>();
        eventManager.Setup(x => x.PublishAsync(It.IsAny<AuthenticationRequestEventArgs>())).Returns(Task.CompletedTask);
        eventManager.Setup(x => x.PublishAsync(It.IsAny<AuthenticationResultEventArgs>())).Returns(Task.CompletedTask);

        await using var sessionManager = new Emby.Server.Implementations.Session.SessionManager(
            NullLogger<Emby.Server.Implementations.Session.SessionManager>.Instance,
            eventManager.Object,
            Mock.Of<IUserDataManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ILibraryManager>(),
            userManager.Object,
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(x => x.SystemId == "test-server"),
            deviceManager.Object,
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>());

        var requestOne = new AuthenticationRequest
        {
            App = "android-tv",
            AppVersion = "1.0.0",
            DeviceId = "shared-device-id",
            DeviceName = "Android TV",
            Username = "user-one"
        };

        var requestTwo = new AuthenticationRequest
        {
            App = "web-browser",
            AppVersion = "1.0.0",
            DeviceId = "shared-device-id",
            DeviceName = "Mobile Browser",
            Username = "user-two"
        };

        var resultOne = await sessionManager.AuthenticateNewSessionInternal(requestOne, false);
        var resultTwo = await sessionManager.AuthenticateNewSessionInternal(requestTwo, false);

        Assert.NotEqual(resultOne.SessionInfo.Id, resultTwo.SessionInfo.Id);
        Assert.Equal(2, sessionManager.Sessions.Count());
        Assert.Contains(sessionManager.Sessions, x => x.UserId == userOne.Id);
        Assert.Contains(sessionManager.Sessions, x => x.UserId == userTwo.Id);

        await sessionManager.Logout(new Device(userOne.Id, requestOne.App, requestOne.AppVersion, requestOne.DeviceName, requestOne.DeviceId)
        {
            AccessToken = resultOne.AccessToken
        });

        Assert.Single(sessionManager.Sessions);
        Assert.Equal(userTwo.Id, sessionManager.Sessions.Single().UserId);
    }

    public static TheoryData<AuthenticationRequest, Type> AuthenticateNewSessionInternal_Exception_TestData()
    {
        var data = new TheoryData<AuthenticationRequest, Type>
        {
            {
                new AuthenticationRequest { App = string.Empty, DeviceId = "device_id", DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = null, DeviceId = "device_id", DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentNullException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = string.Empty, DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = null, DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentNullException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = string.Empty, AppVersion = "app_version" },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = null, AppVersion = "app_version" },
                typeof(ArgumentNullException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = "device_name", AppVersion = string.Empty },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = "device_name", AppVersion = null },
                typeof(ArgumentNullException)
            }
        };

        return data;
    }
}
