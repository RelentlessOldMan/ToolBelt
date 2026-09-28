// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Text
{
    /// <summary>
    /// Levenshtein edit distance — the minimum number of single-character insertions, deletions, or
    /// substitutions to turn one string into another — plus a normalized similarity ratio. Uses a
    /// two-row dynamic-programming rolling buffer, so memory is O(min length) rather than O(n·m).
    /// </summary>
    public static class LevenshteinDistance
    {
        /// <summary>Computes the edit distance between <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static int Distance(string a, string b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));

            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            // Iterate columns over the shorter string to keep the rolling buffers small.
            if (a.Length < b.Length)
                (a, b) = (b, a);

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                char ai = a[i - 1];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = ai == b[j - 1] ? 0 : 1;
                    int deletion = previous[j] + 1;
                    int insertion = current[j - 1] + 1;
                    int substitution = previous[j - 1] + cost;
                    current[j] = Math.Min(Math.Min(deletion, insertion), substitution);
                }

                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }

        /// <summary>
        /// Similarity in [0, 1]: <c>1 - distance / max(len)</c>. Two empty strings are defined as
        /// identical (1.0). 1.0 means equal; 0.0 means maximally different.
        /// </summary>
        public static double Similarity(string a, string b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));

            int max = Math.Max(a.Length, b.Length);
            if (max == 0)
                return 1.0;
            return 1.0 - (double)Distance(a, b) / max;
        }
    }
}
