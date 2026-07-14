#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.Plugins.MidiaStorageOnline;

internal static class MidiaStorageOnlineWorldStorageDownloader
{
    private const string RepoOwner = "Ramys";
    private const string RepoName = "Iptv-Brasil-2026";
    private const string GistUser = "sempreconceito";
    private static readonly string[] PlaylistExtensions = [".m3u", ".m3u8"];

    internal static async Task<string> DownloadCombinedPlaylistAsync(IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken)
    {
        var sources = new List<(string Name, string Url)>();
        sources.AddRange(await DiscoverRepositorySourcesAsync(httpClientFactory, cancellationToken).ConfigureAwait(false));
        sources.AddRange(await DiscoverGistSourcesAsync(httpClientFactory, cancellationToken).ConfigureAwait(false));

        var uniqueSources = sources
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (uniqueSources.Count == 0)
        {
            throw new InvalidOperationException("Nenhuma fonte M3U/M3U8 encontrada para storage mundial.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");

        foreach (var source in uniqueSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var content = await DownloadTextAsync(httpClientFactory, source.Url, cancellationToken).ConfigureAwait(false);
                AppendPlaylistContent(builder, content);
                logger.LogInformation("Storage mundial incluiu fonte: {Source}", source.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao baixar fonte de storage mundial {Source}", source.Name);
            }
        }

        if (builder.Length <= "#EXTM3U".Length + Environment.NewLine.Length)
        {
            throw new InvalidOperationException("Nenhuma lista M3U/M3U8 valida foi obtida do storage mundial.");
        }

        return builder.ToString();
    }

    private static async Task<List<(string Name, string Url)>> DiscoverRepositorySourcesAsync(IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
    {
        var repoInfo = await DownloadJsonAsync(httpClientFactory, $"https://api.github.com/repos/{RepoOwner}/{RepoName}", cancellationToken).ConfigureAwait(false);
        var defaultBranch = repoInfo.RootElement.GetProperty("default_branch").GetString() ?? "master";
        var treeDoc = await DownloadJsonAsync(httpClientFactory, $"https://api.github.com/repos/{RepoOwner}/{RepoName}/git/trees/{defaultBranch}?recursive=1", cancellationToken).ConfigureAwait(false);

        var sources = new List<(string Name, string Url)>();
        foreach (var entry in treeDoc.RootElement.GetProperty("tree").EnumerateArray())
        {
            if (!entry.TryGetProperty("path", out var pathProp))
            {
                continue;
            }

            var path = pathProp.GetString();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!PlaylistExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var rawPath = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            var rawUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{defaultBranch}/{rawPath}";
            sources.Add(($"{RepoName}/{path}", rawUrl));
        }

        return sources;
    }

    private static async Task<List<(string Name, string Url)>> DiscoverGistSourcesAsync(IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
    {
        var sources = new List<(string Name, string Url)>();
        for (var page = 1; page <= 10; page++)
        {
            var json = await DownloadJsonAsync(httpClientFactory, $"https://api.github.com/users/{GistUser}/gists?per_page=100&page={page}", cancellationToken).ConfigureAwait(false);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var gist in json.RootElement.EnumerateArray())
            {
                var gistId = gist.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(gistId))
                {
                    continue;
                }

                if (!gist.TryGetProperty("files", out var filesProp) || filesProp.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var file in filesProp.EnumerateObject())
                {
                    if (!PlaylistExtensions.Any(ext => file.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (!file.Value.TryGetProperty("raw_url", out var rawUrlProp))
                    {
                        continue;
                    }

                    var rawUrl = rawUrlProp.GetString();
                    if (string.IsNullOrWhiteSpace(rawUrl))
                    {
                        continue;
                    }

                    sources.Add(($"{GistUser}/{gistId}/{file.Name}", rawUrl));
                }
            }
        }

        return sources;
    }

    private static async Task<string> DownloadTextAsync(IHttpClientFactory httpClientFactory, string url, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; MidiaStorageOnline/1.0)");
        request.Headers.TryAddWithoutValidation("Accept", "text/plain, */*");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> DownloadJsonAsync(IHttpClientFactory httpClientFactory, string url, CancellationToken cancellationToken)
    {
        var text = await DownloadTextAsync(httpClientFactory, url, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(text);
    }

    private static void AppendPlaylistContent(StringBuilder builder, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var lines = content.Split('\n');
        var start = 0;
        while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        if (start < lines.Length && lines[start].StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
        {
            start++;
        }

        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            builder.AppendLine(line);
        }
    }
}
