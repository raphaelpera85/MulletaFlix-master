using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.Plugins.YouTube
{
    public class YouTubeSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<YouTubeSeriesProvider> _logger;
        private readonly YouTubeInnerTubeClient _client;

        public YouTubeSeriesProvider(IHttpClientFactory httpClientFactory, ILogger<YouTubeSeriesProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _client = new YouTubeInnerTubeClient(httpClientFactory);
        }

        /// <inheritdoc />
        public string Name => "YouTube / ShortSeries";

        /// <inheritdoc />
        public int Order => 1;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            if (searchInfo.TryGetProviderId("YouTube", out var ytId))
            {
                var directResult = await GetDirectResult(ytId, cancellationToken).ConfigureAwait(false);
                if (directResult != null)
                {
                    results.Add(directResult);
                    return results;
                }
            }

            var potentialId = searchInfo.Name?.Trim() ?? string.Empty;
            if (IsValidYouTubeId(potentialId) || potentialId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var directResult = await GetDirectResult(potentialId, cancellationToken).ConfigureAwait(false);
                if (directResult != null)
                {
                    results.Add(directResult);
                    return results;
                }
            }

            try
            {
                _logger.LogInformation("Searching YouTube InnerTube for series: {Name}", searchInfo.Name);
                var searchResponse = await _client.SearchAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
                if (searchResponse == null)
                {
                    return results;
                }

                var items = searchResponse["contents"]?["twoColumnSearchResultRenderer"]?["primaryContents"]?["sectionListRenderer"]?["contents"]?[0]?["itemSectionRenderer"]?["contents"]?.AsArray();
                if (items == null)
                {
                    return results;
                }

                foreach (var item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var playlistRenderer = item["playlistRenderer"];
                    if (playlistRenderer != null)
                    {
                        var playlistId = playlistRenderer["playlistId"]?.ToString();
                        var title = playlistRenderer["title"]?["runs"]?[0]?["text"]?.ToString() ??
                                    playlistRenderer["title"]?["simpleText"]?.ToString();

                        if (!string.IsNullOrEmpty(playlistId) && !string.IsNullOrEmpty(title))
                        {
                            var imgUrl = playlistRenderer["thumbnails"]?[0]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString() ??
                                         playlistRenderer["thumbnails"]?[0]?["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString();

                            var resultItem = new RemoteSearchResult
                            {
                                Name = title,
                                SearchProviderName = Name,
                                ImageUrl = imgUrl,
                                Overview = $"YouTube Playlist - {playlistRenderer["videoCount"]?.ToString()} vídeos"
                            };
                            resultItem.SetProviderId("YouTube", playlistId);
                            results.Add(resultItem);
                        }
                    }

                    var channelRenderer = item["channelRenderer"];
                    if (channelRenderer != null)
                    {
                        var channelId = channelRenderer["channelId"]?.ToString();
                        var title = channelRenderer["title"]?["simpleText"]?.ToString() ??
                                    channelRenderer["title"]?["runs"]?[0]?["text"]?.ToString();

                        if (!string.IsNullOrEmpty(channelId) && !string.IsNullOrEmpty(title))
                        {
                            var imgUrl = channelRenderer["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString();

                            var resultItem = new RemoteSearchResult
                            {
                                Name = title,
                                SearchProviderName = Name,
                                ImageUrl = imgUrl,
                                Overview = channelRenderer["descriptionSnippet"]?["runs"]?[0]?["text"]?.ToString() ?? "YouTube Canal"
                            };
                            resultItem.SetProviderId("YouTube", channelId);
                            results.Add(resultItem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching search results from YouTube for: {Name}", searchInfo.Name);
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>
            {
                QueriedById = false
            };

            var ytId = info.GetProviderId("YouTube");
            if (string.IsNullOrEmpty(ytId))
            {
                ytId = ExtractIdFromPath(info.Path);
            }

            if (string.IsNullOrEmpty(ytId))
            {
                // Try to extract URL from local .strm files
                var strmUrl = ExtractUrlFromStrmFiles(info.Path);
                if (!string.IsNullOrEmpty(strmUrl))
                {
                    if (strmUrl.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || strmUrl.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                    {
                        ytId = ExtractYouTubeIdFromUrl(strmUrl);
                    }
                    else
                    {
                        // Direct Open Graph parsing for other short platforms (ReelShort, DramaBox, etc.)
                        return await GetOpenGraphMetadata(strmUrl, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // Híbrido fallback: search by title
            if (string.IsNullOrEmpty(ytId))
            {
                var searchResults = await GetSearchResults(info, cancellationToken).ConfigureAwait(false);
                var firstResult = searchResults.FirstOrDefault();
                if (firstResult != null)
                {
                    ytId = firstResult.GetProviderId("YouTube");
                }
            }

            if (string.IsNullOrEmpty(ytId))
            {
                return result;
            }

            // If it is a full web URL for a non-YouTube platform, fetch meta Open Graph
            if (ytId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return await GetOpenGraphMetadata(ytId, cancellationToken).ConfigureAwait(false);
            }

            result.QueriedById = true;

            try
            {
                _logger.LogInformation("Fetching YouTube EPG/metadata for ID: {Id}", ytId);
                var browseResponse = await _client.BrowseAsync(ytId, cancellationToken).ConfigureAwait(false);
                if (browseResponse == null)
                {
                    return result;
                }

                var series = new Series();
                series.SetProviderId("YouTube", ytId);

                if (ytId.StartsWith("PL", StringComparison.OrdinalIgnoreCase))
                {
                    var title = browseResponse["header"]?["playlistHeaderRenderer"]?["title"]?["simpleText"]?.ToString() ??
                                browseResponse["header"]?["playlistHeaderRenderer"]?["title"]?["runs"]?[0]?["text"]?.ToString() ??
                                browseResponse["metadata"]?["playlistMetadataRenderer"]?["title"]?.ToString();

                    var desc = browseResponse["header"]?["playlistHeaderRenderer"]?["descriptionText"]?["simpleText"]?.ToString() ??
                               browseResponse["header"]?["playlistHeaderRenderer"]?["descriptionText"]?["runs"]?[0]?["text"]?.ToString() ??
                               browseResponse["metadata"]?["playlistMetadataRenderer"]?["description"]?.ToString();

                    series.Name = title ?? info.Name;
                    series.Overview = desc ?? string.Empty;
                }
                else if (ytId.StartsWith("UC", StringComparison.OrdinalIgnoreCase))
                {
                    var title = browseResponse["header"]?["c4TabbedHeaderRenderer"]?["title"]?.ToString() ??
                                browseResponse["metadata"]?["channelMetadataRenderer"]?["title"]?.ToString();

                    var desc = browseResponse["metadata"]?["channelMetadataRenderer"]?["description"]?.ToString();

                    series.Name = title ?? info.Name;
                    series.Overview = desc ?? string.Empty;
                }

                result.Item = series;
                result.HasMetadata = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching YouTube metadata for: {Id}", ytId);
            }

            return result;
        }

        private async Task<RemoteSearchResult?> GetDirectResult(string ytId, CancellationToken cancellationToken)
        {
            if (ytId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var ogResult = await GetOpenGraphMetadata(ytId, cancellationToken).ConfigureAwait(false);
                if (ogResult.HasMetadata && ogResult.Item != null)
                {
                    var searchRes = new RemoteSearchResult
                    {
                        Name = ogResult.Item.Name,
                        SearchProviderName = Name,
                        Overview = ogResult.Item.Overview
                    };
                    searchRes.SetProviderId("YouTube", ytId);
                    return searchRes;
                }

                return null;
            }

            try
            {
                var response = await _client.BrowseAsync(ytId, cancellationToken).ConfigureAwait(false);
                if (response == null)
                {
                    return null;
                }

                var title = string.Empty;
                var imgUrl = string.Empty;
                var desc = string.Empty;

                if (ytId.StartsWith("PL", StringComparison.OrdinalIgnoreCase))
                {
                    title = response["header"]?["playlistHeaderRenderer"]?["title"]?["simpleText"]?.ToString() ??
                            response["header"]?["playlistHeaderRenderer"]?["title"]?["runs"]?[0]?["text"]?.ToString() ??
                            response["metadata"]?["playlistMetadataRenderer"]?["title"]?.ToString();

                    imgUrl = response["sidebar"]?["playlistSidebarRenderer"]?["items"]?[0]?["playlistSidebarPrimaryInfoRenderer"]?["thumbnailRenderer"]?["playlistVideoThumbnailRenderer"]?["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString() ??
                             response["metadata"]?["playlistMetadataRenderer"]?["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString();

                    desc = "YouTube Playlist";
                }
                else if (ytId.StartsWith("UC", StringComparison.OrdinalIgnoreCase))
                {
                    title = response["header"]?["c4TabbedHeaderRenderer"]?["title"]?.ToString() ??
                            response["metadata"]?["channelMetadataRenderer"]?["title"]?.ToString();

                    imgUrl = response["header"]?["c4TabbedHeaderRenderer"]?["avatar"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString() ??
                             response["metadata"]?["channelMetadataRenderer"]?["avatar"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.ToString();

                    desc = "YouTube Channel";
                }

                if (!string.IsNullOrEmpty(title))
                {
                    var result = new RemoteSearchResult
                    {
                        Name = title,
                        SearchProviderName = Name,
                        ImageUrl = imgUrl,
                        Overview = desc
                    };
                    result.SetProviderId("YouTube", ytId);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get direct YouTube result for: {Id}", ytId);
            }

            return null;
        }

        private async Task<MetadataResult<Series>> GetOpenGraphMetadata(string url, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            try
            {
                _logger.LogInformation("Fetching Open Graph metadata from: {Url}", url);
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                var html = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

                var title = Regex.Match(html, @"<meta\s+property=""og:title""\s+content=""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
                if (string.IsNullOrEmpty(title))
                {
                    title = Regex.Match(html, @"<meta\s+name=""twitter:title""\s+content=""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
                }

                var desc = Regex.Match(html, @"<meta\s+property=""og:description""\s+content=""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
                if (string.IsNullOrEmpty(desc))
                {
                    desc = Regex.Match(html, @"<meta\s+name=""twitter:description""\s+content=""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
                }

                if (!string.IsNullOrEmpty(title))
                {
                    var series = new Series
                    {
                        Name = WebUtility.HtmlDecode(title),
                        Overview = WebUtility.HtmlDecode(desc)
                    };
                    series.SetProviderId("YouTube", url);

                    result.Item = series;
                    result.HasMetadata = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Open Graph metadata from: {Url}", url);
            }

            return result;
        }

        private static bool IsValidYouTubeId(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            return (input.StartsWith("PL", StringComparison.OrdinalIgnoreCase) && input.Length >= 15) ||
                   (input.StartsWith("UC", StringComparison.OrdinalIgnoreCase) && input.Length == 24);
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
