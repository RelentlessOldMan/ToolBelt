// ToolBelt drop-in — also copy Numerics/Distributions.cs (normal tails for PPM).
using System;
using System.Collections.Generic;
using ToolBelt.Numerics;

namespace ToolBelt.Quality
{
    /// <summary>How the process standard deviation is estimated — the choice that yields two different
    /// (and often confused) answers from the same data.</summary>
    public enum SigmaEstimator
    {
        /// <summary>Overall sample standard deviation (long-term variation) — pairs with Pp/Ppk.</summary>
        Overall,
        /// <summary>Short-term within-subgroup sigma from the average moving range — pairs with Cp/Cpk.</summary>
        WithinSubgroup,
    }

    /// <summary>Process capability indices and derived metrics.</summary>
    public sealed class CapabilityResult
    {
        internal CapabilityResult(double mean, double sigma, double cp, double cpk, double sigmaLevel, double ppmDefective)
        {
            Mean = mean; Sigma = sigma; Cp = cp; Cpk = cpk; SigmaLevel = sigmaLevel; PpmDefective = ppmDefective;
        }

        public double Mean { get; }
        public double Sigma { get; }
        /// <summary>Potential capability (spec width / 6σ). With <see cref="SigmaEstimator.Overall"/> this is Pp.</summary>
        public double Cp { get; }
        /// <summary>Actual capability, accounting for centering. With <see cref="SigmaEstimator.Overall"/> this is Ppk.</summary>
        public double Cpk { get; }
        /// <summary>Process sigma level (Z_min = 3·Cpk).</summary>
        public double SigmaLevel { get; }
        /// <summary>Expected parts-per-million outside the spec, under a normal model.</summary>
        public double PpmDefective { get; }
    }

    /// <summary>
    /// Computes the standard process-capability indices plus sigma level and parts-per-million defective from
    /// a sample and its specification limits. The sigma estimator is an explicit parameter because
    /// within-subgroup (short-term, Cp/Cpk) and overall (long-term, Pp/Ppk) sigma give different answers
    /// from the same data — conflating them is the classic mistake this API prevents. The result's <c>Cp</c>/<c>Cpk</c>
    /// are Pp/Ppk when the estimator is <see cref="SigmaEstimator.Overall"/>. Sample values must be finite; for a
    /// one-sided spec pass <c>double.NegativeInfinity</c> or <c>double.PositiveInfinity</c> for the missing limit.
    /// </summary>
    public static class ProcessCapability
    {
        private const double MovingRangeD2 = 1.128; // d2 for a moving range of 2 consecutive points

        public static CapabilityResult Compute(IReadOnlyList<double> sample, double lowerSpec, double upperSpec,
            SigmaEstimator estimator = SigmaEstimator.Overall)
        {
            if (sample is null) throw new ArgumentNullException(nameof(sample));
            if (sample.Count < 2) throw new ArgumentException("At least two samples are required.", nameof(sample));
            if (double.IsNaN(lowerSpec)) throw new ArgumentOutOfRangeException(nameof(lowerSpec), lowerSpec, "Spec limit is NaN.");
            if (double.IsNaN(upperSpec)) throw new ArgumentOutOfRangeException(nameof(upperSpec), upperSpec, "Spec limit is NaN.");
            if (lowerSpec >= upperSpec) throw new ArgumentException("lowerSpec must be less than upperSpec.", nameof(upperSpec));
            for (int i = 0; i < sample.Count; i++)
                if (double.IsNaN(sample[i]) || double.IsInfinity(sample[i]))
                    throw new ArgumentException($"Value [{i}] is not finite.", nameof(sample));

            // In units of the largest magnitude p (a power of two, so normal-range data give the same bits): the squares
            // underflowed for data near 1e-160 ("zero variation") and overflowed near 1e+160.
            double big = 0;
            for (int i = 0; i < sample.Count; i++) big = Math.Max(big, Math.Abs(sample[i]));
            double p = Pow2Scale(big);

            // Mean about the first value, so a constant sample has exactly zero spread (not an ulp of round-off,
            // which gave Cp ≈ 1e16 instead of "zero variation").
            double x0 = sample[0] / p, sum = 0;
            for (int i = 0; i < sample.Count; i++) sum += sample[i] / p - x0;
            double scaledMean = x0 + sum / sample.Count, mean = scaledMean * p;

            double sigma = (estimator == SigmaEstimator.Overall ? OverallSigma(sample, p, scaledMean) : WithinSigma(sample, p)) * p;
            if (!(sigma > 0)) throw new ArgumentException("Sample has zero variation; capability is undefined.", nameof(sample));
            if (double.IsInfinity(sigma)) throw new ArgumentException("Sample spread overflows a double.", nameof(sample));

            double cp = (upperSpec - lowerSpec) / (6 * sigma);
            double cpk = Math.Min(upperSpec - mean, mean - lowerSpec) / (3 * sigma);
            double sigmaLevel = 3 * cpk;
            // Upper tail from the survival function: 1 − Φ rounds to 0 below about 1e-16 (and to multiples of 1e-16 above).
            double ppm = (Distributions.NormalCdf(lowerSpec, mean, sigma)
                          + Distributions.NormalSurvival(upperSpec, mean, sigma)) * 1_000_000.0;

            return new CapabilityResult(mean, sigma, cp, cpk, sigmaLevel, ppm);
        }

        // Both in units of p.
        private static double OverallSigma(IReadOnlyList<double> sample, double p, double mean)
        {
            double sumSq = 0;
            for (int i = 0; i < sample.Count; i++) { double d = sample[i] / p - mean; sumSq += d * d; }
            return Math.Sqrt(sumSq / (sample.Count - 1));
        }

        private static double WithinSigma(IReadOnlyList<double> sample, double p)
        {
            double sumRange = 0;
            for (int i = 1; i < sample.Count; i++) sumRange += Math.Abs(sample[i] / p - sample[i - 1] / p);
            double meanRange = sumRange / (sample.Count - 1);
            return meanRange / MovingRangeD2;
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
