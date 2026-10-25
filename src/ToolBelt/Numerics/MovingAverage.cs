// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// A simple moving average over the most recent <c>windowSize</c> samples. Backed by a ring buffer
    /// and a compensated running sum, so each <see cref="Add"/> is amortised O(1); the sum is rebuilt from the
    /// window once per lap, so round-off cannot accumulate. Huge finite samples whose sum would overflow
    /// (1e308 + 1e308) are summed scaled down by 2⁻³², so the average stays finite. Infinities and NaN are counted rather than summed,
    /// so they affect the average only while they are inside the window. Before the window fills, the average is over
    /// however many samples have been seen. Not thread-safe.
    /// </summary>
    public sealed class MovingAverage
    {
        private readonly double[] _buffer;
        private int _next;   // index to write next; equals the oldest slot once full
        private int _count;
        private double _sum;
        private double _compensation; // Neumaier running error term
        private int _nan, _posInf, _negInf; // non-finite samples in the window (kept out of the sum)
        private bool _scaled;     // the sum holds v·2⁻³² because the plain sum overflowed (huge finite samples)
        private bool _overflowed; // set by Accumulate; the next consistent point rebuilds in scaled mode
        private const double Down = 1.0 / 4294967296.0, Up = 4294967296.0; // 2⁻³², 2³²: exact, and room for 2³¹ samples

        public MovingAverage(int windowSize)
        {
            if (windowSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowSize), windowSize, "Window size must be positive.");
            _buffer = new double[windowSize];
        }

        public int WindowSize => _buffer.Length;

        public int Count => _count;

        public bool IsFull => _count == _buffer.Length;

        /// <summary>The average of the current window, or NaN if no samples have been added.</summary>
        public double Average
        {
            get
            {
                if (_count == 0) return double.NaN;
                if (_nan > 0 || (_posInf > 0 && _negInf > 0)) return double.NaN;
                if (_posInf > 0) return double.PositiveInfinity;
                if (_negInf > 0) return double.NegativeInfinity;
                double mean = (_sum + _compensation) / _count;
                return _scaled ? mean * Up : mean;
            }
        }

        /// <summary>Adds a sample and returns the updated average.</summary>
        public double Add(double value)
        {
            if (_count < _buffer.Length)
            {
                _buffer[_next] = value;
                Include(value, +1);
                _count++;
            }
            else
            {
                // Full: replace the oldest sample (at _next) and adjust the running sum by the delta.
                double old = _buffer[_next];
                _buffer[_next] = value;
                Include(value, +1);
                Include(old, -1);
            }
            _next = (_next + 1) % _buffer.Length;
            // Rebuild only here, where the buffer and count agree: once per lap (unscaled if it fits), or at once when
            // the sum just overflowed.
            if (_next == 0 && IsFull) Rebuild(scaled: false);
            else if (_overflowed) Rebuild(scaled: true);
            return Average;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _next = 0;
            _count = 0;
            _sum = 0;
            _compensation = 0;
            _nan = _posInf = _negInf = 0;
            _scaled = _overflowed = false;
        }

        private void Include(double v, int sign)
        {
            if (double.IsNaN(v)) _nan += sign;
            else if (double.IsPositiveInfinity(v)) _posInf += sign;
            else if (double.IsNegativeInfinity(v)) _negInf += sign;
            else Accumulate(sign * v);
        }

        private void Accumulate(double v)
        {
            if (_scaled) v *= Down;
            double t = _sum + v;
            if (double.IsInfinity(t)) _overflowed = true;
            if (Math.Abs(_sum) >= Math.Abs(v)) _compensation += (_sum - t) + v;
            else _compensation += (v - t) + _sum;
            _sum = t;
        }

        private void Rebuild(bool scaled)
        {
            _scaled = scaled;
            _overflowed = false;
            _sum = 0;
            _compensation = 0;
            _nan = _posInf = _negInf = 0;
            for (int i = 0; i < _count; i++) Include(_buffer[i], +1);
            if (_overflowed && !scaled) Rebuild(scaled: true);
        }
    }
}
