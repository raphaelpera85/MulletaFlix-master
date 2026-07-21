using System;
using System.IO;
using System.Reflection;
using MediaBrowser.Providers.Plugins.MidiaStorageOnline;
using MediaBrowser.Providers.Plugins.MidiaStorageOnline.ScheduledTasks;
using MulletaFlix.Database.Implementations.Entities;
using Xunit;

namespace MulletaFlix.Providers.Tests.Plugins.MidiaStorageOnline;

public class MidiaStorageOnlineSyncTaskTests
{
    [Fact]
    public void BuildMovieRelativePath_UsesOriginalFileName_WhenDownloadMode()
    {
        var entry = CreateEntry(
            name: "#Alive (2020)",
            url: "http://po17.eu:80/movie/5513988112/27330511464/52263.mkv");

        var path = InvokeBuildMovieRelativePath(entry, "download");

        Assert.Equal(Path.Combine("Filmes", "#Alive (2020)", "52263.mkv"), path);
    }

    [Fact]
    public void BuildMovieRelativePath_UsesStrmExtension_WhenStrmMode()
    {
        var entry = CreateEntry(
            name: "#Alive (2020)",
            url: "http://po17.eu:80/movie/5513988112/27330511464/52263.mkv");

        var path = InvokeBuildMovieRelativePath(entry, "strm");

        Assert.Equal(Path.Combine("Filmes", "#Alive (2020)", "#Alive (2020).strm"), path);
    }

    [Fact]
    public void BuildRecognitionMetadata_PersistsMovieIdentityFields()
    {
        var entry = CreateEntry(
            name: "#Alive (2020)",
            url: "http://po17.eu:80/movie/5513988112/27330511464/52263.mkv");

        var metadata = InvokeBuildRecognitionMetadata(entry, Path.Combine("Filmes", "#Alive (2020)", "52263.mkv"), "download");

        Assert.Equal("movie", metadata.ContentType);
        Assert.Equal("#Alive", metadata.Title);
        Assert.Equal(2020, metadata.Year);
        Assert.Equal("download", metadata.Mode);
        Assert.Equal("52263.mkv", metadata.OriginalFileName);
        Assert.Equal("Filmes" + Path.DirectorySeparatorChar + "#Alive (2020)" + Path.DirectorySeparatorChar + "52263.mkv", metadata.RelativePath);
        Assert.NotEmpty(metadata.SourceId);
    }

    private static object CreateEntry(string name, string url)
    {
        var entryType = typeof(MidiaStorageOnlineSyncTask).GetNestedType("M3uEntry", BindingFlags.NonPublic);
        if (entryType is null)
        {
            throw new InvalidOperationException("M3uEntry type not found.");
        }

        var entry = Activator.CreateInstance(entryType, nonPublic: true)
            ?? throw new InvalidOperationException("Could not create M3uEntry.");

        entryType.GetProperty("Name")!.SetValue(entry, name);
        entryType.GetProperty("Url")!.SetValue(entry, url);
        entryType.GetProperty("Type")!.SetValue(entry, "Filme");
        return entry;
    }

    private static string InvokeBuildMovieRelativePath(object entry, string outputMode)
    {
        var method = typeof(MidiaStorageOnlineSyncTask).GetMethod(
            "BuildMovieRelativePath",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method is null)
        {
            throw new InvalidOperationException("BuildMovieRelativePath not found.");
        }

        return (string)(method.Invoke(null, new[] { entry, 96, 120, outputMode }) ?? throw new InvalidOperationException("No path returned."));
    }

    private static MidiaStorageOnlineMediaMetadata InvokeBuildRecognitionMetadata(object entry, string relativePath, string outputMode)
    {
        var method = typeof(MidiaStorageOnlineSyncTask).GetMethod(
            "BuildRecognitionMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method is null)
        {
            throw new InvalidOperationException("BuildRecognitionMetadata not found.");
        }

        return (MidiaStorageOnlineMediaMetadata)(method.Invoke(null, new[] { entry, relativePath, outputMode }) ?? throw new InvalidOperationException("No metadata returned."));
    }
}
