using System.Collections.Generic;
using System.Text.Json;
using MediaBrowser.Providers.Plugins.MidiaStorageOnline;
using Xunit;

namespace MulletaFlix.Providers.Tests.Plugins.MidiaStorageOnline
{
    public class MidiaStorageOnlineEpgMapperTests
    {
        [Fact]
        public void BuildCatalog_UsesPlanilhaMappedXmltvId_WhenEntryAlreadyHasWrongTvgId()
        {
            var entries = new List<IMidiaStorageOnlineM3uEntry>
            {
                new FakeM3uEntry
                {
                    Type = "Canal",
                    Name = "DISCOVERY CHANNEL FHD",
                    TvgName = "DISCOVERY CHANNEL FHD",
                    TvgId = "WRONG.ID"
                }
            };

            using var guideDocument = JsonDocument.Parse("[]");

            var result = MidiaStorageOnlineEpgMapper.BuildCatalog(entries, guideDocument);

            Assert.Single(result.Channels);
            Assert.Equal("DSC.BR", result.Channels[0].XmltvId);
            Assert.Equal("manual", result.Channels[0].Site);
            Assert.Equal(1, result.UniqueChannelCount);
            Assert.Equal(0, result.GuideMatchCount);
            Assert.Equal(0, result.OverrideCount);
            Assert.Equal(1, result.SyntheticCount);
        }

        [Fact]
        public void BuildCatalog_UsesApprovedWorkbookMapping_WhenChannelIsListedInApprovedSpreadsheet()
        {
            var entries = new List<IMidiaStorageOnlineM3uEntry>
            {
                new FakeM3uEntry
                {
                    Type = "Canal",
                    Name = "HBO MUNDI FHD",
                    TvgName = "HBO MUNDI FHD"
                }
            };

            using var guideDocument = JsonDocument.Parse("[]");

            var result = MidiaStorageOnlineEpgMapper.BuildCatalog(entries, guideDocument);

            Assert.Single(result.Channels);
            Assert.Equal("MAX.BR", result.Channels[0].XmltvId);
            Assert.Equal("synthetic", result.Channels[0].Source);
            Assert.Equal(0, result.GuideMatchCount);
            Assert.Equal(1, result.SyntheticCount);
        }

        [Fact]
        public void ApprovedMappings_ExposeMatchedChannel_FromWorkbook()
        {
            var matchedChannel = MidiaStorageOnlineApprovedChannelMappings.TryGetMatchedChannel(
                "DISCOVERY CHANNEL FHD",
                "DISCOVERY CHANNEL FHD",
                "DSC.BR");

            Assert.Equal("DiscoveryChannel.au", matchedChannel);
        }

        private sealed class FakeM3uEntry : IMidiaStorageOnlineM3uEntry
        {
            public string Type { get; init; } = "Canal";

            public string Name { get; init; } = string.Empty;

            public string? TvgId { get; init; }

            public string? TvgName { get; init; }

            public string? GroupTitle { get; init; }

            public string? TvgLogo { get; set; }
        }
    }
}

