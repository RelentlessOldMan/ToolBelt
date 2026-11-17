// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// A simple moving average over the most recent <c>windowSize</c> samples. Backed by a ring buffer
    /// and an exact running sum (Shewchuk's non-overlapping partials, as Python's math.fsum), so each <see cref="Add"/>
    /// is amortised O(1) and a sample that leaves the window leaves no round-off behind. Huge finite samples whose sum would overflow
    /// (1e308 + 1e308) are summed scaled down by 2⁻³², so the average stays finite. Infinities and NaN are counted rather than summed,
    /// so they affect the average only while they are inside the window. Before the window fills, the average is over
    /// however many samples have been seen. Not thread-safe.
    /// </summary>
    public sealed class MovingAverage
    {
        private readonly double[] _buffer;
        private int _next;   // index to write next; equals the oldest slot once full
        private int _count;
        // The window's exact sum as non-overlapping partials, smallest first. One compensation term could not hold three
        // magnitudes: after 1e300 and 1e200 left a window of three, {1, 1, 1} averaged 0.
        private double[] _partials = new double[4];
        private int _np;
        private int _nan, _posInf, _negInf; // non-finite samples in the window (kept out of the sum)
        private int _huge;        // finite samples of magnitude 2⁹⁶⁰ or more in the window: only these can overflow the sum
        private const double Huge = 9.7453140114e288; // 2⁹⁶⁰, roughly: below it 2³¹ samples sum to under 2⁹⁹¹
        private bool _scaled;     // the sum holds v·2⁻³² because the plain sum overflowed (huge finite samples)
        private bool _overflowed; // set by Accumulate; the next consistent point rebuilds in scaled mode
        private const double Down = 1.0 / 4294967296.0, Up = 4294967296.0; // 2⁻³², 2³²: exact, and room for 2³¹ samples

        public MovingAverage(int windowSize)
        {
            // Past Array.MaxLength the buffer threw OutOfMemoryException ("array dimensions exceeded").
            if (windowSize <= 0 || windowSize > 0x7FFFFFC7)
                throw new ArgumentOutOfRangeException(nameof(windowSize), windowSize, "Window size must be 1 to Array.MaxLength (0x7FFFFFC7).");
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
                double mean = Total() / _count;
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
            // Scaled mode ends once the huge samples have left: kept to the lap, tiny samples after them were flushed to 0.
            if ((_next == 0 && IsFull) || (_scaled && _huge == 0)) Rebuild(scaled: false);
            else if (_overflowed) Rebuild(scaled: true);
            return Average;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _next = 0;
            _count = 0;
            _np = 0;
            _nan = _posInf = _negInf = _huge = 0;
            _scaled = _overflowed = false;
        }

        private void Include(double v, int sign)
        {
            if (double.IsNaN(v)) _nan += sign;
            else if (double.IsPositiveInfinity(v)) _posInf += sign;
            else if (double.IsNegativeInfinity(v)) _negInf += sign;
            else
            {
                if (Math.Abs(v) >= Huge) _huge += sign;
                Accumulate(sign * v);
            }
        }

        private void Accumulate(double v)
        {
            if (_scaled) v *= Down;
            int k = 0;
            for (int i = 0; i < _np; i++)
            {
                double y = _partials[i];
                if (Math.Abs(v) < Math.Abs(y)) { double s = v; v = y; y = s; }
                double hi = v + y;
                if (double.IsInfinity(hi)) { _overflowed = true; _partials[0] = hi; _np = 1; return; } // rebuilt scaled
                double lo = y - (hi - v);
                if (lo != 0) _partials[k++] = lo;
                v = hi;
            }
            if (k == _partials.Length) Array.Resize(ref _partials, 2 * k);
            _partials[k++] = v;
            _np = k;
        }

        // The partials' sum rounded once (math.fsum's last step, with its half-way correction).
        private double Total()
        {
            int n = _np;
            if (n == 0) return 0;
            double hi = _partials[--n], lo = 0;
            while (n > 0)
            {
                double x = hi, y = _partials[--n];
                hi = x + y;
                lo = y - (hi - x);
                if (lo != 0) break;
            }
            if (n > 0 && ((lo < 0 && _partials[n - 1] < 0) || (lo > 0 && _partials[n - 1] > 0)))
            {
                double y = lo * 2, x = hi + y;
                if (y == x - hi) hi = x;
            }
            return hi;
        }

        private void Rebuild(bool scaled)
        {
            _scaled = scaled;
            _overflowed = false;
            _np = 0;
            _nan = _posInf = _negInf = _huge = 0;
            for (int i = 0; i < _count; i++) Include(_buffer[i], +1);
            if (_overflowed && !scaled) Rebuild(scaled: true);
        }
    }
}
