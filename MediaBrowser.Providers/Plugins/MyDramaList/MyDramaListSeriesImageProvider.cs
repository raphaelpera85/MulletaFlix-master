using System;
using System.Collections.Generic;
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

namespace MediaBrowser.Providers.Plugins.MyDramaList
{
    public class MyDramaListSeriesImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MyDramaListSeriesImageProvider> _logger;

        public MyDramaListSeriesImageProvider(IHttpClientFactory httpClientFactory, ILogger<MyDramaListSeriesImageProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "MyDramaList";

        /// <inheritdoc />
        public int Order => 2;

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
            var mdlId = item.GetProviderId("MyDramaList");

            if (string.IsNullOrEmpty(mdlId))
            {
                return results;
            }

            try
            {
                _logger.LogInformation("Fetching MyDramaList images for ID: {Id}", mdlId);
                var detailUrl = "https://mydramalist.com/" + mdlId;

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                var html = await client.GetStringAsync(detailUrl, cancellationToken).ConfigureAwait(false);
                var jsonLdMatch = Regex.Match(html, @"<script type=""application/ld\+json"">\s*({""@context"":""https://schema\.org"",""@type"":""(?:TVSeries|Movie)"",.*?})\s*</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (jsonLdMatch.Success)
                {
                    var jsonNode = JsonNode.Parse(jsonLdMatch.Groups[1].Value);
                    var imgUrl = jsonNode?["image"]?.ToString();

                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        results.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Type = ImageType.Primary,
                            Url = imgUrl
                        });

                        results.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Type = ImageType.Backdrop,
                            Url = imgUrl
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching MyDramaList images for: {Id}", mdlId);
            }

            return results;
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }
    }
}
