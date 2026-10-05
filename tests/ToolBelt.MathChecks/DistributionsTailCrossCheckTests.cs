using System;
using MathNet.Numerics.Distributions;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades the Student-t, chi-square, F and log-normal functions against Math.NET. Math.NET is NOT treated as ground truth
    /// in its weak regimes — adjudicated with 40-digit mpmath when this was written, it is ~7e-13 off for the t CDF at
    /// v ≈ 1e3, returns 0 for the chi-square CDF at x ≈ 1e-25, gives NaN for the F density at d1 ≈ 1000, and its t inverse
    /// can fail to converge. Those cases are graded at Math.NET's accuracy or skipped (with a floor on how many were
    /// compared); the main suite's DistributionReferenceTests hold the tight 40-digit references. Quantiles are graded two ways:
    /// Math.NET's own CDF evaluated at ToolBelt's quantile must give back p (tight, and independent of Math.NET's
    /// root-finder tolerance), and a looser direct comparison with Math.NET's inverse.
    /// </summary>
    public sealed class DistributionsTailCrossCheckTests
    {
        private static readonly DeterministicRandom Rng = new DeterministicRandom(20261005);

        private static void Rel(double expected, double actual, double tol, string msg)
            => Check.True(Math.Abs(expected - actual) <= tol * Math.Max(Math.Abs(expected), 1e-300),
                $"{msg}: expected {expected:R}, got {actual:R} (rel {Math.Abs(expected - actual) / Math.Max(Math.Abs(expected), 1e-300):E2})");

        private static double Df() => Rng.NextDouble() < 0.3 ? Rng.Next(1, 6) : Math.Exp(Rng.NextDouble() * 9 - 1.5); // 0.2 … 1800

        // p spread log-uniformly into both tails.
        private static double P()
        {
            double tail = Math.Pow(10, -Rng.NextDouble() * 12);
            return Rng.NextDouble() < 0.5 ? tail * 0.5 : 1 - tail * 0.5;
        }

        public void StudentT_PdfCdfQuantile()
        {
            for (int t = 0; t < 800; t++)
            {
                double v = Df(), x = (Rng.NextDouble() - 0.5) * 20;
                Rel(StudentT.PDF(0, 1, v, x), Distributions.StudentTPdf(x, v), 1e-11, $"pdf v={v} x={x}");
                Check.Close(StudentT.CDF(0, 1, v, x), Distributions.StudentTCdf(x, v), 2e-11, $"cdf v={v} x={x}"); // Math.NET's own error

                double p = P();
                double q = Distributions.StudentTQuantile(p, v);
                // Grade in the lower tail (full relative precision): F(-|q|) must equal min(p, 1 − p).
                double lowerP = p < 0.5 ? p : 1 - p;
                Rel(lowerP, StudentT.CDF(0, 1, v, -Math.Abs(q)), 1e-9, $"cdf(quantile) v={v} p={p}");
            }
        }

        public void StudentT_QuantileAgreesWithMathNetInverse()
        {
            int compared = 0;
            for (int t = 0; t < 300; t++)
            {
                double v = Df(), p = Rng.NextDouble() * 0.98 + 0.01;
                double theirs;
                try { theirs = StudentT.InvCDF(0, 1, v, p); }
                catch (MathNet.Numerics.NonConvergenceException) { continue; } // Math.NET's root finder gave up
                Rel(theirs, Distributions.StudentTQuantile(p, v), 1e-8, $"v={v} p={p}");
                compared++;
            }
            Check.True(compared >= 200, $"only {compared} cases compared");
        }

        public void ChiSquare_PdfCdfQuantile()
        {
            for (int t = 0; t < 800; t++)
            {
                double k = Df(), x = Rng.NextDouble() * k * 3 + 1e-6;
                Rel(ChiSquared.PDF(k, x), Distributions.ChiSquarePdf(x, k), 1e-11, $"pdf k={k} x={x}");
                Check.Close(ChiSquared.CDF(k, x), Distributions.ChiSquareCdf(x, k), 2e-12, $"cdf k={k} x={x}"); // Math.NET ~5e-13 off at k ≈ 1e3

                double p = Rng.NextDouble() < 0.5 ? Math.Pow(10, -Rng.NextDouble() * 12) * 0.5 : Rng.NextDouble() * 0.5;
                double q = Distributions.ChiSquareQuantile(p, k);
                double theirs = ChiSquared.CDF(k, q);
                if (theirs == 0 && q < 1e-10)
                {
                    // Math.NET's chi-square CDF returns 0 for tiny x (wrong: confirmed with mpmath). Grade against the
                    // lower-tail series P(a, y) = y^a/Γ(a+1)·(1 − a·y/(a+1) + …), built from Math.NET's Gamma instead.
                    double a = k / 2, y = q / 2;
                    theirs = Math.Pow(y, a) / MathNet.Numerics.SpecialFunctions.Gamma(a + 1) * (1 - a * y / (a + 1));
                }
                Rel(p, theirs, 1e-10, $"cdf(quantile) k={k} p={p}");
            }
        }

        public void ChiSquare_QuantileAgreesWithMathNetInverse()
        {
            for (int t = 0; t < 300; t++)
            {
                double k = Df(), p = Rng.NextDouble() * 0.98 + 0.01;
                Rel(ChiSquared.InvCDF(k, p), Distributions.ChiSquareQuantile(p, k), 1e-8, $"k={k} p={p}");
            }
        }

        public void F_PdfCdfQuantile()
        {
            for (int t = 0; t < 800; t++)
            {
                double d1 = Df(), d2 = Df(), x = Math.Exp(Rng.NextDouble() * 6 - 3);
                // Math.NET's F density returns NaN or 0 for large df (wrong: confirmed with mpmath), so grade in log space
                // against Math.NET's BetaLn instead of its distribution code.
                double logPdf = d1 / 2 * Math.Log(d1 * x) + d2 / 2 * Math.Log(d2) - (d1 + d2) / 2 * Math.Log(d1 * x + d2)
                    - Math.Log(x) - MathNet.Numerics.SpecialFunctions.BetaLn(d1 / 2, d2 / 2);
                Rel(Math.Exp(logPdf), Distributions.FPdf(x, d1, d2), 1e-9, $"pdf ({d1},{d2}) x={x}");
                Check.Close(FisherSnedecor.CDF(d1, d2, x), Distributions.FCdf(x, d1, d2), 1e-12, $"cdf ({d1},{d2}) x={x}");

                double p = Rng.NextDouble() * 0.5 + 1e-9;
                double q = Distributions.FQuantile(p, d1, d2);
                Rel(p, FisherSnedecor.CDF(d1, d2, q), 1e-9, $"cdf(quantile) ({d1},{d2}) p={p}");
            }
        }

        public void LogNormal_PdfCdfQuantile()
        {
            for (int t = 0; t < 500; t++)
            {
                double mu = Rng.NextDouble() * 4 - 2, sigma = Rng.NextDouble() * 2 + 0.05, x = Math.Exp(mu + (Rng.NextDouble() - 0.5) * 6 * sigma);
                Rel(LogNormal.PDF(mu, sigma, x), Distributions.LogNormalPdf(x, mu, sigma), 1e-12, $"pdf t{t}");
                Check.Close(LogNormal.CDF(mu, sigma, x), Distributions.LogNormalCdf(x, mu, sigma), 1e-14, $"cdf t{t}");
                double p = Rng.NextDouble() * 0.98 + 0.01;
                Rel(LogNormal.InvCDF(mu, sigma, p), Distributions.LogNormalQuantile(p, mu, sigma), 1e-12, $"quantile t{t}");
            }
        }
    }
}
