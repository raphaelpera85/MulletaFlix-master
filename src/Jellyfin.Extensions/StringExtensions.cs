using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ICU4N.Text;

namespace MulletaFlix.Extensions
{
    public static partial class StringExtensions
    {
        private static readonly Lazy<string> _transliteratorId = new(() =>
            Environment.GetEnvironmentVariable("MulletaFlix_TRANSLITERATOR_ID")
            ?? "Any-Latin; Latin-Ascii; Lower; NFD; [:Nonspacing Mark:] Remove; [:Punctuation:] Remove;");

        private static readonly Lazy<Transliterator?> _transliterator = new(() =>
        {
            try
            {
                return Transliterator.GetInstance(_transliteratorId.Value);
            }
            catch (ArgumentException)
            {
                return null;
            }
        });

        [GeneratedRegex("([\ud800-\udbff](?![\udc00-\udfff]))|((?<![\ud800-\udbff])[\udc00-\udfff])|(\ufffd)")]
        private static partial Regex NonConformingUnicodeRegex();

        public static string RemoveDiacritics(this string text)
        {
            text = NonConformingUnicodeRegex().Replace(text, string.Empty);
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            try
            {
                var sb = new StringBuilder(text.Length);
                foreach (var ch in text)
                {
                    switch (ch)
                    {
                        case '\u0152': sb.Append("OE"); break;
                        case '\u0153': sb.Append("oe"); break;
                        case '\u00C6': sb.Append("AE"); break;
                        case '\u00E6': sb.Append("ae"); break;
                        default: sb.Append(ch); break;
                    }
                }

                var normalized = sb.ToString().Normalize(NormalizationForm.FormKD);
                sb.Clear();
                foreach (var ch in normalized)
                {
                    var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                    if (cat == UnicodeCategory.NonSpacingMark)
                    {
                        continue;
                    }

                    if (cat == UnicodeCategory.Format && (ch == 0x200B || ch == 0x200C || ch == 0x200D || ch == 0xFEFF))
                    {
                        continue;
                    }

                    sb.Append(ch);
                }

                return sb.ToString().Normalize(NormalizationForm.FormC);
            }
            catch
            {
                return text;
            }
        }

        public static bool HasDiacritics(this string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (NonConformingUnicodeRegex().IsMatch(text))
            {
                return true;
            }

            try
            {
                var withoutDiacritics = RemoveDiacritics(text);
                return !string.Equals(text, withoutDiacritics, StringComparison.Ordinal);
            }
            catch
            {
            }

            return false;
        }

        public static int Count(this ReadOnlySpan<char> value, char needle)
        {
            var count = 0;
            var length = value.Length;
            for (var i = 0; i < length; i++)
            {
                if (value[i] == needle)
                {
                    count++;
                }
            }

            return count;
        }

        public static ReadOnlySpan<char> LeftPart(this ReadOnlySpan<char> haystack, char needle)
        {
            if (haystack.IsEmpty)
            {
                return ReadOnlySpan<char>.Empty;
            }

            var pos = haystack.IndexOf(needle);
            return pos == -1 ? haystack : haystack[..pos];
        }

        public static ReadOnlySpan<char> RightPart(this ReadOnlySpan<char> haystack, char needle)
        {
            if (haystack.IsEmpty)
            {
                return ReadOnlySpan<char>.Empty;
            }

            var pos = haystack.LastIndexOf(needle);
            if (pos == -1)
            {
                return haystack;
            }

            if (pos == haystack.Length - 1)
            {
                return ReadOnlySpan<char>.Empty;
            }

            return haystack[(pos + 1)..];
        }

        public static string Transliterated(this string text)
        {
            return (_transliterator.Value is null) ? text : _transliterator.Value.Transliterate(text);
        }

        public static IEnumerable<string> Trimmed(this IEnumerable<string?> values)
        {
            return values.Select(i => (i ?? string.Empty).Trim());
        }

        public static string TruncateAtNull(this string text)
        {
            return string.IsNullOrEmpty(text) ? text : text.AsSpan().LeftPart('\0').ToString();
        }

        public static string GetCleanValue(this string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var cleaned = value.RemoveDiacritics().ToLowerInvariant();
            cleaned = Regex.Replace(cleaned, @"[^\p{L}\p{N}\s]", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }
    }
}
