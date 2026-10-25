// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// A simple moving average over the most recent <c>windowSize</c> samples. Backed by a ring buffer
    /// and a compensated running sum, so each <see cref="Add"/> is amortised O(1); the sum is rebuilt from the
    /// window once per lap, so round-off cannot accumulate. Infinities and NaN are counted rather than summed,
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
                return (_sum + _compensation) / _count;
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
            if (_next == 0 && IsFull) Rebuild();
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
            double t = _sum + v;
            if (Math.Abs(_sum) >= Math.Abs(v)) _compensation += (_sum - t) + v;
            else _compensation += (v - t) + _sum;
            _sum = t;
        }

        private void Rebuild()
        {
            _sum = 0;
            _compensation = 0;
            _nan = _posInf = _negInf = 0;
            for (int i = 0; i < _count; i++) Include(_buffer[i], +1);
        }
    }
}
