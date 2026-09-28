using System;
using MathNet.Numerics.Distributions;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades ToolBelt's closed-form distribution functions against Math.NET. The interesting ones are the
    /// tails and the inverse-CDF (quantile), where a hand-rolled rational approximation can drift.
    /// </summary>
    public sealed class DistributionsCrossCheckTests
    {
        public void NormalPdfCdf_MatchMathNet()
        {
            var rng = new Random(10);
            for (int t = 0; t < 500; t++)
            {
                double mean = rng.NextDouble() * 10 - 5;
                double sd = rng.NextDouble() * 3 + 0.1;
                double x = mean + (rng.NextDouble() * 8 - 4) * sd;
                Check.Close(Normal.PDF(mean, sd, x), Distributions.NormalPdf(x, mean, sd), 1e-10, $"pdf t{t}");
                // ToolBelt's CDF uses an Abramowitz–Stegun erf approximation (max error ~1.5e-7) rather
                // than Math.NET's exact erf; this cross-check pins that documented accuracy grade.
                Check.Close(Normal.CDF(mean, sd, x), Distributions.NormalCdf(x, mean, sd), 2e-7, $"cdf t{t}");
            }
        }

        public void NormalQuantile_MatchesMathNet()
        {
            var rng = new Random(11);
            for (int t = 0; t < 500; t++)
            {
                double p = rng.NextDouble() * 0.998 + 0.001; // avoid the exact 0/1 asymptotes
                double mean = rng.NextDouble() * 4 - 2;
                double sd = rng.NextDouble() * 2 + 0.2;
                Check.Close(Normal.InvCDF(mean, sd, p), Distributions.NormalQuantile(p, mean, sd), 1e-6, $"quantile t{t} p={p}");
            }
        }

        public void NormalQuantile_DeepTails()
        {
            foreach (double p in new[] { 1e-6, 1e-4, 0.01, 0.99, 0.9999, 1 - 1e-6 })
                Check.Close(Normal.InvCDF(0, 1, p), Distributions.NormalQuantile(p), 1e-5, $"p={p}");
        }

        public void Exponential_MatchesMathNet()
        {
            var rng = new Random(12);
            for (int t = 0; t < 300; t++)
            {
                double rate = rng.NextDouble() * 3 + 0.1;
                double x = rng.NextDouble() * 10;
                Check.Close(Exponential.PDF(rate, x), Distributions.ExponentialPdf(x, rate), 1e-10, $"pdf t{t}");
                Check.Close(Exponential.CDF(rate, x), Distributions.ExponentialCdf(x, rate), 1e-10, $"cdf t{t}");

                double p = rng.NextDouble() * 0.99 + 0.001;
                Check.Close(Exponential.InvCDF(rate, p), Distributions.ExponentialQuantile(p, rate), 1e-9, $"quantile t{t}");
            }
        }
    }
}
