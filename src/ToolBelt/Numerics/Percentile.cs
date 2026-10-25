// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Computes percentiles of a data set using linear interpolation between closest ranks (the
    /// inclusive "type 7" method used by NumPy and Excel's PERCENTILE.INC). The input is never mutated —
    /// a sorted copy is taken internally. NaN values are rejected (they sorted first and silently shifted
    /// every rank); ±infinity is allowed.
    /// </summary>
    public static class Percentile
    {
        /// <summary>The <paramref name="percentile"/>-th percentile (0..100) of <paramref name="data"/>.</summary>
        public static double Compute(IReadOnlyList<double> data, double percentile)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Count == 0) throw new ArgumentException("Data must not be empty.", nameof(data));
            if (double.IsNaN(percentile) || percentile < 0 || percentile > 100)
                throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "Percentile must be in [0, 100].");

            var sorted = new double[data.Count];
            for (int i = 0; i < data.Count; i++)
            {
                sorted[i] = data[i];
                if (double.IsNaN(sorted[i])) throw new ArgumentException($"Value [{i}] is NaN.", nameof(data));
            }
            Array.Sort(sorted);

            if (sorted.Length == 1)
                return sorted[0];

            double rank = percentile / 100.0 * (sorted.Length - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            if (lo == hi)
                return sorted[lo];

            double frac = rank - lo;
            double a = sorted[lo], b = sorted[hi];
            if (a == b) return a;                         // also ∞ and ∞, where b − a is NaN
            if (double.IsInfinity(a) && double.IsInfinity(b)) return frac < 0.5 ? a : b;   // −∞ to +∞, as EmpiricalDistribution
            double d = b - a;
            return double.IsInfinity(d) ? (1 - frac) * a + frac * b : a + frac * d; // d overflows for ±1e308
        }

        /// <summary>The median (50th percentile).</summary>
        public static double Median(IReadOnlyList<double> data) => Compute(data, 50);

        /// <summary>The <paramref name="quartile"/>-th quartile: 1 → 25th, 2 → 50th, 3 → 75th percentile.</summary>
        public static double Quartile(IReadOnlyList<double> data, int quartile)
        {
            if (quartile < 1 || quartile > 3)
                throw new ArgumentOutOfRangeException(nameof(quartile), quartile, "Quartile must be 1, 2, or 3.");
            return Compute(data, quartile * 25.0);
        }
    }
}
