using System;
using System.Threading.Tasks;
using MulletaFlix.Api.Controllers;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MulletaFlix.Api.Tests.Controllers;

public class DevicesControllerTests
{
    private readonly DevicesController _subject;
    private readonly Mock<IDeviceManager> _mockDeviceManager;
    private readonly Mock<ISessionManager> _mockSessionManager;

    public DevicesControllerTests()
    {
        _mockDeviceManager = new Mock<IDeviceManager>();
        _mockSessionManager = new Mock<ISessionManager>();

        _subject = new DevicesController(
            _mockDeviceManager.Object,
            _mockSessionManager.Object);
    }

    [Fact]
    public void GetDevices_WhenUserIdNotProvided_ReturnsAllDevices()
    {
        var expected = new QueryResult<DeviceInfoDto>(Array.Empty<DeviceInfoDto>());

        _mockDeviceManager
            .Setup(m => m.GetDevicesForUser(null))
            .Returns(expected);

        var result = _subject.GetDevices(null);

        var okResult = Assert.IsType<ActionResult<QueryResult<DeviceInfoDto>>>(result);
        Assert.Same(expected, okResult.Value);
        _mockDeviceManager.Verify(m => m.GetDevicesForUser(null), Times.Once);
    }
}
