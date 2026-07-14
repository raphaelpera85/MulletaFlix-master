#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace MediaBrowser.Providers.Plugins.MidiaStorageOnline;

internal static class MidiaStorageOnlineEpgXmlMerger
{
    public static void AddDocument(
        XmlDocument document,
        IReadOnlyList<MidiaStorageOnlineEpgChannel> catalog,
        IDictionary<string, string> channelNodes,
        ICollection<string> programmeNodes)
    {
        var sourceToTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var channels = document.SelectNodes("//channel");
        if (channels is not null)
        {
            foreach (XmlNode channel in channels)
            {
                var sourceId = channel.Attributes?["id"]?.Value;
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    continue;
                }

                var target = catalog.FirstOrDefault(item =>
                    string.Equals(item.XmltvId, sourceId, StringComparison.OrdinalIgnoreCase)
                    || Normalize(item.DisplayName) == Normalize(channel.SelectSingleNode("display-name")?.InnerText));

                var targetId = target?.XmltvId ?? sourceId;
                if (!string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    channel.Attributes!["id"]!.Value = targetId;
                    sourceToTarget[sourceId] = targetId;
                }

                channelNodes[targetId] = channel.OuterXml;
            }
        }

        var programmes = document.SelectNodes("//programme");
        if (programmes is not null)
        {
            foreach (XmlNode programme in programmes)
            {
                var sourceId = programme.Attributes?["channel"]?.Value;
                if (!string.IsNullOrWhiteSpace(sourceId)
                    && sourceToTarget.TryGetValue(sourceId, out var targetId))
                {
                    programme.Attributes!["channel"]!.Value = targetId;
                }

                programmeNodes.Add(programme.OuterXml);
            }
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ToUpperInvariant().Replace("BR - ", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"\b(FHD|UHD|HD|SD|4K|H265|H264|HEVC)\b", string.Empty);
        return Regex.Replace(normalized, "[^A-Z0-9]+", string.Empty);
    }
}
