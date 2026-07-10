using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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

            // 1. Download and Cache iptv-org APIs
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

            // 2. Load API data into memory
            _logger.LogInformation("Loading iptv-org metadata...");
            var iptvChannels = await LoadJsonAsync<List<IptvChannelDto>>(channelsJsonPath, cancellationToken).ConfigureAwait(false) ?? new List<IptvChannelDto>();
            var iptvGuides = await LoadJsonAsync<List<IptvGuideDto>>(guidesJsonPath, cancellationToken).ConfigureAwait(false) ?? new List<IptvGuideDto>();

            _logger.LogInformation("Loaded {ChannelCount} channels and {GuideCount} guides from iptv-org API.", iptvChannels.Count, iptvGuides.Count);

            var channelsByNormalizedName = GroupChannelsByNormalizedName(iptvChannels);
            var guidesByChannelId = iptvGuides.ToDictionary(g => g.Channel, g => g.Site, StringComparer.OrdinalIgnoreCase);

            // 3. Resolve tuner channels
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

            _logger.LogInformation("Found {Count} sintonized channels to resolve.", tunerChannels.Count);

            // 4. Match tuner channels with iptv-org channels
            var newMappings = new List<IptvOrgChannelMapping>();
            var xmlsToDownload = new HashSet<(string Country, string Site)>();

            var preferredCountry = GetPreferredCountry();

            foreach (var tc in tunerChannels)
            {
                if (string.IsNullOrWhiteSpace(tc.Name))
                {
                    continue;
                }

                var match = FindBestMatch(tc, channelsByNormalizedName, preferredCountry);
                if (match != null && guidesByChannelId.TryGetValue(match.Id, out var site))
                {
                    var country = match.Country.ToLowerInvariant();
                    var localXmlPath = Path.Combine(baseCacheDir, "xmltv", $"{country}_{site}.xml");

                    newMappings.Add(new IptvOrgChannelMapping
                    {
                        TunerChannelId = tc.Id,
                        TunerChannelName = tc.Name,
                        IptvOrgChannelId = match.Id,
                        Country = country,
                        Site = site,
                        LocalXmlPath = localXmlPath
                    });

                    xmlsToDownload.Add((country, site));
                }
                else
                {
                    _logger.LogDebug("Could not resolve iptv-org EPG for channel: {Name} (Id: {Id})", tc.Name, tc.Id);
                }
            }

            _logger.LogInformation("Resolved {Count} mappings. Downloading XMLTV files...", newMappings.Count);

            // 5. Download required XMLTV files
            var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
            var downloadTasks = xmlsToDownload.Select(async target =>
            {
                var url = $"https://iptv-org.github.io/epg/guides/{target.Country}/{target.Site}.epg.xml";
                var localPath = Path.Combine(baseCacheDir, "xmltv", $"{target.Country}_{target.Site}.xml");

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

            // 6. Save mappings to disk
            lock (_lock)
            {
                _mappings = newMappings;
                SaveMappingsToDisk();
            }

            // 7. Ensure Listing Provider is added to config
            EnsureListingProviderAdded();

            _logger.LogInformation("iptv-org EPG synchronization completed successfully.");
        }

        private string GetPreferredCountry()
        {
            var culture = _config.Configuration.PreferredMetadataLanguage;
            if (string.IsNullOrWhiteSpace(culture))
            {
                return "br";
            }

            if (culture.Contains("-", StringComparison.OrdinalIgnoreCase))
            {
                return culture.Split('-').Last().ToLowerInvariant();
            }

            return culture.ToLowerInvariant();
        }

        private static Dictionary<string, List<IptvChannelDto>> GroupChannelsByNormalizedName(List<IptvChannelDto> channels)
        {
            var groups = new Dictionary<string, List<IptvChannelDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var ch in channels)
            {
                if (string.IsNullOrWhiteSpace(ch.Name))
                {
                    continue;
                }

                var normalized = NormalizeName(ch.Name);
                if (string.IsNullOrEmpty(normalized))
                {
                    continue;
                }

                if (!groups.TryGetValue(normalized, out var list))
                {
                    list = new List<IptvChannelDto>();
                    groups[normalized] = list;
                }

                list.Add(ch);
            }

            return groups;
        }

        private static IptvChannelDto? FindBestMatch(ChannelInfo tc, Dictionary<string, List<IptvChannelDto>> channelsByName, string preferredCountry)
        {
            var normalizedTunerName = NormalizeName(tc.Name);
            if (string.IsNullOrEmpty(normalizedTunerName))
            {
                return null;
            }

            if (!channelsByName.TryGetValue(normalizedTunerName, out var candidates))
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            var tcCountry = ExtractCountryFromChannel(tc);
            if (!string.IsNullOrEmpty(tcCountry))
            {
                var countryMatch = candidates.FirstOrDefault(c => string.Equals(c.Country, tcCountry, StringComparison.OrdinalIgnoreCase));
                if (countryMatch != null)
                {
                    return countryMatch;
                }
            }

            var preferredMatch = candidates.FirstOrDefault(c => string.Equals(c.Country, preferredCountry, StringComparison.OrdinalIgnoreCase));
            if (preferredMatch != null)
            {
                return preferredMatch;
            }

            return candidates[0];
        }

        private static string ExtractCountryFromChannel(ChannelInfo tc)
        {
            var match = Regex.Match(tc.Name, @"\b([A-Z]{2})\b");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return string.Empty;
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

        // API DTOs
        private class IptvChannelDto
        {
            public string Id { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;

            public string Country { get; set; } = string.Empty;
        }

        private class IptvGuideDto
        {
            public string Channel { get; set; } = string.Empty;

            public string Site { get; set; } = string.Empty;
        }
    }
}
