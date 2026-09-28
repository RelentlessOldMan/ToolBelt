// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Computes running summary statistics in a single pass using Welford's algorithm, which is
    /// numerically stable (no catastrophic cancellation from the naive sum-of-squares approach) and
    /// needs only O(1) memory. Tracks count, mean, variance, standard deviation, min, max, and sum.
    /// Statistics that are undefined for the current count return <see cref="double.NaN"/>.
    /// </summary>
    public sealed class RunningStatistics
    {
        private long _count;
        private double _mean;
        private double _m2;   // sum of squared deviations from the running mean
        private double _min = double.PositiveInfinity;
        private double _max = double.NegativeInfinity;
        private double _sum;

        /// <summary>Adds one sample.</summary>
        public void Push(double value)
        {
            _count++;
            double delta = value - _mean;
            _mean += delta / _count;
            double delta2 = value - _mean;
            _m2 += delta * delta2;

            if (value < _min) _min = value;
            if (value > _max) _max = value;
            _sum += value;
        }

        /// <summary>Adds a batch of samples.</summary>
        public void PushRange(IEnumerable<double> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            foreach (double v in values)
                Push(v);
        }

        public long Count => _count;

        public double Sum => _sum;

        public double Mean => _count > 0 ? _mean : double.NaN;

        public double Min => _count > 0 ? _min : double.NaN;

        public double Max => _count > 0 ? _max : double.NaN;

        /// <summary>Population variance (divides by N). NaN when empty.</summary>
        public double PopulationVariance => _count > 0 ? _m2 / _count : double.NaN;

        /// <summary>Sample variance (divides by N-1, Bessel's correction). NaN with fewer than two samples.</summary>
        public double SampleVariance => _count > 1 ? _m2 / (_count - 1) : double.NaN;

        public double PopulationStandardDeviation => Math.Sqrt(PopulationVariance);

        public double SampleStandardDeviation => Math.Sqrt(SampleVariance);

        public void Clear()
        {
            _count = 0;
            _mean = 0;
            _m2 = 0;
            _min = double.PositiveInfinity;
            _max = double.NegativeInfinity;
            _sum = 0;
        }
    }
}
