using System;
using System.Text;

namespace MediaBrowser.Providers.Plugins.MidiaStorageOnline
{
    internal static class MidiaStorageOnlineStreamProxy
    {
        private const string ProxyPath = "/MidiaStorageOnline/stream";
        private static string _localBaseUrl = "http://localhost:8096";

        internal static string LocalBaseUrl
        {
            get => _localBaseUrl;
            set => _localBaseUrl = value?.TrimEnd('/') ?? "http://localhost:8096";
        }

        internal static string BuildProxyUrl(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return rawUrl;
            }

            var normalizedUrl = NormalizeAbsoluteHttpUrl(rawUrl);
            return $"{LocalBaseUrl}{ProxyPath}?u={Uri.EscapeDataString(normalizedUrl ?? rawUrl.Trim())}";
        }

        internal static string NormalizeM3uContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            var builder = new StringBuilder(content.Length + 1024);
            var lines = content.Split('\n');
            var proxyPrefix = $"{LocalBaseUrl}{ProxyPath}?u=";

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Length == 0)
                {
                    builder.AppendLine();
                    continue;
                }

                var proxyIndex = line.LastIndexOf(proxyPrefix, StringComparison.OrdinalIgnoreCase);
                if (proxyIndex > 0)
                {
                    builder.AppendLine(line[..proxyIndex]);
                    builder.AppendLine(line[proxyIndex..]);
                    continue;
                }

                builder.AppendLine(line);
            }

            return builder.ToString();
        }

        internal static bool TryGetUpstreamUri(string encodedUrl, out Uri uri)
        {
            uri = null!;
            if (string.IsNullOrWhiteSpace(encodedUrl))
            {
                return false;
            }

            var decoded = Uri.UnescapeDataString(encodedUrl.Trim());
            var normalized = NormalizeAbsoluteHttpUrl(decoded);
            if (normalized is null || !Uri.TryCreate(normalized, UriKind.Absolute, out var parsed))
            {
                return false;
            }

            if (!parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        internal static string? NormalizeAbsoluteHttpUrl(string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return null;
            }

            if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var parsed))
            {
                return null;
            }

            if (!parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var builder = new UriBuilder(parsed);
            var path = builder.Path;
            if (!string.IsNullOrWhiteSpace(path))
            {
                while (path.Contains("//", StringComparison.Ordinal))
                {
                    path = path.Replace("//", "/", StringComparison.Ordinal);
                }

                builder.Path = path;
            }

            return builder.Uri.AbsoluteUri;
        }

        internal static string GetBrowserUserAgent()
        {
            return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36";
        }
    }
}
