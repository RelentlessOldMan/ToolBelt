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

            // Comparisons first, then the key's hash: a comparer or hash that throws leaves the queue as it was (a throw
            // mid-sift left the order broken).
            int at = FindUp(priority, _keys.Count);
            _indexOf[key] = at;
            _keys.Add(key);
            _priorities.Add(priority);
            int i = _keys.Count - 1;
            while (i > at)
            {
                int parent = (i - 1) / 2;
                Place(i, _keys[parent], _priorities[parent]);
                i = parent;
            }
            _keys[at] = key;
            _priorities[at] = priority;
        }

        /// <summary>Changes the priority of an existing key, re-heapifying. Throws if the key is absent.</summary>
        public void UpdatePriority(TKey key, TPriority priority)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (!_indexOf.TryGetValue(key, out int i))
                throw new KeyNotFoundException($"Key '{key}' is not in the queue.");

            // Comparisons first: a throw mid-sift (after the priority was stored) left the order broken.
            int cmp = _comparer.Compare(priority, _priorities[i]);
            int at = cmp < 0 ? FindUp(priority, i) : cmp > 0 ? FindDown(priority, i, _keys.Count) : i;
            Settle(i, at, key, priority);
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

            // Move the last element to the root and sink it. Comparisons first, then the hash: a throw mid-sift lost the
            // dequeued key (already out of the index) and left the order broken.
            int last = _keys.Count - 1;
            TKey movedKey = _keys[last];
            TPriority moved = _priorities[last];
            int at = last > 0 ? FindDown(moved, 0, last) : 0;
            key = _keys[0];
            priority = _priorities[0];
            _indexOf.Remove(key);
            _keys.RemoveAt(last);
            _priorities.RemoveAt(last);
            if (last > 0) Settle(0, at, movedKey, moved);
            return true;
        }

        // Where a priority placed in the empty slot i would rise to. Compares only.
        private int FindUp(TPriority priority, int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_comparer.Compare(priority, _priorities[parent]) >= 0)
                    break;
                i = parent;
            }
            return i;
        }

        // Where a priority placed in the empty slot i would sink to among the first n slots. Compares only.
        private int FindDown(TPriority priority, int i, int n)
        {
            while (i < n / 2) // past n / 2 is a leaf; 2 * i + 1 overflowed int past 2^30 elements
            {
                int child = 2 * i + 1;
                if (child + 1 < n && _comparer.Compare(_priorities[child + 1], _priorities[child]) < 0)
                    child++;
                if (_comparer.Compare(_priorities[child], priority) >= 0)
                    break;
                i = child;
            }
            return i;
        }

        // Moves each entry on the path between slots from and at (at is an ancestor or a descendant of from) one level
        // toward from and puts the entry at slot at. No comparisons.
        private void Settle(int from, int at, TKey key, TPriority priority)
        {
            if (at < from)
            {
                // Rising: at is an ancestor; shift the ancestors on the way down by one.
                int i = from;
                while (i > at)
                {
                    int parent = (i - 1) / 2;
                    Place(i, _keys[parent], _priorities[parent]);
                    i = parent;
                }
            }
            else if (at > from)
            {
                // Sinking: from is an ancestor of at; walk up from at carrying each displaced entry up a level.
                TKey carryKey = key;
                TPriority carry = priority;
                int i = at;
                while (i > from)
                {
                    TKey k = _keys[i];
                    TPriority p = _priorities[i];
                    Place(i, carryKey, carry);
                    carryKey = k;
                    carry = p;
                    i = (i - 1) / 2;
                }
                Place(from, carryKey, carry);
                return;
            }
            Place(at, key, priority);
        }

        private void Place(int i, TKey key, TPriority priority)
        {
            _keys[i] = key;
            _priorities[i] = priority;
            _indexOf[key] = i;
        }
    }
}
