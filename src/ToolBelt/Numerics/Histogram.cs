// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// A fixed-bin histogram over a half-open range <c>[min, max)</c> divided into equal-width bins.
    /// Values below <c>min</c> increment <see cref="Underflow"/>; values at or above <c>max</c> increment
    /// <see cref="Overflow"/>; NaN increments <see cref="NaNCount"/>; everything else falls into exactly one bin,
    /// the one whose <see cref="BinLowerBound"/> is the largest not above it. Not thread-safe.
    /// </summary>
    public sealed class Histogram
    {
        private readonly long[] _bins;
        private readonly double _min;
        private readonly double _max;
        private readonly double _binWidth;

        public Histogram(double min, double max, int binCount)
        {
            if (double.IsNaN(min) || double.IsInfinity(min)) throw new ArgumentException("min must be finite.", nameof(min));
            if (double.IsNaN(max) || double.IsInfinity(max)) throw new ArgumentException("max must be finite.", nameof(max));
            if (min >= max) throw new ArgumentException("Require min < max.", nameof(min));
            if (binCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(binCount), binCount, "Bin count must be positive.");

            _min = min;
            _max = max;
            _bins = new long[binCount];
            Bins = Array.AsReadOnly(_bins); // the bare array could be cast back and written
            _halfSpan = 0.5 * max - 0.5 * min; // halves: max − min overflows for a range like ±1e308
            _binWidth = 2 * (_halfSpan / binCount);
            // A range of a few subnormals has a half-span of 0, so no value could be placed in a bin.
            if (!(_halfSpan > 0)) throw new ArgumentException($"The range [{min:R}, {max:R}) is too narrow to bin.", nameof(max));
        }

        private readonly double _halfSpan;

        public int BinCount => _bins.Length;

        public double BinWidth => _binWidth;

        public long Underflow { get; private set; }

        public long Overflow { get; private set; }

        /// <summary>NaN values added (they belong to no bin and to neither tail).</summary>
        public long NaNCount { get; private set; }

        /// <summary>Per-bin counts (index 0 is the bin starting at <c>min</c>).</summary>
        public IReadOnlyList<long> Bins { get; }

        /// <summary>Total values added, including under/overflow and NaN.</summary>
        public long Total { get; private set; }

        /// <summary>The inclusive lower bound of bin <paramref name="index"/>.</summary>
        public double BinLowerBound(int index)
        {
            if ((uint)index >= (uint)_bins.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {_bins.Length}).");
            return LowerBound(index);
        }

        // min + (index/n)·(max − min) in two half steps; every partial sum stays within [min, max].
        private double LowerBound(int index)
        {
            if (index == 0) return _min;
            double step = (double)index / _bins.Length * _halfSpan;
            return _min + step + step;
        }

        /// <summary>Adds a value to the appropriate bin (or under/overflow).</summary>
        public void Add(double value)
        {
            Total++;
            if (double.IsNaN(value))
            {
                NaNCount++;
                return;
            }
            if (value < _min)
            {
                Underflow++;
                return;
            }
            if (value >= _max)
            {
                Overflow++;
                return;
            }

            double estimate = (0.5 * value - 0.5 * _min) / _halfSpan * _bins.Length;
            int bin = estimate >= _bins.Length ? _bins.Length - 1 : (estimate > 0 ? (int)estimate : 0);
            // Agree exactly with BinLowerBound: the estimate above can be one bin off at an edge.
            while (bin > 0 && value < LowerBound(bin)) bin--;
            while (bin < _bins.Length - 1 && value >= LowerBound(bin + 1)) bin++;
            _bins[bin]++;
        }

        public void AddRange(IEnumerable<double> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            foreach (double v in values)
                Add(v);
        }
    }
}
