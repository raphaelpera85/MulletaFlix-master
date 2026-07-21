#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.Plugins.MidiaStorageOnline;

internal static class MidiaStorageOnlineLinkValidator
{
    private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(15);
    private static readonly int _maxParallelism = Math.Max(4, Environment.ProcessorCount * 2);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    internal static async Task<IReadOnlyList<T>> FilterOnlineEntriesAsync<T>(
        IReadOnlyList<T> entries,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken,
        int maxDegreeOfParallelism = 0,
        string? offlineCachePath = null)
        where T : IMidiaStorageOnlineM3uEntry
    {
        if (entries.Count == 0)
        {
            return entries;
        }

        var keep = new bool[entries.Count];
        var offlineCache = LoadOfflineCache(offlineCachePath);
        var offlineCacheChanged = 0;
        using var client = httpClientFactory.CreateClient();
        client.Timeout = _requestTimeout;
        var degreeOfParallelism = GetDegreeOfParallelism(maxDegreeOfParallelism);
        logger.LogInformation(
            "Midia Storage Online validacao configurada com paralelismo solicitado {RequestedConcurrency} e efetivo {EffectiveConcurrency}.",
            maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : _maxParallelism,
            degreeOfParallelism);

        logger.LogInformation(
            "Midia Storage Online validando {Total} links em uma passagem unica com paralelismo {Concurrency}.",
            entries.Count,
            degreeOfParallelism);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, entries.Count),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = degreeOfParallelism
            },
            async (index, ct) =>
            {
                var entry = entries[index];
                var normalizedUrl = NormalizeUrl(entry.Url);
                if (normalizedUrl is null)
                {
                    return;
                }

                if (offlineCache.ContainsKey(normalizedUrl))
                {
                    logger.LogInformation(
                        "Midia Storage Online pulou link ja marcado como offline: [{Type}] {Name} | {Url}",
                        entry.Type,
                        entry.Name,
                        entry.Url);
                    return;
                }

                if (await IsOnlineAsync(normalizedUrl, client, ct).ConfigureAwait(false))
                {
                    keep[index] = true;
                    return;
                }

                if (offlineCache.TryAdd(normalizedUrl, 0))
                {
                    Interlocked.Exchange(ref offlineCacheChanged, 1);
                }

                logger.LogInformation(
                    "Midia Storage Online removeu link offline: [{Type}] {Name} | {Url}",
                    entry.Type,
                    entry.Name,
                    entry.Url);
            }).ConfigureAwait(false);

        if (offlineCacheChanged != 0 && !string.IsNullOrWhiteSpace(offlineCachePath))
        {
            SaveOfflineCache(offlineCachePath, offlineCache);
        }

        var filtered = new List<T>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            if (keep[i])
            {
                filtered.Add(entries[i]);
            }
        }

        return filtered;
    }

    private static int GetDegreeOfParallelism(int requestedDegreeOfParallelism)
    {
        if (requestedDegreeOfParallelism > 0)
        {
            return Math.Min(requestedDegreeOfParallelism, _maxParallelism);
        }

        return _maxParallelism;
    }

    private static ConcurrentDictionary<string, byte> LoadOfflineCache(string? offlineCachePath)
    {
        if (string.IsNullOrWhiteSpace(offlineCachePath) || !File.Exists(offlineCachePath))
        {
            return new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(offlineCachePath);
            var cached = JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
            var dict = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            foreach (var url in cached.Where(url => !string.IsNullOrWhiteSpace(url)))
            {
                dict.TryAdd(url, 0);
            }

            return dict;
        }
        catch
        {
            return new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveOfflineCache(string offlineCachePath, ConcurrentDictionary<string, byte> offlineCache)
    {
        var directory = Path.GetDirectoryName(offlineCachePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(offlineCache.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase), _jsonOptions);
        File.WriteAllText(offlineCachePath, json);
    }

    private static string? NormalizeUrl(string? url)
    {
        return MidiaStorageOnlineStreamProxy.NormalizeAbsoluteHttpUrl(url);
    }

    private static async Task<bool> IsOnlineAsync(
        string normalizedUrl,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(normalizedUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var headRequest = CreateProbeRequest(uri, HttpMethod.Head);
            using var headResponse = await client
                .SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (headResponse.IsSuccessStatusCode)
            {
                return true;
            }

            if (headResponse.StatusCode is not System.Net.HttpStatusCode.MethodNotAllowed
                and not System.Net.HttpStatusCode.NotImplemented)
            {
                return false;
            }

            using var getRequest = CreateProbeRequest(uri, HttpMethod.Get);
            getRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-0");

            using var getResponse = await client
                .SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            return getResponse.IsSuccessStatusCode || getResponse.StatusCode == System.Net.HttpStatusCode.PartialContent;
        }
        catch
        {
            return false;
        }
    }

    private static HttpRequestMessage CreateProbeRequest(Uri uri, HttpMethod method)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", MidiaStorageOnlineStreamProxy.GetBrowserUserAgent());
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.Referrer = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        request.Headers.TryAddWithoutValidation("Origin", uri.GetLeftPart(UriPartial.Authority));
        return request;
    }
}
