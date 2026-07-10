using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.Plugins.YouTube
{
    public class YouTubeSeriesImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<YouTubeSeriesImageProvider> _logger;
        private readonly YouTubeInnerTubeClient _client;

        public YouTubeSeriesImageProvider(IHttpClientFactory httpClientFactory, ILogger<YouTubeSeriesImageProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _client = new YouTubeInnerTubeClient(httpClientFactory);
        }

        /// <inheritdoc />
        public string Name => "YouTube";

        /// <inheritdoc />
        public int Order => 1;

        /// <inheritdoc />
        public bool Supports(BaseItem item)
        {
            return item is Series;
        }

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[]
            {
                ImageType.Primary,
                ImageType.Backdrop
            };
        }

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var results = new List<RemoteImageInfo>();
            var ytId = item.GetProviderId("YouTube");

            if (string.IsNullOrEmpty(ytId))
            {
                ytId = ExtractIdFromPath(item.Path);
            }

            if (string.IsNullOrEmpty(ytId))
            {
                var strmUrl = ExtractUrlFromStrmFiles(item.Path);
                if (!string.IsNullOrEmpty(strmUrl))
                {
                    if (strmUrl.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || strmUrl.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                    {
                        ytId = ExtractYouTubeIdFromUrl(strmUrl);
                    }
                    else
                    {
                        // Open Graph image scraping for other platforms
                        var ogImage = await GetOpenGraphImage(strmUrl, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(ogImage))
                        {
                            results.Add(new RemoteImageInfo
                            {
                                ProviderName = Name,
                                Type = ImageType.Primary,
                                Url = ogImage
                            });
                            results.Add(new RemoteImageInfo
                            {
                                ProviderName = Name,
                                Type = ImageType.Backdrop,
                                Url = ogImage
                            });
                        }

                        return results;
                    }
                }
            }

            if (string.IsNullOrEmpty(ytId))
            {
                return results;
            }

            if (ytId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var ogImage = await GetOpenGraphImage(ytId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(ogImage))
                {
                    results.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Primary,
                        Url = ogImage
                    });
                    results.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Backdrop,
                        Url = ogImage
                    });
                }

                return results;
            }

            try
            {
                _logger.LogInformation("Fetching YouTube images for ID: {Id}", ytId);
                var browseResponse = await _client.BrowseAsync(ytId, cancellationToken).ConfigureAwait(false);
                if (browseResponse == null)
                {
                    return results;
                }

                if (ytId.StartsWith("PL", StringComparison.OrdinalIgnoreCase))
                {
                    var imgUrl = browseResponse["sidebar"]?["playlistSidebarRenderer"]?["items"]?[0]?["playlistSidebarPrimaryInfoRenderer"]?["thumbnailRenderer"]?["playlistVideoThumbnailRenderer"]?["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString() ??
                                 browseResponse["metadata"]?["playlistMetadataRenderer"]?["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString();

                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        results.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Type = ImageType.Primary,
                            Url = imgUrl
                        });
                    }
                }
                else if (ytId.StartsWith("UC", StringComparison.OrdinalIgnoreCase))
                {
                    var avatarUrl = browseResponse["header"]?["c4TabbedHeaderRenderer"]?["avatar"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString() ??
                                    browseResponse["metadata"]?["channelMetadataRenderer"]?["avatar"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString();

                    var bannerUrl = browseResponse["header"]?["c4TabbedHeaderRenderer"]?["banner"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString();

                    if (!string.IsNullOrEmpty(avatarUrl))
                    {
                        results.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Type = ImageType.Primary,
                            Url = avatarUrl
                        });
                    }

                    if (!string.IsNullOrEmpty(bannerUrl))
                    {
                        results.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Type = ImageType.Backdrop,
                            Url = bannerUrl
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching YouTube images for: {Id}", ytId);
            }

            return results;
        }

        private async Task<string> GetOpenGraphImage(string url, CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                var html = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                var imageUrl = Regex.Match(html, @"<meta\s+property=""og:image""\s+content=""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;

                return imageUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Open Graph image from: {Url}", url);
            }

            return string.Empty;
        }

        private static string ExtractIdFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var match = Regex.Match(dirName, @"\[youtube-(PL[a-zA-Z0-9_-]+|UC[a-zA-Z0-9_-]{22})\]", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return string.Empty;
        }

        private static string ExtractYouTubeIdFromUrl(string url)
        {
            var match = Regex.Match(url, @"[?&]list=(PL[a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return string.Empty;
        }

        private static string ExtractUrlFromStrmFiles(string seriesPath)
        {
            if (string.IsNullOrWhiteSpace(seriesPath) || !Directory.Exists(seriesPath))
            {
                return string.Empty;
            }

            try
            {
                var strmFiles = Directory.GetFiles(seriesPath, "*.strm", SearchOption.AllDirectories);
                foreach (var file in strmFiles)
                {
                    var lines = File.ReadLines(file);
                    var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
                    if (!string.IsNullOrEmpty(firstLine) && (firstLine.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || firstLine.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                    {
                        return firstLine;
                    }
                }
            }
            catch (Exception)
            {
                // Ignore error
            }

            return string.Empty;
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }
    }
}
