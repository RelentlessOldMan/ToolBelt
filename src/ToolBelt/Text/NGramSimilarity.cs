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

        private static Dictionary<string, int> Grams(string s, int n, out int total)
        {
            var grams = new Dictionary<string, int>();
            total = 0;
            for (int i = 0; i + n <= s.Length; i++)
            {
                string g = s.Substring(i, n);
                grams.TryGetValue(g, out int count);
                grams[g] = count + 1;
                total++;
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
