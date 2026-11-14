using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    // Round 74 review regressions (Text & small types, fourth pass).
    public sealed class TextReview4Tests
    {
        private static string ParamOf(Action action)
        {
            try { action(); }
            catch (ArgumentException ex) { return ex.ParamName ?? "(null)"; }
            catch (Exception ex) { return ex.GetType().Name; }
            return "(no throw)";
        }

        private static long Allocated(Action action)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static double Seconds(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            return sw.Elapsed.TotalSeconds;
        }

        // ---- SnowflakeIdGenerator: no lead over the clock ----

        public void Snowflake_FrozenClock_ThrowsInsteadOfBorrowingTheNextMillisecond()
        {
            var now = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero);
            var gen = new SnowflakeIdGenerator(machineId: 1, clock: () => now);
            for (int i = 0; i < 4096; i++) gen.NextId();
            // The 4097th id was stamped with the next millisecond, which the clock had not reached.
            Check.Throws<InvalidOperationException>(() => gen.NextId());
        }

        public void Snowflake_IdsNeverRunAheadOfTheClock()
        {
            var t0 = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero);
            long reads = 0;
            DateTimeOffset Clock() => t0.AddMilliseconds(reads++ / 5000);    // 1 ms per 5,000 reads
            var epoch = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var gen = new SnowflakeIdGenerator(machineId: 1, epoch: epoch, clock: Clock);
            long last = -1;
            for (int i = 0; i < 50000; i++)
            {
                long id = gen.NextId();
                Check.True(id > last);
                last = id;
                long clockMs = (long)(t0.AddMilliseconds(reads / 5000) - epoch).TotalMilliseconds;
                Check.True(SnowflakeIdGenerator.Decode(id).TimestampMs <= clockMs, $"id {i} is ahead of the clock");
            }
        }

        // ---- GlobMatcher ----

        public void Glob_ManyStarsAndALongSegment_IsLinear()
        {
            string pattern = "*" + new string('a', 20000) + "b*";
            string input = new string('a', 200000);
            bool result = true;
            double s = Seconds(() => result = GlobMatcher.IsMatch(input, pattern));
            Check.False(result);
            Check.True(s < 3, $"took {s:F1} s (was about 14 s)");
        }

        public void Glob_MatchesARegexReference_OverRandomPatterns()
        {
            var rng = new Random(74);
            string[] parts = { "a", "b", "?", "*", "[ab]", "[!a]", "[a-b]", "c" };
            for (int trial = 0; trial < 3000; trial++)
            {
                var pattern = new StringBuilder();
                var regex = new StringBuilder("^");
                int count = rng.Next(0, 7);
                for (int p = 0; p < count; p++)
                {
                    string part = parts[rng.Next(parts.Length)];
                    pattern.Append(part);
                    regex.Append(part == "?" ? "." : part == "*" ? ".*" : part == "[!a]" ? "[^a]" : part);
                }
                regex.Append('$');
                var input = new string(Enumerable.Range(0, rng.Next(0, 9)).Select(_ => "abc"[rng.Next(3)]).ToArray());
                bool expected = Regex.IsMatch(input, regex.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
                Check.Equal(expected, GlobMatcher.IsMatch(input, pattern.ToString()), $"'{input}' against '{pattern}'");
            }
        }

        public void Glob_IgnoreCase_IsSymmetric()
        {
            // 'ſ' upper-cases to 'S', so it matched "S" but not "s"; 'k' matched the Kelvin sign but 'K' did not.
            string[] linked = { "s", "S", "ſ", "k", "K", "\u212A", "å", "Å", "\u212B", "ǅ", "ǆ", "Ǆ" };
            foreach (string x in linked)
                foreach (string y in linked)
                {
                    bool m = GlobMatcher.IsMatch(x, y, ignoreCase: true);
                    Check.Equal(m, GlobMatcher.IsMatch(y, x, ignoreCase: true), $"'{x}' and '{y}'");
                    Check.Equal(m, GlobMatcher.IsMatch(x, "[" + y + "]", ignoreCase: true), $"'{x}' and '[{y}]'");
                }
#if !NETFRAMEWORK
            // .NET Framework's invariant casing maps none of these, so there each matches only itself.
            Check.True(GlobMatcher.IsMatch("s", "ſ", ignoreCase: true));
            Check.True(GlobMatcher.IsMatch("ſ", "s", ignoreCase: true));
            Check.True(GlobMatcher.IsMatch("K", "\u212A", ignoreCase: true));
            Check.True(GlobMatcher.IsMatch("\u212A", "K", ignoreCase: true));
#endif
            Check.False(GlobMatcher.IsMatch("s", "ſ"));
        }

        public void Glob_ReversedRangeIsEmpty_AndLeadingBracketCanStartARange()
        {
            Check.False(GlobMatcher.IsMatch("b", "[z-a]"));                 // swapped into a-z
            Check.True(GlobMatcher.IsMatch("b", "[!z-a]"));
            Check.True(GlobMatcher.IsMatch("_", "[]-a]"));                  // ']' to 'a' takes in '^', '_' and '`'
            Check.True(GlobMatcher.IsMatch("]", "[]-a]"));
            Check.False(GlobMatcher.IsMatch("-", "[]-a]"));
        }

        // ---- NGramSimilarity ----

        public void NGram_LongGrams_DoNotAllocateASubstringEach()
        {
            var rng = new Random(1);
            string a = new string(Enumerable.Range(0, 100000).Select(_ => (char)('a' + rng.Next(4))).ToArray());
            double d = 0;
            long bytes = Allocated(() => d = NGramSimilarity.Dice(a, a, 1000));
            Check.Equal(1.0, d);
            Check.True(bytes < 64L << 20, $"allocated {bytes >> 20} MB (was about 400 MB)");
        }

        public void NGram_MatchesASubstringReference()
        {
            var rng = new Random(2);
            for (int trial = 0; trial < 2000; trial++)
            {
                string a = RandomText(rng, rng.Next(0, 12), 3), b = RandomText(rng, rng.Next(0, 12), 3);
                int n = rng.Next(1, 5);
                var ga = Grams(a, n);
                var gb = Grams(b, n);
                if (ga.Count == 0 && gb.Count == 0) continue;
                int inter = ga.Keys.Sum(k => Math.Min(ga[k], gb.TryGetValue(k, out int c) ? c : 0));
                double dice = 2.0 * inter / (ga.Values.Sum() + gb.Values.Sum());
                Check.True(Math.Abs(dice - NGramSimilarity.Dice(a, b, n)) < 1e-12, $"Dice('{a}', '{b}', {n})");
            }
        }

        private static Dictionary<string, int> Grams(string s, int n)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i + n <= s.Length; i++)
            {
                string g = s.Substring(i, n);
                d[g] = d.TryGetValue(g, out int c) ? c + 1 : 1;
            }
            return d;
        }

        private static string RandomText(Random rng, int length, int letters)
            => new string(Enumerable.Range(0, length).Select(_ => (char)('a' + rng.Next(letters))).ToArray());

        // ---- JaroWinkler ----

        public void Jaro_LongStrings_IsLinear()
        {
            var rng = new Random(3);
            string a = RandomText(rng, 200000, 2), b = RandomText(rng, 200000, 2);
            double s = Seconds(() => JaroWinkler.Jaro(a, b));
            Check.True(s < 3, $"took {s:F1} s");
        }

        public void Jaro_MatchesTheWindowScanReference()
        {
            var rng = new Random(4);
            for (int trial = 0; trial < 5000; trial++)
            {
                string a = RandomText(rng, rng.Next(0, 15), rng.Next(1, 5)), b = RandomText(rng, rng.Next(0, 15), rng.Next(1, 5));
                Check.True(Math.Abs(JaroReference(a, b) - JaroWinkler.Jaro(a, b)) < 1e-12, $"Jaro('{a}', '{b}')");
            }
        }

        private static double JaroReference(string a, string b)
        {
            if (a.Length == 0 && b.Length == 0) return 1;
            if (a.Length == 0 || b.Length == 0) return 0;
            int window = Math.Max(0, Math.Max(a.Length, b.Length) / 2 - 1);
            var ma = new bool[a.Length];
            var mb = new bool[b.Length];
            int m = 0;
            for (int i = 0; i < a.Length; i++)
                for (int j = Math.Max(0, i - window); j < Math.Min(b.Length, i + window + 1); j++)
                    if (!mb[j] && a[i] == b[j]) { ma[i] = mb[j] = true; m++; break; }
            if (m == 0) return 0;
            int t = 0;
            for (int i = 0, k = 0; i < a.Length; i++)
            {
                if (!ma[i]) continue;
                while (!mb[k]) k++;
                if (a[i] != b[k]) t++;
                k++;
            }
            return (m / (double)a.Length + m / (double)b.Length + (m - t / 2.0) / m) / 3;
        }

        // ---- TextDiff ----

        public void TextDiff_TableTakesBitsNotInts()
        {
            string a = string.Join("\n", Enumerable.Range(0, 10000).Select(i => "a" + i));
            string b = string.Join("\n", Enumerable.Range(0, 10000).Select(i => "b" + i));
            IReadOnlyList<DiffLine> diff = Array.Empty<DiffLine>();
            long bytes = Allocated(() => diff = TextDiff.Lines(a, b));
            Check.Equal(20000, diff.Count);
            Check.True(bytes < 100L << 20, $"allocated {bytes >> 20} MB (was about 400 MB)");
        }

        public void TextDiff_PastTheTableLimit_ThrowsArgumentException()
        {
            string a = string.Concat(Enumerable.Repeat("a\n", 100000));
            string b = string.Concat(Enumerable.Repeat("b\n", 100000));
            Check.Equal("newText", ParamOf(() => TextDiff.Lines(a, b)));
        }

        public void TextDiff_IsALongestCommonSubsequence_OverRandomTexts()
        {
            var rng = new Random(5);
            for (int trial = 0; trial < 1500; trial++)
            {
                string[] a = Enumerable.Range(0, rng.Next(0, 12)).Select(_ => "xyz"[rng.Next(3)].ToString()).ToArray();
                string[] b = Enumerable.Range(0, rng.Next(0, 12)).Select(_ => "xyz"[rng.Next(3)].ToString()).ToArray();
                var diff = TextDiff.Lines(string.Join("\n", a), string.Join("\n", b));
                var oldBack = diff.Where(d => d.Op != DiffOp.Insert).Select(d => d.Text).ToArray();
                var newBack = diff.Where(d => d.Op != DiffOp.Delete).Select(d => d.Text).ToArray();
                Check.Equal(string.Join("\n", a), string.Join("\n", oldBack));
                Check.Equal(string.Join("\n", b), string.Join("\n", newBack));
                if (a.Length > 0 && b.Length > 0)
                    Check.Equal(Lcs(a, b), diff.Count(d => d.Op == DiffOp.Equal));
            }
        }

        private static int Lcs(string[] a, string[] b)
        {
            var t = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length - 1; i >= 0; i--)
                for (int j = b.Length - 1; j >= 0; j--)
                    t[i, j] = a[i] == b[j] ? t[i + 1, j + 1] + 1 : Math.Max(t[i + 1, j], t[i, j + 1]);
            return t[0, 0];
        }

        // ---- NanoId, CommandLineBuilder, TextAlign, Mask argument checks ----

        private sealed class OutOfRangeRandom : Random
        {
            public override int Next(int maxValue) => maxValue;
        }

        public void NanoId_ARandomBreakingNextsContract_ThrowsInvalidOperation()
        {
            var ids = new NanoId(new OutOfRangeRandom());
            Check.Throws<InvalidOperationException>(() => ids.New());   // was IndexOutOfRangeException
        }

        public void CommandLineBuilder_Join_NullElement_NamesArguments()
        {
            Check.Equal("arguments", ParamOf(() => CommandLineBuilder.Join(new[] { "a", null! })));
        }

        public void TextAlign_LoneSurrogatePad_IsRefused()
        {
            Check.Equal("pad", ParamOf(() => TextAlign.PadRight("ab", 4, '\uD800')));
            Check.Equal("pad", ParamOf(() => TextAlign.Fit("ab", 4, pad: '\uDC00')));
        }

        public void Mask_LoneSurrogateMaskChar_IsRefused()
        {
            Check.Equal("maskChar", ParamOf(() => Mask.Trailing("123456", 2, '\uD83D')));
            Check.Equal("maskChar", ParamOf(() => Mask.Leading("123456", 2, '\uDE00')));
            Check.Equal("****56", Mask.Trailing("123456", 2));
        }

        // ---- CaseConverter ----

        public void CaseConverter_DigitThenCapital_NeedsALowerCaseLetterToSplit()
        {
            Check.Equal("a1b", CaseConverter.ToSnakeCase(CaseConverter.ToConstantCase("a1b")));   // A1B read as A1 | B
            Check.Equal("A1B", CaseConverter.ToConstantCase("A1B"));
            Check.Equal("md5_hash", CaseConverter.ToSnakeCase("md5Hash"));
            Check.Equal("v2_beta", CaseConverter.ToSnakeCase("v2Beta"));
        }

        public void CaseConverter_CamelCase_WithALeadingSharpS_IsIdempotent()
        {
            Check.Equal("ßBa", CaseConverter.ToCamelCase("ß-Ba"));
            Check.Equal("ßBa", CaseConverter.ToCamelCase("ßBa"));               // read back as one word: "ßba"
            Check.Equal("ß_ba", CaseConverter.ToSnakeCase("ßBa"));
            Check.Equal("strasse", CaseConverter.ToSnakeCase("STRAßE").Replace("ß", "ss"));
            Check.Equal(1, CaseConverter.ToWords("STRAßE").Count);
        }

        public void CaseConverter_SnakeKebabConstantAndTitle_AreIdempotent_WithDigitsAndOddLetters()
        {
            var rng = new Random(6);
            string[] pieces = { "a", "b", "B", "1", "2", "ß", "ǅ", "ǆ", "İ", "ı", "x", "Y", "_", "-", " " };
            var styles = new Func<string, string>[] { CaseConverter.ToSnakeCase, CaseConverter.ToKebabCase, CaseConverter.ToConstantCase, CaseConverter.ToTitleCase };
            for (int trial = 0; trial < 5000; trial++)
            {
                string input = string.Concat(Enumerable.Range(0, rng.Next(1, 8)).Select(_ => pieces[rng.Next(pieces.Length)]));
                foreach (var style in styles)
                {
                    string once = style(input);
                    Check.Equal(once, style(once), $"{style.Method.Name}('{input}')");
                }
                // Every style reads back the same words; 'ß', 'ı' and 'İ' are documented not to survive upper case.
                if (input.IndexOfAny(new[] { 'ß', 'ı', 'İ' }) >= 0) continue;
                string snake = CaseConverter.ToSnakeCase(input);
                Check.Equal(snake, CaseConverter.ToSnakeCase(CaseConverter.ToConstantCase(input)), $"constant of '{input}'");
                Check.Equal(snake, CaseConverter.ToSnakeCase(CaseConverter.ToTitleCase(input)), $"title of '{input}'");
            }
        }

        public void CaseConverter_Acronym_EndsBeforeADecomposedCapital()
        {
            string decomposed = "XMLÉcole";
            var words = CaseConverter.ToWords(decomposed);
            Check.Equal(2, words.Count);
            Check.Equal("XML", words[0]);
            Check.Equal("École", words[1]);
            Check.Equal(CaseConverter.ToSnakeCase("XMLÉcole").Normalize(NormalizationForm.FormD), CaseConverter.ToSnakeCase(decomposed));
        }

        // ---- TextAlign widths ----

        public void TextAlign_ConjoiningJamo_JoinTheLeadingConsonant()
        {
            Check.Equal(2, TextAlign.DisplayWidth("한"));        // 한, decomposed
            Check.Equal(TextAlign.DisplayWidth("한"), TextAlign.DisplayWidth("한".Normalize(NormalizationForm.FormD)));
            Check.Equal(2, TextAlign.DisplayWidth("ᄀힰ"));
        }

        public void TextAlign_WideSupplementaryScripts_TakeTwoColumns()
        {
            Check.Equal(2, TextAlign.DisplayWidth("\U00017000"));                 // Tangut
            Check.Equal(2, TextAlign.DisplayWidth("\U0001B001"));                 // Hentaigana
            Check.Equal(2, TextAlign.DisplayWidth("\U0001B170"));                 // Nushu
            Check.Equal(2, TextAlign.DisplayWidth("\U00018B00"));                 // Khitan small script
            Check.Equal(2, TextAlign.DisplayWidth("\U00016FE0"));
        }

        public void TextAlign_EmojiPresentationSelector_WidensTheSymbol()
        {
            Check.Equal(2, TextAlign.DisplayWidth("❤️"));
            Check.Equal(1, TextAlign.DisplayWidth("❤"));
            Check.Equal(1, TextAlign.DisplayWidth("a️"));
            Check.Equal(4, TextAlign.DisplayWidth(TextAlign.PadRight("❤️", 4)));
            Check.Equal("❤️" + "…", TextAlign.Fit("❤️❤️", 3));
        }

        // ---- Escape sequences with control characters inside ----

        public void AnsiText_ControlCharacterInsideASequence_IsKept()
        {
            Check.Equal("a\n", AnsiText.Strip("a\u001b[3\nb"));                   // 'b' ends the CSI, as in a terminal
            Check.Equal("a\n", AnsiText.Strip("a\u001b[3\nm"));
            Check.Equal("\u0018ab", AnsiText.Strip("\u001b[3\u0018ab"));           // CAN aborts the sequence
            Check.Equal("\u001aab", AnsiText.Strip("\u001b]0;t\u001aab"));         // SUB aborts a string too
            Check.Equal("中x", AnsiText.Strip("\u001b(中x"));                       // non-ASCII is text, not a final byte
            Check.Equal("中x", AnsiText.Strip("\u001b[1中x"));
            Check.Equal("red", AnsiText.Strip("\u001b[31mred\u001b[0m"));
            Check.Equal("x", AnsiText.Strip("\u001b(Bx"));
        }

        public void TextAlign_SequenceAbortedByCanOrNonAscii_CountsTheTextAfter()
        {
            Check.Equal(2, TextAlign.DisplayWidth("\u001b[3\u0018ab"));
            Check.Equal(2, TextAlign.DisplayWidth("\u001b[1中"));
            Check.Equal(2, TextAlign.DisplayWidth("\u001b(中"));
            Check.Equal(2, TextAlign.DisplayWidth("\u001b]0;t\u0018ab"));
            Check.Equal(1, TextAlign.DisplayWidth("a\u001b[3\nb"));
            Check.Equal(3, TextAlign.DisplayWidth("\u001b[31mred\u001b[0m"));
        }

        // ---- Slug ----

        public void Slug_LettersWithoutDecomposition_AreFolded()
        {
            Check.Equal("ijssel-l-t", Slug.Slugify("Ĳssel ŀ Ŧ"));
            Check.Equal("hamrun", Slug.Slugify("Ħamrun"));
            Check.Equal("ng-q-b-z", Slug.Slugify("ŋ ĸ ƀ Ƶ"));
            Check.Equal("dz-lj-nj", Slug.Slugify("ǆ ǈ Ǌ"));
            Check.Equal("nabc", Slug.Slugify("ŉabc"));
            Check.Equal("abc-x2", Slug.Slugify("ＡＢＣ x²"));
            Check.Equal("strasse", Slug.Slugify("Straße"));
            foreach (string s in new[] { "Ĳssel ŀ Ŧ", "ꜳ Ꝏ ȸ", "ǽ ǿ ǯ" })
                Check.Equal(Slug.Slugify(s), Slug.Slugify(Slug.Slugify(s)));
        }

        // ---- UlidGenerator ----

        private sealed class MaxRandom : Random
        {
            public override void NextBytes(byte[] buffer) { for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xFF; }
        }

        public void Ulid_RandomOverflow_ThrowsInsteadOfCarryingIntoTheTimestamp()
        {
            var now = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var gen = new UlidGenerator(() => now, new MaxRandom());
            string first = gen.NewUlid();
            Check.Throws<InvalidOperationException>(() => gen.NewUlid());
            Check.Throws<InvalidOperationException>(() => gen.NewUlid());   // and stays refused, never a smaller id
            Check.Equal(UlidGenerator.DecodeTimestamp(first), now.ToUnixTimeMilliseconds());
            now = now.AddMilliseconds(1);
            string next = gen.NewUlid();
            Check.True(string.CompareOrdinal(next, first) > 0);
            Check.Equal(now.ToUnixTimeMilliseconds(), UlidGenerator.DecodeTimestamp(next));
        }
    }
}
