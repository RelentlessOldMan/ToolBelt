using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>
    /// Student-t, chi-square, F and log-normal, plus the full-precision normal. Graded against closed forms (t with 1 and
    /// 2 df, chi-square with 2 df), cross-distribution identities, round trips deep into both tails, and short textbook
    /// table values only — never long constants recalled from memory.
    /// </summary>
    public sealed class DistributionsTailTests
    {
        private static readonly double[] TailPs = { 1e-12, 1e-8, 1e-4, 0.01, 0.1, 0.3, 0.5, 0.7, 0.9, 0.99, 1 - 1e-4, 1 - 1e-8 };

        private static void Rel(double expected, double actual, double tol, string msg)
            => Check.True(Math.Abs(expected - actual) <= tol * Math.Max(Math.Abs(expected), 1e-300), $"{msg}: expected {expected:R}, got {actual:R}");

        // ---------- normal (upgraded to full precision) ----------

        public void Normal_FullPrecisionAndAccurateTails()
        {
            // Φ(-z) = ½·erfc(z/√2); erfc via the identity Φ(-z) + Φ(z) = 1 checked in the far tail by symmetry of the quantile.
            Check.Close(0.5, Distributions.NormalCdf(0), 1e-16);
            Check.Close(Distributions.NormalCdf(-1.3) + Distributions.NormalCdf(1.3), 1.0, 1e-15);
            foreach (double p in TailPs)
            {
                double z = Distributions.NormalQuantile(p);
                if (p < 0.5) Rel(p, Distributions.NormalCdf(z), 1e-13, $"lower round trip p={p}");
                // Mirror an exactly representable pair: 1 − (1 − p) is exact when 1 − p >= ½, p itself may not be.
                double upper = 1 - p, lower = 1 - upper;
                Check.Close(-Distributions.NormalQuantile(lower), Distributions.NormalQuantile(upper), 1e-13 * Math.Max(1, Math.Abs(z)), $"symmetry p={p}");
            }
            Check.Close(1.959964, Distributions.NormalQuantile(0.975), 5e-7);      // textbook
            Check.Close(2.575829, Distributions.NormalQuantile(0.995), 5e-7);
        }

        // ---------- Student's t ----------

        public void StudentT_OneDfIsCauchy()
        {
            foreach (double x in new[] { -50, -3.2, -1, -0.1, 0, 0.4, 2, 1e6 })
                Check.Close(0.5 + Math.Atan(x) / Math.PI, Distributions.StudentTCdf(x, 1), 1e-14, $"cdf x={x}");
            foreach (double p in TailPs)
            {
                // tan(π(p − ½)) is ill-conditioned in both tails; the cotangent forms are exact to rounding.
                double expected = p < 0.5 ? -1 / Math.Tan(Math.PI * p) : (p > 0.5 ? 1 / Math.Tan(Math.PI * (1 - p)) : 0);
                Rel(expected, Distributions.StudentTQuantile(p, 1), 1e-11, $"quantile p={p}");
            }
            Check.Close(1 / (Math.PI * 2), Distributions.StudentTPdf(1, 1), 1e-15);
        }

        public void StudentT_TwoDfClosedForm()
        {
            foreach (double x in new[] { -10, -1, 0, 0.5, 3, 100 })
                Check.Close(0.5 + x / (2 * Math.Sqrt(2 + x * x)), Distributions.StudentTCdf(x, 2), 1e-14, $"cdf x={x}");
            foreach (double p in new[] { 1e-10, 0.05, 0.8, 0.999 })
                Rel((2 * p - 1) / Math.Sqrt(2 * p * (1 - p)), Distributions.StudentTQuantile(p, 2), 1e-11, $"quantile p={p}");
            Check.Equal(0.0, Distributions.StudentTQuantile(0.5, 2));
        }

        public void StudentT_TableValuesAndNormalLimit()
        {
            Check.Close(12.706205, Distributions.StudentTQuantile(0.975, 1), 5e-6);
            Check.Close(2.228139, Distributions.StudentTQuantile(0.975, 10), 5e-6);
            Check.Close(2.042272, Distributions.StudentTQuantile(0.975, 30), 5e-6);
            Check.Close(-2.570582, Distributions.StudentTQuantile(0.025, 5), 5e-6);
            // Huge df tends to the normal.
            Check.Close(Distributions.NormalQuantile(0.975), Distributions.StudentTQuantile(0.975, 1e9), 1e-8);
        }

        public void StudentT_RoundTripsDeepIntoBothTails()
        {
            foreach (double v in new[] { 0.5, 1, 2.5, 5, 30, 1000 })
                foreach (double p in TailPs)
                {
                    double t = Distributions.StudentTQuantile(p, v);
                    // Compare in the lower tail where the CDF has full relative precision (symmetry covers the upper).
                    double lowerP = p < 0.5 ? p : 1 - p;
                    double lowerT = p < 0.5 ? t : -t;
                    Rel(lowerP, Distributions.StudentTCdf(lowerT, v), 1e-11, $"v={v} p={p}");
                }
        }

        public void StudentT_PdfIntegratesToCdf()
        {
            // Trapezoid integral of the pdf from -8 to 1.5 should match CDF(1.5) − CDF(-8).
            double v = 4, a = -8, b = 1.5, sum = 0;
            int n = 20000;
            for (int i = 0; i <= n; i++)
            {
                double x = a + (b - a) * i / n;
                sum += (i == 0 || i == n ? 0.5 : 1) * Distributions.StudentTPdf(x, v);
            }
            Check.Close(Distributions.StudentTCdf(b, v) - Distributions.StudentTCdf(a, v), sum * (b - a) / n, 1e-8);
        }

        // ---------- chi-square ----------

        public void ChiSquare_TwoDfIsExponential()
        {
            foreach (double x in new[] { 1e-8, 0.3, 2, 10, 60 })
                Check.Close(1 - Math.Exp(-x / 2), Distributions.ChiSquareCdf(x, 2), 1e-14, $"cdf x={x}");
            foreach (double p in TailPs)
            {
                // −2·ln(1 − p): for small p use the series (1 − p rounds, and .NET's LogP1 is naive Log(1 + x)).
                double expected = p < 1e-3 ? 2 * (p + p * p / 2 + p * p * p / 3 + p * p * p * p / 4) : -2 * Math.Log(1 - p);
                Rel(expected, Distributions.ChiSquareQuantile(p, 2), 1e-11, $"quantile p={p}");
            }
            Check.Close(0.5, Distributions.ChiSquarePdf(0, 2), 0);
        }

        public void ChiSquare_OneDfIsSquaredNormal()
        {
            foreach (double p in new[] { 0.05, 0.5, 0.95, 0.999 })
            {
                double z = Distributions.NormalQuantile((1 + p) / 2);
                Rel(z * z, Distributions.ChiSquareQuantile(p, 1), 1e-10, $"p={p}");
            }
        }

        public void ChiSquare_TableValuesAndRoundTrips()
        {
            Check.Close(3.841459, Distributions.ChiSquareQuantile(0.95, 1), 5e-6);
            Check.Close(5.991465, Distributions.ChiSquareQuantile(0.95, 2), 5e-6);
            Check.Close(18.307038, Distributions.ChiSquareQuantile(0.95, 10), 5e-6);
            Check.Close(0.215795, Distributions.ChiSquareQuantile(0.025, 3), 5e-6);
            foreach (double k in new[] { 0.3, 1, 3, 7.5, 50, 2000 })
                foreach (double p in TailPs)
                {
                    if (p > 0.5) continue;   // the lower CDF has full relative precision here
                    double x = Distributions.ChiSquareQuantile(p, k);
                    Rel(p, Distributions.ChiSquareCdf(x, k), 1e-10, $"k={k} p={p}");
                }
        }

        // ---------- F ----------

        public void F_IdentitiesWithTAndReciprocal()
        {
            foreach (double v in new[] { 1, 4, 25 })
                foreach (double p in new[] { 0.5, 0.9, 0.99 })
                {
                    double t = Distributions.StudentTQuantile((1 + p) / 2, v);
                    Rel(t * t, Distributions.FQuantile(p, 1, v), 1e-10, $"F(1,{v}) = t² at p={p}");
                }
            foreach (var (d1, d2) in new[] { (3.0, 10.0), (7.0, 2.0), (0.8, 40.0) })
                foreach (double p in new[] { 0.01, 0.3, 0.95 })
                    Rel(1 / Distributions.FQuantile(1 - p, d2, d1), Distributions.FQuantile(p, d1, d2), 1e-10, $"reciprocal ({d1},{d2}) p={p}");
        }

        public void F_TableValuesCdfAndPdf()
        {
            Check.Close(3.708265, Distributions.FQuantile(0.95, 3, 10), 5e-6);
            Check.Close(4.964603, Distributions.FQuantile(0.95, 1, 10), 5e-6);
            Check.Close(0.95, Distributions.FCdf(3.708265, 3, 10), 1e-6);
            Check.Close(1.0, Distributions.FPdf(0, 2, 7), 0);              // d1 = 2: density 1 at the origin
            double x = Distributions.FQuantile(0.3, 4, 9);
            Rel(0.3, Distributions.FCdf(x, 4, 9), 1e-12, "round trip");
        }

        // ---------- log-normal ----------

        public void LogNormal_MedianQuantileAndCdf()
        {
            Check.Close(Math.Exp(1.5), Distributions.LogNormalQuantile(0.5, 1.5, 0.4), 1e-12);
            Check.Close(0.5, Distributions.LogNormalCdf(Math.Exp(1.5), 1.5, 0.4), 1e-15);
            double q = Distributions.LogNormalQuantile(0.9, 0.2, 0.7);
            Check.Close(Math.Exp(0.2 + 0.7 * Distributions.NormalQuantile(0.9)), q, 1e-12);
            Check.Close(0.9, Distributions.LogNormalCdf(q, 0.2, 0.7), 1e-13);
            Check.Equal(0.0, Distributions.LogNormalCdf(-1));
            Check.Equal(0.0, Distributions.LogNormalPdf(0));
            // pdf(x) = φ(z) / (σx) with z = (ln x − μ)/σ
            Rel(Distributions.NormalPdf(0.5) / (2 * Math.E), Distributions.LogNormalPdf(Math.E, 0, 2), 1e-14, "pdf");
        }

        public void Validation()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.StudentTCdf(1, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.StudentTQuantile(0, 3));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.StudentTQuantile(1, 3));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.ChiSquareQuantile(0.5, double.PositiveInfinity));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.FQuantile(0.5, 1, -2));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.LogNormalCdf(1, 0, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.NormalQuantile(double.NaN));
        }
    }
}
