// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A Count-Min Sketch: a compact, fixed-memory structure for estimating item frequencies in a stream.
    /// Estimates never under-count the true frequency; they may over-count, with error bounded by the
    /// configured width and depth. Complements a Bloom filter (membership) with approximate counts.
    /// Not thread-safe.
    /// </summary>
    public sealed class CountMinSketch<T>
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

        private readonly long[][] _counts; // [depth][width]
        private readonly int _width;
        private readonly int _depth;
        private readonly uint[] _seeds;
        private readonly IEqualityComparer<T> _comparer;

        /// <summary>Total number of increments recorded (sum of all added counts).</summary>
        public long TotalCount { get; private set; }

        /// <param name="width">Counters per row; larger reduces collision over-count. Must be positive.</param>
        /// <param name="depth">Number of hash rows; larger reduces the chance of a bad estimate. Must be positive.</param>
        /// <param name="comparer">Equality/hash comparer for items; defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
        public CountMinSketch(int width = 2048, int depth = 5, IEqualityComparer<T>? comparer = null)
        {
            // Past Array.MaxLength the row allocation threw OutOfMemoryException.
            if (width < 1 || width > 0x7FFFFFC7) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be in [1, Array.MaxLength] (0x7FFFFFC7).");
            if (depth < 1 || depth > 0x7FFFFFC7) throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth must be in [1, Array.MaxLength] (0x7FFFFFC7).");

            _width = width;
            _depth = depth;
            _comparer = comparer ?? ValueKeys.For<T>();
            _counts = new long[depth][];
            _seeds = new uint[depth];
            for (int i = 0; i < depth; i++)
            {
                _counts[i] = new long[width];
                _seeds[i] = unchecked(0x9E3779B1u * (uint)(i + 1)); // distinct per-row mixing constant
            }
        }

        /// <summary>Adds <paramref name="amount"/> (default 1) to the count for <paramref name="item"/>.</summary>
        public void Add(T item, long amount = 1)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
            int baseHash = _comparer.GetHashCode(item!);
            for (int row = 0; row < _depth; row++)
            {
                int col = Column(baseHash, row);
                _counts[row][col] = Saturating(_counts[row][col], amount);
            }
            TotalCount = Saturating(TotalCount, amount);
        }

        // Wrapping made a count negative, under-counting; stopping at long.MaxValue keeps estimates upper bounds.
        private static long Saturating(long count, long amount) => count > long.MaxValue - amount ? long.MaxValue : count + amount;

        /// <summary>Returns the estimated frequency of <paramref name="item"/> (never below the true count).</summary>
        public long Estimate(T item)
        {
            int baseHash = _comparer.GetHashCode(item!);
            long min = long.MaxValue;
            for (int row = 0; row < _depth; row++)
            {
                int col = Column(baseHash, row);
                long v = _counts[row][col];
                if (v < min) min = v;
            }
            return min;
        }

        private int Column(int baseHash, int row)
        {
            // Mix the item's hash with the row seed through the full MurmurHash3 finalizer, so every input
            // bit reaches the low bits that survive the reduction to [0, width).
            unchecked
            {
                uint h = (uint)baseHash ^ _seeds[row];
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return (int)(h % (uint)_width);
            }
        }
    }
}
