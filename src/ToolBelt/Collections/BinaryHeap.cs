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
                SiftDown(i);
        }

        public int Count => _items.Count;

        public void Push(T item)
        {
            _items.Add(item);
            SiftUp(_items.Count - 1);
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

            item = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            if (_items.Count > 0)
                SiftDown(0);
            return true;
        }

        public void Clear() => _items.Clear();

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_comparer.Compare(_items[i], _items[parent]) >= 0)
                    break;
                Swap(i, parent);
                i = parent;
            }
        }

        private void SiftDown(int i)
        {
            int n = _items.Count;
            while (true)
            {
                int left = 2 * i + 1;
                int right = 2 * i + 2;
                int best = i;

                if (left < n && _comparer.Compare(_items[left], _items[best]) < 0)
                    best = left;
                if (right < n && _comparer.Compare(_items[right], _items[best]) < 0)
                    best = right;
                if (best == i)
                    break;

                Swap(i, best);
                i = best;
            }
        }

        private void Swap(int a, int b)
        {
            (_items[a], _items[b]) = (_items[b], _items[a]);
        }
    }
}
