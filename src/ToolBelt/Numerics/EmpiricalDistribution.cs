// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// The empirical cumulative distribution of a sample as a step function, with lookups in both
    /// directions: <see cref="Cdf"/> gives the fraction of samples ≤ a value, and <see cref="Quantile"/> the
    /// value at a probability (type-7 interpolation, matching <see cref="Percentile"/>). This is what you
    /// actually plot when arguing about a distribution's shape. Immutable.
    /// </summary>
    public sealed class EmpiricalDistribution
    {
        private readonly double[] _sorted;

        public EmpiricalDistribution(IEnumerable<double> samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            _sorted = new List<double>(samples).ToArray();
            if (_sorted.Length == 0) throw new ArgumentException("At least one sample is required.", nameof(samples));
            Array.Sort(_sorted);
        }

        public int Count => _sorted.Length;
        public double Min => _sorted[0];
        public double Max => _sorted[_sorted.Length - 1];

        /// <summary>The fraction of samples less than or equal to <paramref name="value"/>, in [0, 1].</summary>
        public double Cdf(double value)
        {
            // Count of samples <= value via the index of the first element strictly greater.
            int lo = 0, hi = _sorted.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_sorted[mid] <= value) lo = mid + 1;
                else hi = mid;
            }
            return (double)lo / _sorted.Length;
        }

        /// <summary>The value at probability <paramref name="p"/> in [0, 1] (type-7 linear interpolation).</summary>
        public double Quantile(double p)
        {
            if (p < 0 || p > 1) throw new ArgumentOutOfRangeException(nameof(p), p, "Probability must be in [0, 1].");
            if (_sorted.Length == 1) return _sorted[0];
            double position = p * (_sorted.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper) return _sorted[lower];
            double frac = position - lower;
            return _sorted[lower] * (1 - frac) + _sorted[upper] * frac;
        }
    }
}
