using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Emby.Naming.Common;

/// <summary>
/// Helpers for normalizing lookup titles derived from filenames and folders.
/// </summary>
public static partial class TitleNormalization
{
    private static readonly HashSet<string> ReleaseTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "leg",
        "legendado",
        "legendada",
        "dublado",
        "dublada",
        "dub",
        "sub",
        "ptbr",
        "pt-br",
        "pt br",
        "br",
        "brasil",
        "latino",
        "audiolatino",
        "audio latino",
        "originalaudio",
        "audio original"
    };

    [GeneratedRegex(@"^(?<cleaned>.+?)(?:[\s\._-]*[\(\[\{]\s*(?<tag>[^()\[\]{}]+?)\s*[\)\]\}]|[\s\._-]+(?<tag>[^()\[\]{}]+?))\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingReleaseTagRegex();

    /// <summary>
    /// Removes common release tags from the end of a lookup title.
    /// </summary>
    /// <param name="name">Raw title.</param>
    /// <returns>Normalized title.</returns>
    public static string RemoveTrailingReleaseTags(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var current = name.Trim();

        while (TryRemoveTrailingReleaseTag(current, out var cleaned))
        {
            current = cleaned.Trim();
        }

        return current;
    }

    private static bool TryRemoveTrailingReleaseTag(string name, out string cleaned)
    {
        var match = TrailingReleaseTagRegex().Match(name);
        if (!match.Success)
        {
            cleaned = string.Empty;
            return false;
        }

        var tag = NormalizeToken(match.Groups["tag"].Value);
        if (!ReleaseTags.Contains(tag))
        {
            cleaned = string.Empty;
            return false;
        }

        cleaned = match.Groups["cleaned"].Value;
        return true;
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value, @"[^\p{L}\p{N}]+", string.Empty).ToLowerInvariant();
    }
}
