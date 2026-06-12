using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace MulletaFlix.Api.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Content-Security-Policy"] =
            "default-src 'self' 'unsafe-inline' 'unsafe-eval' data: blob:; "
            + "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://www.gstatic.com; "
            + "script-src-elem 'self' 'unsafe-inline' 'unsafe-eval' https://www.gstatic.com; "
            + "img-src 'self' data: blob: https:; "
            + "media-src 'self' data: blob: https:; "
            + "font-src 'self' data:; "
            + "connect-src 'self' http: https: ws: wss:;";

        await _next(context);
    }
}
