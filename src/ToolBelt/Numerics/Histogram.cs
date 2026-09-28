// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// A fixed-bin histogram over a half-open range <c>[min, max)</c> divided into equal-width bins.
    /// Values below <c>min</c> increment <see cref="Underflow"/>; values at or above <c>max</c> increment
    /// <see cref="Overflow"/>; everything else falls into exactly one bin. Not thread-safe.
    /// </summary>
    public sealed class Histogram
    {
        private readonly long[] _bins;
        private readonly double _min;
        private readonly double _max;
        private readonly double _binWidth;

        public Histogram(double min, double max, int binCount)
        {
            if (double.IsNaN(min) || double.IsNaN(max) || min >= max)
                throw new ArgumentException("Require min < max and neither NaN.", nameof(min));
            if (binCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(binCount), binCount, "Bin count must be positive.");

            _min = min;
            _max = max;
            _bins = new long[binCount];
            _binWidth = (max - min) / binCount;
        }

        public int BinCount => _bins.Length;

        public double BinWidth => _binWidth;

        public long Underflow { get; private set; }

        public long Overflow { get; private set; }

        /// <summary>Per-bin counts (index 0 is the bin starting at <c>min</c>).</summary>
        public IReadOnlyList<long> Bins => _bins;

        /// <summary>Total values added, including under/overflow.</summary>
        public long Total { get; private set; }

        /// <summary>The inclusive lower bound of bin <paramref name="index"/>.</summary>
        public double BinLowerBound(int index)
        {
            if ((uint)index >= (uint)_bins.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {_bins.Length}).");
            return _min + index * _binWidth;
        }

        /// <summary>Adds a value to the appropriate bin (or under/overflow).</summary>
        public void Add(double value)
        {
            Total++;
            if (double.IsNaN(value) || value < _min)
            {
                Underflow++;
                return;
            }
            if (value >= _max)
            {
                Overflow++;
                return;
            }

            int bin = (int)((value - _min) / _binWidth);
            if (bin >= _bins.Length) // guard the top edge against floating-point drift
                bin = _bins.Length - 1;
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
