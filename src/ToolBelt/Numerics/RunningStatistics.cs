// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Computes running summary statistics in a single pass using Welford's algorithm, which is
    /// numerically stable (no catastrophic cancellation from the naive sum-of-squares approach) and
    /// needs only O(1) memory. Tracks count, mean, variance, standard deviation, min, max, and sum.
    /// Statistics that are undefined for the current count return <see cref="double.NaN"/>. Only finite samples
    /// are accepted: one NaN or infinity used to turn the mean and variance into NaN for good. The mean stays finite for
    /// any finite samples. The sum of squared deviations is kept as a mantissa and a power of two, so it neither
    /// underflows (1e-200 and 2e-200 had a deviation of 0) nor overflows: only a variance beyond the double range
    /// saturates (to +∞ for 1e400, whose standard deviation 1e200 is still returned).
    /// </summary>
    public sealed class RunningStatistics
    {
        private long _count;
        private double _mean;
        private double _m2;   // sum of squared deviations from the running mean, as _m2 · 2^_m2Exp
        private int _m2Exp;
        private double _min = double.PositiveInfinity;
        private double _max = double.NegativeInfinity;
        private double _sum;

        /// <summary>Adds one sample.</summary>
        public void Push(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Only finite values can be added to running statistics.", nameof(value));
            _count++;
            double delta = value - _mean;
            if (double.IsInfinity(delta))
            {
                // The samples span more than the double range (e.g. 1e308 then -1e308): step with halved operands,
                // which cannot overflow, and count the halving in the exponent.
                double half = 0.5 * value - 0.5 * _mean;
                _mean += half / _count * 2;
                AddSquare(half, 0.5 * value - 0.5 * _mean, 2);
            }
            else
            {
                _mean += delta / _count;
                AddSquare(delta, value - _mean, 0);
            }

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

        /// <summary>
        /// The sum of the samples. When the running total overflowed but the true sum is within range (1e308 + 1e308 −
        /// 1e308) it is recovered from the mean; a sum truly beyond the double range is ±∞.
        /// </summary>
        public double Sum => double.IsInfinity(_sum) ? _mean * _count : _sum;

        public double Mean => _count > 0 ? _mean : double.NaN;

        public double Min => _count > 0 ? _min : double.NaN;

        public double Max => _count > 0 ? _max : double.NaN;

        /// <summary>Population variance (divides by N). NaN when empty.</summary>
        public double PopulationVariance => _count > 0 ? ScaleB(_m2 / _count, _m2Exp) : double.NaN;

        /// <summary>Sample variance (divides by N-1, Bessel's correction). NaN with fewer than two samples.</summary>
        public double SampleVariance => _count > 1 ? ScaleB(_m2 / (_count - 1), _m2Exp) : double.NaN;

        public double PopulationStandardDeviation => _count > 0 ? Root(_count) : double.NaN;

        public double SampleStandardDeviation => _count > 1 ? Root(_count - 1) : double.NaN;

        // √(m2 / divisor) with the exponent halved separately, so a deviation of 1e200 (variance 1e400) stays finite.
        private double Root(long divisor)
        {
            double v = _m2 / divisor;
            int e = _m2Exp;
            if ((e & 1) != 0) { v *= 2; e--; }
            return ScaleB(Math.Sqrt(v), e / 2);
        }

        // Adds d1·d2·2^extra to the sum of squares, keeping both as a mantissa in [1, 2) and an exponent.
        private void AddSquare(double d1, double d2, int extra)
        {
            if (d1 == 0 || d2 == 0) return;
            int e1 = Exponent(Math.Abs(d1)), e2 = Exponent(Math.Abs(d2));
            double t = ScaleB(d1, -e1) * ScaleB(d2, -e2);
            int te = e1 + e2 + extra;
            if (_m2 == 0) { _m2 = t; _m2Exp = te; }
            else if (te > _m2Exp) { _m2 = ScaleB(_m2, _m2Exp - te) + t; _m2Exp = te; }
            else _m2 += ScaleB(t, te - _m2Exp);
            if (_m2 == 0) { _m2Exp = 0; return; }
            int e = Exponent(Math.Abs(_m2));
            _m2 = ScaleB(_m2, -e);
            _m2Exp += e;
        }

        // floor(log2 x) for a positive finite x, subnormals included.
        private static int Exponent(double x)
        {
            int e = (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF);
            return e == 0 ? Exponent(x * 18446744073709551616.0) - 64 : e - 1023;
        }

        // x·2^k without overflowing the factor itself (2^k alone is out of range for |k| > 1023).
        private static double ScaleB(double x, int k)
        {
            while (k > 1023) { x *= Pow2(1023); k -= 1023; if (double.IsInfinity(x)) return x; }
            while (k < -1022) { x *= Pow2(-1022); k += 1022; if (x == 0) return x; }
            return x * Pow2(k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        public void Clear()
        {
            _count = 0;
            _mean = 0;
            _m2 = 0;
            _m2Exp = 0;
            _min = double.PositiveInfinity;
            _max = double.NegativeInfinity;
            _sum = 0;
        }
    }
}
