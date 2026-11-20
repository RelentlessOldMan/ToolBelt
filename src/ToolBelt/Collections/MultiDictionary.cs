// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A multimap: each key maps to an ordered list of values, and a key may hold duplicates. Adding
    /// appends; removing a value deletes the first matching occurrence and drops the key once its last
    /// value is gone. Value lookups return an independent snapshot, so the caller cannot mutate internal
    /// state. Not thread-safe.
    /// </summary>
    public sealed class MultiDictionary<TKey, TValue>
        where TKey : notnull
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

        private readonly Dictionary<TKey, List<TValue>> _map = new Dictionary<TKey, List<TValue>>(ValueKeys.For<TKey>());
        private long _count;

        /// <summary>Number of distinct keys.</summary>
        public int Count => _map.Count;

        /// <summary>Total number of values across all keys. A long: values under many keys can pass int.MaxValue, which
        /// wrapped negative (and, in a checked build, threw after the value was stored).</summary>
        public long Total => _count;

        public IReadOnlyCollection<TKey> Keys => _map.Keys;

        /// <summary>Appends <paramref name="value"/> under <paramref name="key"/>.</summary>
        public void Add(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            if (!_map.TryGetValue(key, out var list))
            {
                list = new List<TValue>();
                _map[key] = list;
            }
            list.Add(value);
            _count++;
        }

        /// <summary>Removes the first occurrence of <paramref name="value"/> under <paramref name="key"/>.</summary>
        public bool Remove(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var list))
            {
                int index = list.IndexOf(value);
                if (index >= 0)
                {
                    // The key first (its hash is the call that can throw, for a disposed object say): the count and the
                    // list were changed before it, leaving a key with no values or a total that disagreed with them.
                    if (list.Count == 1)
                        _map.Remove(key);
                    list.RemoveAt(index);
                    _count--;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Removes a key and all of its values. Returns whether the key was present.</summary>
        public bool RemoveAll(TKey key)
        {
            ThrowIfKeyNull(key);
            if (_map.TryGetValue(key, out var list))
            {
                _map.Remove(key);       // first, as Remove does
                _count -= list.Count;
                return true;
            }
            return false;
        }

        /// <summary>The values under a key as a snapshot, or an empty list if the key is absent.</summary>
        public IReadOnlyList<TValue> this[TKey key] => GetValues(key);

        /// <summary>The values under a key as an independent snapshot (empty if absent).</summary>
        public IReadOnlyList<TValue> GetValues(TKey key)
        {
            ThrowIfKeyNull(key);
            return _map.TryGetValue(key, out var list) ? list.ToArray() : Array.Empty<TValue>();
        }

        public bool ContainsKey(TKey key)
        {
            ThrowIfKeyNull(key);
            return _map.ContainsKey(key);
        }

        public bool Contains(TKey key, TValue value)
        {
            ThrowIfKeyNull(key);
            return _map.TryGetValue(key, out var list) && list.Contains(value);
        }

        public void Clear()
        {
            _map.Clear();
            _count = 0;
        }

        // Guard.NotNull carries a class constraint, so it cannot validate a possibly-value-type TKey.
        // This is the documented inline-null-throw fallback for generic keys.
        private static void ThrowIfKeyNull(TKey key)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
        }
    }
}
