using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    // Regressions from review round 50 (text & small types, third pass).
    public sealed class TextReview3Tests
    {
        private static string ParamOf(Action action)
        {
            try { action(); }
            catch (ArgumentException ex) { return ex.ParamName ?? "(null)"; }
            catch (Exception ex) { return ex.GetType().Name; }
            return "(no throw)";
        }

        public void Snowflake_KeepsThrowingAfterTheLastTimestamp()
        {
            var epoch = SnowflakeIdGenerator.DefaultEpoch;
            var clock = epoch.AddMilliseconds((1L << 41) - 1);
            var gen = new SnowflakeIdGenerator(1, epoch, () => clock);
            var seen = new HashSet<long>();
            for (int i = 0; i < 4096; i++)
                Check.True(seen.Add(gen.NextId()));
            Check.Throws<InvalidOperationException>(() => gen.NextId());
            // The sequence was advanced before the throw, so this returned (max, 1, 1): a duplicate.
            for (int i = 0; i < 3; i++)
                Check.Throws<InvalidOperationException>(() => gen.NextId());
        }

        public void Generators_RejectAClockJustBeforeTheEpoch()
        {
            var epoch = SnowflakeIdGenerator.DefaultEpoch;
            var snow = new SnowflakeIdGenerator(1, epoch, () => epoch.AddTicks(-5000));      // 0.5 ms before: was id time 0
            Check.Throws<InvalidOperationException>(() => snow.NextId());
            var ulid = new UlidGenerator(() => DateTimeOffset.FromUnixTimeMilliseconds(0).AddTicks(-5000));
            Check.Throws<InvalidOperationException>(() => ulid.NewUlid());
        }

        public void Generators_NeverRoundTheClockUp()
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).AddTicks(9999);
            Check.Equal(1700000000000L, UlidGenerator.DecodeTimestamp(new UlidGenerator(() => t).NewUlid()));     // ...001
            var epoch = DateTimeOffset.FromUnixTimeMilliseconds(0);
            long id = new SnowflakeIdGenerator(1, epoch, () => t).NextId();
            Check.Equal(1700000000000L, SnowflakeIdGenerator.Decode(id).TimestampMs);
        }

        public void Slug_NoncharactersAreSeparators()
        {
            Check.Equal("a-b", Slug.Slugify("a\uFFFEb"));                           // ArgumentException from Normalize
            Check.Equal("a-b", Slug.Slugify("a\uFFFFb"));
            Check.Equal("a-b", Slug.Slugify("a\uFDD0b"));
            Check.Equal("a-b", Slug.Slugify("a\U0001FFFEb"));
            Check.Equal("a-b", Slug.Slugify("a\U0010FFFFb"));
        }

        public void Slug_SeparatorMustBeSafe()
        {
            Check.Equal("separator", ParamOf(() => Slug.Slugify("a b", separator: 'É')));   // "aÉb", then "aeb"
            Check.Equal("separator", ParamOf(() => Slug.Slugify("a b", separator: '/')));
            Check.Equal("separator", ParamOf(() => Slug.Slugify("a b", separator: 'x')));
            Check.Equal("a.b", Slug.Slugify("a b", separator: '.'));
            Check.Equal("a~b", Slug.Slugify("a b", separator: '~'));
        }

        public void QueryString_FormatsLongValues()
        {
            string value = new string('x', 70000) + "é" + new string('y', 31999) + "\U0001F600";
            string q = QueryString.Format(new[] { new KeyValuePair<string, string>("k", value) });
            Check.Equal(value, QueryString.Parse(q).Single().Value);                 // UriFormatException on .NET Framework
        }

        public void Hamming_BitDistanceIsLong()
        {
            // An int wrapped negative past 2^31 differing bits (two 256 MiB arrays).
            Check.Equal(typeof(long), typeof(HammingDistance).GetMethod(nameof(HammingDistance.BitDistance))!.ReturnType);
            Check.Equal(8L, HammingDistance.BitDistance(new byte[] { 0xFF }, new byte[] { 0 }));
        }

        public void Roman_LongInputIsRejectedBeforeSumming()
        {
            string huge = new string('M', 3_000_000);                             // OverflowException in a checked build
            Check.False(RomanNumerals.TryFromRoman(huge, out _));
            Check.Throws<FormatException>(() => RomanNumerals.FromRoman(huge));
            Check.Throws<FormatException>(() => RomanNumerals.FromRoman("MMMDCCCLXXXVIIII"));
            Check.Equal(3888, RomanNumerals.FromRoman("MMMDCCCLXXXVIII"));
        }

        public void Glob_ClassMatchesWhatTheLiteralMatches()
        {
            // .NET Framework's invariant casing maps none of these, so there the literal doesn't match either.
            bool mapped = char.ToLowerInvariant('\u212A') == 'k';
            Check.Equal(mapped, GlobMatcher.IsMatch("k", "\u212A", true));
            Check.Equal(mapped, GlobMatcher.IsMatch("k", "[\u212A]", true));        // false
            Check.Equal(mapped, GlobMatcher.IsMatch("S", "[\u017F]", true));
            Check.Equal(mapped, GlobMatcher.IsMatch("\u01C4", "[\u01C5]", true));
            Check.Equal(mapped, GlobMatcher.IsMatch("\u01C6", "[\u01C5]", true));
            Check.False(GlobMatcher.IsMatch("k", "[\u212A]", false));
            for (int i = 0; i <= char.MaxValue; i++)
            {
                char c = (char)i;
                if (char.IsSurrogate(c) || "[]!*?\\-".IndexOf(c) >= 0) continue;
                foreach (char p in new[] { 'k', 's', '\u212A', '\u017F', '\u00B5', '\u01C5', 'i', '\u0130', 'ı' })
                    if (GlobMatcher.IsMatch(c.ToString(), p.ToString(), true) != GlobMatcher.IsMatch(c.ToString(), "[" + p + "]", true))
                        throw new Exception($"U+{i:X4} vs U+{(int)p:X4}");
            }
        }

        public void Ansi_StripsDeviceControlAndApplicationStrings()
        {
            Check.Equal("AB", AnsiText.Strip("A\u001bP1$r0m\u001b\\B"));             // "A1$r0mB"
            Check.Equal("AB", AnsiText.Strip("A\u001b_Gf=24,s=10;AAAA\u001b\\B"));
            Check.Equal("AB", AnsiText.Strip("A\u001bXsos\u001b\\B"));
            Check.Equal("AB", AnsiText.Strip("A\u001b^pm\aB"));
            Check.Equal(2, TextAlign.DisplayWidth("A\u001bP1$r0m\u001b\\B"));
            Check.Equal(2, TextAlign.DisplayWidth("A\u001b_Gf=24;AAAA\u001b\\B"));
        }

        public void TextAlign_WideBmpRanges()
        {
            Check.Equal(2, TextAlign.DisplayWidth("\uFE10"));                       // 1
            Check.Equal(2, TextAlign.DisplayWidth("\uFE50"));
            Check.Equal(2, TextAlign.DisplayWidth("\uA960"));
            Check.Equal(0, TextAlign.DisplayWidth("\uFE20"));                       // a combining half mark, not a vertical form
        }

        public void TextAlign_PaddingAfterAnUnfinishedEscapeCounts()
        {
            foreach (string text in new[] { "ab\u001b", "ab\u001b[", "ab\u001b]0;title", "ab\u001b[3", "ab\u001bP12", "ab\u001b(" })
            {
                foreach (var a in new[] { Alignment.Left, Alignment.Center, Alignment.Right })
                    Check.Equal(6, TextAlign.DisplayWidth(TextAlign.Fit(text, 6, a)));     // 2 or 4
                Check.Equal(6, TextAlign.DisplayWidth(TextAlign.PadRight(text, 6)));
                Check.Equal(1, TextAlign.DisplayWidth(TextAlign.Fit(text, 1)));          // the ellipsis was swallowed
            }
            Check.Equal("ab\u001b[0m    ", TextAlign.Fit("ab\u001b[0m", 6));             // finished escapes are kept
        }

        public void PercentEncoding_NonAsciiSafeCharactersStay()
        {
            Check.Equal("é/", PercentEncoding.Encode("é/", safe: "é/"));                 // "%C3%A9/"
            Check.Equal("\U0001F600%C3%A9", PercentEncoding.Encode("\U0001F600é", safe: "\U0001F600"));
            Check.Equal("é%2F", PercentEncoding.Encode("é/", safe: "é"));
            Check.Equal("é/", PercentEncoding.Decode(PercentEncoding.Encode("é/", safe: "é/")));
            Check.Equal("text", ParamOf(() => PercentEncoding.Encode("\uD800é", safe: "é")));
        }

        public void Soundex_AnAstralLetterActsLikeAVowel()
        {
            Check.Equal("B100", Soundex.Encode("B\U00010428B"));                       // "B000"
            Check.Equal(Soundex.Encode("BαB"), Soundex.Encode("B\U00010428B"));
            Check.Equal("B000", Soundex.Encode("B\U0001F600B"));                      // an emoji is not a letter
        }

        public void CaseConverter_TitlecasesDigraphs()
        {
            Check.Equal("\u01C5emal", CaseConverter.ToTitleCase("\u01C6emal"));        // "\u01C4emal"
            Check.Equal("\u01C8ubljana", CaseConverter.ToPascalCase("\u01C9ubljana"));
            Check.Equal("Foo\u01C5emal", CaseConverter.ToPascalCase("foo_\u01C6emal"));
            Check.Equal("foo_\u01C6emal", CaseConverter.ToSnakeCase("Foo\u01C5emal"));
        }

        public void CaseConverter_LettersWithoutAnInvariantCase()
        {
            Check.Equal("STRASSE", CaseConverter.ToConstantCase("straße"));             // "STRAßE"
            Check.Equal("STRASSE", CaseConverter.ToConstantCase(CaseConverter.ToConstantCase("straße")));
            Check.Equal("STRASSE", CaseConverter.ToConstantCase("STRAßE"));             // "STR_Aß_E"
            Check.Equal("istanbul", CaseConverter.ToSnakeCase("\u0130STANBUL"));            // "\u0130stanbul"
            Check.Equal("my-id", CaseConverter.ToKebabCase("My\u0130D"));                    // "my-\u0130d"
            Check.Equal("ai", CaseConverter.ToSnakeCase("A\u0130"));
            Check.Equal("ai", CaseConverter.ToSnakeCase(CaseConverter.ToSnakeCase("A\u0130")));
            Check.Equal("AnaIşık", CaseConverter.ToPascalCase("ana_ışık"));             // "Anaışık"
            Check.Equal("ana_işık", CaseConverter.ToSnakeCase("AnaIşık"));
            Check.Equal("AbSsc", CaseConverter.ToPascalCase("ab_ßc"));
            Check.Equal("ab_ssc", CaseConverter.ToSnakeCase(CaseConverter.ToPascalCase("ab_ßc")));
            Check.Equal("ab\u0138c", CaseConverter.ToSnakeCase("AB\u0138C"));          // kra has no capital: stays one word
            Check.Equal("fooß|Bar", string.Join("|", CaseConverter.ToWords("fooßBar")));
            string[] words = { "abc", "abé", "aıb", "ßab", "ab" };
            foreach (string w1 in words)
                foreach (string w2 in words)
                {
                    string snake = CaseConverter.ToSnakeCase(w1 + "_" + w2);
                    foreach (Func<string, string> style in new Func<string, string>[]
                        { CaseConverter.ToSnakeCase, CaseConverter.ToKebabCase, CaseConverter.ToConstantCase,
                          CaseConverter.ToPascalCase, CaseConverter.ToCamelCase, CaseConverter.ToTitleCase })
                    {
                        string once = style(snake);
                        Check.Equal(once, style(once));                               // idempotent per style
                        Check.Equal(2, CaseConverter.ToWords(once).Count);              // the boundary survives
                    }
                }
        }
    }
}
