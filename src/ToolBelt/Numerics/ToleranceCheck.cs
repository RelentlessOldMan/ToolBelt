// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>The outcome of evaluating a series against tolerance limits.</summary>
    public sealed class ToleranceResult
    {
        internal ToleranceResult(bool pass, int violationCount, int firstViolationIndex, int worstViolationIndex, double worstDeviation)
        {
            Pass = pass;
            ViolationCount = violationCount;
            FirstViolationIndex = firstViolationIndex;
            WorstViolationIndex = worstViolationIndex;
            WorstDeviation = worstDeviation;
        }

        public bool Pass { get; }
        public int ViolationCount { get; }
        /// <summary>Index of the first out-of-limits sample, or -1 if none.</summary>
        public int FirstViolationIndex { get; }
        /// <summary>Index of the sample furthest outside the limits, or -1 if none.</summary>
        public int WorstViolationIndex { get; }
        /// <summary>How far the worst sample was outside its limit (0 if all pass).</summary>
        public double WorstDeviation { get; }
    }

    /// <summary>
    /// Evaluates a series against lower/upper limits, reporting the count and location of violations and the
    /// worst deviation. Eliminates a mountain of ad-hoc comparison code and produces consistent, reportable
    /// output.
    /// </summary>
    public static class ToleranceCheck
    {
        public static ToleranceResult Evaluate(IReadOnlyList<double> series, double lowerLimit, double upperLimit)
        {
            if (series is null) throw new ArgumentNullException(nameof(series));
            if (lowerLimit > upperLimit) throw new ArgumentException("lowerLimit must not exceed upperLimit.");

            int count = 0, first = -1, worstIndex = -1;
            double worstDeviation = 0;
            for (int i = 0; i < series.Count; i++)
            {
                double v = series[i];
                double deviation = v < lowerLimit ? lowerLimit - v : (v > upperLimit ? v - upperLimit : 0);
                if (deviation > 0)
                {
                    count++;
                    if (first < 0) first = i;
                    if (deviation > worstDeviation) { worstDeviation = deviation; worstIndex = i; }
                }
            }
            return new ToleranceResult(count == 0, count, first, worstIndex, worstDeviation);
        }
    }
}
