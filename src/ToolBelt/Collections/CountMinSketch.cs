// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A Count-Min Sketch: a compact, fixed-memory structure for estimating item frequencies in a stream.
    /// Estimates never under-count the true frequency; they may over-count, with error bounded by the
    /// configured width and depth. Complements a Bloom filter (membership) with approximate counts.
    /// Items are told apart by their hash code: two items the comparer hashes alike share a counter in every row, so
    /// the bound holds for items with distinct hash codes. With the default comparer, 64-bit keys (<c>long</c>,
    /// <c>ulong</c>, <c>double</c>, <c>DateTime</c>, <c>DateTimeOffset</c>, <c>TimeSpan</c>) are hashed from all 64 bits.
    /// Not thread-safe.
    /// </summary>
    public sealed class CountMinSketch<T>
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

        private readonly long[][] _counts; // [depth][width]
        private readonly int _width;
        private readonly int _depth;
        private readonly ulong[] _seeds;
        private readonly IEqualityComparer<T> _comparer;
        private readonly bool _wide;   // hash 64-bit keys by their whole value, not their 32-bit hash code

#if NETSTANDARD2_0
        // .NET Framework caps an array of elements wider than a byte at 0x7FEFFFFF; Array.MaxLength (0x7FFFFFC7) is .NET's.
        // A length between passed the check there and threw OutOfMemoryException.
        private static readonly int ArrayMaxLength = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            .StartsWith(".NET Framework", StringComparison.Ordinal) ? 0x7FEFFFFF : 0x7FFFFFC7;
#else
        private const int ArrayMaxLength = 0x7FFFFFC7;   // Array.MaxLength
#endif

        /// <summary>Total number of increments recorded (sum of all added counts).</summary>
        public long TotalCount { get; private set; }

        /// <param name="width">Counters per row; larger reduces collision over-count. Must be positive.</param>
        /// <param name="depth">Number of hash rows; larger reduces the chance of a bad estimate. Must be positive, and width × depth
        /// at most Array.MaxLength.</param>
        /// <param name="comparer">Equality/hash comparer for items; defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
        public CountMinSketch(int width = 2048, int depth = 5, IEqualityComparer<T>? comparer = null)
        {
            // Past Array.MaxLength the row allocation threw OutOfMemoryException.
            if (width < 1 || width > ArrayMaxLength) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be in [1, Array.MaxLength] (0x7FFFFFC7; 0x7FEFFFFF on .NET Framework).");
            if (depth < 1 || depth > ArrayMaxLength) throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth must be in [1, Array.MaxLength] (0x7FFFFFC7; 0x7FEFFFFF on .NET Framework).");
            // Each checked alone, 0x7FFFFFC7 by 0x7FFFFFC7 asked for 2^62 counters and threw OutOfMemoryException.
            if ((long)width * depth > ArrayMaxLength) throw new ArgumentOutOfRangeException(nameof(depth), depth, "Width × depth must be at most Array.MaxLength (0x7FFFFFC7; 0x7FEFFFFF on .NET Framework).");

            _width = width;
            _depth = depth;
            _comparer = comparer ?? ValueKeys.For<T>();
            _wide = comparer is null && Wide64(typeof(T));
            _counts = new long[depth][];
            _seeds = new ulong[depth];
            for (int i = 0; i < depth; i++)
            {
                _counts[i] = new long[width];
                _seeds[i] = unchecked(0x9E3779B97F4A7C15ul * (ulong)(i + 1)); // distinct per-row mixing constant
            }
        }

        /// <summary>Adds <paramref name="amount"/> (default 1) to the count for <paramref name="item"/>.</summary>
        public void Add(T item, long amount = 1)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
            ulong baseHash = Hash(item);
            for (int row = 0; row < _depth; row++)
            {
                int col = Column(baseHash, row);
                _counts[row][col] = Saturating(_counts[row][col], amount);
            }
            TotalCount = Saturating(TotalCount, amount);
        }

        // A null item is counted under hash 0, as the default comparer hashes it; a caller's comparer (StringComparer.Ordinal)
        // threw ArgumentNullException for a parameter "obj" the caller never passed.
        // A long hashes as its two halves XORed, so ordinary keys (i * (2^32 + 1), packed (x << 32) | y grids) shared a
        // hash code, and so a counter in every row: thousands of keys over-counted by each other's totals.
        private ulong Hash(T item)
        {
            if (item is null) return 0;
            if (_wide)
            {
                switch (item)
                {
                    case long l: return unchecked((ulong)l);
                    case ulong u: return u;
                    // Equal doubles hash alike: 0.0 and -0.0, and every NaN.
                    case double d: return d == 0 ? 0 : double.IsNaN(d) ? 0x7FF8000000000000ul : unchecked((ulong)BitConverter.DoubleToInt64Bits(d));
                    case DateTime t: return unchecked((ulong)t.Ticks);                // equality ignores the kind
                    case DateTimeOffset o: return unchecked((ulong)o.UtcTicks);       // equality is the instant
                    case TimeSpan s: return unchecked((ulong)s.Ticks);
                }
            }
            return unchecked((uint)_comparer.GetHashCode(item));
        }

        private static bool Wide64(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            return t == typeof(long) || t == typeof(ulong) || t == typeof(double) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan);
        }

        // Wrapping made a count negative, under-counting; stopping at long.MaxValue keeps estimates upper bounds.
        private static long Saturating(long count, long amount) => count > long.MaxValue - amount ? long.MaxValue : count + amount;

        /// <summary>Returns the estimated frequency of <paramref name="item"/> (never below the true count).</summary>
        public long Estimate(T item)
        {
            ulong baseHash = Hash(item);
            long min = long.MaxValue;
            for (int row = 0; row < _depth; row++)
            {
                int col = Column(baseHash, row);
                long v = _counts[row][col];
                if (v < min) min = v;
            }
            return min;
        }

        private int Column(ulong baseHash, int row)
        {
            // Mix the item's hash with the row seed through the full 64-bit MurmurHash3 finalizer, so every input
            // bit reaches the low bits that survive the reduction to [0, width).
            unchecked
            {
                ulong h = baseHash ^ _seeds[row];
                h ^= h >> 33;
                h *= 0xFF51AFD7ED558CCDul;
                h ^= h >> 33;
                h *= 0xC4CEB9FE1A85EC53ul;
                h ^= h >> 33;
                return (int)(h % (ulong)_width);
            }
        }
    }
}
