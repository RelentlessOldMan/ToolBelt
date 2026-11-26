// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Lazily enumerates the Cartesian product of several sequences: every combination that picks one
    /// element from each input, in input order (the last sequence varies fastest, like an odometer). If
    /// any input is empty the product is empty; with no inputs the product is a single empty tuple. Each
    /// yielded tuple is a fresh array. An input whose count changes during enumeration throws
    /// InvalidOperationException; a change that keeps the count is not noticed.
    /// </summary>
    public static class CartesianProduct
    {
        /// <summary>The product of <paramref name="sequences"/>; use this overload for zero or one sequence.</summary>
        public static IEnumerable<T[]> Of<T>(IReadOnlyList<IReadOnlyList<T>> sequences)
        {
            if (sequences is null) throw new ArgumentNullException(nameof(sequences));
            // The list of sequences is copied as it is checked: the iterator read the caller's list again, so a slot set to
            // null after this call (which had passed the check) threw NullReferenceException mid-enumeration.
            var copy = new IReadOnlyList<T>[sequences.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = sequences[i] ?? throw new ArgumentException($"Sequence at index {i} is null.", nameof(sequences));
            return Iterator(copy);
        }

        /// <summary>The product of two or more sequences passed as separate arguments.</summary>
        /// <remarks>
        /// This takes at least two so that a single argument always means "the list of sequences": with a
        /// one-sequence params form, C# would bind a <c>List&lt;List&lt;int&gt;&gt;</c> to it as one sequence
        /// of lists (T = <c>List&lt;int&gt;</c>) rather than to the overload above.
        /// </remarks>
        public static IEnumerable<T[]> Of<T>(IReadOnlyList<T> first, IReadOnlyList<T> second, params IReadOnlyList<T>[] rest)
        {
            if (first is null) throw new ArgumentNullException(nameof(first));
            if (second is null) throw new ArgumentNullException(nameof(second));
            if (rest is null) throw new ArgumentNullException(nameof(rest));
            for (int i = 0; i < rest.Length; i++)
                if (rest[i] is null) throw new ArgumentException($"Sequence at index {i} of rest is null.", nameof(rest));
            var all = new IReadOnlyList<T>[2 + rest.Length];
            all[0] = first;
            all[1] = second;
            Array.Copy(rest, 0, all, 2, rest.Length);
            return Iterator(all);
        }

        private static IEnumerable<T[]> Iterator<T>(IReadOnlyList<T>[] sequences)
        {
            int n = sequences.Length;
            if (n == 0)
            {
                yield return Array.Empty<T>();
                yield break;
            }
            for (int i = 0; i < n; i++)
                if (sequences[i].Count == 0)
                    yield break; // an empty factor makes the whole product empty

            // As Combinatorics.Combinations: an input that changed is an InvalidOperationException. A shrunk one threw
            // ArgumentOutOfRangeException for an "index" the caller never passed, or ended the product early; a grown one
            // gave tuples of neither product.
            var counts = new int[n];
            for (int i = 0; i < n; i++) counts[i] = sequences[i].Count;
            var indices = new int[n];
            while (true)
            {
                for (int i = 0; i < n; i++)
                    if (sequences[i].Count != counts[i]) throw new InvalidOperationException("A sequence changed during enumeration.");
                var tuple = new T[n];
                for (int i = 0; i < n; i++)
                    tuple[i] = sequences[i][indices[i]];
                yield return tuple;

                // Odometer increment from the last position.
                int p = n - 1;
                while (p >= 0)
                {
                    if (++indices[p] < counts[p])
                        break;
                    indices[p] = 0;
                    p--;
                }
                if (p < 0)
                {
                    // After the last tuple too: an input that grew then ended the product as if unchanged.
                    for (int i = 0; i < n; i++)
                        if (sequences[i].Count != counts[i]) throw new InvalidOperationException("A sequence changed during enumeration.");
                    yield break;
                }
            }
        }
    }
}
