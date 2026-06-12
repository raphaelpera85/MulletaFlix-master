using System;
using System.IO;
using System.Linq;

namespace MulletaFlix.Api.Helpers;

/// <summary>
/// Resolves files that were indexed under a previous Windows drive letter.
/// </summary>
internal static class PathFallbackHelper
{
    /// <summary>
    /// Returns the original path when it exists, otherwise tries the same relative path on other mounted drives.
    /// </summary>
    /// <param name="path">The indexed file path.</param>
    /// <returns>An existing fallback path, or the original path when no fallback is found.</returns>
    public static string ResolveExistingFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
        {
            return path;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':')
        {
            return path;
        }

        var relativePath = path[root.Length..];
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return path;
        }

        foreach (var drive in Directory.GetLogicalDrives().Where(d => !string.Equals(d, root, StringComparison.OrdinalIgnoreCase)))
        {
            var candidate = Path.Combine(drive, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }
}
