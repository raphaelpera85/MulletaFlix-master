using Emby.Naming.Common;
using Emby.Naming.Video;
using Xunit;

namespace MulletaFlix.Naming.Tests.Video
{
    public sealed class CleanStringTests
    {
        private readonly NamingOptions _namingOptions = new NamingOptions();

        [Theory]
        [InlineData("Super movie 480p.mp4", "Super movie")]
        [InlineData("Super movie Multi.mp4", "Super movie")]
        [InlineData("Super movie 480p 2001.mp4", "Super movie")]
        [InlineData("Super movie [480p].mp4", "Super movie")]
        [InlineData("480 Super movie [tmdbid=12345].mp4", "480 Super movie")]
        [InlineData("Crouching.Tiger.Hidden.Dragon.4k.mkv", "Crouching.Tiger.Hidden.Dragon")]
        [InlineData("Crouching.Tiger.Hidden.Dragon.UltraHD.mkv", "Crouching.Tiger.Hidden.Dragon")]
        [InlineData("Crouching.Tiger.Hidden.Dragon.UHD.mkv", "Crouching.Tiger.Hidden.Dragon")]
        [InlineData("Crouching.Tiger.Hidden.Dragon.HDR.mkv", "Crouching.Tiger.Hidden.Dragon")]
        [InlineData("Crouching.Tiger.Hidden.Dragon.HDC.mkv", "Crouching.Tiger.Hidden.Dragon")]
        [InlineData("Crouching.Tiger.Hidden.Dragon-HDC.mkv", "Crouching.Tiger.Hidden.Dragon")]
        [InlineData("Crouching.Tiger.Hidden.Dragon.BDrip.mkv", "Crouching.Tiger.Hidden.Dragon")]
        [InlineData("Crouching.Tiger.Hidden.Dragon.BDrip-HDC.mkv", "Crouching.Tiger.Hidden.Dragon")]
        [InlineData("Crouching.Tiger.Hidden.Dragon.4K.UltraHD.HDR.BDrip-HDC.mkv", "Crouching.Tiger.Hidden.Dragon")]
        [InlineData("[HorribleSubs] Made in Abyss - 13 [720p].mkv", "Made in Abyss")]
        [InlineData("[Tsundere] Kore wa Zombie Desu ka of the Dead [BDRip h264 1920x1080 FLAC]", "Kore wa Zombie Desu ka of the Dead")]
        [InlineData("[Erai-raws] Jujutsu Kaisen - 03 [720p][Multiple Subtitle].mkv", "Jujutsu Kaisen")]
        [InlineData("[OCN] ì• íƒ€ëŠ” ë¡œë§¨ìŠ¤ 720p-NEXT", "ì• íƒ€ëŠ” ë¡œë§¨ìŠ¤")]
        [InlineData("[tvN] í˜¼ìˆ ë‚¨ë…€.E01-E16.720p-NEXT", "í˜¼ìˆ ë‚¨ë…€")]
        [InlineData("[tvN] ì—°ì• ë§ê³  ê²°í˜¼ E01~E16 END HDTV.H264.720p-WITH", "ì—°ì• ë§ê³  ê²°í˜¼")]
        [InlineData("2026å¹´01æœˆ10æ—¥23æ™‚00åˆ†00ç§’-[æ–°]TRIGUNã€€STARGAZE[å­—].mp4", "2026å¹´01æœˆ10æ—¥23æ™‚00åˆ†00ç§’-[æ–°]TRIGUNã€€STARGAZE")]
        // FIXME: [InlineData("After The Sunset - [0004].mkv", "After The Sunset")]
        public void CleanStringTest_NeedsCleaning_Success(string input, string expectedName)
        {
            Assert.True(VideoResolver.TryCleanString(input, _namingOptions, out var newName));
            Assert.Equal(expectedName, newName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Super movie(2009).mp4")]
        [InlineData("[rec].mkv")]
        [InlineData("American.Psycho.mkv")]
        [InlineData("American Psycho.mkv")]
        [InlineData("Run lola run (lola rennt) (2009).mp4")]
        [InlineData("2026å¹´01æœˆ05æ—¥00æ™‚55åˆ†00ç§’-[æ–°]é•å›½æ—¥è¨˜ã€ï¼¡ï¼®ï½‰ï¼­ï½‰ï¼¤ï¼®ï½‰ï¼§ï¼¨ï¼´ï¼ï¼ï¼ã€‘ï¼ƒï¼‘.mp4")]
        public void CleanStringTest_DoesntNeedCleaning_False(string? input)
        {
            Assert.False(VideoResolver.TryCleanString(input, _namingOptions, out var newName));
            Assert.True(string.IsNullOrEmpty(newName));
        }
    }
}

