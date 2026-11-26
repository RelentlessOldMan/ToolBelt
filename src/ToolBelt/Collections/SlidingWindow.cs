// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Lazily enumerates fixed-size sliding windows over a list, advancing one element at a time
    /// (window i covers indices [i, i+size)). A source shorter than the window yields nothing. Each
    /// window is a fresh array the caller may keep. A list whose count changes during enumeration throws
    /// InvalidOperationException; a change that keeps the count (an item replaced) is not noticed.
    /// </summary>
    public static class SlidingWindow
    {
        public static IEnumerable<T[]> Over<T>(IReadOnlyList<T> source, int size)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size), size, "Window size must be positive.");
            return Iterator(source, size);
        }

        private static IEnumerable<T[]> Iterator<T>(IReadOnlyList<T> source, int size)
        {
            int count = source.Count;
            int last = count - size;
            for (int start = 0; start <= last; start++)
            {
                // As Combinatorics.Combinations: a list that changed is an InvalidOperationException, not a bad "index".
                if (source.Count != count) throw new InvalidOperationException("The list changed during enumeration.");
                var window = new T[size];
                for (int j = 0; j < size; j++)
                    window[j] = source[start + j];
                yield return window;
            }
            // After the last window too: a list that grew then ended the enumeration as if nothing had changed.
            if (source.Count != count) throw new InvalidOperationException("The list changed during enumeration.");
        }
    }
}
