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

            double mean = 0;
            for (int i = 0; i < series.Count; i++) mean += series[i];
            mean /= series.Count;

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
        /// Finds the split point that best divides the series into two segments by mean, returning the index
        /// where the second segment begins and the absolute difference of the two segment means.
        /// </summary>
        public static (int Index, double Magnitude) DetectShift(IReadOnlyList<double> series)
        {
            if (series is null) throw new ArgumentNullException(nameof(series));
            if (series.Count < 2) throw new ArgumentException("At least two points are required.", nameof(series));

            int n = series.Count;
            double total = 0;
            for (int i = 0; i < n; i++) total += series[i];

            double leftSum = 0;
            int bestIndex = 1;
            double bestMagnitude = -1;
            for (int k = 1; k < n; k++)
            {
                leftSum += series[k - 1];
                double leftMean = leftSum / k;
                double rightMean = (total - leftSum) / (n - k);
                double magnitude = Math.Abs(rightMean - leftMean);
                if (magnitude > bestMagnitude)
                {
                    bestMagnitude = magnitude;
                    bestIndex = k;
                }
            }
            return (bestIndex, bestMagnitude);
        }
    }
}
