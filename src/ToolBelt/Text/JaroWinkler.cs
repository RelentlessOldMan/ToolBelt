// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Text
{
    /// <summary>
    /// Jaro and Jaro–Winkler string similarity in [0, 1] (1 = identical). Jaro–Winkler boosts the Jaro
    /// score for strings that share a common prefix, which suits short strings like names. Comparison is
    /// per UTF-16 code unit.
    /// </summary>
    public static class JaroWinkler
    {
        /// <summary>The Jaro similarity of two strings.</summary>
        public static double Jaro(string a, string b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));

            int lenA = a.Length, lenB = b.Length;
            if (lenA == 0 && lenB == 0) return 1.0;
            if (lenA == 0 || lenB == 0) return 0.0;

            int matchDistance = Math.Max(0, Math.Max(lenA, lenB) / 2 - 1);
            var aMatched = new bool[lenA];
            var bMatched = new bool[lenB];

            // Each a[i] takes the first unmatched b[j] equal to it in the window. Scanning the window was O(n·m) (80,000
            // characters took 3.4 s); instead each character keeps a pointer into its positions in b. The window start
            // never moves back and matches are taken in order, so every position before the pointer is used or behind.
            var nextSame = new int[lenB];
            var pointer = new Dictionary<char, int>();
            for (int j = lenB - 1; j >= 0; j--)
            {
                nextSame[j] = pointer.TryGetValue(b[j], out int later) ? later : -1;
                pointer[b[j]] = j;
            }
            int matches = 0;
            for (int i = 0; i < lenA; i++)
            {
                if (!pointer.TryGetValue(a[i], out int j)) continue;
                int start = Math.Max(0, i - matchDistance);
                int end = Math.Min(i + matchDistance + 1, lenB);
                while (j >= 0 && j < start) j = nextSame[j];
                if (j >= 0 && j < end)
                {
                    aMatched[i] = true;
                    bMatched[j] = true;
                    matches++;
                    j = nextSame[j];
                }
                pointer[a[i]] = j;
            }

            if (matches == 0)
                return 0.0;

            // Count transpositions: matched characters that appear in a different order.
            double transpositions = 0;
            int k = 0;
            for (int i = 0; i < lenA; i++)
            {
                if (!aMatched[i]) continue;
                while (!bMatched[k]) k++;
                if (a[i] != b[k]) transpositions++;
                k++;
            }
            transpositions /= 2;

            double m = matches;
            return (m / lenA + m / lenB + (m - transpositions) / m) / 3.0;
        }

        /// <summary>
        /// The Jaro–Winkler similarity: Jaro plus a prefix bonus for up to <paramref name="maxPrefix"/>
        /// (default 4) leading characters in common, weighted by <paramref name="prefixScale"/>.
        /// </summary>
        public static double Similarity(string a, string b, double prefixScale = 0.1, int maxPrefix = 4)
        {
            if (!(prefixScale >= 0 && prefixScale <= 0.25))
                throw new ArgumentOutOfRangeException(nameof(prefixScale), prefixScale, "Prefix scale must be in [0, 0.25].");
            if (maxPrefix < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPrefix), maxPrefix, "Max prefix must not be negative.");
            if (prefixScale * maxPrefix > 1)
                throw new ArgumentOutOfRangeException(nameof(maxPrefix), maxPrefix, "Prefix scale × max prefix must not exceed 1, or the score could pass 1.");

            double jaro = Jaro(a, b);

            int limit = Math.Min(maxPrefix, Math.Min(a.Length, b.Length));
            int prefix = 0;
            for (int i = 0; i < limit; i++)
            {
                if (a[i] == b[i]) prefix++;
                else break;
            }

            return jaro + prefix * prefixScale * (1 - jaro);
        }
    }
}
