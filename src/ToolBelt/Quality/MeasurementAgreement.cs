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
            if (methodA.Count != methodB.Count) throw new ArgumentException("Both methods must have the same number of paired measurements.", nameof(methodB));
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
            var (bias, sd) = MeanSd(diffs);

            return new AgreementResult(bias, sd, bias - z * sd, bias + z * sd);
        }

        // Mean and sample standard deviation of finite data, in units of the largest magnitude p (a power of two, so
        // normal-range data give the same bits): the mean about the first value, so a constant sample has exactly that
        // mean and zero spread, and the deviations squared relative to the largest. Unscaled, the sums overflowed for
        // data near 1e308 and the squares under- or overflowed for data near 1e±160.
        private static (double Mean, double Sd) MeanSd(IReadOnlyList<double> x)
        {
            int n = x.Count;
            double big = 0;
            for (int i = 0; i < n; i++) big = Math.Max(big, Math.Abs(x[i]));
            double p = Pow2Scale(big), x0 = x[0] / p, shift = 0;
            for (int i = 0; i < n; i++) shift += x[i] / p - x0;
            double mean = x0 + shift / n;
            double max = 0;
            for (int i = 0; i < n; i++) max = Math.Max(max, Math.Abs(x[i] / p - mean));
            double s = Pow2Scale(max), ss = 0;
            for (int i = 0; i < n; i++) { double d = (x[i] / p - mean) / s; ss += d * d; }
            return (mean * p, Math.Sqrt(ss / (n - 1)) * s * p);
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
