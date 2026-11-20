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
        // by value, and so do such values inside a tuple or an object key; everything else uses the default comparer.
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
                    : IsTuple(typeof(TK)) || (!typeof(TK).IsValueType && !typeof(TK).IsSealed) ? new DeepKeys<TK>()
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

            // Tuples (System.ValueTuple and System.Tuple, nested too) and keys of a type that can hold a boxed value
            // (object, an interface, an unsealed class) hashed a NaN inside them by its bits as well: (NaN, 1) and
            // (NaN with another payload, 1) are Equals but counted twice, and SequenceComparer gave them different hashes.
            // Equality stays the default; the hash walks tuples and hashes doubles, floats and decimals by value.
            private sealed class DeepKeys<TV> : IEqualityComparer<TV>
            {
                private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, object?>[]?> Members =
                    new System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, object?>[]?>();

                public bool Equals(TV x, TV y) => EqualityComparer<TV>.Default.Equals(x, y);
                public int GetHashCode(TV v) => Hash(v);

                private static int Hash(object? o)
                {
                    if (o is null) return 0;
                    if (o is double d) return DoubleKeys.Instance.GetHashCode(d);
                    if (o is float f) return SingleKeys.Instance.GetHashCode(f);
                    if (o is decimal m) return DecimalKeys.Instance.GetHashCode(m);
                    Func<object, object?>[]? items = Members.GetOrAdd(o.GetType(), TupleItems);
                    if (items is null) return o.GetHashCode();
                    int h = 17;
                    foreach (Func<object, object?> item in items)
                        h = unchecked(h * 31 + Hash(item(o)));
                    return h;
                }

                // The items of a tuple type, in a fixed order; null for any other type.
                private static Func<object, object?>[]? TupleItems(Type t)
                {
                    if (!IsTuple(t)) return null;
                    var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                    System.Reflection.MemberInfo[] members = t.IsValueType ? t.GetFields(flags) : (System.Reflection.MemberInfo[])t.GetProperties(flags);
                    Array.Sort(members, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                    var items = new Func<object, object?>[members.Length];
                    for (int i = 0; i < members.Length; i++)
                        items[i] = members[i] is System.Reflection.FieldInfo field
                            ? (Func<object, object?>)field.GetValue
                            : ((System.Reflection.PropertyInfo)members[i]).GetValue;
                    return items;
                }
            }

            private static bool IsTuple(Type t)
            {
                if (!t.IsGenericType) return false;
                string? name = t.GetGenericTypeDefinition().FullName;
                return name is not null && (name.StartsWith("System.ValueTuple`", StringComparison.Ordinal)
                    || name.StartsWith("System.Tuple`", StringComparison.Ordinal));
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
