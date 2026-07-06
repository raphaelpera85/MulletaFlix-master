using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Updates;

namespace Emby.Server.Implementations.Plugins;

internal static class BootstrapPluginCatalog
{
    internal static readonly RepositoryInfo[] DefaultPluginRepositories =
    [
        new RepositoryInfo
        {
            Name = "Jellyfin Plugin Manifest",
            Url = "https://raw.githubusercontent.com/danieladov/JellyfinPluginManifest/master/manifest.json",
            Enabled = true
        },
        new RepositoryInfo
        {
            Name = "Jellyfin Enhanced Plugins",
            Url = "https://raw.githubusercontent.com/n00bcodr/jellyfin-plugins/main/10.11/manifest.json",
            Enabled = true
        },
        new RepositoryInfo
        {
            Name = "IAmParadox Plugins",
            Url = "https://www.iamparadox.dev/jellyfin/plugins/manifest.json",
            Enabled = true
        }
    ];

    internal static readonly BootstrapRepositorySpec[] BootstrapRepositories =
    [
        new(
            "https://raw.githubusercontent.com/danieladov/JellyfinPluginManifest/master/manifest.json",
            [
                Guid.Parse("f21bbed8-3a97-4d8b-88b2-48aaa65427cb"),
                Guid.Parse("afe1de9c-63e4-4692-8d8c-7c964df19eb2")
            ]),
        new(
            "https://raw.githubusercontent.com/n00bcodr/jellyfin-plugins/main/10.11/manifest.json",
            []),
        new(
            "https://www.iamparadox.dev/jellyfin/plugins/manifest.json",
            [
                Guid.Parse("b8298e01-2697-407a-b44d-aa8dc795e850"),
                Guid.Parse("fbacd0b6-fd46-4a05-b0a4-2045d6a135b0"),
                Guid.Parse("08f615ea-2107-4f04-89cc-091035f54448"),
                Guid.Parse("043b2c48-b3e0-4610-b398-8217b146d1a4"),
                Guid.Parse("5e87cc92-571a-4d8d-8d98-d2d4147f9f90"),
                Guid.Parse("5b6550fa-a014-4f4c-8a2c-59a43680ac6d")
            ])
    ];

    internal sealed record BootstrapRepositorySpec(string Url, Guid[] PluginIds);

    internal static IEnumerable<string> GetManifestCandidates(string repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            yield break;
        }

        yield return repositoryUrl;

        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
        {
            yield break;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            yield break;
        }

        var owner = segments[0];
        var repository = segments[1];
        foreach (var branch in new[] { "main", "master" })
        {
            yield return $"https://raw.githubusercontent.com/{owner}/{repository}/{branch}/manifest.json";
            yield return $"https://raw.githubusercontent.com/{owner}/{repository}/{branch}/repository.json";
        }
    }

    internal static bool MatchesRepositoryUrl(string configuredUrl, string bootstrapUrl)
    {
        return GetManifestCandidates(configuredUrl)
            .Any(candidate => string.Equals(
                candidate.TrimEnd('/'),
                bootstrapUrl.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase));
    }
}
