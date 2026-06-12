using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Api.Middleware;

public class RateLimitMiddleware
{
    private static readonly ConcurrentDictionary<string, RateLimitEntry> _failedLogins = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, RateLimitEntry> _anonymousRequests = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(15);
    private const int MaxFailedLogins = 10;

    private static readonly TimeSpan AnonymousWindow = TimeSpan.FromSeconds(10);
    private const int MaxAnonymousRequests = 30;

    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(ip))
        {
            await _next(context);
            return;
        }

        var isAuth = context.User?.Identity?.IsAuthenticated ?? false;
        var path = context.Request.Path.Value;
        var isLoginAttempt = path is not null && (
            path.StartsWith("/Users/Authenticate", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Users/Register", StringComparison.OrdinalIgnoreCase));

        if (isLoginAttempt)
        {
            if (IsBlocked(_failedLogins, ip, LoginWindow, MaxFailedLogins))
            {
                _logger.LogWarning("Rate limit exceeded for login from IP {IP}", ip);
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                return;
            }
        }
        else if (!isAuth)
        {
            if (IsBlocked(_anonymousRequests, ip, AnonymousWindow, MaxAnonymousRequests))
            {
                _logger.LogWarning("Rate limit exceeded for anonymous requests from IP {IP}", ip);
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                return;
            }
        }

        await _next(context);

        if (isLoginAttempt && context.Response.StatusCode == (int)HttpStatusCode.Unauthorized)
        {
            RecordAttempt(_failedLogins, ip);
        }
    }

    private static bool IsBlocked(ConcurrentDictionary<string, RateLimitEntry> store, string key, TimeSpan window, int max)
    {
        var now = DateTime.UtcNow;
        if (store.TryGetValue(key, out var entry))
        {
            lock (entry)
            {
                entry.Prune(now, window);
                return entry.Count >= max;
            }
        }

        return false;
    }

    private static void RecordAttempt(ConcurrentDictionary<string, RateLimitEntry> store, string key)
    {
        var now = DateTime.UtcNow;
        var entry = store.GetOrAdd(key, _ => new RateLimitEntry());
        lock (entry)
        {
            entry.Prune(now, LoginWindow);
            entry.Timestamps.Add(now);
        }
    }

    private class RateLimitEntry
    {
        public List<DateTime> Timestamps { get; } = new();

        public int Count => Timestamps.Count;

        public void Prune(DateTime now, TimeSpan window)
        {
            var cutoff = now - window;
            Timestamps.RemoveAll(t => t < cutoff);
        }
    }
}
