// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A Count-Min Sketch: a compact, fixed-memory structure for estimating item frequencies in a stream.
    /// Estimates never under-count the true frequency; they may over-count, with error bounded by the
    /// configured width and depth. Complements <see cref="BloomFilter"/> (membership) with approximate
    /// counts. Not thread-safe.
    /// </summary>
    public sealed class CountMinSketch<T>
    {
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
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (depth < 1) throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth must be positive.");

            _width = width;
            _depth = depth;
            _comparer = comparer ?? EqualityComparer<T>.Default;
            _counts = new long[depth][];
            _seeds = new uint[depth];
            for (int i = 0; i < depth; i++)
            {
                _counts[i] = new long[width];
                _seeds[i] = 0x9E3779B1u * (uint)(i + 1); // distinct per-row mixing constant
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
                _counts[row][col] += amount;
            }
            TotalCount += amount;
        }

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
            // Mix the item's hash with the row seed, then reduce to [0, width).
            uint h = (uint)baseHash ^ _seeds[row];
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            return (int)(h % (uint)_width);
        }
    }
}
