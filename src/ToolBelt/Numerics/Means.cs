// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// The classical Pythagorean means plus root-mean-square. Geometric and harmonic means require all
    /// values to be strictly positive (NaN is rejected); arithmetic mean and RMS accept any values. Each is
    /// computed so that it does not overflow or underflow when the mean itself is representable (1e308 values,
    /// subnormals). For positive data the ordering HM ≤ GM ≤ AM ≤ RMS holds up to rounding (equal values can come
    /// out an ulp apart), and each mean lies between the smallest and largest value.
    /// </summary>
    public static class Means
    {
        /// <summary>Arithmetic mean (sum / n).</summary>
        public static double Arithmetic(IReadOnlyList<double> values)
        {
            Require(values);
            double sum = 0;
            bool nan = false, posInf = false, negInf = false;
            for (int i = 0; i < values.Count; i++)
            {
                double v = values[i];
                if (double.IsNaN(v)) nan = true;
                else if (double.IsPositiveInfinity(v)) posInf = true;
                else if (double.IsNegativeInfinity(v)) negInf = true;
                else sum += v;
            }
            // Infinities decide the mean by themselves: summed in, an overflowed finite part (1e308 + 1e308) met −∞ as NaN.
            if (nan || (posInf && negInf)) return double.NaN;
            if (posInf) return double.PositiveInfinity;
            if (negInf) return double.NegativeInfinity;
            if (double.IsInfinity(sum))
            {
                // The sum of finite values overflowed (two 1e308s); Σ(x/n) cannot.
                sum = 0;
                for (int i = 0; i < values.Count; i++) sum += values[i] / values.Count;
                return sum;
            }
            return sum / values.Count;
        }

        /// <summary>Geometric mean (n-th root of the product), computed in log space. Requires positive values.</summary>
        public static double Geometric(IReadOnlyList<double> values)
        {
            Require(values);
            // Each value split into a power of two and a mantissa in [1, 2): only the mantissas' logs are summed
            // (compensated), and the exponents exactly. exp(Σ ln x / n) lost hundreds of ulps at large exponents (three
            // equal 1e300s gave 9.999999999999763e299) and fell below the harmonic mean.
            long exponents = 0;
            double sum = 0, compensation = 0, min = double.PositiveInfinity, max = 0;
            bool infinite = false;
            for (int i = 0; i < values.Count; i++)
            {
                double v = values[i];
                if (!(v > 0))
                    throw new ArgumentException("Geometric mean requires strictly positive values.", nameof(values));
                if (double.IsPositiveInfinity(v)) { infinite = true; continue; }
                if (v < min) min = v;
                if (v > max) max = v;
                int e = Exponent(v);
                exponents += e;
                double term = Math.Log(ScaleB(v, -e)), t = sum + term;
                compensation += Math.Abs(sum) >= Math.Abs(term) ? (sum - t) + term : (term - t) + sum;
                sum = t;
            }
            if (infinite) return double.PositiveInfinity;
            int n = values.Count;
            long q = exponents / n, r = exponents % n;
            double g = ScaleB(Math.Exp((sum + compensation) / n + (double)r / n * 0.69314718055994531), (int)q);
            return Math.Max(min, Math.Min(max, g));
        }

        /// <summary>Harmonic mean (n / sum of reciprocals). Requires positive values.</summary>
        public static double Harmonic(IReadOnlyList<double> values)
        {
            Require(values);
            double min = double.PositiveInfinity;
            for (int i = 0; i < values.Count; i++)
            {
                if (!(values[i] > 0))
                    throw new ArgumentException("Harmonic mean requires strictly positive values.", nameof(values));
                if (values[i] < min) min = values[i];
            }
            if (double.IsPositiveInfinity(min)) return min;
            // Reciprocals scaled by the smallest value lie in (0, 1]: 1/x overflowed for a subnormal x.
            double sumScaled = 0;
            for (int i = 0; i < values.Count; i++) sumScaled += min / values[i];
            return min * (values.Count / sumScaled);
        }

        /// <summary>Root mean square (quadratic mean).</summary>
        public static double RootMeanSquare(IReadOnlyList<double> values)
        {
            Require(values);
            // Scale by the largest magnitude, so the squares neither overflow (1e200) nor underflow (1e-200).
            double max = 0;
            for (int i = 0; i < values.Count; i++)
            {
                double a = Math.Abs(values[i]);
                if (double.IsNaN(a)) return double.NaN;
                if (a > max) max = a;
            }
            if (max == 0 || double.IsInfinity(max)) return max;
            double sumSquares = 0;
            for (int i = 0; i < values.Count; i++)
            {
                double r = values[i] / max;
                sumSquares += r * r;
            }
            return max * Math.Sqrt(sumSquares / values.Count);
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
            while (k > 1000) { x *= Pow2(1000); k -= 1000; }
            while (k < -1000) { x *= Pow2(-1000); k += 1000; }
            return x * Pow2(k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        private static void Require(IReadOnlyList<double> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (values.Count == 0) throw new ArgumentException("At least one value is required.", nameof(values));
        }
    }
}
