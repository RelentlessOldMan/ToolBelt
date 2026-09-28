// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// A simple moving average over the most recent <c>windowSize</c> samples. Backed by a ring buffer
    /// and a running sum, so each <see cref="Add"/> is O(1). Before the window fills, the average is over
    /// however many samples have been seen. Not thread-safe.
    /// </summary>
    public sealed class MovingAverage
    {
        private readonly double[] _buffer;
        private int _next;   // index to write next; equals the oldest slot once full
        private int _count;
        private double _sum;

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
        public double Average => _count > 0 ? _sum / _count : double.NaN;

        /// <summary>Adds a sample and returns the updated average.</summary>
        public double Add(double value)
        {
            if (_count < _buffer.Length)
            {
                _buffer[_next] = value;
                _sum += value;
                _count++;
            }
            else
            {
                // Full: replace the oldest sample (at _next) and adjust the running sum by the delta.
                _sum += value - _buffer[_next];
                _buffer[_next] = value;
            }
            _next = (_next + 1) % _buffer.Length;
            return Average;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _next = 0;
            _count = 0;
            _sum = 0;
        }
    }
}
