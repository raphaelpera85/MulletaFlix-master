using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Extensions;
using Jellyfin.XmlTv;
using Jellyfin.XmlTv.Entities;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.LiveTv.Listings
{
    public class IptvOrgListingsProvider : IListingsProvider
    {
        private readonly IIptvOrgEpgSynchronizer _synchronizer;
        private readonly IServerConfigurationManager _config;
        private readonly ILogger<IptvOrgListingsProvider> _logger;

        public IptvOrgListingsProvider(
            IIptvOrgEpgSynchronizer synchronizer,
            IServerConfigurationManager config,
            ILogger<IptvOrgListingsProvider> logger)
        {
            _synchronizer = synchronizer;
            _config = config;
            _logger = logger;
        }

        public string Name => "iptv-org";

        public string Type => "iptvorg";

        public Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings)
        {
            return Task.CompletedTask;
        }

        public Task<List<NameIdPair>> GetLineups(ListingsProviderInfo info, string country, string location)
        {
            return Task.FromResult(new List<NameIdPair>());
        }

        public Task<List<ChannelInfo>> GetChannels(ListingsProviderInfo info, CancellationToken cancellationToken)
        {
            var mappings = _synchronizer.GetMappings();
            var channels = mappings.Select(m => new ChannelInfo
            {
                Id = m.IptvOrgChannelId,
                Name = m.TunerChannelName,
                Number = m.TunerChannelId
            }).ToList();

            return Task.FromResult(channels);
        }

        public Task<IEnumerable<ProgramInfo>> GetProgramsAsync(
            ListingsProviderInfo info,
            string channelId,
            DateTime startDateUtc,
            DateTime endDateUtc,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                throw new ArgumentNullException(nameof(channelId));
            }

            var mappings = _synchronizer.GetMappings();
            var mapping = mappings.FirstOrDefault(m => string.Equals(m.TunerChannelId, channelId, StringComparison.OrdinalIgnoreCase) ||
                                                       string.Equals(m.IptvOrgChannelId, channelId, StringComparison.OrdinalIgnoreCase));

            // Fallback: search by channel name/display name
            if (mapping == null)
            {
                var normalizedId = NormalizeName(channelId);
                mapping = mappings.FirstOrDefault(m => string.Equals(m.TunerChannelName, channelId, StringComparison.OrdinalIgnoreCase) ||
                                                       NormalizeName(m.TunerChannelName) == normalizedId ||
                                                       NormalizeName(m.IptvOrgChannelId) == normalizedId);
            }

            if (mapping == null)
            {
                _logger.LogDebug("No iptv-org EPG mapping found for channel: {Id}", channelId);
                return Task.FromResult(Enumerable.Empty<ProgramInfo>());
            }

            if (!File.Exists(mapping.LocalXmlPath))
            {
                _logger.LogWarning("EPG XML file does not exist: {Path}", mapping.LocalXmlPath);
                return Task.FromResult(Enumerable.Empty<ProgramInfo>());
            }

            try
            {
                var preferredLanguage = info.PreferredLanguage;
                if (string.IsNullOrWhiteSpace(preferredLanguage))
                {
                    preferredLanguage = _config.Configuration.PreferredMetadataLanguage;
                }

                var reader = new XmlTvReader(mapping.LocalXmlPath, preferredLanguage);
                var programmes = reader.GetProgrammes(mapping.IptvOrgChannelId, startDateUtc, endDateUtc, cancellationToken);

                var programInfos = programmes.Select(p => GetProgramInfo(p, info)).ToList();
                return Task.FromResult<IEnumerable<ProgramInfo>>(programInfos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading XMLTV programmes for channel {ChannelId} from {Path}", channelId, mapping.LocalXmlPath);
                return Task.FromResult(Enumerable.Empty<ProgramInfo>());
            }
        }

        private static ProgramInfo GetProgramInfo(XmlTvProgram program, ListingsProviderInfo info)
        {
            string? episodeTitle = program.Episode?.Title;
            var programCategories = program.Categories.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            var imageUrl = program.Icons.FirstOrDefault()?.Source;
            var rating = program.Ratings.FirstOrDefault()?.Value;
            var starRating = program.StarRatings?.FirstOrDefault()?.StarRating;

            var programInfo = new ProgramInfo
            {
                ChannelId = program.ChannelId,
                EndDate = program.EndDate.UtcDateTime,
                EpisodeNumber = program.Episode?.Episode,
                EpisodeTitle = episodeTitle,
                Genres = programCategories,
                StartDate = program.StartDate.UtcDateTime,
                Name = program.Title,
                Overview = program.Description,
                ProductionYear = program.CopyrightDate?.Year,
                SeasonNumber = program.Episode?.Series,
                IsSeries = program.Episode?.Episode is not null,
                IsRepeat = program.IsPreviouslyShown && !program.IsNew,
                IsPremiere = program.Premiere is not null,
                IsLive = program.IsLive,
                IsKids = programCategories.Any(c => info.KidsCategories.Contains(c, StringComparison.OrdinalIgnoreCase)),
                IsMovie = programCategories.Any(c => info.MovieCategories.Contains(c, StringComparison.OrdinalIgnoreCase)),
                IsNews = programCategories.Any(c => info.NewsCategories.Contains(c, StringComparison.OrdinalIgnoreCase)),
                IsSports = programCategories.Any(c => info.SportsCategories.Contains(c, StringComparison.OrdinalIgnoreCase)),
                ImageUrl = string.IsNullOrEmpty(imageUrl) ? null : imageUrl,
                HasImage = !string.IsNullOrEmpty(imageUrl),
                OfficialRating = string.IsNullOrEmpty(rating) ? null : rating,
                CommunityRating = starRating is null ? null : (float)starRating.Value,
                SeriesId = program.Episode?.Episode is null ? null : program.Title?.GetMD5().ToString("N", CultureInfo.InvariantCulture)
            };

            if (string.IsNullOrWhiteSpace(program.ProgramId))
            {
                string uniqueString = (program.Title ?? string.Empty) + (episodeTitle ?? string.Empty);

                if (programInfo.SeasonNumber.HasValue)
                {
                    uniqueString = "-" + programInfo.SeasonNumber.Value.ToString(CultureInfo.InvariantCulture);
                }

                if (programInfo.EpisodeNumber.HasValue)
                {
                    uniqueString = "-" + programInfo.EpisodeNumber.Value.ToString(CultureInfo.InvariantCulture);
                }

                programInfo.ShowId = uniqueString.GetMD5().ToString("N", CultureInfo.InvariantCulture);

                if (programInfo.IsSeries
                    && !programInfo.IsRepeat
                    && (programInfo.EpisodeNumber ?? 0) == 0)
                {
                    programInfo.ShowId += programInfo.StartDate.Ticks.ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                programInfo.ShowId = program.ProgramId;
            }

            programInfo.Id = string.Format(CultureInfo.InvariantCulture, "{0}_{1:O}", program.ChannelId, program.StartDate);

            if (programInfo.IsMovie)
            {
                programInfo.IsSeries = false;
                programInfo.EpisodeNumber = null;
                programInfo.EpisodeTitle = null;
            }

            return programInfo;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var clean = System.Text.RegularExpressions.Regex.Replace(name, @"\b(hd|fhd|sd|4k|1080p|720p|h\.264|hevc)\b", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"[^\w]", string.Empty);
            return clean.ToLowerInvariant().Trim();
        }
    }
}
