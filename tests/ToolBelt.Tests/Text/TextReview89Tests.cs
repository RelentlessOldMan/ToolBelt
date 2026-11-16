using System;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    // Round 89 review regressions (Text & small types, fifth pass).
    public sealed class TextReview89Tests
    {
        private static string Cp(int cp) => char.ConvertFromUtf32(cp);

        public void TextAlign_NarrowSymbolsInTheEmojiPlanesCountOne()
        {
            // The whole of U+1F000–1FAFF counted two: alchemical symbols, enclosed letters, chess symbols, cards and
            // text-style pictographs are East Asian Width N.
            foreach (int cp in new[] { 0x1F700, 0x1F800, 0x1F130, 0x1FA00, 0x1F0A1, 0x1F321, 0x1F3F3, 0x1F650, 0x1F1E6 })
                Check.Equal(1, TextAlign.DisplayWidth(Cp(cp)), cp.ToString("X"));
            Check.Equal(2, TextAlign.DisplayWidth("\U0001F1FA\U0001F1F8"));               // a flag measured 4
            foreach (int cp in new[] { 0x1F004, 0x1F0CF, 0x1F18E, 0x1F600, 0x1F680, 0x1F90C, 0x1FAF8, 0x1F300 })
                Check.Equal(2, TextAlign.DisplayWidth(Cp(cp)), cp.ToString("X"));
        }

        public void TextAlign_CircledNumbersOnBlackSquaresAreNarrow()
        {
            for (char c = '\u3248'; c <= '\u324F'; c++)
                Check.Equal(1, TextAlign.DisplayWidth(c.ToString()), ((int)c).ToString("X"));
            Check.Equal(2, TextAlign.DisplayWidth("\u3247"));
            Check.Equal(2, TextAlign.DisplayWidth("\u3250"));
            Check.Equal(2, TextAlign.DisplayWidth("\u3042"));
        }

        public void TextAlign_MarksNewerThanUnicode8AreZeroWidthOnEveryRuntime()
        {
            // .NET Framework's Unicode 8 tables don't know these marks, so they counted one column there.
            foreach (char mark in new[] { '\u08D3', '\u08E1', '\u0AFA', '\u0C3C', '\u1AC0', '\u1ACE', '\u1DF6', '\u1DFB' })
                Check.Equal(1, TextAlign.DisplayWidth("a" + mark), ((int)mark).ToString("X"));
            Check.Equal(1, TextAlign.DisplayWidth("a" + Cp(0x11F00)));                  // Kawi, Unicode 15
            Check.Equal(2, TextAlign.DisplayWidth("a\u1734"));                            // a spacing mark since Unicode 14
            Check.Equal(1, TextAlign.DisplayWidth("e\u0301"));
            Check.Equal(0, TextAlign.DisplayWidth("\u00AD\u200B"));
        }

        public void Soundex_LettersOutsideAToZActLikeVowelsOnEveryRuntime()
        {
            // ToUpperInvariant took 'ſ' to 'S' on .NET 8 (S530) but not on .NET Framework (M300).
            Check.Equal("M300", Soundex.Encode("\u017Fmith"));
            Check.Equal("O100", Soundex.Encode("\u017Fob"));                            // a vowel can start the code
            Check.Equal("V500", Soundex.Encode("\u0131van"));
            Check.Equal("S530", Soundex.Encode("smith"));
            Check.Equal("A626", Soundex.Encode("\u00C4rger"));
        }

        public void Slug_CapitalSharpSFoldsToSs()
        {
            Check.Equal("ss-istanbul", Slug.Slugify("\u1E9E \u0130stanbul"));             // .NET Framework dropped it
            Check.Equal("strasse", Slug.Slugify("STRA\u1E9EE"));
        }

        public void WordWrap_NoBreakSpacesDoNotBreak()
        {
            var lines = WordWrap.Wrap("price 10\u00A0km and 20\u202Fkg", 9);
            Check.Equal(3, lines.Count);
            Check.Equal("price", lines[0]);
            Check.Equal("10\u00A0km and", lines[1]);                                      // was "price 10" / "km and 20" / "kg"
            Check.Equal("20\u202Fkg", lines[2]);
            Check.Equal("a\u2007b", WordWrap.Wrap("a\u2007b c", 3)[0]);
        }

        public void Truncate_NoBreakSpacesAreNotWordBreaks()
        {
            Check.Equal("ab\u2026", Truncate.WithEllipsisOnWord("ab 10\u00A0kmzz", 8));    // was "ab 10…"
            Check.Equal("ab 10\u2026", Truncate.WithEllipsisOnWord("ab 10 kmzz", 8));
        }
    }
}
