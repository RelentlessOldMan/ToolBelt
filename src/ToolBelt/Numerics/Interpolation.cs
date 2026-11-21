// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Scalar interpolation and range-mapping helpers: linear interpolation and its inverse, clamping,
    /// range remapping, and smoothstep. All operate on <see cref="double"/>.
    /// </summary>
    public static class Interpolation
    {
        /// <summary>Linear interpolation: returns <c>a + (b - a) * t</c>. <paramref name="t"/> is not clamped.</summary>
        public static double Lerp(double a, double b, double t)
        {
            // The ends are exact and an interior t stays between them: a + (b − a)·1 lost a small b next to a large a
            // (Lerp(1e16, 1, 1) gave 0).
            if (t == 0) return a;
            if (t == 1) return b;
            // Equal ends are the whole line: ∞ − ∞ made Lerp(∞, ∞, 0.5) NaN though both ends are ∞.
            if (a == b && !double.IsNaN(t) && !double.IsInfinity(t)) return a;
            double d = b - a, r;
            // Finite ends of opposite sign can overflow b − a (−1e308 to 1e308): interpolate the halves instead.
            if (double.IsInfinity(d) && !double.IsInfinity(a) && !double.IsInfinity(b))
                r = 2 * (0.5 * a + (0.5 * b - 0.5 * a) * t);
            // An infinite end with a finite t: a + (b − a)·t was ∞ + (−∞)·t, so Lerp(∞, 1, 0.5) was NaN though
            // Lerp(1, ∞, 0.5) was ∞. The weighted form gives both ends' signs their due.
            else if ((double.IsInfinity(a) || double.IsInfinity(b)) && !double.IsInfinity(t))
                r = (1 - t) * a + t * b;
            else
            {
                r = a + d * t;
                // (b − a)·t can overflow where the result does not (Lerp(1.5e308, 0, 1.5) gave −∞ for −7.5e307): in
                // halves then, which overflow only where the result does.
                if (double.IsInfinity(r) && !double.IsInfinity(t) && !double.IsInfinity(a) && !double.IsInfinity(b))
                    r = 2 * (0.5 * a + (0.5 * b - 0.5 * a) * t);
            }
            if (t > 0 && t < 1) r = Math.Min(Math.Max(r, Math.Min(a, b)), Math.Max(a, b));
            return r;
        }

        /// <summary>Linear interpolation with <paramref name="t"/> clamped to [0, 1].</summary>
        public static double LerpClamped(double a, double b, double t) => Lerp(a, b, Clamp(t, 0, 1));

        /// <summary>The inverse of <see cref="Lerp"/>: the <c>t</c> for which <c>Lerp(a, b, t) == value</c>. Returns 0 if a == b.</summary>
        public static double InverseLerp(double a, double b, double value)
        {
            if (a == b) return 0.0;
            double num = value - a, den = b - a;
            if ((double.IsInfinity(num) || double.IsInfinity(den))
                && !double.IsInfinity(a) && !double.IsInfinity(b) && !double.IsInfinity(value))
                return (0.5 * value - 0.5 * a) / (0.5 * b - 0.5 * a); // the differences overflowed; the ratio of halves does not
            return num / den;
        }

        /// <summary>Clamps <paramref name="value"/> to the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(min)) throw new ArgumentOutOfRangeException(nameof(min), min, "Bound is NaN."); // was silently ignored
            if (double.IsNaN(max)) throw new ArgumentOutOfRangeException(nameof(max), max, "Bound is NaN.");
            if (min > max)
                throw new ArgumentException("min must not exceed max.", nameof(min));
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Maps <paramref name="value"/> from the input range onto the output range (linearly, unclamped).</summary>
        public static double Remap(double value, double inMin, double inMax, double outMin, double outMax)
        {
            double t = InverseLerp(inMin, inMax, value);
            // t out of the normal range where the result is not: an input span of 1e-310 overflowed it (Remap(1, 0, 1e-310,
            // 0, 1e-300) gave ∞ for 1e10), and one of 1e300 underflowed it (1e-320 onto [0, 1e300] gave 0). Then the
            // product is formed with its exponents apart.
            if ((double.IsInfinity(t) || (Math.Abs(t) < 2.2250738585072014E-308 && value != inMin)) && inMin != inMax
                && IsFinite(value) && IsFinite(inMin) && IsFinite(inMax) && IsFinite(outMin) && IsFinite(outMax))
            {
                double r = outMin + Scaled(value, inMin, inMax, outMin, outMax);
                if (t > 0 && t < 1) r = Math.Min(Math.Max(r, Math.Min(outMin, outMax)), Math.Max(outMin, outMax));
                return r;
            }
            return Lerp(outMin, outMax, t);
        }

        // (value − lo)·(outMax − outMin)/(hi − lo), all finite, with a difference that overflows halved and the
        // halving undone in the exponent.
        private static double Scaled(double value, double lo, double hi, double outMin, double outMax)
        {
            double num = value - lo, den = hi - lo, span = outMax - outMin;
            int k = 0;
            if (double.IsInfinity(num)) { num = 0.5 * value - 0.5 * lo; k++; }
            if (double.IsInfinity(den)) { den = 0.5 * hi - 0.5 * lo; k--; }
            if (double.IsInfinity(span)) { span = 0.5 * outMax - 0.5 * outMin; k++; }
            return MulDiv(num, span, den, k);
        }

        // x·y/z·2^k with the binary exponents kept apart: no step over- or underflows unless the result does.
        private static double MulDiv(double x, double y, double z, int k)
        {
            if (x == 0 || y == 0) return x * y / z;
            int ex = Exponent(x), ey = Exponent(y), ez = Exponent(z);
            return ScaleB(ScaleB(x, -ex) * ScaleB(y, -ey) / ScaleB(z, -ez), ex + ey - ez + k);
        }

        // floor(log2 |x|) for a finite x other than 0, subnormals included.
        private static int Exponent(double x)
        {
            int e = (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF);
            return e == 0 ? Exponent(x * 18446744073709551616.0) - 64 : e - 1023;
        }

        // x·2^k, rounded at most once: every step but the last stays in the normal range.
        private static double ScaleB(double x, int k)
        {
            for (; k > 1023; k -= 1023) x *= Pow2(1023);
            for (; k < -1022; k += 960) x *= Pow2(-960);
            return x * Pow2(k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        private static bool IsFinite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        /// <summary>
        /// Hermite smoothstep: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, and a
        /// smooth S-curve in between.
        /// </summary>
        public static double SmoothStep(double edge0, double edge1, double x)
        {
            if (edge0 == edge1)
                throw new ArgumentException("edge0 and edge1 must differ.", nameof(edge1));
            double t = Clamp(InverseLerp(edge0, edge1, x), 0, 1);   // edge1 − edge0 overflows for ±1e308
            return t * t * (3 - 2 * t);
        }
    }
}
