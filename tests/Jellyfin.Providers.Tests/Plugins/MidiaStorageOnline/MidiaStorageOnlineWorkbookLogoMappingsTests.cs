using MediaBrowser.Providers.Plugins.MidiaStorageOnline;
using Xunit;

namespace MulletaFlix.Providers.Tests.Plugins.MidiaStorageOnline
{
    public class MidiaStorageOnlineWorkbookLogoMappingsTests
    {
        [Theory]
        [InlineData("CNN BRASIL MONEY FHD", "CNN BRASIL MONEY FHD", "CNNBRASILMONEYFHD", "https://upload.wikimedia.org/wikipedia/commons/thumb/5/5f/CNN_Brasil.svg/960px-CNN_Brasil.svg.png")]
        [InlineData("PREMIERE CLUBES FHD", "PREMIERE CLUBES FHD", "PRFCL.BR", "https://upload.wikimedia.org/wikipedia/commons/thumb/2/20/Premiere_(2017)_logo.png/960px-Premiere_(2017)_logo.png")]
        [InlineData("DISCOVERY CHANNEL FHD", "DISCOVERY CHANNEL FHD", "DSC.BR", "https://upload.wikimedia.org/wikipedia/commons/2/27/Discovery_Channel_-_Logo_2019.svg")]
        [InlineData("HBO MUNDI FHD", "HBO MUNDI FHD", "MAX.BR", "https://upload.wikimedia.org/wikipedia/commons/thumb/e/e1/HBO_Mundi.svg/960px-HBO_Mundi.svg.png")]
        public void TryGetLogoUrl_UsesWorkbookCatalog_ForKnownChannels(string tvgName, string displayName, string tvgId, string expectedUrl)
        {
            var actual = MidiaStorageOnlineWorkbookLogoMappings.TryGetLogoUrl(tvgName, displayName, tvgId);

            Assert.Equal(expectedUrl, actual);
        }
    }
}

