// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Detects a step change in the level of a series — where did it shift? Complements peak detection,
    /// which finds peaks in value rather than shifts in level (a different question). Provides the CUSUM
    /// curve and a single-split binary-segmentation finder.
    /// </summary>
    public static class ChangePoint
    {
        /// <summary>Cumulative sum of deviations from the overall mean; a sustained slope marks a level shift.</summary>
        public static double[] Cusum(IReadOnlyList<double> series)
        {
            if (series is null) throw new ArgumentNullException(nameof(series));
            if (series.Count == 0) return Array.Empty<double>();

            // The mean in units of the largest magnitude (a power of two): summed outright it overflowed for data near 1e308.
            double big = 0;
            for (int i = 0; i < series.Count; i++) big = Math.Max(big, Math.Abs(series[i]));
            double p = Pow2Scale(big), mean = 0;
            for (int i = 0; i < series.Count; i++) mean += series[i] / p;
            mean = mean / series.Count * p;

            var cusum = new double[series.Count];
            double running = 0;
            for (int i = 0; i < series.Count; i++)
            {
                running += series[i] - mean;
                cusum[i] = running;
            }
            return cusum;
        }

        /// <summary>
        /// Finds the single split that best divides the series into two segments by mean — the one that minimises the two
        /// segments' total squared error, equivalently maximises k(n − k)/n · Δ² — returning the index where the second
        /// segment begins and the absolute difference Δ of the two segment means. (Maximising |Δ| alone favours splits
        /// that cut off one or two points at the ends.) Values must be finite.
        /// </summary>
        public static (int Index, double Magnitude) DetectShift(IReadOnlyList<double> series)
        {
            if (series is null) throw new ArgumentNullException(nameof(series));
            if (series.Count < 2) throw new ArgumentException("At least two points are required.", nameof(series));

            int n = series.Count;
            double big = 0;
            for (int i = 0; i < n; i++)
            {
                double v = series[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException($"Value [{i}] is not finite.", nameof(series));
                big = Math.Max(big, Math.Abs(v));
            }
            // Sums in units of the largest magnitude p (a power of two, so normal-range data give the same bits): summed
            // outright they overflowed for data near 1e308, and every split scored ∞ (the first one won).
            double p = Pow2Scale(big), total = 0;
            for (int i = 0; i < n; i++) total += series[i] / p;

            double leftSum = 0;
            int bestIndex = 1;
            double bestScore = -1, bestMagnitude = 0;
            for (int k = 1; k < n; k++)
            {
                leftSum += series[k - 1] / p;
                double leftMean = leftSum / k;
                double rightMean = (total - leftSum) / (n - k);
                double magnitude = Math.Abs(rightMean - leftMean);
                // The reduction in squared error from splitting at k is k(n − k)/n · Δ²; its square root ranks the same
                // splits without squaring Δ, which underflowed to a tie of zeros for data near 1e-160.
                double score = Math.Sqrt((double)k * (n - k) / n) * magnitude;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = k;
                    bestMagnitude = magnitude;
                }
            }
            return (bestIndex, bestMagnitude * p);
        }

        // The power of two at or below m (1 for 0): dividing by it is exact, so data in the normal range give the same
        // bits, while data near 1e±308 neither over- nor underflow when summed or squared.
        private static double Pow2Scale(double m)
        {
            if (m == 0 || double.IsInfinity(m)) return 1;
            double p = Math.Pow(2, Math.Floor(Math.Log(m) / Math.Log(2)));
            // The logarithm can land a hair either side of an integer; settle on exactly the power at or below m.
            while (p > m) p /= 2;
            while (p * 2 <= m) p *= 2;
            return p;
        }
    }
}
