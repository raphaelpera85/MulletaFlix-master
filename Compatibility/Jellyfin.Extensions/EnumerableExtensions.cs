using System;
using System.Collections.Generic;

namespace Jellyfin.Extensions;

public static class EnumerableExtensions
{
    public static bool Contains(this IEnumerable<string> source, ReadOnlySpan<char> value, StringComparison comparisonType)
    {
        ArgumentNullException.ThrowIfNull(source);

        foreach (var item in source)
        {
            if (value.Equals(item.AsSpan(), comparisonType))
            {
                return true;
            }
        }

        return false;
    }
}
