// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>The result of a Mann-Kendall trend analysis.</summary>
    public readonly struct TrendResult
    {
        public TrendResult(int s, double tau, double sensSlope)
        {
            S = s;
            Tau = tau;
            SensSlope = sensSlope;
        }

        /// <summary>The Mann-Kendall S statistic (positive = increasing, negative = decreasing).</summary>
        public int S { get; }

        /// <summary>Kendall's tau in [-1, 1]: the normalized trend strength.</summary>
        public double Tau { get; }

        /// <summary>Sen's slope: the median of pairwise slopes, a robust trend rate.</summary>
        public double SensSlope { get; }
    }

    /// <summary>
    /// Non-parametric trend detection: the Mann-Kendall statistic with Sen's slope estimator. Answers
    /// "is this series slowly rising or falling?" without a human eyeballing a plot, and the slope is robust
    /// to outliers. Samples are assumed equally spaced (unit step) for Sen's slope.
    /// </summary>
    public static class Trend
    {
        public static TrendResult MannKendall(IReadOnlyList<double> series)
        {
            if (series is null) throw new ArgumentNullException(nameof(series));
            if (series.Count < 2) throw new ArgumentException("At least two points are required.", nameof(series));

            int n = series.Count;
            int s = 0;
            var slopes = new List<double>(n * (n - 1) / 2);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    s += Math.Sign(series[j] - series[i]);
                    slopes.Add((series[j] - series[i]) / (j - i));
                }

            double tau = (double)s / (n * (n - 1) / 2.0);
            slopes.Sort();
            double sens = Median(slopes);
            return new TrendResult(s, tau, sens);
        }

        private static double Median(List<double> sorted)
        {
            int m = sorted.Count;
            if (m == 0) return 0;
            return (m & 1) == 1 ? sorted[m / 2] : (sorted[m / 2 - 1] + sorted[m / 2]) / 2.0;
        }
    }
}
