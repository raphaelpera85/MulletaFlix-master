using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Providers.Plugins.MidiaStorageOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MulletaFlix.Providers.Tests.Plugins.MidiaStorageOnline;

public class MidiaStorageOnlineLinkValidatorTests
{
    [Fact]
    public async Task FilterOnlineEntriesAsync_RemovesOfflineLinks()
    {
        var entries = new List<IMidiaStorageOnlineM3uEntry>
        {
            new FakeM3uEntry { Type = "Filme", Name = "Online", Url = "https://online.example/movie.m3u8" },
            new FakeM3uEntry { Type = "Filme", Name = "Offline", Url = "https://offline.example/movie.m3u8" }
        };

        var factory = new FakeHttpClientFactory(request =>
        {
            if (request.RequestUri?.Host == "online.example")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var filtered = await MidiaStorageOnlineLinkValidator.FilterOnlineEntriesAsync(
            entries,
            factory,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Single(filtered);
        Assert.Equal("Online", filtered[0].Name);
    }

    [Fact]
    public async Task FilterOnlineEntriesAsync_UsesHeadProbeBeforeGetFallback()
    {
        var requests = new List<(HttpMethod Method, string Host, string? Range)>();
        var entries = new List<IMidiaStorageOnlineM3uEntry>
        {
            new FakeM3uEntry { Type = "Filme", Name = "Online", Url = "https://online.example/movie.m3u8" }
        };

        var factory = new FakeHttpClientFactory(request =>
        {
            requests.Add((request.Method, request.RequestUri?.Host ?? string.Empty, request.Headers.Range?.ToString()));
            return request.Method == HttpMethod.Head
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        var filtered = await MidiaStorageOnlineLinkValidator.FilterOnlineEntriesAsync(
            entries,
            factory,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Single(filtered);
        Assert.Single(requests);
        Assert.Equal(HttpMethod.Head, requests[0].Method);
    }

    [Fact]
    public async Task FilterOnlineEntriesAsync_FallsBackToGetRangeWhenHeadNotAllowed()
    {
        var requests = new List<(HttpMethod Method, string Host, string? Range)>();
        var entries = new List<IMidiaStorageOnlineM3uEntry>
        {
            new FakeM3uEntry { Type = "Filme", Name = "Online", Url = "https://online.example/movie.m3u8" }
        };

        var factory = new FakeHttpClientFactory(request =>
        {
            requests.Add((request.Method, request.RequestUri?.Host ?? string.Empty, request.Headers.Range?.ToString()));
            return request.Method == HttpMethod.Head
                ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
                : new HttpResponseMessage(HttpStatusCode.PartialContent);
        });

        var filtered = await MidiaStorageOnlineLinkValidator.FilterOnlineEntriesAsync(
            entries,
            factory,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Single(filtered);
        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Head, requests[0].Method);
        Assert.Equal(HttpMethod.Get, requests[1].Method);
        Assert.Equal("bytes=0-0", requests[1].Range);
    }

    [Fact]
    public async Task FilterOnlineEntriesAsync_NormalizesDuplicateSlashesInUrlPath()
    {
        var requests = new List<string>();
        var entries = new List<IMidiaStorageOnlineM3uEntry>
        {
            new FakeM3uEntry { Type = "Filme", Name = "Normalized", Url = "https://example.com//live//channel.ts" }
        };

        var factory = new FakeHttpClientFactory(request =>
        {
            requests.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var filtered = await MidiaStorageOnlineLinkValidator.FilterOnlineEntriesAsync(
            entries,
            factory,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Single(filtered);
        Assert.Single(requests);
        Assert.Equal("https://example.com/live/channel.ts", requests[0]);
    }

    [Fact]
    public async Task FilterOnlineEntriesAsync_SkipsLinksAlreadyMarkedOffline()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var cachePath = Path.Combine(tempDir, "offline-links.json");
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(new[] { "https://offline.example/movie.m3u8" }));

        var requests = 0;
        var entries = new List<IMidiaStorageOnlineM3uEntry>
        {
            new FakeM3uEntry { Type = "Filme", Name = "Offline", Url = "https://offline.example/movie.m3u8" },
            new FakeM3uEntry { Type = "Filme", Name = "Online", Url = "https://online.example/movie.m3u8" }
        };

        using var factory = new FakeHttpClientFactory(request =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var filtered = await MidiaStorageOnlineLinkValidator.FilterOnlineEntriesAsync(
            entries,
            factory,
            NullLogger.Instance,
            CancellationToken.None,
            offlineCachePath: cachePath);

        Assert.Single(filtered);
        Assert.Equal("Online", filtered[0].Name);
        Assert.Equal(1, requests);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(new DelegatingHandlerStub(_handler), disposeHandler: true);

        public void Dispose()
        {
        }
    }

    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class FakeM3uEntry : IMidiaStorageOnlineM3uEntry
    {
        public string Type { get; init; } = "Canal";
        public string Name { get; init; } = string.Empty;
        public string? TvgId { get; init; }
        public string? TvgName { get; init; }
        public string? GroupTitle { get; init; }
        public string? TvgLogo { get; set; }
        public string Url { get; init; } = string.Empty;
    }
}
