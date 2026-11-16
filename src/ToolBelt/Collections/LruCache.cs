// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A fixed-capacity least-recently-used cache. Reads and writes are O(1). When a write would exceed
    /// the capacity, the least recently used entry is evicted. Both reads (<see cref="TryGet"/>) and
    /// writes (<see cref="Set"/>) count as "use" and refresh recency; <see cref="ContainsKey"/> and
    /// <see cref="Peek"/> do not. Not thread-safe.
    /// </summary>
    public sealed class LruCache<TKey, TValue>
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

        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
        // Most-recently-used at the head, least-recently-used at the tail.
        private readonly LinkedList<Entry> _order = new LinkedList<Entry>();

        public LruCache(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<Entry>>(Math.Min(capacity, 1024), ValueKeys.For<TKey>()); // grows on demand
        }

        public int Capacity => _capacity;

        public int Count => _map.Count;

        /// <summary>Keys ordered most-recently-used first. Enumerating does not affect recency.</summary>
        public IReadOnlyList<TKey> Keys
        {
            get
            {
                var keys = new List<TKey>(_map.Count);
                for (var node = _order.First; node is not null; node = node.Next)
                    keys.Add(node.Value.Key);
                return keys;
            }
        }

        /// <summary>Looks up a key, refreshing its recency on a hit.</summary>
        public bool TryGet(TKey key, out TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }

        /// <summary>Inserts or updates a key, marking it most-recently-used and evicting the LRU entry if over capacity.</summary>
        public void Set(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var node))
            {
                node.Value = new Entry(node.Value.Key, value);   // as Dictionary does, keep the first key
                _order.Remove(node);
                _order.AddFirst(node);
                return;
            }

            var fresh = new LinkedListNode<Entry>(new Entry(key, value));
            _map[key] = fresh; // first: a key whose hash throws must leave no orphan node to be evicted later
            _order.AddFirst(fresh);

            if (_map.Count > _capacity)
            {
                var lru = _order.Last!;
                _order.RemoveLast();
                _map.Remove(lru.Value.Key);
            }
        }

        /// <summary>Tests membership without affecting recency.</summary>
        public bool ContainsKey(TKey key)
        {
            ThrowIfKeyNull(key);
            return _map.ContainsKey(key);
        }

        /// <summary>Reads a value without affecting recency.</summary>
        public bool Peek(TKey key, out TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var node))
            {
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }

        /// <summary>Removes a key if present. Returns whether anything was removed.</summary>
        public bool Remove(TKey key)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _map.Remove(key);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _map.Clear();
            _order.Clear();
        }

        // Guard.NotNull carries a class constraint, so it cannot validate a possibly-value-type TKey.
        // This is the documented inline-null-throw fallback for generic keys.
        private static void ThrowIfKeyNull(TKey key)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
        }

        private readonly struct Entry
        {
            public Entry(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }

            public TKey Key { get; }
            public TValue Value { get; }
        }
    }
}
