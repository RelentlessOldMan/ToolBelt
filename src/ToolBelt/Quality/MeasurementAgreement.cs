// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Quality
{
    /// <summary>Bland-Altman agreement between two measurement methods.</summary>
    public sealed class AgreementResult
    {
        internal AgreementResult(double bias, double stdDevOfDifferences, double lowerLimit, double upperLimit)
        {
            Bias = bias;
            StdDevOfDifferences = stdDevOfDifferences;
            LowerLimit = lowerLimit;
            UpperLimit = upperLimit;
        }

        /// <summary>Mean difference (method A − method B) — the systematic bias between methods.</summary>
        public double Bias { get; }
        public double StdDevOfDifferences { get; }
        /// <summary>Lower limit of agreement (bias − z·sd).</summary>
        public double LowerLimit { get; }
        /// <summary>Upper limit of agreement (bias + z·sd).</summary>
        public double UpperLimit { get; }
    }

    /// <summary>
    /// Compares two measurement methods via the Bland-Altman limits of agreement: the mean difference
    /// (bias) and the interval within which most differences lie. The right tool for "do these two setups
    /// agree?" — generic statistics with no domain content. Values must be finite and z (1.96 for 95%) positive.
    /// </summary>
    public static class MeasurementAgreement
    {
        public static AgreementResult BlandAltman(IReadOnlyList<double> methodA, IReadOnlyList<double> methodB, double z = 1.96)
        {
            if (methodA is null) throw new ArgumentNullException(nameof(methodA));
            if (methodB is null) throw new ArgumentNullException(nameof(methodB));
            if (methodA.Count != methodB.Count) throw new ArgumentException("Both methods must have the same number of paired measurements.");
            if (methodA.Count < 2) throw new ArgumentException("At least two paired measurements are required.", nameof(methodA));
            if (!(z > 0) || double.IsInfinity(z)) throw new ArgumentOutOfRangeException(nameof(z), z, "z must be positive and finite.");

            int n = methodA.Count;
            var diffs = new double[n];
            for (int i = 0; i < n; i++)
            {
                diffs[i] = methodA[i] - methodB[i];
                if (double.IsNaN(diffs[i]) || double.IsInfinity(diffs[i]))
                    throw new ArgumentException($"Pair [{i}] has a non-finite value or difference.", nameof(methodA));
            }
            // Mean about the first difference: a constant offset gives exactly that bias and a zero spread.
            double sum = 0;
            for (int i = 0; i < n; i++) sum += diffs[i] - diffs[0];
            double bias = diffs[0] + sum / n;

            double variance = 0;
            for (int i = 0; i < n; i++) { double d = diffs[i] - bias; variance += d * d; }
            variance /= n - 1;
            double sd = Math.Sqrt(variance);

            return new AgreementResult(bias, sd, bias - z * sd, bias + z * sd);
        }
    }
}
