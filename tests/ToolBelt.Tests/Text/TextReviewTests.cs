using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ToolBelt.Enums;
using ToolBelt.Guards;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    /// <summary>Regression tests for review round 11: Text, Identifiers, Enums and Guards.</summary>
    public sealed class TextReviewTests
    {
        [Flags]
        private enum Perm
        {
            None = 0,
            Read = 1,
            Write = 2,
            ReadWrite = Read | Write,
            Execute = 4,
        }

        private enum Size
        {
            Small = 1,
            Medium = 2,
            Large = 3,
        }

        private enum Aliased
        {
            First = 1,
            Primary = 1,
            Second = 2,
        }

        // ---------------- Slug ----------------

        public void Slug_MaxLength_IsNeverExceeded()
        {
            Check.Equal("ab", Slug.Slugify("ab cd", maxLength: 3));
            for (int max = 1; max <= 12; max++)
                Check.True(Slug.Slugify("one two three four", max).Length <= max, $"max {max}");
        }

        public void Slug_LoneSurrogate_IsASeparator()
        {
            Check.Equal("a-b", Slug.Slugify("a\uD800b"));
            Check.Equal("a-b", Slug.Slugify("a\uDC00b"));
        }

        // ---------------- CaseConverter ----------------

        public void CaseConverter_CombiningMarks_StayInTheWord()
        {
            var words = CaseConverter.ToWords("café bar");
            Check.Equal(2, words.Count);
            Check.Equal("café", words[0]);
            Check.Equal("bar", words[1]);
        }

        public void CaseConverter_SupplementaryLetters_AreLetters()
        {
            // U+10428 DESERET SMALL LETTER LONG I is a lower-case letter outside the BMP.
            var words = CaseConverter.ToWords("x\U00010428y");
            Check.Equal(1, words.Count);
            Check.Equal("x\U00010428y", words[0]);
            // U+10400 DESERET CAPITAL LETTER LONG I starts a camelCase hump.
            var humps = CaseConverter.ToWords("ab\U00010400cd");
            Check.Equal(2, humps.Count);
            Check.Equal("\U00010400cd", humps[1]);
        }

        // ---------------- JaroWinkler ----------------

        public void JaroWinkler_PrefixBonusCannotPassOne()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => JaroWinkler.Similarity("abcdefgh", "abcdefgx", 0.25, 8));
            Check.Throws<ArgumentOutOfRangeException>(() => JaroWinkler.Similarity("a", "a", double.NaN));
            Check.True(JaroWinkler.Similarity("abcdefgh", "abcdefgx", 0.25, 4) <= 1.0);
        }

        // ---------------- Truncate ----------------

        public void Truncate_OnWord_CutAtASpaceKeepsTheWord()
        {
            Check.Equal("hello world…", Truncate.WithEllipsisOnWord("hello world foo", 12));
        }

        public void Truncate_OnWord_TrimsWhitespaceAndUsesAnyWhitespace()
        {
            Check.Equal("ab…", Truncate.WithEllipsisOnWord("ab  cdefgh", 6));
            Check.Equal("ab…", Truncate.WithEllipsisOnWord("ab\tcdefgh", 5));
            Check.Equal("ab…", Truncate.WithEllipsisOnWord("ab\ncdefgh", 5));
        }

        // ---------------- WordWrap ----------------

        public void WordWrap_HardBreakOfAHugeWord_IsLinear()
        {
            string word = new string('x', 300_000);
            var sw = Stopwatch.StartNew();
            var lines = WordWrap.Wrap(word, 1);
            sw.Stop();
            Check.Equal(300_000, lines.Count);
            Check.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");
        }

        // ---------------- TextDiff ----------------

        public void TextDiff_NearIdenticalLargeInputs_AreFast()
        {
            var oldLines = Enumerable.Range(0, 20_000).Select(i => "line " + i).ToArray();
            var newLines = (string[])oldLines.Clone();
            newLines[10_000] = "changed";
            var sw = Stopwatch.StartNew();
            var diff = TextDiff.Lines(string.Join("\n", oldLines), string.Join("\n", newLines));
            sw.Stop();
            Check.Equal(20_001, diff.Count);
            Check.Equal(1, diff.Count(d => d.Op == DiffOp.Delete));
            Check.Equal(1, diff.Count(d => d.Op == DiffOp.Insert));
            Check.True(sw.ElapsedMilliseconds < 1500, $"took {sw.ElapsedMilliseconds} ms");
        }

        // ---------------- EnumExtensions / EnumMap ----------------

        public void GetFlags_FlagsEnum_ReturnsOnlySingleBitMembers()
        {
            var flags = (Perm.Read | Perm.Write).GetFlags();
            Check.True(flags.SequenceEqual(new[] { Perm.Read, Perm.Write }), string.Join(",", flags));
        }

        public void GetFlags_NonFlagsEnum_ReturnsTheValueItself()
        {
            Check.True(Size.Large.GetFlags().SequenceEqual(new[] { Size.Large }), string.Join(",", Size.Large.GetFlags()));
            Check.Equal(0, ((Size)7).GetFlags().Count);
        }

        public void EnumCaches_AreNotExposedAsArrays()
        {
            Check.False(EnumExtensions.GetValues<Size>() is Size[]);
            Check.False(EnumExtensions.GetNames<Size>() is string[]);
            Check.False(new EnumMap<Size, int>().Keys is Size[]);
        }

        public void EnumMap_Aliases_ShareOneSlot()
        {
            var map = new EnumMap<Aliased, int>();
            Check.Equal(2, map.Count);
            Check.Equal(2, map.Keys.Count);
            Check.Equal(2, Enumerable.Count(map));
            map[Aliased.First] = 5;
            Check.Equal(5, map[Aliased.Primary]);
        }

        // ---------------- Identifiers ----------------

        public void Snowflake_TimestampPast41Bits_Throws()
        {
            var epoch = SnowflakeIdGenerator.DefaultEpoch;
            var gen = new SnowflakeIdGenerator(1, epoch, () => epoch.AddMilliseconds(1L << 41));
            Check.Throws<InvalidOperationException>(() => gen.NextId());
            var last = new SnowflakeIdGenerator(1, epoch, () => epoch.AddMilliseconds((1L << 41) - 1));
            Check.True(last.NextId() > 0);
        }

        public void ShortGuid_NonCanonicalText_IsRejected()
        {
            string canonical = ShortGuid.Encode(Guid.Empty);
            string sloppy = canonical.Substring(0, 21) + "B";                 // unused low bits set
            Check.Throws<FormatException>(() => ShortGuid.Decode(sloppy));
            Check.False(ShortGuid.TryDecode(sloppy, out _));

            string encoded = ShortGuid.Encode(new Guid("fbffffff-ffff-ffff-ffff-ffffffffffff"));
            Check.True(encoded.Contains("_") || encoded.Contains("-"), encoded);
            string plain = encoded.Replace('-', '+').Replace('_', '/');
            Check.Throws<FormatException>(() => ShortGuid.Decode(plain));
        }

        public void Ulid_DecodeTimestamp_RejectsOverflowAndBadRandomness()
        {
            Check.Throws<FormatException>(() => UlidGenerator.DecodeTimestamp("8" + new string('0', 25)));
            Check.Throws<FormatException>(() => UlidGenerator.DecodeTimestamp(new string('0', 10) + "U" + new string('0', 15)));
            Check.Equal((1L << 48) - 1, UlidGenerator.DecodeTimestamp("7ZZZZZZZZZ" + new string('0', 16)));
        }

        // ---------------- NaturalComparer ----------------

        public void NaturalComparer_LeadingZeros_BreakTiesSoNoValueIsLost()
        {
            Check.True(NaturalComparer.Ordinal.Compare("a1", "a01") < 0);
            Check.True(NaturalComparer.Ordinal.Compare("a01", "a1") > 0);
            Check.True(NaturalComparer.Ordinal.Compare("a01", "a2") < 0);
            var set = new SortedSet<string>(NaturalComparer.Ordinal) { "a01", "a1", "a001" };
            Check.True(set.SequenceEqual(new[] { "a1", "a01", "a001" }), string.Join(",", set));
        }

        // ---------------- QueryString ----------------

        public void QueryString_Parse_DecodesPlusAsSpace()
        {
            var pairs = QueryString.Parse("q=hello+world&a+b=c");
            Check.Equal("hello world", pairs[0].Value);
            Check.Equal("a b", pairs[1].Key);
            var round = QueryString.Parse(QueryString.Format(new[] { new KeyValuePair<string, string>("k", "1+1") }));
            Check.Equal("1+1", round[0].Value);
        }

        // ---------------- AnsiText / TextAlign ----------------

        public void AnsiText_EscapeWithIntermediateBytes_IsStrippedWhole()
        {
            Check.Equal("ok", AnsiText.Strip("\u001b(Bok"));
            Check.Equal("ok", AnsiText.Strip("\u001b#8ok"));
            Check.Equal(2, TextAlign.DisplayWidth("\u001b(Bok"));
        }

        public void TextAlign_WideBmpEmoji_TakeTwoColumns()
        {
            Check.Equal(2, TextAlign.DisplayWidth("⭐"));
            Check.Equal(2, TextAlign.DisplayWidth("✅"));
            Check.Equal(2, TextAlign.DisplayWidth("⌚"));
            Check.Equal(1, TextAlign.DisplayWidth("☀"));   // narrow by default (no emoji presentation)
        }

        public void TextAlign_TruncationPadding_UsesThePadCharacter()
        {
            Check.Equal("ab.", TextAlign.Fit("ab中", 3, Alignment.Left, '.', truncate: true, ellipsis: ""));
        }

        // ---------------- GlobMatcher ----------------

        public void Glob_IgnoreCaseRange_MatchesTheRangeAsWritten()
        {
            Check.True(GlobMatcher.IsMatch("_", "[Z-a]", ignoreCase: true));
            Check.True(GlobMatcher.IsMatch("A", "[Z-a]", ignoreCase: true));
            Check.True(GlobMatcher.IsMatch("z", "[Z-a]", ignoreCase: true));
            Check.False(GlobMatcher.IsMatch("b", "[Z-a]", ignoreCase: true));
            Check.True(GlobMatcher.IsMatch("Q", "[a-z]", ignoreCase: true));
        }

        // ---------------- Indent ----------------

        public void Indent_WhitespaceOnlyLines_AreBlankInBothDirections()
        {
            Check.Equal("> a\n  \n> b", Indent.IndentLines("a\n  \nb", "> "));
            Check.Equal("a\n\nb", Indent.Dedent("    a\n  \n    b"));
            Check.Equal("a\r\n\r\nb", Indent.Dedent("    a\r\n  \r\n    b"));
        }

        // ---------------- TemplateFormatter ----------------

        public void Template_BraceInsideAPlaceholder_Throws()
        {
            Check.Throws<FormatException>(() => TemplateFormatter.Format("{a{b}", n => "x"));
        }

        // ---------------- Guard ----------------

        public void Guard_Index_NegativeCount_Throws()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => Guard.Index(0, -1));
            Check.Equal("count", ex.ParamName);
        }

        // ---------------- Soundex ----------------

        public void Soundex_AccentedLetters_FoldToTheirBase()
        {
            Check.Equal("A626", Soundex.Encode("Ärger"));
            Check.Equal("E420", Soundex.Encode("Éloïse"));
            Check.Equal(Soundex.Encode("Muller"), Soundex.Encode("Müller"));
        }

        // ---------------- PercentEncoding ----------------

        public void PercentEncoding_PercentInSafe_StillEscaped()
        {
            Check.Equal("%2541", PercentEncoding.Encode("%41", safe: "%/"));
            Check.Equal("%41", PercentEncoding.Decode(PercentEncoding.Encode("%41", safe: "%")));
        }

        // ---------------- Tokenizer ----------------

        public void TokenizerOptions_NullSets_Throw()
        {
            Check.Throws<ArgumentNullException>(() => new TokenizerOptions { Delimiters = null! });
            Check.Throws<ArgumentNullException>(() => new TokenizerOptions { Quotes = null! });
        }
    }
}
