// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A binary-heap priority queue. By default it is a min-heap ordered by <see cref="Comparer{T}.Default"/>
    /// (smallest element pops first); pass a custom comparer to change the ordering. Push and pop are
    /// O(log n); peek is O(1). Not thread-safe.
    /// </summary>
    public sealed class BinaryHeap<T>
    {
        private readonly List<T> _items;
        private readonly IComparer<T> _comparer;

        public BinaryHeap(IComparer<T>? comparer = null)
        {
            _items = new List<T>();
            _comparer = comparer ?? Comparer<T>.Default;
        }

        /// <summary>Builds a heap from existing items in O(n) (Floyd's heapify).</summary>
        public BinaryHeap(IEnumerable<T> items, IComparer<T>? comparer = null)
        {
            if (items is null)
                throw new ArgumentNullException(nameof(items));
            _comparer = comparer ?? Comparer<T>.Default;
            _items = new List<T>(items);
            for (int i = _items.Count / 2 - 1; i >= 0; i--)
            {
                T x = _items[i];
                Settle(i, FindDown(x, i, _items.Count), x);
            }
        }

        public int Count => _items.Count;

        /// <remarks>Every comparison happens before the heap changes, so a comparer that throws leaves it as it was (here
        /// and in <see cref="TryPop"/>); a throw mid-sift left the item out of order, or lost the popped one.</remarks>
        public void Push(T item)
        {
            int at = FindUp(item, _items.Count);
            _items.Add(item);
            int i = _items.Count - 1;
            while (i > at)
            {
                int parent = (i - 1) / 2;
                _items[i] = _items[parent];
                i = parent;
            }
            _items[at] = item;
        }

        /// <summary>Returns the highest-priority element without removing it. Throws if empty.</summary>
        public T Peek()
            => _items.Count > 0 ? _items[0] : throw new InvalidOperationException("The heap is empty.");

        public bool TryPeek(out T item)
        {
            if (_items.Count == 0)
            {
                item = default!;
                return false;
            }
            item = _items[0];
            return true;
        }

        /// <summary>Removes and returns the highest-priority element. Throws if empty.</summary>
        public T Pop()
        {
            if (!TryPop(out var item))
                throw new InvalidOperationException("The heap is empty.");
            return item;
        }

        public bool TryPop(out T item)
        {
            if (_items.Count == 0)
            {
                item = default!;
                return false;
            }

            int last = _items.Count - 1;
            T moved = _items[last];
            int at = last > 0 ? FindDown(moved, 0, last) : 0;
            item = _items[0];
            _items.RemoveAt(last);
            if (last > 0) Settle(0, at, moved);
            return true;
        }

        public void Clear() => _items.Clear();

        // Where an item placed in the empty slot i would rise to. Compares only.
        private int FindUp(T item, int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_comparer.Compare(item, _items[parent]) >= 0)
                    break;
                i = parent;
            }
            return i;
        }

        // Where an item placed in the empty slot i would sink to among the first n slots. Compares only.
        private int FindDown(T item, int i, int n)
        {
            while (i < n / 2) // past n / 2 is a leaf; 2 * i + 1 overflowed int past 2^30 elements
            {
                int child = 2 * i + 1;
                if (child + 1 < n && _comparer.Compare(_items[child + 1], _items[child]) < 0)
                    child++;
                if (_comparer.Compare(_items[child], item) >= 0)
                    break;
                i = child;
            }
            return i;
        }

        // Moves each item on the path from slot top down to slot at up one level and puts item at the bottom. The path
        // is at's ancestors, so it is walked from the bottom, carrying each displaced item up. No comparisons.
        private void Settle(int top, int at, T item)
        {
            T carry = item;
            int i = at;
            while (i > top)
            {
                (_items[i], carry) = (carry, _items[i]);
                i = (i - 1) / 2;
            }
            _items[top] = carry;
        }
    }
}
