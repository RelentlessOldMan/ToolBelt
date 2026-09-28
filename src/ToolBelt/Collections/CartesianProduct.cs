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
        public static IEnumerable<T[]> Of<T>(IReadOnlyList<IReadOnlyList<T>> sequences)
        {
            if (sequences is null) throw new ArgumentNullException(nameof(sequences));
            for (int i = 0; i < sequences.Count; i++)
                if (sequences[i] is null)
                    throw new ArgumentException($"Sequence at index {i} is null.", nameof(sequences));
            return Iterator(sequences);
        }

        public static IEnumerable<T[]> Of<T>(params IReadOnlyList<T>[] sequences)
            => Of((IReadOnlyList<IReadOnlyList<T>>)sequences);

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
