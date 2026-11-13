// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// An exponential moving average: each new sample is blended as
    /// <c>value = alpha·sample + (1 - alpha)·value</c>. A higher <c>alpha</c> tracks recent samples more
    /// closely; a lower one smooths more. The first sample seeds the average. Only finite samples are accepted
    /// (a NaN or infinity poisoned the average for good). Not thread-safe.
    /// </summary>
    public sealed class ExponentialMovingAverage
    {
        private readonly double _alpha;
        private double _value;

        /// <param name="alpha">Smoothing factor in (0, 1].</param>
        public ExponentialMovingAverage(double alpha)
        {
            if (double.IsNaN(alpha) || alpha <= 0 || alpha > 1)
                throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "Alpha must be in (0, 1].");
            _alpha = alpha;
        }

        public double Alpha => _alpha;

        public long Count { get; private set; }

        public bool HasValue => Count > 0;

        /// <summary>The current average, or NaN before any sample is added.</summary>
        public double Value => Count > 0 ? _value : double.NaN;

        /// <summary>Adds a sample and returns the updated average.</summary>
        public double Add(double sample)
        {
            if (double.IsNaN(sample) || double.IsInfinity(sample))
                throw new ArgumentException("Only finite samples can be averaged.", nameof(sample));
            if (Count == 0 || _alpha == 1)
                _value = sample;
            else
            {
                // As a step toward the sample: α·s + (1 − α)·v drifted on constant input (0.1 at α = 0.3 became
                // 0.10000000000000002), where s − v is 0. A step that overflows (1e308 to −1e308) is taken in halves.
                double step = sample - _value;
                double next = double.IsInfinity(step)
                    ? 2 * (0.5 * _value + _alpha * (0.5 * sample - 0.5 * _value))
                    : _value + _alpha * step;
                // Never past the sample or behind the old average.
                _value = Math.Max(Math.Min(sample, _value), Math.Min(Math.Max(sample, _value), next));
            }
            Count++;
            return _value;
        }

        public void Clear()
        {
            _value = 0;
            Count = 0;
        }
    }
}
