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
                // The map first: a key whose hash throws on a later call (a disposed object) was taken off the list and
                // left in the map, so the cache grew past its capacity and that key failed every call after. If it
                // throws, the new entry is undone and the cache is as it was.
                var lru = _order.Last!;
                try { _map.Remove(lru.Value.Key); }
                catch
                {
                    _order.Remove(fresh);
                    _map.Remove(key);
                    throw;
                }
                _order.RemoveLast();
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
                _map.Remove(key);       // first: if the hash throws, the entry is still on the list (see Set)
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
