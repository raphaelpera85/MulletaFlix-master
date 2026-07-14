using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MulletaFlix.Api.Constants;
using MulletaFlix.Api.Controllers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MulletaFlix.Api.Tests.Controllers;

public class StrmPrebufferControllerTests
{
    [Fact]
    public async Task Get_WhenItemIsNotAccessible_ReturnsNotFound()
    {
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(m => m.GetItemById<BaseItem>(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .Returns((BaseItem?)null);

        var prebufferManager = new Mock<IStrmPrebufferManager>();
        var controller = new StrmPrebufferController(libraryManager.Object, prebufferManager.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            new[]
                            {
                                new Claim(InternalClaimTypes.UserId, Guid.NewGuid().ToString("N"))
                            },
                            "test"))
                }
            }
        };

        var result = await controller.Get(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
        prebufferManager.Verify(
            m => m.CopyToAsync(
                It.IsAny<Guid>(),
                It.IsAny<System.IO.Stream>(),
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Never);
    }
}
