using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class NGramSimilarityTests
    {
        public void IdenticalStrings_ScoreOne()
        {
            Check.Close(1.0, NGramSimilarity.Dice("night", "night"));
            Check.Close(1.0, NGramSimilarity.Jaccard("night", "night"));
        }

        public void DisjointStrings_ScoreZero()
        {
            Check.Close(0.0, NGramSimilarity.Dice("abc", "xyz"));
            Check.Close(0.0, NGramSimilarity.Jaccard("abc", "xyz"));
        }

        public void KnownDiceValue()
        {
            // "night"/"nacht" bigrams: {ni,ig,gh,ht} vs {na,ac,ch,ht}; intersection {ht}=1.
            // Dice = 2*1/(4+4) = 0.25.
            Check.Close(0.25, NGramSimilarity.Dice("night", "nacht"));
        }

        public void ShortStrings_FallBackToEquality()
        {
            Check.Close(1.0, NGramSimilarity.Dice("a", "a"));   // no bigrams, equal
            Check.Close(0.0, NGramSimilarity.Dice("a", "b"));   // no bigrams, unequal
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => NGramSimilarity.Dice(null!, "x"));
            Check.Throws<ArgumentOutOfRangeException>(() => NGramSimilarity.Dice("x", "y", 0));
        }

        public void Properties_OverRandomStrings()
        {
            var rng = new Random(2718);
            for (int trial = 0; trial < 1000; trial++)
            {
                string a = RandomString(rng, "abcd", 8);
                string b = RandomString(rng, "abcd", 8);
                int n = rng.Next(1, 4);

                double dice = NGramSimilarity.Dice(a, b, n);
                double jac = NGramSimilarity.Jaccard(a, b, n);

                foreach (var v in new[] { dice, jac })
                    Check.True(v >= -1e-12 && v <= 1 + 1e-12, $"trial {trial}: score {v} out of range");

                Check.Close(dice, NGramSimilarity.Dice(b, a, n), 1e-12, $"trial {trial}: Dice symmetry");
                Check.Close(jac, NGramSimilarity.Jaccard(b, a, n), 1e-12, $"trial {trial}: Jaccard symmetry");
            }
        }

        // Differential: independent LINQ-based reference implementations over random strings.
        public void Differential_MatchesLinqReference()
        {
            var rng = new Random(161803);
            for (int trial = 0; trial < 2000; trial++)
            {
                string a = RandomString(rng, "abc", 7);
                string b = RandomString(rng, "abc", 7);
                int n = rng.Next(1, 4);

                Check.Close(RefDice(a, b, n), NGramSimilarity.Dice(a, b, n), 1e-12, $"trial {trial}: Dice '{a}'/'{b}' n={n}");
                Check.Close(RefJaccard(a, b, n), NGramSimilarity.Jaccard(a, b, n), 1e-12, $"trial {trial}: Jaccard '{a}'/'{b}' n={n}");
            }
        }

        private static List<string> RefGrams(string s, int n)
            => Enumerable.Range(0, Math.Max(0, s.Length - n + 1)).Select(i => s.Substring(i, n)).ToList();

        private static double RefDice(string a, string b, int n)
        {
            var la = RefGrams(a, n);
            var lb = RefGrams(b, n);
            if (la.Count + lb.Count == 0)
                return a == b ? 1.0 : 0.0;

            // Multiset intersection via grouped min-counts.
            var ca = la.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var cb = lb.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            int inter = ca.Keys.Where(cb.ContainsKey).Sum(k => Math.Min(ca[k], cb[k]));
            return 2.0 * inter / (la.Count + lb.Count);
        }

        private static double RefJaccard(string a, string b, int n)
        {
            var sa = new HashSet<string>(RefGrams(a, n));
            var sb = new HashSet<string>(RefGrams(b, n));
            if (sa.Count == 0 && sb.Count == 0)
                return a == b ? 1.0 : 0.0;
            int inter = sa.Count(sb.Contains);
            int union = sa.Count + sb.Count - inter;
            return (double)inter / union;
        }

        private static string RandomString(Random rng, string palette, int maxLen)
        {
            int len = rng.Next(0, maxLen + 1);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append(palette[rng.Next(palette.Length)]);
            return sb.ToString();
        }
    }
}
