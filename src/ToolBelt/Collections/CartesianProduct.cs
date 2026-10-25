// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Lazily enumerates the Cartesian product of several sequences: every combination that picks one
    /// element from each input, in input order (the last sequence varies fastest, like an odometer). If
    /// any input is empty the product is empty; with no inputs the product is a single empty tuple. Each
    /// yielded tuple is a fresh array.
    /// </summary>
    public static class CartesianProduct
    {
        /// <summary>The product of <paramref name="sequences"/>; use this overload for zero or one sequence.</summary>
        public static IEnumerable<T[]> Of<T>(IReadOnlyList<IReadOnlyList<T>> sequences)
        {
            if (sequences is null) throw new ArgumentNullException(nameof(sequences));
            for (int i = 0; i < sequences.Count; i++)
                if (sequences[i] is null)
                    throw new ArgumentException($"Sequence at index {i} is null.", nameof(sequences));
            return Iterator(sequences);
        }

        /// <summary>The product of two or more sequences passed as separate arguments.</summary>
        /// <remarks>
        /// This takes at least two so that a single argument always means "the list of sequences": with a
        /// one-sequence params form, C# would bind a <c>List&lt;List&lt;int&gt;&gt;</c> to it as one sequence
        /// of lists (T = <c>List&lt;int&gt;</c>) rather than to the overload above.
        /// </remarks>
        public static IEnumerable<T[]> Of<T>(IReadOnlyList<T> first, IReadOnlyList<T> second, params IReadOnlyList<T>[] rest)
        {
            if (rest is null) throw new ArgumentNullException(nameof(rest));
            var all = new IReadOnlyList<T>[2 + rest.Length];
            all[0] = first;
            all[1] = second;
            Array.Copy(rest, 0, all, 2, rest.Length);
            return Of((IReadOnlyList<IReadOnlyList<T>>)all);
        }

        private static IEnumerable<T[]> Iterator<T>(IReadOnlyList<IReadOnlyList<T>> sequences)
        {
            int n = sequences.Count;
            if (n == 0)
            {
                yield return Array.Empty<T>();
                yield break;
            }
            for (int i = 0; i < n; i++)
                if (sequences[i].Count == 0)
                    yield break; // an empty factor makes the whole product empty

            var indices = new int[n];
            while (true)
            {
                var tuple = new T[n];
                for (int i = 0; i < n; i++)
                    tuple[i] = sequences[i][indices[i]];
                yield return tuple;

                // Odometer increment from the last position.
                int p = n - 1;
                while (p >= 0)
                {
                    if (++indices[p] < sequences[p].Count)
                        break;
                    indices[p] = 0;
                    p--;
                }
                if (p < 0)
                    yield break;
            }
        }
    }
}
