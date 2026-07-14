using System;
using System.Reflection;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;
using Moq;
using MulletaFlix.Api.Helpers;
using Xunit;

namespace MulletaFlix.Api.Tests.Helpers;

public static class StreamingHelpersTests
{
    [Fact]
    public static void GetOutputFilePath_IsUniquePerUser()
    {
        var state = CreateState();
        state.MediaPath = @"C:\media\movie.mkv";
        state.UserAgent = "Mozilla/5.0";

        var configManager = new Mock<IServerConfigurationManager>();
        configManager.Setup(x => x.GetConfiguration("encoding")).Returns(new EncodingOptions());

        var appPaths = new Mock<IApplicationPaths>();
        appPaths.SetupGet(x => x.CachePath).Returns(@"C:\transcodes");
        appPaths.Setup(x => x.CreateAndCheckMarker(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()));
        configManager.SetupGet(x => x.CommonApplicationPaths).Returns(appPaths.Object);

        var method = typeof(StreamingHelpers).GetMethod(
            "GetOutputFilePath",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var first = Invoke(method!, state, configManager.Object, Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var second = Invoke(method!, state, configManager.Object, Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.NotEqual(first, second);
    }

    private static string Invoke(MethodInfo method, StreamState state, IServerConfigurationManager configManager, Guid userId)
        => (string)method.Invoke(null, new object[] { state, ".mp4", configManager, userId, "device-1", "play-1" })!;

    private static StreamState CreateState()
    {
        var mediaSourceManager = new Mock<IMediaSourceManager>().Object;
        var transcodeManager = new Mock<ITranscodeManager>().Object;
        return new StreamState(mediaSourceManager, TranscodingJobType.Hls, transcodeManager);
    }
}
