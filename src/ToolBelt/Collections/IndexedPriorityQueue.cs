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
        // .NET Framework hashes a double or float NaN by its bits and a decimal by its scale, so equal keys (NaNs with
        // other payloads, 1.0m and 1.00m) fell into separate buckets: one key counted twice. Keys of those types hash
        // by value; everything else uses the default comparer.
        private static class ValueKeys
        {
            public static IEqualityComparer<TK> For<TK>()
            {
#if NETSTANDARD2_0
                object? c = typeof(TK) == typeof(double) ? DoubleKeys.Instance
                    : typeof(TK) == typeof(float) ? SingleKeys.Instance
                    : typeof(TK) == typeof(decimal) ? DecimalKeys.Instance
                    : typeof(TK) == typeof(double?) ? new NullableKeys<double>(DoubleKeys.Instance)
                    : typeof(TK) == typeof(float?) ? new NullableKeys<float>(SingleKeys.Instance)
                    : typeof(TK) == typeof(decimal?) ? new NullableKeys<decimal>(DecimalKeys.Instance)
                    : (object?)null;
                if (c is not null) return (IEqualityComparer<TK>)c;
#endif
                return EqualityComparer<TK>.Default;
            }
#if NETSTANDARD2_0

            private sealed class DoubleKeys : IEqualityComparer<double>
            {
                public static readonly DoubleKeys Instance = new DoubleKeys();
                public bool Equals(double x, double y) => x.Equals(y);
                public int GetHashCode(double v) => double.IsNaN(v) ? -1 : v.GetHashCode();
            }

            private sealed class SingleKeys : IEqualityComparer<float>
            {
                public static readonly SingleKeys Instance = new SingleKeys();
                public bool Equals(float x, float y) => x.Equals(y);
                public int GetHashCode(float v) => float.IsNaN(v) ? -1 : v.GetHashCode();
            }

            private sealed class DecimalKeys : IEqualityComparer<decimal>
            {
                public static readonly DecimalKeys Instance = new DecimalKeys();
                public bool Equals(decimal x, decimal y) => x == y;
                // Not through double: the conversion can round 1.0m and 1.00m-style pairs apart (the Framework's own bug).
                // Dividing by one drops trailing zeros, so equal nonzero decimals have one representation.
                public int GetHashCode(decimal v)
                {
                    if (v == 0) return 0;   // 0m and -0m
                    int[] b = decimal.GetBits(v / 1.000000000000000000000000000000000m);
                    return unchecked(b[0] ^ (b[1] * 31) ^ (b[2] * 961) ^ b[3]);
                }
            }

            private sealed class NullableKeys<TV> : IEqualityComparer<TV?> where TV : struct
            {
                private readonly IEqualityComparer<TV> _inner;
                public NullableKeys(IEqualityComparer<TV> inner) => _inner = inner;
                public bool Equals(TV? x, TV? y) => x.HasValue ? y.HasValue && _inner.Equals(x.Value, y.Value) : !y.HasValue;
                public int GetHashCode(TV? v) => v.HasValue ? _inner.GetHashCode(v.Value) : 0;
            }
#endif
        }

        // Each entry is a node that knows its own heap slot, so moving it (every sift) touches no hash: a key whose hash
        // threw on a later call, written into the index mid-sift, lost one key and duplicated another. Only Enqueue,
        // dequeue and lookups hash a key, each before anything changes.
        private sealed class Node
        {
            public Node(TKey key, TPriority priority) { Key = key; Priority = priority; }
            public readonly TKey Key;
            public TPriority Priority;
            public int Index;
        }

        private readonly List<Node> _heap = new List<Node>();
        private readonly Dictionary<TKey, Node> _nodes = new Dictionary<TKey, Node>(ValueKeys.For<TKey>());
        private readonly IComparer<TPriority> _comparer;

        public IndexedPriorityQueue(IComparer<TPriority>? comparer = null)
            => _comparer = comparer ?? Comparer<TPriority>.Default;

        public int Count => _heap.Count;

        public bool Contains(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _nodes.ContainsKey(key);
        }

        public bool TryGetPriority(TKey key, out TPriority priority)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (_nodes.TryGetValue(key, out Node? node))
            {
                priority = node.Priority;
                return true;
            }
            priority = default!;
            return false;
        }

        /// <summary>Adds a new key with a priority. Throws if the key is already present.</summary>
        public void Enqueue(TKey key, TPriority priority)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (_nodes.ContainsKey(key))
                throw new ArgumentException($"Key '{key}' is already in the queue.", nameof(key));

            // Comparisons first, then the key's hash: a comparer or hash that throws leaves the queue as it was (a throw
            // mid-sift left the order broken).
            int at = FindUp(priority, _heap.Count);
            var node = new Node(key, priority);
            _nodes.Add(key, node);
            _heap.Add(node);
            Settle(_heap.Count - 1, at, node);
        }

        /// <summary>
        /// Changes the priority of an existing key, re-heapifying. Throws if the key is absent. The key first enqueued is
        /// kept (as a dictionary keeps the first of two equal keys); the one passed here only finds it.
        /// </summary>
        public void UpdatePriority(TKey key, TPriority priority)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (!_nodes.TryGetValue(key, out Node? node))
                throw new KeyNotFoundException($"Key '{key}' is not in the queue.");

            // Comparisons first: a throw mid-sift (after the priority was stored) left the order broken.
            int i = node.Index;
            int cmp = _comparer.Compare(priority, node.Priority);
            int at = cmp < 0 ? FindUp(priority, i) : cmp > 0 ? FindDown(priority, i, _heap.Count) : i;
            node.Priority = priority;
            Settle(i, at, node);
        }

        public bool TryPeekMin(out TKey key, out TPriority priority)
        {
            if (_heap.Count == 0)
            {
                key = default!;
                priority = default!;
                return false;
            }
            key = _heap[0].Key;
            priority = _heap[0].Priority;
            return true;
        }

        /// <summary>Removes and returns the minimum-priority entry.</summary>
        public bool TryDequeueMin(out TKey key, out TPriority priority)
        {
            if (_heap.Count == 0)
            {
                key = default!;
                priority = default!;
                return false;
            }

            // Move the last element to the root and sink it. Comparisons first, then the hash: a throw mid-sift lost the
            // dequeued key (already out of the index) and left the order broken.
            int last = _heap.Count - 1;
            Node moved = _heap[last];
            int at = last > 0 ? FindDown(moved.Priority, 0, last) : 0;
            Node min = _heap[0];
            _nodes.Remove(min.Key);
            _heap.RemoveAt(last);
            if (last > 0) Settle(0, at, moved);
            key = min.Key;
            priority = min.Priority;
            return true;
        }

        // Where a priority placed in the empty slot i would rise to. Compares only.
        private int FindUp(TPriority priority, int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_comparer.Compare(priority, _heap[parent].Priority) >= 0)
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
                if (child + 1 < n && _comparer.Compare(_heap[child + 1].Priority, _heap[child].Priority) < 0)
                    child++;
                if (_comparer.Compare(_heap[child].Priority, priority) >= 0)
                    break;
                i = child;
            }
            return i;
        }

        // Moves each entry on the path between slots from and at (at is an ancestor or a descendant of from) one level
        // toward from and puts node at slot at. No comparisons and no hashing, so it cannot fail part way.
        private void Settle(int from, int at, Node node)
        {
            if (at < from)
            {
                // Rising: at is an ancestor; shift the ancestors on the way down by one.
                int i = from;
                while (i > at)
                {
                    int parent = (i - 1) / 2;
                    Place(i, _heap[parent]);
                    i = parent;
                }
            }
            else if (at > from)
            {
                // Sinking: from is an ancestor of at; walk up from at carrying each displaced entry up a level.
                Node carry = node;
                int i = at;
                while (i > from)
                {
                    Node displaced = _heap[i];
                    Place(i, carry);
                    carry = displaced;
                    i = (i - 1) / 2;
                }
                Place(from, carry);
                return;
            }
            Place(at, node);
        }

        private void Place(int i, Node node)
        {
            _heap[i] = node;
            node.Index = i;
        }
    }
}
