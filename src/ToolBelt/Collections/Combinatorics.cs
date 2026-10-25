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

        /// <summary>
        /// All permutations of <paramref name="items"/> (n! of them), starting with the input order. Each
        /// enumeration copies <paramref name="items"/> when it starts, so enumerations are independent.
        /// </summary>
        public static IEnumerable<T[]> Permutations<T>(IReadOnlyList<T> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            return Permute(items);
        }

        // The swap-and-recurse order (at depth d, swap each of positions d..n-1 into d, recurse, swap back),
        // run on an explicit stack: next[d] is the position currently swapped into d. A recursive version
        // nests one iterator per element and overflows the stack for a long list.
        private static IEnumerable<T[]> Permute<T>(IReadOnlyList<T> items)
        {
            int n = items.Count;
            var arr = new T[n];
            for (int i = 0; i < n; i++)
                arr[i] = items[i];
            if (n <= 1)
            {
                yield return arr;
                yield break;
            }

            var next = new int[n];
            int depth = 0;
            while (depth >= 0)
            {
                if (depth == n - 1)
                {
                    yield return (T[])arr.Clone();
                    depth--;
                    Unswap(arr, next, depth);
                    continue;
                }

                int i = next[depth];
                if (i < n)
                {
                    (arr[depth], arr[i]) = (arr[i], arr[depth]);
                    depth++;
                    next[depth] = depth;
                }
                else if (--depth >= 0)
                {
                    Unswap(arr, next, depth);
                }
            }
        }

        // Restore the swap made at this depth and move on to the next candidate position.
        private static void Unswap<T>(T[] arr, int[] next, int depth)
        {
            int i = next[depth];
            (arr[depth], arr[i]) = (arr[i], arr[depth]);
            next[depth] = i + 1;
        }
    }
}
