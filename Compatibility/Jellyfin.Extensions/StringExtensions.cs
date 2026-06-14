using System;

namespace Jellyfin.Extensions;

public static class StringExtensions
{
    public static ReadOnlySpan<char> LeftPart(this ReadOnlySpan<char> str, char separator)
    {
        var index = str.IndexOf(separator);
        return index == -1 ? str : str[..index];
    }
}
