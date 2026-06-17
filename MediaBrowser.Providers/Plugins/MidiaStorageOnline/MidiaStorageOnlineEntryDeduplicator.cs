#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaBrowser.Providers.Plugins.MidiaStorageOnline
{
    internal static class MidiaStorageOnlineEntryDeduplicator
    {
        internal static IReadOnlyList<TEntry> DeduplicateByKey<TEntry>(
            IEnumerable<TEntry> entries,
            Func<TEntry, string> keySelector)
        {
            var deduped = new Dictionary<string, TEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var key = keySelector(entry);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!deduped.ContainsKey(key))
                {
                    deduped[key] = entry;
                }
            }

            return deduped.Values.ToList();
        }
    }
}
