// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Lazily enumerates the combinations and permutations of a list. Each yielded result is a fresh
    /// array the caller may keep. Combinations preserve the input's order (index-increasing subsets);
    /// permutations cover all n! orderings. These grow explosively — iterate, don't materialize blindly.
    /// </summary>
    public static class Combinatorics
    {
        /// <summary>All <paramref name="k"/>-element combinations of <paramref name="items"/> (order preserved).</summary>
        public static IEnumerable<T[]> Combinations<T>(IReadOnlyList<T> items, int k)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), k, "k must not be negative.");
            return CombinationsIterator(items, k);
        }

        private static IEnumerable<T[]> CombinationsIterator<T>(IReadOnlyList<T> items, int k)
        {
            int n = items.Count;
            if (k > n)
                yield break; // C(n,k) = 0 when k > n

            var indices = new int[k];
            for (int i = 0; i < k; i++)
                indices[i] = i;

            while (true)
            {
                var combo = new T[k];
                for (int i = 0; i < k; i++)
                    combo[i] = items[indices[i]];
                yield return combo;

                // Advance the rightmost index that still has room, then reset those to its right.
                int p = k - 1;
                while (p >= 0 && indices[p] == n - k + p)
                    p--;
                if (p < 0)
                    yield break;
                indices[p]++;
                for (int j = p + 1; j < k; j++)
                    indices[j] = indices[j - 1] + 1;
            }
        }

        /// <summary>All permutations of <paramref name="items"/> (n! of them).</summary>
        public static IEnumerable<T[]> Permutations<T>(IReadOnlyList<T> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var working = new T[items.Count];
            for (int i = 0; i < working.Length; i++)
                working[i] = items[i];
            return Permute(working, 0);
        }

        private static IEnumerable<T[]> Permute<T>(T[] arr, int start)
        {
            if (start >= arr.Length - 1)
            {
                yield return (T[])arr.Clone();
                yield break;
            }

            for (int i = start; i < arr.Length; i++)
            {
                (arr[start], arr[i]) = (arr[i], arr[start]);
                foreach (var p in Permute(arr, start + 1))
                    yield return p;
                (arr[start], arr[i]) = (arr[i], arr[start]); // restore for the next swap
            }
        }
    }
}
