// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Numerics;

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
        // The exact sum, as non-overlapping partials (Shewchuk) in two tiers: values of at least 2⁻⁹⁵⁸ scaled by 2⁻⁶⁴
        // (exact, and no count can overflow them) and smaller values unscaled, where 2⁻⁶⁴ would round them. A double-double
        // copy read only after the plain sum overflowed still lost small values: 1.7e308, 3e307, 61, −1.7e308, −3e307
        // summed to 0, and so did 1e308, 61, −1e308, which never overflowed.
        private double[] _big = new double[4], _small = new double[4];
        private int _nBig, _nSmall;
        private const double BigFrom = 4.1045368012983762e-289; // 2⁻⁹⁵⁸: scaled by 2⁻⁶⁴ it is still normal

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
            if (Math.Abs(value) >= BigFrom) Accumulate(ref _big, ref _nBig, value * (1.0 / 18446744073709551616.0));
            else Accumulate(ref _small, ref _nSmall, value);
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
        /// The sum of the samples, exact and rounded once: 1e308, 61, −1e308 sums to 61, and 1e308 + 1e308 − 1e308 to
        /// 1e308. A sum truly beyond the double range is ±∞.
        /// </summary>
        // The mean was the old fallback, but its rounding error (ε·max|x|) swamped a small sum left after huge values
        // cancelled: 1.7e308, 3e307, −1.7e308, −3e307, 61 summed to −6.5e292.
        public double Sum
            => _nSmall == 0 ? Total(_big, _nBig) * 18446744073709551616.0 // exact: the scaled total is a multiple of 2⁻¹⁰⁷⁴
             : _nBig == 0 ? Total(_small, _nSmall)
             : ExactSum(_big, _nBig, 64, _small, _nSmall);

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
        // x·2^k rounded once, as ZScore and WeightedAverage do: stepping by 2^-1022 rounded a result in the subnormal range
        // twice (once on the step that first went subnormal, again on the last), off by an ulp for a tiny variance.
        private static double ScaleB(double x, int k)
        {
            if (x == 0 || double.IsNaN(x) || double.IsInfinity(x)) return x;
            long kk = k;
            if (Math.Abs(x) < 2.2250738585072014E-308) { x *= 18446744073709551616.0; kk -= 64; } // subnormal: made normal, exactly
            int e = Exponent(Math.Abs(x));
            long target = e + kk; // the result's exponent
            if (target > 1023) return x > 0 ? double.PositiveInfinity : double.NegativeInfinity;
            if (target >= -1022) return ScaleSteps(x, kk);
            x = ScaleSteps(x, -1022 - e); // exactly ±[1, 2)·2^-1022
            long rest = target + 1022;
            return rest < -1022 ? x * 0.0 : x * Pow2((int)rest);
        }

        // Exact: x and x·2^k are normal, and so is every step between them.
        private static double ScaleSteps(double x, long k)
        {
            while (k > 1023) { x *= Pow2(1023); k -= 1023; }
            while (k < -1022) { x *= Pow2(-1022); k += 1022; }
            return x * Pow2((int)k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        // Adds v to the exact sum held as non-overlapping partials, smallest first (Shewchuk; Python's math.fsum).
        private static void Accumulate(ref double[] partials, ref int count, double v)
        {
            int k = 0;
            for (int i = 0; i < count; i++)
            {
                double y = partials[i];
                if (Math.Abs(v) < Math.Abs(y)) { double s = v; v = y; y = s; }
                double hi = v + y;
                double lo = y - (hi - v);
                if (lo != 0) partials[k++] = lo;
                v = hi;
            }
            if (k == partials.Length) Array.Resize(ref partials, 2 * k);
            partials[k++] = v;
            count = k;
        }

        // The partials' sum rounded once (math.fsum's last step, with its half-way correction).
        private static double Total(double[] partials, int count)
        {
            int n = count;
            if (n == 0) return 0;
            double hi = partials[--n], lo = 0;
            while (n > 0)
            {
                double x = hi, y = partials[--n];
                hi = x + y;
                lo = y - (hi - x);
                if (lo != 0) break;
            }
            if (n > 0 && ((lo < 0 && partials[n - 1] < 0) || (lo > 0 && partials[n - 1] > 0)))
            {
                double y = lo * 2, x = hi + y;
                if (y == x - hi) hi = x;
            }
            return hi;
        }

        // mÂ·2^e rounded once to the nearest double (ties to even), subnormals and overflow included.
        private static double RoundToDouble(BigInteger m, long e)
        {
            if (m.IsZero) return 0;
            bool neg = m.Sign < 0;
            if (neg) m = -m;
            long top = BitLength(m) - 1 + e;               // floor(log2 |mÂ·2^e|)
            if (top > 1023) return neg ? double.NegativeInfinity : double.PositiveInfinity;
            long q = Math.Max(top - 52, -1074);            // the result's ulp
            long shift = q - e;
            BigInteger kept;
            if (shift <= 0) kept = m << (int)-shift;
            else
            {
                kept = m >> (int)shift;
                BigInteger rest = m - (kept << (int)shift), half = BigInteger.One << (int)(shift - 1);
                if (rest > half || (rest == half && !kept.IsEven)) kept += 1;
            }
            double r = (long)kept;                         // at most 2^53, so exact; 2^53Â·2^971 overflows to âˆž
            r = q >= -1022 ? r * Pow2((int)q) : r * Pow2(-1022) * Pow2((int)(q + 1022));
            return neg ? -r : r;
        }

        private static long BitLength(BigInteger m)
        {
            byte[] b = m.ToByteArray();
            int i = b.Length - 1;
            while (i > 0 && b[i] == 0) i--;
            long n = 8L * i;
            for (int t = b[i]; t != 0; t >>= 1) n++;
            return n;
        }

        // x = mantissaÂ·2^exponent exactly.
        private static void Split(double x, out long mantissa, out int exponent)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else m |= 1L << 52;
            exponent = be - 1075;
            mantissa = bits < 0 ? -m : m;
        }

        // The exact sum of aÂ·2^shiftA (each partial) and b, rounded once.
        private static double ExactSum(double[] a, int na, int shiftA, double[] b, int nb)
        {
            var mant = new long[na + nb];
            var exp = new int[na + nb];
            int min = int.MaxValue;
            for (int i = 0; i < na + nb; i++)
            {
                Split(i < na ? a[i] : b[i - na], out mant[i], out exp[i]);
                if (i < na) exp[i] += shiftA;
                if (mant[i] != 0) min = Math.Min(min, exp[i]);
            }
            if (min == int.MaxValue) return 0;
            BigInteger sum = BigInteger.Zero;
            for (int i = 0; i < mant.Length; i++)
                if (mant[i] != 0) sum += (BigInteger)mant[i] << (exp[i] - min);
            return RoundToDouble(sum, min);
        }

        public void Clear()
        {
            _count = 0;
            _mean = 0;
            _m2 = 0;
            _m2Exp = 0;
            _min = double.PositiveInfinity;
            _max = double.NegativeInfinity;
            _nBig = _nSmall = 0;
        }
    }
}
