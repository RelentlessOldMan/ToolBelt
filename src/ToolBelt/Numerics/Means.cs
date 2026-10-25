// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// The classical Pythagorean means plus root-mean-square. Geometric and harmonic means require all
    /// values to be strictly positive (NaN is rejected); arithmetic mean and RMS accept any values. Each is
    /// computed so that it does not overflow or underflow when the mean itself is representable (1e308 values,
    /// subnormals). For positive data the ordering HM ≤ GM ≤ AM ≤ RMS always holds.
    /// </summary>
    public static class Means
    {
        /// <summary>Arithmetic mean (sum / n).</summary>
        public static double Arithmetic(IReadOnlyList<double> values)
        {
            Require(values);
            double sum = 0;
            bool anyInfinite = false;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
                if (double.IsInfinity(values[i])) anyInfinite = true;
            }
            if (double.IsInfinity(sum) && !anyInfinite)
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
            double sumLog = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (!(values[i] > 0))
                    throw new ArgumentException("Geometric mean requires strictly positive values.", nameof(values));
                sumLog += Math.Log(values[i]);
            }
            return Math.Exp(sumLog / values.Count);
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

        private static void Require(IReadOnlyList<double> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (values.Count == 0) throw new ArgumentException("At least one value is required.", nameof(values));
        }
    }
}
