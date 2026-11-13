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
        private readonly int _binCount;   // set before _bins, for the range checks
        private readonly double _min;
        private readonly double _max;
        private readonly double _binWidth;

        public Histogram(double min, double max, int binCount)
        {
            if (double.IsNaN(min) || double.IsInfinity(min)) throw new ArgumentException("min must be finite.", nameof(min));
            if (double.IsNaN(max) || double.IsInfinity(max)) throw new ArgumentException("max must be finite.", nameof(max));
            if (min >= max) throw new ArgumentException("Require min < max.", nameof(min));
            if (binCount <= 0 || binCount > 0x7FFFFFC7)   // Array.MaxLength
                throw new ArgumentOutOfRangeException(nameof(binCount), binCount, "Bin count must be 1 to Array.MaxLength (0x7FFFFFC7).");

            _min = min;
            _max = max;
            // The full span when it is finite: the halves rounded in the subnormal range ([0, 4ε) in 4 bins had a width of 0
            // and bounds 0, 0, 2ε, 4ε). Halves only where max − min overflows, for a range like ±1e308.
            _binCount = binCount;
            _span = max - min;
            _halfSpan = 0.5 * max - 0.5 * min;
            _binWidth = double.IsInfinity(_span) ? 2 * (_halfSpan / binCount) : _span / binCount;
            if (!(_span > 0)) throw new ArgumentException($"The range [{min:R}, {max:R}) is too narrow to bin.", nameof(max));
            // Bins narrower than the spacing of doubles there would share a bound: checked near the subnormals and wherever
            // a bin is under 64 ulps of the range's ends (only 9 of [1e16, 1e16 + 16)'s 64 bounds were distinct).
            if (_binWidth < 1e-290 || _binWidth < Math.Max(Math.Abs(min), Math.Abs(max)) * 1.4210854715202004e-14)
                for (int i = 1; i <= binCount; i++)
                    if (!(LowerBound(i - 1) < (i == binCount ? max : LowerBound(i))))
                        throw new ArgumentException($"The range [{min:R}, {max:R}) is too narrow for {binCount} bins.", nameof(binCount));
            // Allocated after the range checks: a too-narrow range threw only after allocating the bins.
            _bins = new long[binCount];
            Bins = Array.AsReadOnly(_bins); // the bare array could be cast back and written
        }

        private readonly double _halfSpan, _span;

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
            if (index < 0 || index >= _bins.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {_bins.Length}).");
            return LowerBound(index);
        }

        // min + (index/n)·(max − min) in two half steps; every partial sum stays within [min, max].
        private double LowerBound(int index)
        {
            if (index == 0) return _min;
            if (!double.IsInfinity(_span)) return _min + (double)index / _binCount * _span;
            double step = (double)index / _binCount * _halfSpan;
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

            double estimate = double.IsInfinity(_span)
                ? (0.5 * value - 0.5 * _min) / _halfSpan * _bins.Length
                : (value - _min) / _span * _bins.Length;
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
