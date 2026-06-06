using MediaBrowser.Providers.Plugins.MidiaStorageOnline;
using Xunit;

namespace Jellyfin.Providers.Tests.Plugins.MidiaStorageOnline
{
    public class MidiaStorageOnlineWorkbookLogoMappingsTests
    {
        [Theory]
        [InlineData("CNN BRASIL MONEY FHD", "CNN BRASIL MONEY FHD", "CNNBRASILMONEYFHD", "https://upload.wikimedia.org/wikipedia/commons/thumb/5/5f/CNN_Brasil.svg/960px-CNN_Brasil.svg.png")]
        [InlineData("PREMIERE CLUBES FHD", "PREMIERE CLUBES FHD", "PRFCL.BR", "https://upload.wikimedia.org/wikipedia/commons/thumb/2/20/Premiere_(2017)_logo.png/960px-Premiere_(2017)_logo.png")]
        [InlineData("DISCOVERY CHANNEL FHD", "DISCOVERY CHANNEL FHD", "DSC.BR", "https://upload.wikimedia.org/wikipedia/commons/thumb/f/f1/2019_Discovery_logo.svg/960px-2019_Discovery_logo.svg.png")]
        public void TryGetLogoUrl_UsesWorkbookCatalog_ForKnownChannels(string tvgName, string displayName, string tvgId, string expectedUrl)
        {
            var actual = MidiaStorageOnlineWorkbookLogoMappings.TryGetLogoUrl(tvgName, displayName, tvgId);

            Assert.Equal(expectedUrl, actual);
        }
    }
}
