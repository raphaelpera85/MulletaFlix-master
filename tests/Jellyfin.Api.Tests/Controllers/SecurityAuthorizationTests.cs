using System;
using System.Reflection;
using MulletaFlix.Api.Controllers;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace MulletaFlix.Api.Tests.Controllers;

public class SecurityAuthorizationTests
{
    [Fact]
    public void LiveTvController_RequiresAuthorization()
    {
        AssertClassHasAuthorize(typeof(LiveTvController));
    }

    [Fact]
    public void VideoAttachmentsController_RequiresAuthorization()
    {
        AssertClassHasAuthorize(typeof(VideoAttachmentsController));
    }

    [Fact]
    public void StartupMutatingEndpoints_RequireLanAccess()
    {
        AssertMethodHasPolicy(typeof(StartupController), nameof(StartupController.CompleteWizard), Policies.AnonymousLanAccessPolicy);
        AssertMethodHasPolicy(typeof(StartupController), nameof(StartupController.UpdateInitialConfiguration), Policies.AnonymousLanAccessPolicy);
        AssertMethodHasPolicy(typeof(StartupController), nameof(StartupController.SetRemoteAccess), Policies.AnonymousLanAccessPolicy);
        AssertMethodHasPolicy(typeof(StartupController), nameof(StartupController.UpdateStartupUser), Policies.AnonymousLanAccessPolicy);
    }

    private static void AssertClassHasAuthorize(Type controllerType)
    {
        Assert.Contains(controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: true), _ => true);
    }

    private static void AssertMethodHasPolicy(Type controllerType, string methodName, string policy)
    {
        var method = controllerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.Contains(method!.GetCustomAttributes<AuthorizeAttribute>(inherit: true), attribute => attribute.Policy == policy);
    }
}
