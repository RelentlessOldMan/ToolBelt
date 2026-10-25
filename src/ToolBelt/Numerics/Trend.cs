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

        /// <summary>Kendall's tau-a in [-1, 1]: S over the number of pairs, n(n − 1)/2 (ties are not corrected for).</summary>
        public double Tau { get; }

        /// <summary>Sen's slope: the median of pairwise slopes, a robust trend rate.</summary>
        public double SensSlope { get; }
    }

    /// <summary>
    /// Non-parametric trend detection: the Mann-Kendall statistic with Sen's slope estimator. Answers
    /// "is this series slowly rising or falling?" without a human eyeballing a plot, and the slope is robust
    /// to outliers. Samples are assumed equally spaced (unit step) for Sen's slope.
    /// Sen's slope is the exact median of all n(n − 1)/2 pairwise slopes, as other implementations compute it, so time and
    /// memory grow as n²: 10,000 points hold 50 million slopes (400 MB). At most <see cref="MaxPoints"/> points.
    /// </summary>
    public static class Trend
    {
        /// <summary>The largest series accepted: its n(n − 1)/2 pairwise slopes must fit in one array.</summary>
        public const int MaxPoints = 65536;

        /// <summary>Mann-Kendall S, tau and Sen's slope of <paramref name="series"/> (at least 2 finite values).</summary>
        public static TrendResult MannKendall(IReadOnlyList<double> series)
        {
            if (series is null) throw new ArgumentNullException(nameof(series));
            if (series.Count < 2) throw new ArgumentException("At least two points are required.", nameof(series));
            int n = series.Count;
            if (n > MaxPoints)
                throw new ArgumentException($"Sen's slope needs all n(n − 1)/2 pairwise slopes; at most {MaxPoints} points are supported.", nameof(series));
            for (int i = 0; i < n; i++)
                if (double.IsNaN(series[i]) || double.IsInfinity(series[i]))
                    throw new ArgumentException($"Value [{i}] is not finite.", nameof(series));

            long pairs = (long)n * (n - 1) / 2;   // <= 2,147,450,880 at MaxPoints, so |S| fits an int
            var slopes = new double[pairs];
            long s = 0, at = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double d = series[j] - series[i];
                    s += d > 0 ? 1 : d < 0 ? -1 : 0;
                    // A difference past double.MaxValue (values of opposite sign near 1e308) keeps its sign for S; the slope
                    // comes from the halves, which are exact, so it is ∞ only when the slope itself overflows.
                    slopes[at++] = double.IsInfinity(d) ? (series[j] / 2 - series[i] / 2) / ((j - i) / 2.0) : d / (j - i);
                }

            double tau = s / (double)pairs;
            Array.Sort(slopes);
            double sens = Median(slopes);
            return new TrendResult((int)s, tau, sens);
        }

        private static double Median(double[] sorted)
        {
            long m = sorted.LongLength;
            return (m & 1) == 1 ? sorted[m / 2] : 0.5 * sorted[m / 2 - 1] + 0.5 * sorted[m / 2];
        }
    }
}
