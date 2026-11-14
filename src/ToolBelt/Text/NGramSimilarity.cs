// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Text
{
    /// <summary>
    /// String similarity based on character n-grams. <see cref="Dice"/> is the Sørensen–Dice coefficient
    /// over the n-gram multiset (counts with multiplicity); <see cref="Jaccard"/> is the Jaccard index
    /// over the distinct n-gram set. Both return a value in [0, 1] (1 = identical). Two strings too short
    /// to yield any n-gram are treated as similar (1.0) only if they are equal.
    /// </summary>
    public static class NGramSimilarity
    {
        /// <summary>Sørensen–Dice coefficient over n-gram multisets.</summary>
        public static double Dice(string a, string b, int n = 2)
        {
            Validate(a, b, n);
            var ga = Grams(a, n, out int totalA);
            var gb = Grams(b, n, out int totalB);

            if (totalA + totalB == 0)
                return a == b ? 1.0 : 0.0;

            int intersection = 0;
            foreach (var pair in ga)
                if (gb.TryGetValue(pair.Key, out int countB))
                    intersection += Math.Min(pair.Value, countB);

            return 2.0 * intersection / (totalA + totalB);
        }

        /// <summary>Jaccard index over the set of distinct n-grams.</summary>
        public static double Jaccard(string a, string b, int n = 2)
        {
            Validate(a, b, n);
            var ga = Grams(a, n, out _);
            var gb = Grams(b, n, out _);

            if (ga.Count == 0 && gb.Count == 0)
                return a == b ? 1.0 : 0.0;

            int intersection = 0;
            foreach (var key in ga.Keys)
                if (gb.ContainsKey(key))
                    intersection++;

            int union = ga.Count + gb.Count - intersection;
            return (double)intersection / union;
        }

        // An n-gram is kept as (string, start) with a rolling hash, compared in place: one Substring per gram allocated
        // O(length·n) (40,000 characters with n = 20,000 took 1.5 GB).
        private readonly struct Gram
        {
            public readonly string Source;
            public readonly int Start;
            public readonly ulong Hash;

            public Gram(string source, int start, ulong hash)
            {
                Source = source;
                Start = start;
                Hash = hash;
            }
        }

        private sealed class GramComparer : IEqualityComparer<Gram>
        {
            private readonly int _n;
            public GramComparer(int n) => _n = n;
            public bool Equals(Gram x, Gram y) => x.Hash == y.Hash && string.CompareOrdinal(x.Source, x.Start, y.Source, y.Start, _n) == 0;
            public int GetHashCode(Gram g) => unchecked((int)(g.Hash ^ (g.Hash >> 32)));
        }

        private const ulong HashBase = 0x100000001B3UL;

        private static Dictionary<Gram, int> Grams(string s, int n, out int total)
        {
            var grams = new Dictionary<Gram, int>(new GramComparer(n));
            total = 0;
            if (n > s.Length) return grams;
            unchecked
            {
                ulong hash = 0, top = 1;                 // top = HashBase^(n-1)
                for (int j = 0; j < n; j++)
                {
                    hash = hash * HashBase + s[j];
                    if (j > 0) top *= HashBase;
                }
                for (int i = 0; ; i++)
                {
                    var g = new Gram(s, i, hash);
                    grams.TryGetValue(g, out int count);
                    grams[g] = count + 1;
                    total++;
                    if (i + n >= s.Length) break;
                    hash = (hash - s[i] * top) * HashBase + s[i + n];
                }
            }
            return grams;
        }

        private static void Validate(string a, string b, int n)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), n, "n must be at least 1.");
        }
    }
}
