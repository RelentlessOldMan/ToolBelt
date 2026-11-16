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

        private readonly Dictionary<T, int> _counts = new Dictionary<T, int>(ValueKeys.For<T>());

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
                return _counts.TryGetValue(item, out int c) ? c : 0;
            }
        }

        /// <summary>Increments an item's count by <paramref name="amount"/> (default 1).</summary>
        public void Add(T item, int amount = 1)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");

            _counts.TryGetValue(item, out int current);
            // checked so a per-item count exceeding int.MaxValue throws rather than wrapping negative
            // and silently desynchronizing from the long Total.
            _counts[item] = checked(current + amount);
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

            if (!_counts.TryGetValue(item, out int current))
                return 0;

            int removed = Math.Min(current, amount);
            int remaining = current - removed;
            if (remaining == 0)
                _counts.Remove(item);
            else
                _counts[item] = remaining;
            Total -= removed;
            return removed;
        }

        public bool Contains(T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            return _counts.ContainsKey(item);
        }

        /// <summary>The <paramref name="k"/> most common items, highest count first.</summary>
        public IReadOnlyList<KeyValuePair<T, int>> MostCommon(int k)
        {
            if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), k, "Count must not be negative.");
            return _counts
                .OrderByDescending(pair => pair.Value)
                .Take(k)
                .ToList();
        }

        public void Clear()
        {
            _counts.Clear();
            Total = 0;
        }
    }
}
