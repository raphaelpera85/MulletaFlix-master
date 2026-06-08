using System;
using Xunit;

namespace MulletaFlix.Extensions.Tests
{
    public class StringExtensionsTests
    {
        [Theory]
        [InlineData("", "")] // Identity edge-case (no diacritics)
        [InlineData("Indiana Jones", "Indiana Jones")] // Identity (no diacritics)
        [InlineData("a\ud800b", "ab")] // Invalid UTF-16 char stripping
        [InlineData("\u00E5\u00E4\u00F6", "aao")] // Issue #7484
        [InlineData("J\u00F6n", "Jon")] // Issue #7484
        [InlineData("J\u00F6nssonligan", "Jonssonligan")] // Issue #7484
        [InlineData("Kie\u015Blowski", "Kieslowski")] // Issue #7450
        [InlineData("Cidad\u00E3o Kane", "Cidadao Kane")] // Issue #7560
        [InlineData("\uC6B4\uBA85\uCC98\uB7FC \uB110 \uC0AC\uB791\uD574", "\uC6B4\uBA85\uCC98\uB7FC \uB110 \uC0AC\uB791\uD574")] // Issue #6393 (Korean language support)
        [InlineData("\uC560\uD0C0\uB294 \uB85C\uB9E8\uC2A4", "\uC560\uD0C0\uB294 \uB85C\uB9E8\uC2A4")] // Issue #6393
        [InlineData("Le c\u0153ur a ses raisons", "Le coeur a ses raisons")] // Issue #8893
        [InlineData("B\u00E9la Tarr", "Bela Tarr")] // Issue #8893
        public void RemoveDiacritics_ValidInput_Corrects(string input, string expectedResult)
        {
            string result = input.RemoveDiacritics();
            Assert.Equal(expectedResult, result);
        }

        [Theory]
        [InlineData("", false)] // Identity edge-case (no diacritics)
        [InlineData("Indiana Jones", false)] // Identity (no diacritics)
        [InlineData("a\ud800b", true)] // Invalid UTF-16 char stripping
        [InlineData("\u00E5\u00E4\u00F6", true)] // Issue #7484
        [InlineData("J\u00F6n", true)] // Issue #7484
        [InlineData("J\u00F6nssonligan", true)] // Issue #7484
        [InlineData("Kie\u015Blowski", true)] // Issue #7450
        [InlineData("Cidad\u00E3o Kane", true)] // Issue #7560
        [InlineData("\uC6B4\uBA85\uCC98\uB7FC \uB110 \uC0AC\uB791\uD574", false)] // Issue #6393 (Korean language support)
        [InlineData("\uC560\uD0C0\uB294 \uB85C\uB9E8\uC2A4", false)] // Issue #6393
        [InlineData("Le c\u0153ur a ses raisons", true)] // Issue #8893
        [InlineData("B\u00E9la Tarr", true)] // Issue #8893
        public void HasDiacritics_ValidInput_Corrects(string input, bool expectedResult)
        {
            bool result = input.HasDiacritics();
            Assert.Equal(expectedResult, result);
        }

        [Theory]
        [InlineData("", '_', 0)]
        [InlineData("___", '_', 3)]
        [InlineData("test\x00", '\x00', 1)]
        [InlineData("Imdb=tt0119567|Tmdb=330|TmdbCollection=328", '|', 2)]
        public void ReadOnlySpan_Count_Success(string str, char needle, int count)
        {
            Assert.Equal(count, str.AsSpan().Count(needle));
        }

        [Theory]
        [InlineData("", 'q', "")]
        [InlineData("Banana split", ' ', "Banana")]
        [InlineData("Banana split", 'q', "Banana split")]
        [InlineData("Banana split 2", ' ', "Banana")]
        public void LeftPart_ValidArgsCharNeedle_Correct(string str, char needle, string expectedResult)
        {
            var result = str.AsSpan().LeftPart(needle).ToString();
            Assert.Equal(expectedResult, result);
        }

        [Theory]
        [InlineData("", 'q', "")]
        [InlineData("Banana split", ' ', "split")]
        [InlineData("Banana split", 'q', "Banana split")]
        [InlineData("Banana split.", '.', "")]
        [InlineData("Banana split 2", ' ', "2")]
        public void RightPart_ValidArgsCharNeedle_Correct(string str, char needle, string expectedResult)
        {
            var result = str.AsSpan().RightPart(needle).ToString();
            Assert.Equal(expectedResult, result);
        }
    }
}

