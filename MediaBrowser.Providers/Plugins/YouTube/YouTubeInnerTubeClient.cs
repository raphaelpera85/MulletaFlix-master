using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Providers.Plugins.YouTube
{
    public class YouTubeInnerTubeClient
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public YouTubeInnerTubeClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<JsonNode?> SearchAsync(string query, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

            var requestBody = new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion = "2.20210621.02.00"
                    }
                },
                query = query
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://www.youtube.com/youtubei/v1/search", content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonNode.Parse(jsonString);
        }

        public async Task<JsonNode?> BrowseAsync(string browseId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(browseId))
            {
                return null;
            }

            var targetId = browseId;
            if (targetId.StartsWith("PL", StringComparison.OrdinalIgnoreCase) && !targetId.StartsWith("VLPL", StringComparison.OrdinalIgnoreCase))
            {
                targetId = "VL" + targetId;
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

            var requestBody = new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion = "2.20210621.02.00"
                    }
                },
                browseId = targetId
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://www.youtube.com/youtubei/v1/browse", content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonNode.Parse(jsonString);
        }
    }
}
