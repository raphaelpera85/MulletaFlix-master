using System.Collections.Generic;
using Emby.Naming.ExternalFiles;
using MediaBrowser.Providers.MediaInfo;
using Xunit;

namespace MulletaFlix.Providers.Tests.MediaInfo;

public class ProbeProviderTests
{
    [Fact]
    public void HasExternalFilesChanged_ReturnsFalse_WhenFilesMatchInDifferentOrder()
    {
        IReadOnlyList<string>? currentFiles = new[] { "C:\\Media\\movie.en.srt", "C:\\Media\\movie.fr.srt" };
        IReadOnlyList<ExternalPathParserResult> scannedFiles =
            [
                new ExternalPathParserResult("c:\\media\\movie.fr.srt"),
                new ExternalPathParserResult("c:\\media\\movie.en.srt")
            ];

        Assert.False(ProbeProvider.HasExternalFilesChanged(currentFiles, scannedFiles));
    }

    [Fact]
    public void HasExternalFilesChanged_ReturnsTrue_WhenCurrentFilesContainDuplicates()
    {
        IReadOnlyList<string>? currentFiles = new[] { "C:\\Media\\movie.en.srt", "c:\\media\\movie.en.srt" };
        IReadOnlyList<ExternalPathParserResult> scannedFiles =
            [
                new ExternalPathParserResult("C:\\Media\\movie.en.srt"),
                new ExternalPathParserResult("C:\\Media\\movie.en.srt")
            ];

        Assert.True(ProbeProvider.HasExternalFilesChanged(currentFiles, scannedFiles));
    }
}
