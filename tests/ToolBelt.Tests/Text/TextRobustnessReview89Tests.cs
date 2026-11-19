using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    // Round 89 review regressions (Text & small types, fifth pass): robustness.
    public sealed class TextRobustnessReview89Tests
    {
        private static double NaiveDice(string a, string b, int n)
        {
            var ga = Grams(a, n);
            var gb = Grams(b, n);
            int ta = ga.Values.Sum(), tb = gb.Values.Sum();
            if (ta + tb == 0) return a == b ? 1.0 : 0.0;
            int inter = 0;
            foreach (var p in ga) if (gb.TryGetValue(p.Key, out int c)) inter += Math.Min(p.Value, c);
            return 2.0 * inter / (ta + tb);
        }

        private static double NaiveJaccard(string a, string b, int n)
        {
            var ga = Grams(a, n);
            var gb = Grams(b, n);
            if (ga.Count == 0 && gb.Count == 0) return a == b ? 1.0 : 0.0;
            int inter = ga.Keys.Count(gb.ContainsKey);
            return (double)inter / (ga.Count + gb.Count - inter);
        }

        // Grams over characters, a surrogate pair as one (round 154: grams were UTF-16 units).
        private static Dictionary<string, int> Grams(string s, int n)
        {
            var units = new List<string>();
            for (int i = 0; i < s.Length; i++)
            {
                if (i + 1 < s.Length && char.IsSurrogatePair(s[i], s[i + 1])) { units.Add(s.Substring(i, 2)); i++; }
                else units.Add(s[i].ToString());
            }
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i + n <= units.Count; i++)
            {
                string g = string.Join("", units.GetRange(i, n));
                d.TryGetValue(g, out int c);
                d[g] = c + 1;
            }
            return d;
        }

        public void NGram_MatchesANaiveReference()
        {
            var rng = new Random(8901);
            string[] alphabets = { "ab", "abc", "a\U0001F600", "xyzw" };
            for (int trial = 0; trial < 4000; trial++)
            {
                string alpha = alphabets[trial % alphabets.Length];
                string a = Rand(rng, alpha, rng.Next(0, 40)), b = Rand(rng, alpha, rng.Next(0, 40));
                int n = 1 + rng.Next(12);
                Check.Equal(NaiveDice(a, b, n), NGramSimilarity.Dice(a, b, n), $"Dice {a} {b} {n}");
                Check.Equal(NaiveJaccard(a, b, n), NGramSimilarity.Jaccard(a, b, n), $"Jaccard {a} {b} {n}");
            }
        }

        private static string Rand(Random rng, string alpha, int length)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append(alpha[rng.Next(alpha.Length)]);
            return sb.ToString();
        }

        public void NGram_HashCollidingGramsStayFast()
        {
            // A Thue–Morse block and its complement share a polynomial hash mod 2^64 for any base, so strings built
            // from them gave thousands of distinct grams with one hash (1,024,000 characters took 43 s).
            var block = new char[1 << 11];
            for (int i = 0; i < block.Length; i++) block[i] = CountBits(i) % 2 == 0 ? 'a' : 'b';
            string t = new string(block), u = new string(block.Select(c => c == 'a' ? 'b' : 'a').ToArray());
            var rng = new Random(89);
            var sb = new StringBuilder(1024000);
            while (sb.Length < 1024000) sb.Append(rng.Next(2) == 0 ? t : u);
            string s = sb.ToString();
            var sw = Stopwatch.StartNew();
            double dice = NGramSimilarity.Dice(s, s.Substring(0, 30000), 24576);
            Check.True(sw.Elapsed.TotalSeconds < 10, $"took {sw.Elapsed.TotalSeconds:F1} s");
            Check.True(dice > 0 && dice <= 1);
            // Repetitive text compared every repeat in full: O(length·n).
            string rep = new string('a', 200000);
            sw.Restart();
            Check.Equal(1.0, NGramSimilarity.Dice(rep, rep, 100000));
            Check.Equal(1.0, NGramSimilarity.Jaccard(rep, rep, 100000));
            Check.True(sw.Elapsed.TotalSeconds < 2, $"took {sw.Elapsed.TotalSeconds:F1} s");
        }

        private static int CountBits(int x)
        {
            int c = 0;
            for (; x != 0; x &= x - 1) c++;
            return c;
        }

        public void TextDiff_LongLinesCostNoMoreThanShortOnes()
        {
            // Each cell compared two 10,000-character strings that differ only at the end (2,000 lines a side took 6 s).
            string p = new string('q', 10000);
            string a = string.Join("\n", Enumerable.Repeat(p + "x", 2000));
            string b = string.Join("\n", Enumerable.Repeat(p + "y", 2000));
            var sw = Stopwatch.StartNew();
            var diff = TextDiff.Lines(a, b);
            Check.True(sw.Elapsed.TotalSeconds < 1.5, $"took {sw.Elapsed.TotalSeconds:F1} s");
            Check.Equal(4000, diff.Count);
            Check.Equal(2000, diff.Count(d => d.Op == DiffOp.Delete));
        }

        public void Glob_ManyDistinctUnnamedCharactersShareOneMask()
        {
            // 5,000 distinct CJK characters against a 20,000-character segment rebuilt a mask on nearly every character
            // once the 8 MB cache was cleared (13 s).
            string pattern = "*" + new string('a', 20000) + "*";
            var sb = new StringBuilder(200000);
            for (int i = 0; i < 200000; i++) sb.Append((char)(0x4E00 + i % 5000));
            var sw = Stopwatch.StartNew();
            Check.False(GlobMatcher.IsMatch(sb.ToString(), pattern));
            Check.True(sw.Elapsed.TotalSeconds < 3, $"took {sw.Elapsed.TotalSeconds:F1} s");
            // The shared mask still honours ? and negated classes, and named characters theirs.
            Check.True(GlobMatcher.IsMatch("x\u4E00a\u4E01b\u4E02y", "x*a?b[!c]y*"));
            Check.False(GlobMatcher.IsMatch("x\u4E00acb\u4E02y", "x*a?b[!\u4E02]y*"));
            Check.True(GlobMatcher.IsMatch("..SQ..", "*[s]q*", ignoreCase: true));
            Check.False(GlobMatcher.IsMatch("..SQ..", "*[s]q*"));
            Check.True(GlobMatcher.IsMatch("..kq..", "*[!K]q*"));
            Check.False(GlobMatcher.IsMatch("..kq..", "*[!K]q*", ignoreCase: true));
        }

        public void QueryString_FormatRejectsAnUnpairedSurrogate()
        {
            // .NET 8 wrote U+FFFD (Parse didn't give it back); .NET Framework threw UriFormatException.
            foreach (var pair in new[] { new KeyValuePair<string, string>("k", "\uD800"), new KeyValuePair<string, string>("\uDC00x", "v") })
            {
                var ex = Check.Throws<ArgumentException>(() => QueryString.Format(new[] { pair }));
                Check.Equal("pairs", ex.ParamName);
            }
            string ok = QueryString.Format(new[] { new KeyValuePair<string, string>("k", "\U0001F600") });
            Check.Equal("\U0001F600", QueryString.Parse(ok)[0].Value);
        }

        public void TopMatches_MatchesAFullSortAndKeepsOnlyCount()
        {
            var rng = new Random(8902);
            for (int trial = 0; trial < 500; trial++)
            {
                int size = rng.Next(0, 60), count = rng.Next(1, 70);
                var candidates = Enumerable.Range(0, size).Select(i => "c" + i).ToList();
                var scores = candidates.ToDictionary(c => c, _ => (double)rng.Next(5));
                double min = rng.Next(3) == 0 ? 2 : double.NegativeInfinity;
                var expected = candidates.Select((c, i) => (c, i)).Where(x => scores[x.c] >= min)
                    .OrderByDescending(x => scores[x.c]).ThenBy(x => x.i).Take(count).Select(x => x.c).ToList();
                var got = StringSimilarity.TopMatches("t", candidates, (_, c) => scores[c], count, min).Select(m => m.Value).ToList();
                Check.Equal(string.Join(",", expected), string.Join(",", got));
            }
#if NETCOREAPP
            // Every qualifying candidate was kept and sorted: the top 3 of 2 million held all 2 million.
            long before = GC.GetAllocatedBytesForCurrentThread();
            var top = StringSimilarity.TopMatches("t", Many(2000000), (_, c) => c.Length, 3);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal(3, top.Count);
            Check.True(allocated < 4 << 20, $"allocated {allocated} bytes");
#endif
        }

        private static IEnumerable<string> Many(int count)
        {
            for (int i = 0; i < count; i++) yield return "same";
        }
    }
}
