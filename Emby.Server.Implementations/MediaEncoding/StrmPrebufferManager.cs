using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Branding;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.MediaEncoding;

public sealed class StrmPrebufferManager : IStrmPrebufferManager, IDisposable
{
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36";
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly IServerApplicationHost _applicationHost;
    private readonly ILogger<StrmPrebufferManager> _logger;

    public StrmPrebufferManager(
        IHttpClientFactory httpClientFactory,
        IServerConfigurationManager configurationManager,
        IServerApplicationHost applicationHost,
        ILogger<StrmPrebufferManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configurationManager = configurationManager;
        _applicationHost = applicationHost;
        _logger = logger;
    }

    public async Task PrepareAsync(BaseItem item)
    {
        var options = _configurationManager.GetConfiguration<BrandingOptions>("branding");
        if (!options.PrebufferEnabled || item.Path is null || !item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_sessions.ContainsKey(item.Id))
        {
            return;
        }

        string url;
        try
        {
            url = (await File.ReadAllTextAsync(item.Path).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read STRM item {ItemPath} for prebuffering", item.Path);
            return;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var upstreamUri)
            || (upstreamUri.Scheme != Uri.UriSchemeHttp && upstreamUri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var session = new Session(upstreamUri, Math.Clamp(options.PrebufferSizeMb, 1, 256) * 1024L * 1024L);
        if (!_sessions.TryAdd(item.Id, session))
        {
            session.Dispose();
            return;
        }

        _ = FillAsync(item.Id, session);
    }

    public bool TryGetProxyUrl(Guid itemId, out string url)
    {
        return TryGetProxyUrl(itemId, null, out url);
    }

    public bool TryGetProxyUrl(Guid itemId, string? apiKey, out string url)
    {
        if (_sessions.ContainsKey(itemId))
        {
            url = $"{_applicationHost.GetSmartApiUrl("localhost")}/Videos/{itemId:N}/Prebuffer";
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                url += $"?ApiKey={Uri.EscapeDataString(apiKey)}";
            }

            return true;
        }

        url = string.Empty;
        return false;
    }

    public string? GetContentType(Guid itemId)
    {
        return _sessions.TryGetValue(itemId, out var session) ? session.ContentType : null;
    }

    public async Task<(string ContentType, long? ContentLength)> CopyToAsync(Guid itemId, Stream output, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(itemId, out var session))
        {
            throw new FileNotFoundException("STRM prebuffer session not found.");
        }

        try
        {
            await session.Ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            await using (var prefix = new FileStream(session.BufferPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                await prefix.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            using var request = CreateUpstreamRequest(session.Uri);
            request.Headers.Range = new RangeHeaderValue(session.BufferedBytes, null);
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                return (session.ContentType ?? "application/octet-stream", session.BufferedBytes);
            }

            response.EnsureSuccessStatusCode();
            await using var upstream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                await SkipAsync(upstream, session.BufferedBytes, cancellationToken).ConfigureAwait(false);
            }

            await upstream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            return (session.ContentType ?? response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream", response.Content.Headers.ContentLength);
        }
        finally
        {
            // ponytail: keep completed sessions alive to avoid clobbering concurrent readers on the same item; add TTL cleanup if temp usage grows.
        }
    }

    private async Task FillAsync(Guid itemId, Session session)
    {
        try
        {
            using var request = CreateUpstreamRequest(session.Uri);
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            session.ContentType = response.Content.Headers.ContentType?.ToString();
            await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var output = new FileStream(session.BufferPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, true);
            var buffer = new byte[81920];
            while (session.BufferedBytes < session.MaxBytes)
            {
                var read = await input.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0) break;
                var count = (int)Math.Min(read, session.MaxBytes - session.BufferedBytes);
                await output.WriteAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
                session.BufferedBytes += count;
                if (count != read) break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to prebuffer STRM item {ItemId}", itemId);
        }
        finally
        {
            session.Ready.TrySetResult(true);
        }
    }

    private static async Task SkipAsync(Stream stream, long bytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (bytes > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, bytes)), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            bytes -= read;
        }
    }

    private static HttpRequestMessage CreateUpstreamRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);

        var origin = uri.GetLeftPart(UriPartial.Authority);
        request.Headers.Referrer = new Uri(origin + "/");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        return request;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear();
    }

    private sealed class Session : IDisposable
    {
        public Session(Uri uri, long maxBytes)
        {
            Uri = uri;
            MaxBytes = maxBytes;
            BufferPath = Path.Combine(Path.GetTempPath(), "MulletaFlix", "prebuffer", Guid.NewGuid().ToString("N") + ".bin");
            Directory.CreateDirectory(Path.GetDirectoryName(BufferPath)!);
        }

        public Uri Uri { get; }
        public string BufferPath { get; }
        public long MaxBytes { get; }
        public long BufferedBytes { get; set; }
        public string? ContentType { get; set; }
        public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            try { File.Delete(BufferPath); } catch { }
        }
    }
}
