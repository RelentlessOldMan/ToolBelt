// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Quality
{
    /// <summary>The center line and control limits of a control chart.</summary>
    public sealed class ControlLimits
    {
        internal ControlLimits(double centerLine, double lower, double upper)
        {
            CenterLine = centerLine;
            LowerControlLimit = lower;
            UpperControlLimit = upper;
        }

        public double CenterLine { get; }
        public double LowerControlLimit { get; }
        public double UpperControlLimit { get; }
    }

    /// <summary>
    /// Individuals (I-MR) control-chart limits plus the classic run rules expressed as pure predicates that
    /// return the offending indices. Each rule is independently testable; the limits and rules feed directly
    /// into a plot as annotated lines.
    /// </summary>
    public static class ControlChart
    {
        private const double IndividualsSigmaFactor = 2.66; // 3 / d2 for a moving range of 2

        /// <summary>Center line (mean) and ±3σ limits estimated from the average moving range.</summary>
        public static ControlLimits Individuals(IReadOnlyList<double> data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Count < 2) throw new ArgumentException("At least two points are required.", nameof(data));

            double mean = 0;
            for (int i = 0; i < data.Count; i++) mean += data[i];
            mean /= data.Count;

            double sumRange = 0;
            for (int i = 1; i < data.Count; i++) sumRange += Math.Abs(data[i] - data[i - 1]);
            double meanRange = sumRange / (data.Count - 1);

            double spread = IndividualsSigmaFactor * meanRange;
            return new ControlLimits(mean, mean - spread, mean + spread);
        }

        /// <summary>Rule 1: indices of points beyond the control limits.</summary>
        public static IReadOnlyList<int> PointsBeyondLimits(IReadOnlyList<double> data, ControlLimits limits)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (limits is null) throw new ArgumentNullException(nameof(limits));
            var hits = new List<int>();
            for (int i = 0; i < data.Count; i++)
                if (data[i] > limits.UpperControlLimit || data[i] < limits.LowerControlLimit)
                    hits.Add(i);
            return hits;
        }

        /// <summary>Rule 2: index ending each run of <paramref name="runLength"/> points all on one side of the center line.</summary>
        public static IReadOnlyList<int> RunsOnOneSide(IReadOnlyList<double> data, double centerLine, int runLength = 9)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (runLength < 1) throw new ArgumentOutOfRangeException(nameof(runLength), runLength, "Run length must be positive.");
            var hits = new List<int>();
            for (int i = runLength - 1; i < data.Count; i++)
            {
                bool allAbove = true, allBelow = true;
                for (int k = i - runLength + 1; k <= i; k++)
                {
                    if (data[k] <= centerLine) allAbove = false;
                    if (data[k] >= centerLine) allBelow = false;
                }
                if (allAbove || allBelow) hits.Add(i);
            }
            return hits;
        }

        /// <summary>Rule 3: index ending each monotonic run (steadily increasing or decreasing) of <paramref name="length"/> points.</summary>
        public static IReadOnlyList<int> Trends(IReadOnlyList<double> data, int length = 6)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (length < 2) throw new ArgumentOutOfRangeException(nameof(length), length, "Trend length must be at least 2.");
            var hits = new List<int>();
            for (int i = length - 1; i < data.Count; i++)
            {
                bool increasing = true, decreasing = true;
                for (int k = i - length + 2; k <= i; k++)
                {
                    if (data[k] <= data[k - 1]) increasing = false;
                    if (data[k] >= data[k - 1]) decreasing = false;
                }
                if (increasing || decreasing) hits.Add(i);
            }
            return hits;
        }
    }
}
