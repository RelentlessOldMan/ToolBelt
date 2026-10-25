using System;
using System.Linq;
using ToolBelt.Guards;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    // Regressions from review round 32 (text & small types, second pass).
    public sealed class TextReview2Tests
    {
        private static string ParamOf(Action action)
        {
            try { action(); }
            catch (ArgumentException ex) { return ex.ParamName ?? "(null)"; }
            catch (Exception ex) { return ex.GetType().Name; }
            return "(no throw)";
        }

        public void Slug_FoldsLettersWithoutADecomposition()
        {
            Check.Equal("strasse", Slug.Slugify("Straße"));                         // "stra-e"
            Check.Equal("oresund", Slug.Slugify("Øresund"));                        // "resund"
            Check.Equal("lodz", Slug.Slugify("Łódź"));                              // "odz"
            Check.Equal("aero", Slug.Slugify("Ærø"));                               // "r"
            Check.Equal("strasse", Slug.Slugify(Slug.Slugify("Straße")));
            Check.Equal("stra", Slug.Slugify("Straße", maxLength: 5));              // a fold is never split
        }

        public void PercentEncoding_PlusIsEscapedWhenItMeansSpace()
        {
            string e = PercentEncoding.Encode("a+b c", "+", spaceAsPlus: true);
            Check.Equal("a%2Bb+c", e);                                              // "a+b+c"
            Check.Equal("a+b c", PercentEncoding.DecodeForm(e));
            Check.Equal("a+b%20c", PercentEncoding.Encode("a+b c", "+"));
        }

        public void Ansi_AnEscapeCancelsTheSequenceItInterrupts()
        {
            Check.Equal("X", AnsiText.Strip("\u001b\u001b[31mX"));                  // "[31mX"
            Check.Equal(1, TextAlign.DisplayWidth("\u001b\u001b[31mX"));           // 5
            Check.Equal("X", AnsiText.Strip("\u001b[31\u001b[32mX"));
            Check.Equal(1, TextAlign.DisplayWidth("\u001b[31\u001b[32mX"));
            const string osc = "\u001b]0;title\u001b[31mRed\u001b[0m more text";
            Check.Equal("Red more text", AnsiText.Strip(osc));                      // ""
            Check.Equal(13, TextAlign.DisplayWidth(osc));                           // 0
            Check.Equal("ab", AnsiText.Strip("a\u001b]0;t\u001b\\b"));              // ST still ends it
            Check.Equal(2, TextAlign.DisplayWidth("a\u001b]0;t\u001b\\b"));
        }

        public void NanoId_AlphabetMustBeDistinctCharacters()
        {
            Check.Equal("alphabet", ParamOf(() => new NanoId(null, "aab")));
            Check.Equal("alphabet", ParamOf(() => new NanoId(null, "\U0001F600x")));
            Check.Equal(10, new NanoId(null, "ab").New(10).Length);
        }

        public void StringSimilarity_NaNMinScoreAgrees()
        {
            Func<string, string, double> eq = (a, b) => a == b ? 1 : 0;
            Check.Equal("a", StringSimilarity.BestMatch("a", new[] { "a", "b" }, eq, double.NaN)!.Value.Value);
            Check.Equal(2, StringSimilarity.TopMatches("a", new[] { "a", "b" }, eq, 5, double.NaN).Count);   // 0
        }

        public void CaseConverter_CapitalizesAnAstralFirstLetter()
            => Check.Equal("\U00010400\U00010429\U00010400", CaseConverter.ToPascalCase("\U00010428\U00010429 \U00010428"));

        public void Glob_LiteralsAndClassesFoldAlike()
        {
            Check.Equal(GlobMatcher.IsMatch("ſ", "[s]", true), GlobMatcher.IsMatch("ſ", "s", true));      // True vs False
            Check.Equal(GlobMatcher.IsMatch("K", "[k]", true), GlobMatcher.IsMatch("K", "k", true));
            Check.True(GlobMatcher.IsMatch("README.md", "readme.*", true));
            Check.False(GlobMatcher.IsMatch("README.md", "readme.*"));
        }

        public void Ulid_RejectsNonAsciiLookalikes()
        {
            Check.Throws<FormatException>(() => UlidGenerator.DecodeTimestamp("0ſ00000000ſſſſſſſſſſſſſſſſ"));   // was 27487790694400
            Check.Equal(1469922850259L, UlidGenerator.DecodeTimestamp("01ARZ3NDEKTSV4RRFFQ69G5FAV"));
        }

        public void TextAlign_PadMustBeOneColumn()
        {
            Check.Equal("pad", ParamOf(() => TextAlign.Fit("a", 5, pad: '中')));        // width 9
            Check.Equal("pad", ParamOf(() => TextAlign.PadLeft("a", 5, '́')));
            Check.Equal("a....", TextAlign.Fit("a", 5, pad: '.'));
        }

        public void Truncate_LeadingWhitespaceIsTrimmed()
        {
            Check.Equal("…", Truncate.WithEllipsisOnWord("   abcdef", 4));                // "  …"
            Check.Equal("ab…", Truncate.WithEllipsisOnWord("ab   cdef", 6));
        }

        public void Guard_EmptyRangeBlamesTheBounds()
        {
            Check.Equal("max", ParamOf(() => Guard.InRange(3, 5, 1)));                    // blamed the value
            Check.Equal(3, Guard.InRange(3, 1, 5));
        }
    }
}
