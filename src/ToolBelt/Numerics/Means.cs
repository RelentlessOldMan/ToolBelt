// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// The classical Pythagorean means plus root-mean-square. Geometric and harmonic means require all
    /// values to be strictly positive; arithmetic mean and RMS accept any values. For positive data the
    /// ordering HM ≤ GM ≤ AM ≤ RMS always holds.
    /// </summary>
    public static class Means
    {
        /// <summary>Arithmetic mean (sum / n).</summary>
        public static double Arithmetic(IReadOnlyList<double> values)
        {
            Require(values);
            double sum = 0;
            for (int i = 0; i < values.Count; i++)
                sum += values[i];
            return sum / values.Count;
        }

        /// <summary>Geometric mean (n-th root of the product), computed in log space. Requires positive values.</summary>
        public static double Geometric(IReadOnlyList<double> values)
        {
            Require(values);
            double sumLog = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] <= 0)
                    throw new ArgumentException("Geometric mean requires strictly positive values.", nameof(values));
                sumLog += Math.Log(values[i]);
            }
            return Math.Exp(sumLog / values.Count);
        }

        /// <summary>Harmonic mean (n / sum of reciprocals). Requires positive values.</summary>
        public static double Harmonic(IReadOnlyList<double> values)
        {
            Require(values);
            double sumReciprocal = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] <= 0)
                    throw new ArgumentException("Harmonic mean requires strictly positive values.", nameof(values));
                sumReciprocal += 1.0 / values[i];
            }
            return values.Count / sumReciprocal;
        }

        /// <summary>Root mean square (quadratic mean).</summary>
        public static double RootMeanSquare(IReadOnlyList<double> values)
        {
            Require(values);
            double sumSquares = 0;
            for (int i = 0; i < values.Count; i++)
                sumSquares += values[i] * values[i];
            return Math.Sqrt(sumSquares / values.Count);
        }

        private static void Require(IReadOnlyList<double> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (values.Count == 0) throw new ArgumentException("At least one value is required.", nameof(values));
        }
    }
}
