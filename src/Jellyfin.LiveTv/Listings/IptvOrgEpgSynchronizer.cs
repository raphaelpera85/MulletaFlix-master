using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.LiveTv.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.LiveTv.Listings
{
    public class IptvOrgEpgSynchronizer : IIptvOrgEpgSynchronizer
    {
        private static readonly TimeSpan ApiCacheExpiration = TimeSpan.FromHours(24);
        private static readonly TimeSpan XmlCacheExpiration = TimeSpan.FromHours(4);

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly ILogger<IptvOrgEpgSynchronizer> _logger;
        private readonly IServerConfigurationManager _config;
        private readonly ITunerHostManager _tunerHostManager;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly object _lock = new object();

        private List<IptvOrgChannelMapping> _mappings = new List<IptvOrgChannelMapping>();

        public IptvOrgEpgSynchronizer(
            ILogger<IptvOrgEpgSynchronizer> logger,
            IServerConfigurationManager config,
            ITunerHostManager tunerHostManager,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _config = config;
            _tunerHostManager = tunerHostManager;
            _httpClientFactory = httpClientFactory;
        }

        public IReadOnlyList<IptvOrgChannelMapping> GetMappings()
        {
            lock (_lock)
            {
                if (_mappings.Count == 0)
                {
                    LoadMappingsFromDisk();
                }

                return _mappings.AsReadOnly();
            }
        }

        public async Task SynchronizeAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting iptv-org EPG synchronization...");

            var baseCacheDir = Path.Combine(_config.ApplicationPaths.CachePath, "iptvorg");
            Directory.CreateDirectory(baseCacheDir);
            Directory.CreateDirectory(Path.Combine(baseCacheDir, "xmltv"));

            // 1. Download and cache iptv-org API files
            var channelsJsonPath = await EnsureFileCached(
                "https://iptv-org.github.io/api/channels.json",
                Path.Combine(baseCacheDir, "api_channels.json"),
                ApiCacheExpiration,
                cancellationToken).ConfigureAwait(false);

            var guidesJsonPath = await EnsureFileCached(
                "https://iptv-org.github.io/api/guides.json",
                Path.Combine(baseCacheDir, "api_guides.json"),
                ApiCacheExpiration,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(channelsJsonPath) || string.IsNullOrEmpty(guidesJsonPath))
            {
                _logger.LogError("Failed to obtain iptv-org API files. Aborting synchronization.");
                return;
            }

            // 2. Load API data
            _logger.LogInformation("Loading iptv-org metadata...");
            var iptvChannels = await LoadJsonAsync<List<IptvChannelDto>>(channelsJsonPath, cancellationToken).ConfigureAwait(false) ?? new List<IptvChannelDto>();
            var iptvGuides = await LoadJsonAsync<List<IptvGuideDto>>(guidesJsonPath, cancellationToken).ConfigureAwait(false) ?? new List<IptvGuideDto>();

            _logger.LogInformation("Loaded {ChannelCount} channels and {GuideCount} guides from iptv-org API.", iptvChannels.Count, iptvGuides.Count);

            // 3. Build lookup indexes
            // Primary index: guides by normalized site_name (the channel display name in the guide)
            var guidesBySiteName = new Dictionary<string, List<IptvGuideDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var guide in iptvGuides)
            {
                if (string.IsNullOrWhiteSpace(guide.SiteName))
                {
                    continue;
                }

                var normalized = NormalizeName(guide.SiteName);
                if (string.IsNullOrEmpty(normalized))
                {
                    continue;
                }

                if (!guidesBySiteName.TryGetValue(normalized, out var list))
                {
                    list = new List<IptvGuideDto>();
                    guidesBySiteName[normalized] = list;
                }

                list.Add(guide);
            }

            // Secondary index: channels.json by normalized name and alt_names (for fuzzy fallback)
            var channelsByNormalizedName = new Dictionary<string, List<IptvChannelDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var ch in iptvChannels)
            {
                var names = new List<string>();
                if (!string.IsNullOrWhiteSpace(ch.Name))
                {
                    names.Add(ch.Name);
                }

                if (ch.AltNames is not null)
                {
                    names.AddRange(ch.AltNames.Where(n => !string.IsNullOrWhiteSpace(n)));
                }

                foreach (var name in names)
                {
                    var normalized = NormalizeName(name);
                    if (string.IsNullOrEmpty(normalized))
                    {
                        continue;
                    }

                    if (!channelsByNormalizedName.TryGetValue(normalized, out var list))
                    {
                        list = new List<IptvChannelDto>();
                        channelsByNormalizedName[normalized] = list;
                    }

                    list.Add(ch);
                }
            }

            // Tertiary index: guides by channel ID (for entries that do have a non-null channel field)
            var guidesByChannelId = new Dictionary<string, List<IptvGuideDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var guide in iptvGuides)
            {
                if (string.IsNullOrWhiteSpace(guide.Channel))
                {
                    continue;
                }

                if (!guidesByChannelId.TryGetValue(guide.Channel, out var list))
                {
                    list = new List<IptvGuideDto>();
                    guidesByChannelId[guide.Channel] = list;
                }

                list.Add(guide);
            }

            // 4. Resolve tuner channels
            var tunerChannels = new List<ChannelInfo>();
            foreach (var tuner in _tunerHostManager.TunerHosts)
            {
                try
                {
                    var channels = await tuner.GetChannels(true, cancellationToken).ConfigureAwait(false);
                    tunerChannels.AddRange(channels);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving channels from tuner host {TunerId}", tuner.Name);
                }
            }

            _logger.LogInformation("Found {Count} tuner channels to resolve.", tunerChannels.Count);

            // 5. Match tuner channels with iptv-org guides
            var newMappings = new List<IptvOrgChannelMapping>();
            var xmlsToDownload = new HashSet<(string Lang, string Site)>();
            var preferredLang = GetPreferredLanguage();
            var preferredCountry = GetPreferredCountry();

            foreach (var tc in tunerChannels)
            {
                if (string.IsNullOrWhiteSpace(tc.Name))
                {
                    continue;
                }

                var match = FindBestGuideMatch(tc, guidesBySiteName, channelsByNormalizedName, guidesByChannelId, preferredLang, preferredCountry);
                if (match != null)
                {
                    var lang = match.Lang?.ToLowerInvariant() ?? preferredLang;
                    var site = match.Site;
                    var localXmlPath = Path.Combine(baseCacheDir, "xmltv", $"{lang}_{site}.xml");

                    // Determine the iptv-org channel ID for XMLTV reading
                    var epgChannelId = !string.IsNullOrWhiteSpace(match.Channel) ? match.Channel : match.SiteId;

                    newMappings.Add(new IptvOrgChannelMapping
                    {
                        TunerChannelId = tc.Id,
                        TunerChannelName = tc.Name,
                        IptvOrgChannelId = epgChannelId,
                        Country = preferredCountry,
                        Lang = lang,
                        Site = site,
                        LocalXmlPath = localXmlPath
                    });

                    xmlsToDownload.Add((lang, site));
                }
                else
                {
                    _logger.LogDebug("Could not resolve iptv-org EPG for channel: {Name} (Id: {Id})", tc.Name, tc.Id);
                }
            }

            _logger.LogInformation("Resolved {Count} mappings. Downloading {XmlCount} XMLTV files...", newMappings.Count, xmlsToDownload.Count);

            // 6. Download required XMLTV files (using lang, not country)
            var downloadTasks = xmlsToDownload.Select(async target =>
            {
                var url = $"https://iptv-org.github.io/epg/guides/{target.Lang}/{target.Site}.epg.xml";
                var localPath = Path.Combine(baseCacheDir, "xmltv", $"{target.Lang}_{target.Site}.xml");

                try
                {
                    await EnsureFileCached(url, localPath, XmlCacheExpiration, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download EPG guide from: {Url}", url);
                }
            });

            await Task.WhenAll(downloadTasks).ConfigureAwait(false);

            // 7. Save mappings to disk
            lock (_lock)
            {
                _mappings = newMappings;
                SaveMappingsToDisk();
            }

            // 8. Ensure Listing Provider is added to config
            EnsureListingProviderAdded();

            _logger.LogInformation("iptv-org EPG synchronization completed successfully.");
        }

        private IptvGuideDto? FindBestGuideMatch(
            ChannelInfo tc,
            Dictionary<string, List<IptvGuideDto>> guidesBySiteName,
            Dictionary<string, List<IptvChannelDto>> channelsByName,
            Dictionary<string, List<IptvGuideDto>> guidesByChannelId,
            string preferredLang,
            string preferredCountry)
        {
            var normalizedTunerName = NormalizeName(tc.Name);
            if (string.IsNullOrEmpty(normalizedTunerName))
            {
                return null;
            }

            // Strategy 1: Direct match via guides.json site_name
            if (guidesBySiteName.TryGetValue(normalizedTunerName, out var guideCandidates))
            {
                var best = SelectBestGuide(guideCandidates, preferredLang);
                if (best != null)
                {
                    return best;
                }
            }

            // Strategy 2: Match through channels.json (name/alt_names) → channel ID → guides.json
            if (channelsByName.TryGetValue(normalizedTunerName, out var channelCandidates))
            {
                // Prefer the channel from the preferred country
                var orderedChannels = channelCandidates
                    .OrderByDescending(c => string.Equals(c.Country, preferredCountry, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var channel in orderedChannels)
                {
                    if (!string.IsNullOrWhiteSpace(channel.Id) && guidesByChannelId.TryGetValue(channel.Id, out var channelGuides))
                    {
                        var best = SelectBestGuide(channelGuides, preferredLang);
                        if (best != null)
                        {
                            return best;
                        }
                    }
                }
            }

            return null;
        }

        private static IptvGuideDto? SelectBestGuide(List<IptvGuideDto> candidates, string preferredLang)
        {
            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            // Prefer guide with matching language
            var langMatch = candidates.FirstOrDefault(g => string.Equals(g.Lang, preferredLang, StringComparison.OrdinalIgnoreCase));
            if (langMatch != null)
            {
                return langMatch;
            }

            return candidates[0];
        }

        private string GetPreferredLanguage()
        {
            var culture = _config.Configuration.PreferredMetadataLanguage;
            if (string.IsNullOrWhiteSpace(culture))
            {
                return "pt";
            }

            // "pt-BR" → "pt", "en-US" → "en"
            if (culture.Contains('-', StringComparison.OrdinalIgnoreCase))
            {
                return culture.Split('-').First().ToLowerInvariant();
            }

            return culture.ToLowerInvariant();
        }

        private string GetPreferredCountry()
        {
            var culture = _config.Configuration.PreferredMetadataLanguage;
            if (string.IsNullOrWhiteSpace(culture))
            {
                return "br";
            }

            // "pt-BR" → "br", "en-US" → "us"
            if (culture.Contains('-', StringComparison.OrdinalIgnoreCase))
            {
                return culture.Split('-').Last().ToLowerInvariant();
            }

            return culture.ToLowerInvariant();
        }

        private static string RemoveDiacritics(string text)
        {
            var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
            var stringBuilder = new System.Text.StringBuilder(capacity: normalizedString.Length);

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var clean = RemoveDiacritics(name);

            // Remove common quality suffixes: HD, FHD, SD, 4K, 1080p, etc.
            clean = Regex.Replace(clean, @"\b(hd|fhd|sd|4k|1080p|720p|h\.264|hevc)\b", string.Empty, RegexOptions.IgnoreCase);

            // Remove special characters, brackets, parentheses
            clean = Regex.Replace(clean, @"[^\w]", string.Empty);

            return clean.ToLowerInvariant().Trim();
        }

        private async Task<string?> EnsureFileCached(string url, string localPath, TimeSpan maxAge, CancellationToken cancellationToken)
        {
            if (File.Exists(localPath) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(localPath)) < maxAge)
            {
                return localPath;
            }

            try
            {
                _logger.LogInformation("Caching remote resource: {Url}", url);
                var client = _httpClientFactory.CreateClient(NamedClient.Default);
                using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var tempFile = localPath + ".tmp";
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await response.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
                }

                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }

                File.Move(tempFile, localPath);

                return localPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching and caching remote resource from {Url}", url);
                if (File.Exists(localPath))
                {
                    _logger.LogWarning("Using stale cache file for: {Path}", localPath);
                    return localPath;
                }

                return null;
            }
        }

        private async Task<T?> LoadJsonAsync<T>(string path, CancellationToken cancellationToken)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading JSON cache file from: {Path}", path);
                return default;
            }
        }

        private void LoadMappingsFromDisk()
        {
            var baseCacheDir = Path.Combine(_config.ApplicationPaths.CachePath, "iptvorg");
            var mappingsPath = Path.Combine(baseCacheDir, "mappings.json");

            if (!File.Exists(mappingsPath))
            {
                return;
            }

            try
            {
                using var stream = File.OpenRead(mappingsPath);
                _mappings = JsonSerializer.Deserialize<List<IptvOrgChannelMapping>>(stream) ?? new List<IptvOrgChannelMapping>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading mappings from: {Path}", mappingsPath);
            }
        }

        private void SaveMappingsToDisk()
        {
            var baseCacheDir = Path.Combine(_config.ApplicationPaths.CachePath, "iptvorg");
            var mappingsPath = Path.Combine(baseCacheDir, "mappings.json");

            try
            {
                using var stream = File.Create(mappingsPath);
                JsonSerializer.Serialize(stream, _mappings, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving mappings to disk: {Path}", mappingsPath);
            }
        }

        private void EnsureListingProviderAdded()
        {
            var config = _config.GetLiveTvConfiguration();
            var providers = config.ListingProviders.ToList();

            var iptvProvider = providers.FirstOrDefault(p => string.Equals(p.Type, "iptvorg", StringComparison.OrdinalIgnoreCase));
            if (iptvProvider == null)
            {
                _logger.LogInformation("Registering automatic iptv-org EPG listing provider in configuration.");
                var newProvider = new ListingsProviderInfo
                {
                    Id = "iptvorg-auto-provider",
                    Type = "iptvorg",
                    Path = "https://iptv-org.github.io/epg",
                    EnableAllTuners = true
                };

                providers.Add(newProvider);
                config.ListingProviders = providers.ToArray();
                _config.SaveConfiguration("livetv", config);
            }
        }

        // API DTOs matching the real iptv-org API structure

        private class IptvChannelDto
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("alt_names")]
            public List<string>? AltNames { get; set; }

            [JsonPropertyName("country")]
            public string Country { get; set; } = string.Empty;
        }

        private class IptvGuideDto
        {
            [JsonPropertyName("channel")]
            public string? Channel { get; set; }

            [JsonPropertyName("feed")]
            public string? Feed { get; set; }

            [JsonPropertyName("site")]
            public string Site { get; set; } = string.Empty;

            [JsonPropertyName("site_id")]
            public string SiteId { get; set; } = string.Empty;

            [JsonPropertyName("site_name")]
            public string SiteName { get; set; } = string.Empty;

            [JsonPropertyName("lang")]
            public string Lang { get; set; } = string.Empty;
        }
    }
}
