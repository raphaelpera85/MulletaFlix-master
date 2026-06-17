using System;
using System.Collections.Generic;
using MediaBrowser.Providers.Plugins.MidiaStorageOnline;
using Xunit;

namespace MulletaFlix.Providers.Tests.Plugins.MidiaStorageOnline
{
    public class MidiaStorageOnlineEntryDeduplicatorTests
    {
        [Fact]
        public void DeduplicateByKey_KeepsFirstEntry_ForRepeatedKeys()
        {
            var entries = new[]
            {
                new TestEntry("Filme|Movie A", "first"),
                new TestEntry("Filme|Movie A", "second"),
                new TestEntry("Filme|Movie B", "third")
            };

            var result = MidiaStorageOnlineEntryDeduplicator.DeduplicateByKey(entries, e => e.Key);

            Assert.Equal(2, result.Count);
            Assert.Equal("first", result[0].Value);
            Assert.Equal("third", result[1].Value);
        }

        private sealed record TestEntry(string Key, string Value);
    }
}
