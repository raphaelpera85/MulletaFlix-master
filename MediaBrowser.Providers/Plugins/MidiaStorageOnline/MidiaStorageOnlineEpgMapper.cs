#nullable enable
#pragma warning disable SA1402, SA1649

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaBrowser.Providers.Plugins.MidiaStorageOnline
{
    internal interface IMidiaStorageOnlineM3uEntry
    {
        string Type { get; }

        string Name { get; }

        string? TvgId { get; }

        string? TvgName { get; }

        string? GroupTitle { get; }

        string? TvgLogo { get; set; }
    }

    internal sealed record MidiaStorageOnlineEpgChannel(
        string Site,
        string SiteId,
        string Lang,
        string XmltvId,
        string DisplayName,
        string Source);

    internal sealed record MidiaStorageOnlineEpgBuildResult(
        IReadOnlyList<MidiaStorageOnlineEpgChannel> Channels,
        int UniqueChannelCount,
        int GuideMatchCount,
        int OverrideCount,
        int SyntheticCount);

    internal static class MidiaStorageOnlineEpgMapper
    {
        private static readonly Regex _qualityRegex = new(@"\b(FHD|UHD|HD|SD|4K|H265|H264|HEVC)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Dictionary<string, MidiaStorageOnlineEpgChannel> _manualOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["HDHB2.BR"] = new("clarotvmais.com.br", "261", "pt", "HDHB2.BR", "HBO2", "override"),
            ["PRFC2.BR"] = new("clarotvmais.com.br", "108", "pt", "PRFC2.BR", "PREMIERE 2 HD", "override"),
            ["PRFCL.BR"] = new("clarotvmais.com.br", "107", "pt", "PRFCL.BR", "PREMIERE CLUBES HD", "override"),
            ["HDREC.BR"] = new("claro.com.br", "1_465", "pt", "HDREC.BR", "Record", "override"),
            ["HDFUT.BR"] = new("clarotvmais.com.br", "95", "pt", "HDFUT.BR", "FUTURA HD", "override"),
            ["HDHGT.BR"] = new("clarotvmais.com.br", "115", "pt", "HDHGT.BR", "HGTV HD", "override"),
            ["SPOR.BR"] = new("clarotvmais.com.br", "30", "pt", "SPOR.BR", "SPORTV", "override"),
            ["PBBR.BR"] = new("mi.tv", "br#prime-box-brazil", "pt", "PBBR.BR", "Prime Box Brazil", "override"),
            ["24HS.BR"] = new("pluto.tv", "5f997e44949bc70007a6941e", "pt", "24HS.BR", "Turma da Mônica", "override")
        };

        private static readonly HashSet<string> _syntheticIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "EVENTOS.BR",
            "CINESKY.BR"
        };

        public static MidiaStorageOnlineEpgBuildResult BuildCatalog(IEnumerable<IMidiaStorageOnlineM3uEntry> entries, JsonDocument guidesDocument)
        {
            var uniqueChannels = entries
                .Where(e => string.Equals(e.Type, "Canal", StringComparison.OrdinalIgnoreCase))
                .Select(e => new
                {
                    Entry = e,
                    TvgId = GetEffectiveTvgId(e)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.TvgId))
                .GroupBy(x => x.TvgId!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var guideCandidates = BuildGuideCandidates(guidesDocument);
            var indexedCandidates = BuildCandidateIndex(guideCandidates);

            var channels = new List<MidiaStorageOnlineEpgChannel>(uniqueChannels.Count);
            var guideMatchCount = 0;
            var overrideCount = 0;
            var syntheticCount = 0;

            foreach (var item in uniqueChannels)
            {
                var entry = item.Entry;
                var tvgId = item.TvgId!.Trim();
                if (_manualOverrides.TryGetValue(tvgId, out var overrideEntry))
                {
                    channels.Add(overrideEntry);
                    overrideCount++;
                    continue;
                }

                var target = NormalizeKey(CanonicalizeChannelName(entry.TvgName ?? entry.Name));
                var candidate = FindBestCandidate(target, indexedCandidates);
                if (candidate is not null)
                {
                    channels.Add(new MidiaStorageOnlineEpgChannel(
                        candidate.Site,
                        candidate.SiteId,
                        candidate.Lang,
                        tvgId,
                        candidate.DisplayName,
                        "guide"));
                    guideMatchCount++;
                    continue;
                }

                if (_syntheticIds.Contains(tvgId))
                {
                    channels.Add(CreateSyntheticChannel(entry, tvgId));
                    syntheticCount++;
                    continue;
                }

                channels.Add(CreateSyntheticChannel(entry, tvgId));
                syntheticCount++;
            }

            return new MidiaStorageOnlineEpgBuildResult(
                channels,
                uniqueChannels.Count,
                guideMatchCount,
                overrideCount,
                syntheticCount);
        }

        private static List<GuideCandidate> BuildGuideCandidates(JsonDocument guidesDocument)
        {
            var candidates = new List<GuideCandidate>();

            foreach (var element in guidesDocument.RootElement.EnumerateArray())
            {
                var site = element.TryGetProperty("site", out var siteProp) ? siteProp.GetString() : null;
                var siteId = element.TryGetProperty("site_id", out var siteIdProp) ? siteIdProp.GetString() : null;
                var feed = element.TryGetProperty("feed", out var feedProp) ? feedProp.GetString() : null;
                var lang = (element.TryGetProperty("lang", out var langProp) ? langProp.GetString() : null) ?? "pt";
                var channel = element.TryGetProperty("channel", out var channelProp) ? channelProp.GetString() : null;
                var siteName = element.TryGetProperty("site_name", out var siteNameProp) ? siteNameProp.GetString() : null;

                if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(siteId))
                {
                    continue;
                }

                candidates.Add(new GuideCandidate(
                    site,
                    siteId,
                    feed,
                    lang,
                    channel,
                    siteName,
                    NormalizeKey(siteName),
                    NormalizeKey(channel)));
            }

            return candidates;
        }

        private static Dictionary<string, List<GuideCandidate>> BuildCandidateIndex(List<GuideCandidate> candidates)
        {
            var index = new Dictionary<string, List<GuideCandidate>>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                foreach (var key in candidate.SearchKeys)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (!index.TryGetValue(key, out var bucket))
                    {
                        bucket = new List<GuideCandidate>();
                        index[key] = bucket;
                    }

                    bucket.Add(candidate);
                }
            }

            return index;
        }

        private static GuideCandidate? FindBestCandidate(string target, Dictionary<string, List<GuideCandidate>> indexedCandidates)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            if (indexedCandidates.TryGetValue(target, out var exactMatches))
            {
                return exactMatches
                    .OrderByDescending(GetCandidatePriority)
                    .ThenByDescending(c => string.Equals(c.Lang, "pt", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(c => c.DisplayName.Length)
                    .FirstOrDefault();
            }

            GuideCandidate? bestCandidate = null;
            var bestScore = int.MinValue;

            foreach (var bucket in indexedCandidates.Values)
            {
                foreach (var candidate in bucket)
                {
                    var score = ScoreCandidate(target, candidate);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCandidate = candidate;
                    }
                }
            }

            return bestScore >= 70 ? bestCandidate : null;
        }

        private static int ScoreCandidate(string target, GuideCandidate candidate)
        {
            var best = int.MinValue;
            foreach (var candidateKey in candidate.SearchKeys)
            {
                if (string.IsNullOrWhiteSpace(candidateKey))
                {
                    continue;
                }

                int score;
                if (string.Equals(target, candidateKey, StringComparison.OrdinalIgnoreCase))
                {
                    score = 1000;
                }
                else if (target.Contains(candidateKey, StringComparison.OrdinalIgnoreCase) || candidateKey.Contains(target, StringComparison.OrdinalIgnoreCase))
                {
                    score = 800 - (Math.Abs(target.Length - candidateKey.Length) * 2);
                }
                else
                {
                    score = 100 - Math.Abs(target.Length - candidateKey.Length);
                }

                score += GetCandidatePriority(candidate);
                if (string.Equals(candidate.Lang, "pt", StringComparison.OrdinalIgnoreCase))
                {
                    score += 25;
                }

                if (score > best)
                {
                    best = score;
                }
            }

            return best;
        }

        private static int GetCandidatePriority(GuideCandidate candidate)
        {
            var site = candidate.Site.ToLowerInvariant();
            if (site == "clarotvmais.com.br")
            {
                return 200;
            }

            if (site == "claro.com.br")
            {
                return 190;
            }

            if (site == "mi.tv")
            {
                return 180;
            }

            if (site == "meuguia.tv")
            {
                return 170;
            }

            if (site == "guiadetv.com")
            {
                return 160;
            }

            if (site == "pluto.tv")
            {
                return 150;
            }

            if (site == "vivoplay.com.br")
            {
                return 140;
            }

            if (site == "epgshare01.online")
            {
                return 20;
            }

            return 10;
        }

        private static string GetEffectiveTvgId(IMidiaStorageOnlineM3uEntry entry)
        {
            var mappedTvgId = MidiaStorageOnlineChannelMappings.ResolveTvgId(
                entry.TvgId,
                entry.TvgName,
                entry.GroupTitle,
                entry.Name);

            if (!string.IsNullOrWhiteSpace(mappedTvgId))
            {
                return mappedTvgId;
            }

            return BuildSyntheticSiteId(entry.TvgName ?? entry.GroupTitle ?? entry.Name);
        }

        private static MidiaStorageOnlineEpgChannel CreateSyntheticChannel(IMidiaStorageOnlineM3uEntry entry, string tvgId)
        {
            var displayName = entry.TvgName ?? entry.Name;
            return new MidiaStorageOnlineEpgChannel(
                "manual",
                BuildSyntheticSiteId(displayName),
                "pt",
                tvgId,
                displayName,
                "synthetic");
        }

        private static string BuildSyntheticSiteId(string? sourceName)
        {
            var normalized = NormalizeForSiteId(sourceName);
            return string.IsNullOrWhiteSpace(normalized) ? "manual" : normalized;
        }

        private static string CanonicalizeChannelName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = RemoveDiacritics(value.ToUpperInvariant());
            text = Regex.Replace(text, @"\bSBT\s+(MG|BH)\b", "SBT BELO HORIZONTE", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+SP\b", "SBT SAO PAULO", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+RJ\b", "SBT RIO", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+DF\b", "SBT BRASILIA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+GO\b", "SBT GOIANIA TV SERRA DOURADA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+MS\b", "SBT MS", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+RS\b", "SBT RS", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+SC\b", "SBT SANTA CATARINA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+PA\b", "SBT PARA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+TO\b", "SBT TOCANTINS", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+AM\b", "SBT MANAUS", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+AL\b", "SBT MACEIO", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+ES\b", "SBT VITORIA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+BA\b", "SBT SALVADOR", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+PI\b", "SBT PIAUI", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+RN\b", "SBT NATAL", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+SE\b", "SBT SERGIPE", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+MT\b", "SBT CUIABA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+CE\b", "SBT FORTALEZA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bSBT\s+PR\b", "SBT CASCAVEL", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"\bGLOBO\s+(MG|BH)\b", "GLOBO BELO HORIZONTE MINAS", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bGLOBO\s+SP\b", "GLOBO SAO PAULO", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bGLOBO\s+RJ\b", "GLOBO RIO DE JANEIRO RIO", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bGLOBO\s+RS\b", "GLOBO RS PORTO ALEGRE", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bGLOBO\s+PR\b", "GLOBO CURITIBA PARANA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bGLOBO\s+SC\b", "GLOBO FLORIANOPOLIS SANTA CATARINA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bGLOBO\s+DF\b", "GLOBO BRASILIA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bGLOBO\s+GO\b", "GLOBO GOIANIA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bGLOBO\s+BA\b", "GLOBO SALVADOR", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"\bRECORD\s+(MG|BH)\b", "RECORD MINAS BELO HORIZONTE", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bRECORD\s+SP\b", "RECORD SAO PAULO", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bRECORD\s+RJ\b", "RECORD RIO DE JANEIRO RIO", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bRECORD\s+PR\b", "RECORD CURITIBA PARANA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bRECORD\s+RS\b", "RECORD RS PORTO ALEGRE", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bRECORD\s+SC\b", "RECORD SANTA CATARINA FLORIANOPOLIS", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bRECORD\s+GO\b", "RECORD GOIANIA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bRECORD\s+DF\b", "RECORD BRASILIA", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"\bBAND\s+(MG|BH)\b", "BAND MINAS BELO HORIZONTE", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bBAND\s+SP\b", "BAND SAO PAULO", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bBAND\s+RJ\b", "BAND RIO DE JANEIRO RIO", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bBAND\s+RS\b", "BAND RS PORTO ALEGRE", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bBAND\s+PR\b", "BAND CURITIBA PARANA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bBAND\s+SC\b", "BAND SANTA CATARINA FLORIANOPOLIS", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bBAND\s+DF\b", "BAND BRASILIA", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bBAND\s+GO\b", "BAND GOIANIA", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"DISCOVERY\s+(H\s*&\s*H|HOME\s*&\s*HEALTH)", "DISCOVERY HOME HEALTH", RegexOptions.IgnoreCase);

            return text;
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string NormalizeKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = CanonicalizeChannelName(value).Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                var translated = ch switch
                {
                    '²' => '2',
                    '³' => '3',
                    _ => ch
                };

                sb.Append(translated);
            }

            var text = _qualityRegex.Replace(sb.ToString(), " ");
            text = Regex.Replace(text, @"[^A-Za-z0-9]+", " ");
            text = text.ToUpperInvariant();

            sb.Clear();
            foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    sb.Append(number.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(token);
                }
            }

            return sb.ToString();
        }

        private static string NormalizeForSiteId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = CanonicalizeChannelName(value).Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToUpperInvariant(ch));
                }
            }

            return sb.ToString();
        }

        private sealed record GuideCandidate(
            string Site,
            string SiteId,
            string? Feed,
            string Lang,
            string? Channel,
            string? SiteName,
            string SiteNameKey,
            string ChannelKey)
        {
            public string DisplayName => !string.IsNullOrWhiteSpace(SiteName) ? SiteName! : Channel ?? SiteId;

            public IReadOnlyList<string> SearchKeys => new[]
            {
                SiteNameKey,
                ChannelKey
            };
        }
    }
}
