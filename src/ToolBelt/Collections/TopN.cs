// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Streams over any number of items while retaining only the <c>capacity</c> largest seen so far,
    /// using O(capacity) memory. Backed by a bounded min-heap (kept inline so this file stands alone):
    /// the smallest retained item sits at the root and is evicted when a larger item arrives. "Largest"
    /// is defined by the supplied comparer (default <see cref="Comparer{T}.Default"/>). Not thread-safe.
    /// </summary>
    public sealed class TopN<T>
    {
        private readonly T[] _heap; // min-heap: _heap[0] is the smallest retained item
        private readonly IComparer<T> _comparer;
        private int _count;

        public TopN(int capacity, IComparer<T>? comparer = null)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            _heap = new T[capacity];
            _comparer = comparer ?? Comparer<T>.Default;
        }

        public int Capacity => _heap.Length;

        public int Count => _count;

        /// <summary>Offers an item; it is kept only if it ranks among the largest <see cref="Capacity"/> seen.</summary>
        public void Add(T item)
        {
            if (_count < _heap.Length)
            {
                _heap[_count] = item;
                SiftUp(_count);
                _count++;
            }
            else if (_comparer.Compare(item, _heap[0]) > 0)
            {
                // Larger than the smallest retained item: replace the root and restore the heap.
                _heap[0] = item;
                SiftDown(0);
            }
        }

        /// <summary>The retained items as a fresh array, largest first.</summary>
        public T[] ToArray()
        {
            var items = new T[_count];
            Array.Copy(_heap, items, _count);
            Array.Sort(items, (a, b) => _comparer.Compare(b, a)); // descending
            return items;
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_comparer.Compare(_heap[i], _heap[parent]) >= 0)
                    break;
                (_heap[i], _heap[parent]) = (_heap[parent], _heap[i]);
                i = parent;
            }
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                int left = 2 * i + 1, right = 2 * i + 2, smallest = i;
                if (left < _count && _comparer.Compare(_heap[left], _heap[smallest]) < 0) smallest = left;
                if (right < _count && _comparer.Compare(_heap[right], _heap[smallest]) < 0) smallest = right;
                if (smallest == i) break;
                (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
                i = smallest;
            }
        }
    }
}
