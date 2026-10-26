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
    /// into a plot as annotated lines. Every value must be finite: a NaN compares false both ways, so it would
    /// count as both above and below the center line and complete runs and trends it is not part of.
    /// </summary>
    public static class ControlChart
    {
        private const double IndividualsSigmaFactor = 2.66; // 3 / d2 for a moving range of 2

        /// <summary>Center line (mean) and ±3σ limits estimated from the average moving range.</summary>
        public static ControlLimits Individuals(IReadOnlyList<double> data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Count < 2) throw new ArgumentException("At least two points are required.", nameof(data));
            RequireFinite(data);

            // In units of the largest magnitude p (a power of two, so normal-range data give the same bits): the sums
            // overflowed for data near 1e308 and every limit came out infinite.
            double big = 0;
            for (int i = 0; i < data.Count; i++) big = Math.Max(big, Math.Abs(data[i]));
            double p = Pow2Scale(big);

            // Mean about the first value: constant data gives exactly that value, not one an ulp off — with a zero
            // moving range the limits collapse onto the mean, and an ulp would put every point beyond them.
            double x0 = data[0] / p, sum = 0;
            for (int i = 0; i < data.Count; i++) sum += data[i] / p - x0;
            double mean = (x0 + sum / data.Count) * p;

            double sumRange = 0;
            for (int i = 1; i < data.Count; i++) sumRange += Math.Abs(data[i] / p - data[i - 1] / p);
            double meanRange = sumRange / (data.Count - 1) * p;

            double spread = IndividualsSigmaFactor * meanRange;
            return new ControlLimits(mean, mean - spread, mean + spread);
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

        /// <summary>Rule 1: indices of points beyond the control limits.</summary>
        public static IReadOnlyList<int> PointsBeyondLimits(IReadOnlyList<double> data, ControlLimits limits)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (limits is null) throw new ArgumentNullException(nameof(limits));
            RequireFinite(data);
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
            if (double.IsNaN(centerLine) || double.IsInfinity(centerLine))
                throw new ArgumentOutOfRangeException(nameof(centerLine), centerLine, "Center line must be finite.");
            RequireFinite(data);
            // Lengths of the current runs above and below (a point on the line ends both); one pass, O(n).
            var hits = new List<int>();
            int above = 0, below = 0;
            for (int i = 0; i < data.Count; i++)
            {
                above = data[i] > centerLine ? above + 1 : 0;
                below = data[i] < centerLine ? below + 1 : 0;
                if (above >= runLength || below >= runLength) hits.Add(i);
            }
            return hits;
        }

        /// <summary>Rule 3: index ending each monotonic run (steadily increasing or decreasing) of <paramref name="length"/> points.</summary>
        public static IReadOnlyList<int> Trends(IReadOnlyList<double> data, int length = 6)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (length < 2) throw new ArgumentOutOfRangeException(nameof(length), length, "Trend length must be at least 2.");
            RequireFinite(data);
            // Counts of consecutive rising and falling steps (a tie ends both); length points take length − 1 steps.
            var hits = new List<int>();
            int rising = 0, falling = 0;
            for (int i = 1; i < data.Count; i++)
            {
                rising = data[i] > data[i - 1] ? rising + 1 : 0;
                falling = data[i] < data[i - 1] ? falling + 1 : 0;
                if (rising >= length - 1 || falling >= length - 1) hits.Add(i);
            }
            return hits;
        }

        private static void RequireFinite(IReadOnlyList<double> data)
        {
            for (int i = 0; i < data.Count; i++)
                if (double.IsNaN(data[i]) || double.IsInfinity(data[i]))
                    throw new ArgumentException($"Value [{i}] is not finite.", nameof(data));
        }
    }
}
