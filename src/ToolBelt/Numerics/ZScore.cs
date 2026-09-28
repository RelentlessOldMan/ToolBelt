// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Standard-score (z-score) helpers: expresses a value as the number of standard deviations it lies
    /// from the mean. When the standard deviation is zero (all values equal), every score is defined as
    /// zero rather than NaN.
    /// </summary>
    public static class ZScore
    {
        /// <summary>The z-score of a single value given a mean and standard deviation.</summary>
        public static double Standardize(double value, double mean, double stdDev)
            => stdDev == 0 ? 0.0 : (value - mean) / stdDev;

        /// <summary>
        /// Standardizes a whole data set: computes the mean and standard deviation (population by default,
        /// sample when <paramref name="sample"/> is true) and returns each value's z-score.
        /// </summary>
        public static double[] Standardize(IReadOnlyList<double> data, bool sample = false)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Count == 0) throw new ArgumentException("Data must not be empty.", nameof(data));
            if (sample && data.Count < 2)
                throw new ArgumentException("Sample standardization needs at least two values.", nameof(data));

            double mean = 0;
            for (int i = 0; i < data.Count; i++)
                mean += data[i];
            mean /= data.Count;

            double sumSq = 0;
            for (int i = 0; i < data.Count; i++)
            {
                double d = data[i] - mean;
                sumSq += d * d;
            }
            double variance = sumSq / (sample ? data.Count - 1 : data.Count);
            double stdDev = Math.Sqrt(variance);

            var result = new double[data.Count];
            for (int i = 0; i < data.Count; i++)
                result[i] = Standardize(data[i], mean, stdDev);
            return result;
        }
    }
}
