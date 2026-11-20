// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Linq;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A multiset / frequency counter: tracks how many times each item has been added. Missing items
    /// report a count of zero; a count that drops to zero removes the item. Not thread-safe.
    /// </summary>
    public sealed class Counter<T>
        where T : notnull
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

        // Each count with the order its item was first added, so MostCommon breaks ties by it (dictionary order put an item
        // added after a removal ahead of older ones: a, b, c, remove a, add d gave d, b, c).
        private readonly Dictionary<T, (int Count, long Order)> _counts = new Dictionary<T, (int Count, long Order)>(ValueKeys.For<T>());
        private long _added;

        /// <summary>Number of distinct items with a positive count.</summary>
        public int Count => _counts.Count;

        /// <summary>Sum of all counts across every item.</summary>
        public long Total { get; private set; }

        public IReadOnlyCollection<T> Keys => _counts.Keys;

        /// <summary>The count for an item (zero if absent).</summary>
        public int this[T item]
        {
            get
            {
                if (item is null) throw new ArgumentNullException(nameof(item));
                return _counts.TryGetValue(item, out var c) ? c.Count : 0;
            }
        }

        /// <summary>Increments an item's count by <paramref name="amount"/> (default 1).</summary>
        public void Add(T item, int amount = 1)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");

            long order = _counts.TryGetValue(item, out var entry) ? entry.Order : _added++;
            // checked so a per-item count exceeding int.MaxValue throws rather than wrapping negative
            // and silently desynchronizing from the long Total.
            _counts[item] = (checked(entry.Count + amount), order);
            Total += amount;
        }

        /// <summary>
        /// Decrements an item's count by <paramref name="amount"/>, clamping at zero and removing the item
        /// if it reaches zero. Returns how many were actually removed.
        /// </summary>
        public int Remove(T item, int amount = 1)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");

            if (!_counts.TryGetValue(item, out var entry))
                return 0;

            int removed = Math.Min(entry.Count, amount);
            int remaining = entry.Count - removed;
            if (remaining == 0)
                _counts.Remove(item);
            else
                _counts[item] = (remaining, entry.Order);
            Total -= removed;
            return removed;
        }

        public bool Contains(T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            return _counts.ContainsKey(item);
        }

        /// <summary>
        /// The <paramref name="k"/> most common items, highest count first; equal counts in the order their items were first
        /// added (an item removed to zero and added again counts from then).
        /// </summary>
        public IReadOnlyList<KeyValuePair<T, int>> MostCommon(int k)
        {
            if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), k, "Count must not be negative.");
            return _counts
                .OrderByDescending(pair => pair.Value.Count)
                .ThenBy(pair => pair.Value.Order)
                .Take(k)
                .Select(pair => new KeyValuePair<T, int>(pair.Key, pair.Value.Count))
                .ToList();
        }

        public void Clear()
        {
            _counts.Clear();
            Total = 0;
        }
    }
}
