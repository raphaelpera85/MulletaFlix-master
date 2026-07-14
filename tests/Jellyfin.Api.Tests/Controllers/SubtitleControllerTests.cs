using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using MulletaFlix.Api.Constants;
using MulletaFlix.Api.Controllers;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MulletaFlix.Api.Tests.Controllers;

public class SubtitleControllerTests
{
    [Fact]
    public void SubtitleEndpoints_RequireAuthorization()
    {
        AssertMethodHasAuthorize(nameof(SubtitleController.GetSubtitle));
        AssertMethodHasAuthorize(nameof(SubtitleController.GetSubtitleWithTicks));
    }

    [Fact]
    public async Task GetSubtitle_WhenItemIsNotAccessible_ReturnsNotFound()
    {
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(m => m.GetItemById<Video>(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .Returns((Video?)null);

        var controller = CreateController(libraryManager.Object);

        var result = await controller.GetSubtitle(
            Guid.NewGuid(),
            "source",
            0,
            "srt",
            null,
            null,
            null,
            null,
            null,
            false,
            false,
            0);

        Assert.IsType<NotFoundResult>(result);
    }

    private static SubtitleController CreateController(ILibraryManager libraryManager)
    {
        return new SubtitleController(
            Mock.Of<IServerConfigurationManager>(),
            libraryManager,
            Mock.Of<ISubtitleManager>(),
            Mock.Of<ISubtitleEncoder>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IProviderManager>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ILogger<SubtitleController>>())
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
    }

    private static void AssertMethodHasAuthorize(string methodName)
    {
        var method = typeof(SubtitleController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.Contains(method!.GetCustomAttributes<AuthorizeAttribute>(inherit: true), _ => true);
    }
}
