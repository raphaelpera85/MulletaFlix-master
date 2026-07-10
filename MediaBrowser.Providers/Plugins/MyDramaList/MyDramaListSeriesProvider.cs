using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace MediaBrowser.Providers.Plugins.MyDramaList
{
    public class MyDramaListSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MyDramaListSeriesProvider> _logger;

        public MyDramaListSeriesProvider(IHttpClientFactory httpClientFactory, ILogger<MyDramaListSeriesProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "MyDramaList";

        /// <inheritdoc />
        public int Order => 2;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            if (searchInfo.TryGetProviderId("MyDramaList", out var mdlId))
            {
                var directResult = await GetDirectResult(mdlId, cancellationToken).ConfigureAwait(false);
                if (directResult != null)
                {
                    results.Add(directResult);
                    return results;
                }
            }

            try
            {
                _logger.LogInformation("Searching MyDramaList for series: {Name}", searchInfo.Name);
                var searchUrl = "https://mydramalist.com/search?q=" + Uri.EscapeDataString(searchInfo.Name);

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                var html = await client.GetStringAsync(searchUrl, cancellationToken).ConfigureAwait(false);
                var matches = Regex.Matches(html, @"<a class=""block"" href=""(/(?:[0-9]+-[^""]+))"">.*?<img class=""[^""]*cover[^""]*"" (?:data-)?src=""([^""]+)"" alt=""([^""]+)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                foreach (Match match in matches)
                {
                    var path = match.Groups[1].Value.TrimStart('/');
                    var imgUrl = match.Groups[2].Value;
                    var title = match.Groups[3].Value;

                    var result = new RemoteSearchResult
                    {
                        Name = WebUtility.HtmlDecode(title),
                        SearchProviderName = Name,
                        ImageUrl = imgUrl
                    };
                    result.SetProviderId("MyDramaList", path);
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching MyDramaList for: {Name}", searchInfo.Name);
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

            var mdlId = info.GetProviderId("MyDramaList");
            if (string.IsNullOrEmpty(mdlId))
            {
                var searchResults = await GetSearchResults(info, cancellationToken).ConfigureAwait(false);
                var first = searchResults.GetEnumerator();
                if (first.MoveNext())
                {
                    mdlId = first.Current.GetProviderId("MyDramaList");
                }
            }

            if (string.IsNullOrEmpty(mdlId))
            {
                return result;
            }

            result.QueriedById = true;

            try
            {
                _logger.LogInformation("Fetching MyDramaList details for: {Id}", mdlId);
                var detailUrl = "https://mydramalist.com/" + mdlId;

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                var html = await client.GetStringAsync(detailUrl, cancellationToken).ConfigureAwait(false);
                var jsonLdMatch = Regex.Match(html, @"<script type=""application/ld\+json"">\s*({""@context"":""https://schema\.org"",""@type"":""(?:TVSeries|Movie)"",.*?})\s*</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (jsonLdMatch.Success)
                {
                    var jsonNode = JsonNode.Parse(jsonLdMatch.Groups[1].Value);
                    if (jsonNode != null)
                    {
                        var series = new Series();
                        series.SetProviderId("MyDramaList", mdlId);

                        series.Name = WebUtility.HtmlDecode(jsonNode["name"]?.ToString() ?? info.Name);
                        series.Overview = WebUtility.HtmlDecode(jsonNode["description"]?.ToString() ?? string.Empty);

                        var dateStr = jsonNode["datePublished"]?.ToString();
                        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var premiereDate))
                        {
                            series.PremiereDate = premiereDate.ToUniversalTime();
                            series.ProductionYear = premiereDate.Year;
                        }

                        var genresArray = jsonNode["genre"]?.AsArray();
                        if (genresArray != null)
                        {
                            var genres = new List<string>();
                            foreach (var genreNode in genresArray)
                            {
                                if (genreNode != null)
                                {
                                    genres.Add(genreNode.ToString());
                                }
                            }
                            series.Genres = genres.ToArray();
                        }

                        var ratingStr = jsonNode["aggregateRating"]?["ratingValue"]?.ToString();
                        if (float.TryParse(ratingStr, CultureInfo.InvariantCulture, out var rating))
                        {
                            series.CommunityRating = rating;
                        }

                        result.Item = series;
                        result.HasMetadata = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching MyDramaList metadata for: {Id}", mdlId);
            }

            return result;
        }

        private async Task<RemoteSearchResult?> GetDirectResult(string mdlId, CancellationToken cancellationToken)
        {
            try
            {
                var detailUrl = "https://mydramalist.com/" + mdlId;
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                var html = await client.GetStringAsync(detailUrl, cancellationToken).ConfigureAwait(false);
                var jsonLdMatch = Regex.Match(html, @"<script type=""application/ld\+json"">\s*({""@context"":""https://schema\.org"",""@type"":""(?:TVSeries|Movie)"",.*?})\s*</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (jsonLdMatch.Success)
                {
                    var jsonNode = JsonNode.Parse(jsonLdMatch.Groups[1].Value);
                    if (jsonNode != null)
                    {
                        var result = new RemoteSearchResult
                        {
                            Name = WebUtility.HtmlDecode(jsonNode["name"]?.ToString() ?? string.Empty),
                            SearchProviderName = Name,
                            ImageUrl = jsonNode["image"]?.ToString(),
                            Overview = WebUtility.HtmlDecode(jsonNode["description"]?.ToString() ?? string.Empty)
                        };
                        result.SetProviderId("MyDramaList", mdlId);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get direct MyDramaList result for: {Id}", mdlId);
            }

            return null;
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }
    }
}
