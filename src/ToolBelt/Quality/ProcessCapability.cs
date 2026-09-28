// ToolBelt drop-in — fully self-contained (BCL only).
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
        /// <summary>Potential capability (spec width / 6σ).</summary>
        public double Cp { get; }
        /// <summary>Actual capability, accounting for centering.</summary>
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
    /// from the same data — conflating them is the classic mistake this API prevents.
    /// </summary>
    public static class ProcessCapability
    {
        private const double MovingRangeD2 = 1.128; // d2 for a moving range of 2 consecutive points

        public static CapabilityResult Compute(IReadOnlyList<double> sample, double lowerSpec, double upperSpec,
            SigmaEstimator estimator = SigmaEstimator.Overall)
        {
            if (sample is null) throw new ArgumentNullException(nameof(sample));
            if (sample.Count < 2) throw new ArgumentException("At least two samples are required.", nameof(sample));
            if (lowerSpec >= upperSpec) throw new ArgumentException("lowerSpec must be less than upperSpec.");

            double mean = 0;
            for (int i = 0; i < sample.Count; i++) mean += sample[i];
            mean /= sample.Count;

            double sigma = estimator == SigmaEstimator.Overall ? OverallSigma(sample, mean) : WithinSigma(sample);
            if (sigma <= 0) throw new ArgumentException("Sample has zero variation; capability is undefined.", nameof(sample));

            double cp = (upperSpec - lowerSpec) / (6 * sigma);
            double cpk = Math.Min(upperSpec - mean, mean - lowerSpec) / (3 * sigma);
            double sigmaLevel = 3 * cpk;
            double ppm = (Distributions.NormalCdf(lowerSpec, mean, sigma)
                          + (1 - Distributions.NormalCdf(upperSpec, mean, sigma))) * 1_000_000.0;

            return new CapabilityResult(mean, sigma, cp, cpk, sigmaLevel, ppm);
        }

        private static double OverallSigma(IReadOnlyList<double> sample, double mean)
        {
            double sumSq = 0;
            for (int i = 0; i < sample.Count; i++) { double d = sample[i] - mean; sumSq += d * d; }
            return Math.Sqrt(sumSq / (sample.Count - 1));
        }

        private static double WithinSigma(IReadOnlyList<double> sample)
        {
            double sumRange = 0;
            for (int i = 1; i < sample.Count; i++) sumRange += Math.Abs(sample[i] - sample[i - 1]);
            double meanRange = sumRange / (sample.Count - 1);
            return meanRange / MovingRangeD2;
        }
    }
}
