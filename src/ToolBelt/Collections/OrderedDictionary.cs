// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A generic dictionary that remembers insertion order: enumeration (and <see cref="Keys"/> /
    /// <see cref="Values"/>) yields entries in the order they were first added, while lookups, inserts,
    /// and removals stay O(1). Updating an existing key's value keeps its original position. Not
    /// thread-safe.
    /// </summary>
    public sealed class OrderedDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
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

        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map
            = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(ValueKeys.For<TKey>());
        private readonly LinkedList<KeyValuePair<TKey, TValue>> _order
            = new LinkedList<KeyValuePair<TKey, TValue>>();

        public int Count => _map.Count;

        /// <summary>Adds a new entry at the end. Throws if the key already exists.</summary>
        public void Add(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.ContainsKey(key))
                throw new ArgumentException($"An entry with key '{key}' already exists.", nameof(key));
            Append(key, value);
        }

        /// <summary>Gets or sets a value. The setter updates in place (keeping position) or appends a new entry.</summary>
        public TValue this[TKey key]
        {
            get
            {
                ThrowIfKeyNull(key);
                if (_map.TryGetValue(key, out var node))
                    return node.Value.Value;
                throw new KeyNotFoundException($"Key '{key}' was not found.");
            }
            set
            {
                ThrowIfKeyNull(key);
                if (_map.TryGetValue(key, out var node))
                    node.Value = new KeyValuePair<TKey, TValue>(node.Value.Key, value); // keep order position and, as Dictionary does, the first key
                else
                    Append(key, value);
            }
        }

        // The map first: a key whose hash throws must leave no orphan node in the order.
        private void Append(TKey key, TValue value)
        {
            var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
            _map[key] = node;
            _order.AddLast(node);
        }

        public bool TryGetValue(TKey key, out TValue value)
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

        public bool ContainsKey(TKey key)
        {
            ThrowIfKeyNull(key);
            return _map.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var node))
            {
                _map.Remove(key); // first: a hash that throws must leave the map and the order agreeing
                _order.Remove(node);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _map.Clear();
            _order.Clear();
        }

        /// <summary>Keys in insertion order, as an independent snapshot.</summary>
        public IReadOnlyList<TKey> Keys
        {
            get
            {
                var keys = new List<TKey>(_map.Count);
                foreach (var pair in _order)
                    keys.Add(pair.Key);
                return keys;
            }
        }

        /// <summary>Values in insertion order, as an independent snapshot.</summary>
        public IReadOnlyList<TValue> Values
        {
            get
            {
                var values = new List<TValue>(_map.Count);
                foreach (var pair in _order)
                    values.Add(pair.Value);
                return values;
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _order.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Guard.NotNull has a class constraint and cannot validate a possibly-value-type TKey.
        private static void ThrowIfKeyNull(TKey key)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
        }
    }
}
