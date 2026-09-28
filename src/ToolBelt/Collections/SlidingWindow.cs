// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Lazily enumerates fixed-size sliding windows over a list, advancing one element at a time
    /// (window i covers indices [i, i+size)). A source shorter than the window yields nothing. Each
    /// window is a fresh array the caller may keep.
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
            int last = source.Count - size;
            for (int start = 0; start <= last; start++)
            {
                var window = new T[size];
                for (int j = 0; j < size; j++)
                    window[j] = source[start + j];
                yield return window;
            }
        }
    }
}
