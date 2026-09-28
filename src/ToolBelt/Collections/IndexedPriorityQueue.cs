// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A min-priority queue keyed by a unique <typeparamref name="TKey"/> that also supports changing an
    /// existing key's priority (decrease-key / increase-key) in O(log n) — the operation Dijkstra/Prim
    /// need but the BCL <c>PriorityQueue</c> lacks. Backed by a binary heap plus a key→heap-index map, so
    /// Enqueue, DequeueMin, and UpdatePriority are all O(log n) and Contains/priority lookups are O(1).
    /// Lower priority (per the comparer, default <see cref="Comparer{T}.Default"/>) comes out first. Not
    /// thread-safe.
    /// </summary>
    public sealed class IndexedPriorityQueue<TKey, TPriority>
        where TKey : notnull
    {
        private readonly List<TKey> _keys = new List<TKey>();
        private readonly List<TPriority> _priorities = new List<TPriority>();
        private readonly Dictionary<TKey, int> _indexOf = new Dictionary<TKey, int>();
        private readonly IComparer<TPriority> _comparer;

        public IndexedPriorityQueue(IComparer<TPriority>? comparer = null)
            => _comparer = comparer ?? Comparer<TPriority>.Default;

        public int Count => _keys.Count;

        public bool Contains(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _indexOf.ContainsKey(key);
        }

        public bool TryGetPriority(TKey key, out TPriority priority)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (_indexOf.TryGetValue(key, out int i))
            {
                priority = _priorities[i];
                return true;
            }
            priority = default!;
            return false;
        }

        /// <summary>Adds a new key with a priority. Throws if the key is already present.</summary>
        public void Enqueue(TKey key, TPriority priority)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (_indexOf.ContainsKey(key))
                throw new ArgumentException($"Key '{key}' is already in the queue.", nameof(key));

            _keys.Add(key);
            _priorities.Add(priority);
            int i = _keys.Count - 1;
            _indexOf[key] = i;
            SiftUp(i);
        }

        /// <summary>Changes the priority of an existing key, re-heapifying. Throws if the key is absent.</summary>
        public void UpdatePriority(TKey key, TPriority priority)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (!_indexOf.TryGetValue(key, out int i))
                throw new KeyNotFoundException($"Key '{key}' is not in the queue.");

            int cmp = _comparer.Compare(priority, _priorities[i]);
            _priorities[i] = priority;
            if (cmp < 0) SiftUp(i);
            else if (cmp > 0) SiftDown(i);
        }

        public bool TryPeekMin(out TKey key, out TPriority priority)
        {
            if (_keys.Count == 0)
            {
                key = default!;
                priority = default!;
                return false;
            }
            key = _keys[0];
            priority = _priorities[0];
            return true;
        }

        /// <summary>Removes and returns the minimum-priority entry.</summary>
        public bool TryDequeueMin(out TKey key, out TPriority priority)
        {
            if (_keys.Count == 0)
            {
                key = default!;
                priority = default!;
                return false;
            }

            key = _keys[0];
            priority = _priorities[0];
            _indexOf.Remove(key);

            int last = _keys.Count - 1;
            if (last == 0)
            {
                _keys.RemoveAt(0);
                _priorities.RemoveAt(0);
                return true;
            }

            // Move the last element to the root and sink it.
            _keys[0] = _keys[last];
            _priorities[0] = _priorities[last];
            _indexOf[_keys[0]] = 0;
            _keys.RemoveAt(last);
            _priorities.RemoveAt(last);
            SiftDown(0);
            return true;
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_comparer.Compare(_priorities[i], _priorities[parent]) >= 0)
                    break;
                Swap(i, parent);
                i = parent;
            }
        }

        private void SiftDown(int i)
        {
            int n = _keys.Count;
            while (true)
            {
                int left = 2 * i + 1, right = 2 * i + 2, smallest = i;
                if (left < n && _comparer.Compare(_priorities[left], _priorities[smallest]) < 0) smallest = left;
                if (right < n && _comparer.Compare(_priorities[right], _priorities[smallest]) < 0) smallest = right;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
            (_priorities[a], _priorities[b]) = (_priorities[b], _priorities[a]);
            _indexOf[_keys[a]] = a;
            _indexOf[_keys[b]] = b;
        }
    }
}
