// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// An exponential moving average: each new sample is blended as
    /// <c>value = alpha·sample + (1 - alpha)·value</c>. A higher <c>alpha</c> tracks recent samples more
    /// closely; a lower one smooths more. The first sample seeds the average. Not thread-safe.
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
            if (Count == 0)
                _value = sample;
            else
                _value = _alpha * sample + (1 - _alpha) * _value;
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
