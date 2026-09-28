// ToolBelt drop-in — self-contained except for Guards/Guard.cs (copy that file too).
using System.Collections.Generic;
using ToolBelt.Guards;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Splits a sequence into fixed-size contiguous batches. Lazy and single-pass: the source is
    /// enumerated exactly once and only as batches are pulled, so it composes with infinite/streaming
    /// sequences.
    /// </summary>
    public static class BatchExtensions
    {
        /// <summary>
        /// Groups <paramref name="source"/> into consecutive batches of at most <paramref name="size"/>
        /// elements. Every batch is exactly <paramref name="size"/> long except possibly the last, which
        /// holds the remainder. Argument validation is eager (thrown at call time, not on first
        /// enumeration).
        /// </summary>
        /// <param name="source">The sequence to batch. Enumerated once.</param>
        /// <param name="size">Maximum elements per batch. Must be positive.</param>
        /// <returns>A lazy sequence of independent batches (each is a fresh array you may keep).</returns>
        public static IEnumerable<IReadOnlyList<T>> Batch<T>(this IEnumerable<T> source, int size)
        {
            Guard.NotNull(source);
            Guard.Positive(size);
            return Iterator(source, size);
        }

        private static IEnumerable<IReadOnlyList<T>> Iterator<T>(IEnumerable<T> source, int size)
        {
            var bucket = new List<T>(size);
            foreach (var item in source)
            {
                bucket.Add(item);
                if (bucket.Count == size)
                {
                    yield return bucket.ToArray();
                    bucket.Clear();
                }
            }

            if (bucket.Count > 0)
                yield return bucket.ToArray();
        }
    }
}
