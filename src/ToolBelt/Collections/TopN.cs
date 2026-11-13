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
        private T[] _heap; // min-heap: _heap[0] is the smallest retained item; grows up to _capacity
        private readonly int _capacity;
        private readonly IComparer<T> _comparer;
        private int _count;

        public TopN(int capacity, IComparer<T>? comparer = null)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            _capacity = capacity;
            _heap = new T[Math.Min(capacity, 16)]; // a huge capacity must not allocate up front
            _comparer = comparer ?? Comparer<T>.Default;
        }

        public int Capacity => _capacity;

        public int Count => _count;

        /// <summary>Offers an item; it is kept only if it ranks among the largest <see cref="Capacity"/> seen.</summary>
        public void Add(T item)
        {
            if (_count < _capacity)
            {
                if (_count == _heap.Length)
                    Array.Resize(ref _heap, (int)Math.Min((long)_heap.Length * 2, _capacity));
                // Every comparison happens before the heap changes, so a comparer that throws leaves it as it was. A throw
                // mid-sift lost an item, or (at capacity) left a larger item at the root, so later Adds were measured
                // against the wrong threshold for good.
                int at = FindUp(_heap, item, _count, _comparer);
                for (int i = _count; i > at; i = (i - 1) / 2) _heap[i] = _heap[(i - 1) / 2];
                _heap[at] = item;
                _count++;
            }
            else if (_comparer.Compare(item, _heap[0]) > 0)
            {
                // Larger than the smallest retained item: replace the root and restore the heap.
                Settle(_heap, 0, FindDown(_heap, item, 0, _count, _comparer), item);
            }
        }

        /// <summary>The retained items as a fresh array, largest first.</summary>
        public T[] ToArray()
        {
            // A heapsort of a copy: popping the minimum to the end leaves the largest first. Array.Sort wrapped a comparer's
            // exception in an InvalidOperationException (or turned an IndexOutOfRangeException into an ArgumentException
            // without it); here it propagates unchanged.
            var items = new T[_count];
            Array.Copy(_heap, items, _count);
            for (int n = _count - 1; n > 0; n--)
            {
                T moved = items[n];
                int at = FindDown(items, moved, 0, n, _comparer);
                items[n] = items[0];
                Settle(items, 0, at, moved);
            }
            return items;
        }

        // Where an item placed in the empty slot i would rise to. Compares only.
        private static int FindUp(T[] heap, T item, int i, IComparer<T> comparer)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (comparer.Compare(item, heap[parent]) >= 0)
                    break;
                i = parent;
            }
            return i;
        }

        // Where an item placed in the empty slot i would sink to among the first n slots. Compares only.
        private static int FindDown(T[] heap, T item, int i, int n, IComparer<T> comparer)
        {
            while (i < n / 2) // past n / 2 is a leaf; 2 * i + 1 overflowed int past 2^30 elements
            {
                int child = 2 * i + 1;
                if (child + 1 < n && comparer.Compare(heap[child + 1], heap[child]) < 0)
                    child++;
                if (comparer.Compare(heap[child], item) >= 0)
                    break;
                i = child;
            }
            return i;
        }

        // Moves each item on the path from slot top down to slot at up one level and puts item at the bottom (walked from
        // the bottom, carrying each displaced item up). No comparisons.
        private static void Settle(T[] heap, int top, int at, T item)
        {
            T carry = item;
            int i = at;
            while (i > top)
            {
                (heap[i], carry) = (carry, heap[i]);
                i = (i - 1) / 2;
            }
            heap[top] = carry;
        }
    }
}
