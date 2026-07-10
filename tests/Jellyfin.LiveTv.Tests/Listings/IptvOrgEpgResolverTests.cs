using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.LiveTv.Listings;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MulletaFlix.LiveTv.Tests.Listings
{
    public class IptvOrgEpgResolverTests
    {
        private readonly Mock<IIptvOrgEpgSynchronizer> _synchronizerMock;
        private readonly Mock<IServerConfigurationManager> _configMock;
        private readonly Mock<ILogger<IptvOrgListingsProvider>> _loggerMock;

        public IptvOrgEpgResolverTests()
        {
            _synchronizerMock = new Mock<IIptvOrgEpgSynchronizer>();
            _configMock = new Mock<IServerConfigurationManager>();
            _loggerMock = new Mock<ILogger<IptvOrgListingsProvider>>();

            // Setup default configuration mock
            _configMock.Setup(c => c.Configuration).Returns(new MediaBrowser.Model.Configuration.ServerConfiguration
            {
                PreferredMetadataLanguage = "pt-BR"
            });
        }

        [Fact]
        public async Task GetProgramsAsync_WithExactChannelIdMatch_ReturnsPrograms()
        {
            // Arrange
            var tempXml = Path.GetTempFileName();
            File.WriteAllText(tempXml, GetSampleXmlTvContent("Globo.br"));

            var mappings = new List<IptvOrgChannelMapping>
            {
                new IptvOrgChannelMapping
                {
                    TunerChannelId = "tuner-globo",
                    TunerChannelName = "Globo RJ",
                    IptvOrgChannelId = "Globo.br",
                    Country = "br",
                    Site = "mi.tv",
                    LocalXmlPath = tempXml
                }
            };

            _synchronizerMock.Setup(s => s.GetMappings()).Returns(mappings);

            var provider = new IptvOrgListingsProvider(_synchronizerMock.Object, _configMock.Object, _loggerMock.Object);
            var providerInfo = new ListingsProviderInfo();

            try
            {
                // Act
                var result = await provider.GetProgramsAsync(
                    providerInfo,
                    "Globo.br",
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow.AddDays(1),
                    CancellationToken.None);

                // Assert
                Assert.NotEmpty(result);
                Assert.Equal("Globo.br_2026-07-10T00:00:00.0000000+00:00", result.First().Id); // Check unique constructed program id
            }
            finally
            {
                if (File.Exists(tempXml))
                {
                    File.Delete(tempXml);
                }
            }
        }

        [Fact]
        public async Task GetProgramsAsync_WithChannelNameFallback_ReturnsPrograms()
        {
            // Arrange
            var tempXml = Path.GetTempFileName();
            File.WriteAllText(tempXml, GetSampleXmlTvContent("Globo.br"));

            var mappings = new List<IptvOrgChannelMapping>
            {
                new IptvOrgChannelMapping
                {
                    TunerChannelId = "tuner-globo",
                    TunerChannelName = "Globo RJ HD",
                    IptvOrgChannelId = "Globo.br",
                    Country = "br",
                    Site = "mi.tv",
                    LocalXmlPath = tempXml
                }
            };

            _synchronizerMock.Setup(s => s.GetMappings()).Returns(mappings);

            var provider = new IptvOrgListingsProvider(_synchronizerMock.Object, _configMock.Object, _loggerMock.Object);
            var providerInfo = new ListingsProviderInfo();

            try
            {
                // Act - searching by Name "Globo RJ HD" (with suffixes) instead of ID
                var result = await provider.GetProgramsAsync(
                    providerInfo,
                    "Globo RJ HD",
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow.AddDays(1),
                    CancellationToken.None);

                // Assert
                Assert.NotEmpty(result);
                Assert.Equal("Globo.br", result.First().ChannelId);
            }
            finally
            {
                if (File.Exists(tempXml))
                {
                    File.Delete(tempXml);
                }
            }
        }

        private static string GetSampleXmlTvContent(string channelId)
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<tv generator-info-name=""iptv-org"">
  <channel id=""{channelId}"">
    <display-name>Sample Channel</display-name>
  </channel>
  <programme start=""20260710000000 +0000"" stop=""20260710020000 +0000"" channel=""{channelId}"">
    <title lang=""pt"">Jornal da Globo</title>
    <desc lang=""pt"">Notícias diárias.</desc>
  </programme>
</tv>";
        }
    }
}
