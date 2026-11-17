// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A bidirectional one-to-one map: every key maps to a unique value and vice versa, so you can look
    /// up in either direction in O(1). Adding a key or value that already exists throws, preserving the
    /// bijection. Not thread-safe.
    /// </summary>
    public sealed class BiMap<TKey, TValue>
        where TKey : notnull
        where TValue : notnull
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

        private readonly Dictionary<TKey, TValue> _forward = new Dictionary<TKey, TValue>(ValueKeys.For<TKey>());
        private readonly Dictionary<TValue, TKey> _backward = new Dictionary<TValue, TKey>(ValueKeys.For<TValue>());

        public int Count => _forward.Count;

        public IReadOnlyCollection<TKey> Keys => _forward.Keys;

        /// <summary>The values, in the same order as <see cref="Keys"/> (the i-th value is the i-th key's).</summary>
        public IReadOnlyCollection<TValue> Values => _forward.Values;   // the other map's keys drifted out of step after removals

        /// <summary>Adds a key/value pair. Throws if either side is already present.</summary>
        public void Add(TKey key, TValue value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (_forward.ContainsKey(key))
                throw new ArgumentException($"Key '{key}' is already present.", nameof(key));
            if (_backward.ContainsKey(value))
                throw new ArgumentException($"Value '{value}' is already present.", nameof(value));

            _forward[key] = value;
            try { _backward[value] = key; }
            catch { _forward.Remove(key); throw; } // a value whose hash throws must not leave a one-sided pair
        }

        public TValue GetByKey(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _forward[key];
        }

        public TKey GetByValue(TValue value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return _backward[value];
        }

        public bool TryGetByKey(TKey key, out TValue value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _forward.TryGetValue(key, out value!);
        }

        public bool TryGetByValue(TValue value, out TKey key)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return _backward.TryGetValue(value, out key!);
        }

        public bool ContainsKey(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return _forward.ContainsKey(key);
        }

        public bool ContainsValue(TValue value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return _backward.ContainsKey(value);
        }

        public bool RemoveByKey(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (_forward.TryGetValue(key, out var value))
            {
                // The other side first, undone if this side then throws: a value whose hash throws on a later call (a
                // disposed object) left a one-sided pair, and that value could never be added again. As Add does.
                _backward.Remove(value);
                try { _forward.Remove(key); }
                catch { _backward[value] = key; throw; }
                return true;
            }
            return false;
        }

        public bool RemoveByValue(TValue value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (_backward.TryGetValue(value, out var key))
            {
                // As RemoveByKey: the other side first, undone if this side then throws.
                _forward.Remove(key);
                try { _backward.Remove(value); }
                catch { _forward[key] = value; throw; }
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _forward.Clear();
            _backward.Clear();
        }
    }
}
