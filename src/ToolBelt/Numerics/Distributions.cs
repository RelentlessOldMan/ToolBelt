// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Probability density, cumulative distribution and quantile (inverse CDF) functions for the common
    /// continuous distributions. The normal CDF uses an error-function approximation (accuracy ~1.5e-7);
    /// the normal quantile uses Acklam's rational approximation (relative error ~1e-9), the value everyone
    /// hunts down and pastes badly. Pairs with the reproducible generator for simulation.
    /// </summary>
    public static class Distributions
    {
        private const double Sqrt2 = 1.4142135623730951;
        private const double Sqrt2Pi = 2.5066282746310002;

        // ---- Normal ----

        public static double NormalPdf(double x, double mean = 0, double standardDeviation = 1)
        {
            RequirePositive(standardDeviation, nameof(standardDeviation));
            double z = (x - mean) / standardDeviation;
            return Math.Exp(-0.5 * z * z) / (standardDeviation * Sqrt2Pi);
        }

        public static double NormalCdf(double x, double mean = 0, double standardDeviation = 1)
        {
            RequirePositive(standardDeviation, nameof(standardDeviation));
            double z = (x - mean) / standardDeviation;
            return 0.5 * (1.0 + Erf(z / Sqrt2));
        }

        /// <summary>The inverse normal CDF (quantile) for a probability in (0, 1).</summary>
        public static double NormalQuantile(double p, double mean = 0, double standardDeviation = 1)
        {
            RequirePositive(standardDeviation, nameof(standardDeviation));
            if (p <= 0 || p >= 1) throw new ArgumentOutOfRangeException(nameof(p), p, "Probability must be in (0, 1).");
            return mean + standardDeviation * StandardNormalQuantile(p);
        }

        // ---- Exponential ----

        public static double ExponentialPdf(double x, double rate)
        {
            RequirePositive(rate, nameof(rate));
            return x < 0 ? 0 : rate * Math.Exp(-rate * x);
        }

        public static double ExponentialCdf(double x, double rate)
        {
            RequirePositive(rate, nameof(rate));
            return x < 0 ? 0 : 1.0 - Math.Exp(-rate * x);
        }

        public static double ExponentialQuantile(double p, double rate)
        {
            RequirePositive(rate, nameof(rate));
            if (p < 0 || p >= 1) throw new ArgumentOutOfRangeException(nameof(p), p, "Probability must be in [0, 1).");
            return -Math.Log(1.0 - p) / rate;
        }

        // ---- Uniform ----

        public static double UniformPdf(double x, double a, double b)
        {
            RequireOrdered(a, b);
            return (x < a || x > b) ? 0 : 1.0 / (b - a);
        }

        public static double UniformCdf(double x, double a, double b)
        {
            RequireOrdered(a, b);
            if (x < a) return 0;
            if (x > b) return 1;
            return (x - a) / (b - a);
        }

        // ---- internals ----

        /// <summary>Abramowitz &amp; Stegun 7.1.26 error function, |error| &lt; 1.5e-7.</summary>
        private static double Erf(double x)
        {
            double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
            double poly = ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t;
            double y = 1.0 - poly * Math.Exp(-x * x);
            return Math.Sign(x) * y;
        }

        // Acklam's inverse normal CDF.
        private static double StandardNormalQuantile(double p)
        {
            double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
            double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
            double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
            double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
            const double pLow = 0.02425, pHigh = 1 - 0.02425;

            if (p < pLow)
            {
                double q = Math.Sqrt(-2 * Math.Log(p));
                return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                       ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }
            if (p <= pHigh)
            {
                double q = p - 0.5;
                double r = q * q;
                return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                       (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
            }
            else
            {
                double q = Math.Sqrt(-2 * Math.Log(1 - p));
                return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                        ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }
        }

        private static void RequirePositive(double value, string name)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(name, value, "Must be positive.");
        }

        private static void RequireOrdered(double a, double b)
        {
            if (a >= b) throw new ArgumentException($"Lower bound {a} must be less than upper bound {b}.");
        }
    }
}
