using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 201 (statistics, twelfth pass): each failed on the code before its fix.
    public sealed class StatisticsReview201Tests
    {
        private static void Near(double expected, double actual, double relative, string what)
        {
            Check.True(Math.Abs(actual - expected) <= relative * Math.Abs(expected),
                what + ": expected " + expected.ToString("R") + ", got " + actual.ToString("R"));
        }

        private static readonly double TwoTo1000 = Math.Pow(2, 1000), TwoToMinus1000 = Math.Pow(2, -1000);

        // Shape and spread parameters are scale-free, and scaling by 2^+-1000 is exact, so a fit of the scaled data must
        // agree with the fit at scale 1 to rounding. The logs of values more than 50% from the reference value were
        // taken as a difference of two logs near +-693, each rounded to an ulp of that size: sigma (LogNormal), the gamma
        // shape and the Weibull shape came out 1e-14 to 1e-13 off where the data at scale 1 is within 1e-16.
        public void DistributionFit_ShapeParameters_AreScaleFreeAtExtremeMagnitudes()
        {
            var rng = new Random(201);
            for (int trial = 0; trial < 300; trial++)
            {
                double spread = trial % 3 == 0 ? 1 : trial % 3 == 1 ? 20 : 1000;
                int n = 5 + rng.Next(20);
                double[] x = Enumerable.Range(0, n).Select(_ => 1 + spread * rng.NextDouble()).ToArray();
                var logNormal = DistributionFit.FitLogNormal(x);
                var gamma = DistributionFit.FitGamma(x);
                var weibull = DistributionFit.FitWeibull(x);
                foreach (double k in new[] { TwoTo1000, TwoToMinus1000 })
                {
                    double[] y = x.Select(v => v * k).ToArray();
                    string what = "trial " + trial + " scale 2^" + (k > 1 ? "1000" : "-1000");
                    Near(logNormal.Parameters["sigma"], DistributionFit.FitLogNormal(y).Parameters["sigma"], 4e-15, what + " lognormal sigma");
                    var g = DistributionFit.FitGamma(y);
                    Near(gamma.Parameters["shape"], g.Parameters["shape"], 1e-14, what + " gamma shape");
                    Near(gamma.Parameters["scale"] * k, g.Parameters["scale"], 1e-14, what + " gamma scale");
                    Near(weibull.Parameters["shape"], DistributionFit.FitWeibull(y).Parameters["shape"], 1e-14, what + " weibull shape");
                }
            }
        }

        // The reviewer's case (ten values (1 + 20u) * 2^1000), against mpmath at 60 digits.
        public void DistributionFit_ReviewerCase_MatchesMpmath()
        {
            double[] x =
            {
                2.0479522767793924e+302, 1.5778053326615629e+302, 1.7497933920796633e+302, 2.0457590307533908e+302,
                6.639655534539801e+301, 1.469522342906211e+302, 2.0464650624612025e+302, 1.9761412655271746e+302,
                1.3349725777338298e+302, 4.70131017166214e+301,
            };
            Near(0.48655681171011922, DistributionFit.FitLogNormal(x).Parameters["sigma"], 2e-15, "lognormal sigma");
            Near(5.4161469910871005, DistributionFit.FitGamma(x).Parameters["shape"], 1e-14, "gamma shape");
            Near(3.3968782081781622, DistributionFit.FitWeibull(x).Parameters["shape"], 1e-14, "weibull shape");
        }

        // A fitted gamma scale below the smallest normal double (5.4e-309 for normal data near 1e-307) was refused as
        // "outside the double range", and FitAll silently left Gamma out. Expected values from mpmath at 60 digits.
        public void DistributionFit_FitGamma_SubnormalScaleIsReported()
        {
            double[] x =
            {
                1.009617690239914e-307, 1.6442706053533225e-307, 1.5698115187933455e-307, 1.1170485119179556e-307,
                1.330981407753087e-307, 1.2900898705604756e-307, 1.4699665393998469e-307, 1.5920166274852112e-307,
                9.735673485635359e-308, 9.152596349883858e-308,
            };
            var g = DistributionFit.FitGamma(x);
            Near(23.732602923226031, g.Parameters["shape"], 1e-13, "shape");
            Near(5.4408822314294358e-309, g.Parameters["scale"], 1e-13, "scale");
            Check.True(!double.IsInfinity(g.Aic) && !double.IsNaN(g.Aic), g.ToString());
            Check.True(DistributionFit.FitAll(x).Any(f => f.Family == DistributionFamily.Gamma), "FitAll has no Gamma fit");
            // The same fit at scale 1 (2^1020 is exact): the subnormal scale keeps nearly full precision.
            var unit = DistributionFit.FitGamma(x.Select(v => v * Math.Pow(2, 1020)).ToArray());
            Near(unit.Parameters["shape"], g.Parameters["shape"], 1e-14, "shape against scale 1");
            Near(unit.Parameters["scale"] * Math.Pow(2, -1020), g.Parameters["scale"], 1e-14, "scale against scale 1");
        }

        // ---------- The large-shape beta fraction (all four copies) ----------

        // Bfrac formed n*(b - n) before multiplying by x, which overflowed for b near double.MaxValue: the incomplete beta
        // and the F tails at a denominator df of 8.5e307 came out NaN. For b that large, I_x(a, b) is the gamma P(a, b*x)
        // and the F upper tail is the chi-square one of d1*f, to within a/b.
        public void BetaFraction_HugeSecondShape_MatchesTheGammaLimit()
        {
            var copies = new[] { typeof(Distributions), typeof(SpecialFunctions), typeof(Anova), typeof(GaugeRR) };
            foreach (double a in new[] { 150.0, 1e4 })
                foreach (double b in new[] { 1e307, 8.5e307, double.MaxValue })
                    foreach (double frac in new[] { 0.5, 0.6, 0.7, 0.8, 0.9 })
                    {
                        double x = frac * a / b;
                        double want = SpecialFunctions.RegularizedGammaP(a, x * b);
                        string what = "I(" + x.ToString("R") + ", " + a.ToString("R") + ", " + b.ToString("R") + ")";
                        Near(want, SpecialFunctions.RegularizedBetaI(x, a, b), 1e-10, what);
                        foreach (var type in copies)
                        {
                            var args = new object[] { x, 1 - x, a, b, 0.0, 0.0 };
                            type.GetMethod("BetaI", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
                            Near(want, (double)args[4], 1e-10, type.Name + " " + what);
                        }
                    }
            foreach (double d1 in new[] { 300.0, 2e4 })
                foreach (double d2 in new[] { 8.5e307, double.MaxValue })
                    foreach (double f in new[] { 0.5, 0.9, 1.0, 1.1 })
                    {
                        double want = Distributions.ChiSquareSurvival(f * d1, d1);
                        string what = "F(" + f + ", " + d1 + ", " + d2.ToString("R") + ")";
                        Near(want, Distributions.FSurvival(f, d1, d2), 1e-10, what + " survival");
                        Near(Distributions.ChiSquareCdf(f * d1, d1), Distributions.FCdf(f, d1, d2), 1e-10, what + " cdf");
                        Near(want, Anova.FUpperTailProbability(f, d1, d2), 1e-10, what + " Anova");
                    }
            // The reviewer's saturated cases: probabilities 1 and 0 to double precision, not NaN.
            Check.True(Math.Abs(Distributions.FSurvival(0.5, 1e8, double.MaxValue) - 1) <= 1e-15, "FSurvival(0.5, 1e8, MaxValue)");
            double tail = Distributions.FSurvival(3, double.MaxValue, 1e30);
            Check.True(tail >= 0 && tail <= 1e-300, "FSurvival(3, MaxValue, 1e30) = " + tail.ToString("R"));
            tail = Anova.FUpperTailProbability(3, double.MaxValue, 2e30);
            Check.True(tail >= 0 && tail <= 1e-300, "Anova.FUpperTailProbability(3, MaxValue, 2e30) = " + tail.ToString("R"));
        }

        // ---------- MultipleRegression ----------

        // The scaled slope was a rounding crumb, not 0, and the unit ratio (y near 1e300 over x near 1e-300, 2^1995) took it
        // past double.MaxValue: the slope read -Infinity where the exact least-squares slope is 0. LinearRegression gets 0.
        public void MultipleRegression_ZeroSlopeUnderAHugeUnitRatio_IsZero()
        {
            double u = Math.Pow(2, -1000);
            var x = new double[,] { { u }, { 2 * u }, { 3 * u }, { 4 * u } };
            var y = new[] { 1.001e300, 1e300, 1e300, 1.001e300 };
            var fit = MultipleRegression.Fit(x, y);
            Check.Equal(0.0, fit.Coefficients[1]);
            Near(1.0005e300, fit.Coefficients[0], 1e-15, "intercept");
        }

        // Graded exactly: x = X*2^-1000 and y = Y*2^950 for small integers X, Y, so the least-squares slope is
        // (n*SXY - SX*SY) / (n*SXX - SX^2) * 2^1950 (through the origin, SXY / SXX * 2^1950): exactly 0 when the numerator
        // is, and otherwise far past double.MaxValue, so the fit must give 0 or an infinity of the exact sign.
        public void MultipleRegression_HugeUnitRatio_SlopeIsZeroOrInfinityOfTheExactSign()
        {
            var rng = new Random(2011);
            double u = Math.Pow(2, -1000), v = Math.Pow(2, 950);
            for (int trial = 0; trial < 400; trial++)
            {
                int n = 3 + rng.Next(8);
                bool intercept = trial % 4 != 3;
                var xs = Enumerable.Range(0, n).Select(_ => (long)rng.Next(-50, 50)).ToArray();
                if (xs.Distinct().Count() < 2) continue;
                var ys = new long[n];
                if (trial % 2 == 0)
                {
                    // Symmetric: x evenly spaced, y a palindrome, so the slope with an intercept is exactly 0; through the
                    // origin, x symmetric about 0 with a palindromic y.
                    long step = 1 + rng.Next(5), start = intercept ? rng.Next(-20, 20) : -(n - 1) * step;
                    for (int i = 0; i < n; i++) xs[i] = intercept ? start + i * step : start + 2 * i * step;
                    for (int i = 0; i < (n + 1) / 2; i++) ys[i] = ys[n - 1 - i] = rng.Next(1, 1 << 20);
                }
                else
                    for (int i = 0; i < n; i++) ys[i] = rng.Next(1, 1 << 20);
                BigInteger sx = 0, sy = 0, sxy = 0;
                for (int i = 0; i < n; i++) { sx += xs[i]; sy += ys[i]; sxy += (BigInteger)xs[i] * ys[i]; }
                int sign = (intercept ? n * sxy - sx * sy : sxy).Sign;
                var design = new double[n, 1];
                for (int i = 0; i < n; i++) design[i, 0] = xs[i] * u;
                double[] y = ys.Select(t => t * v).ToArray();
                double slope = MultipleRegression.Fit(design, y, intercept).Coefficients[intercept ? 1 : 0];
                string what = "trial " + trial + " (intercept " + intercept + "): exact sign " + sign + ", got " + slope.ToString("R");
                Check.True(sign == 0 ? slope == 0 : double.IsInfinity(slope) && Math.Sign(slope) == sign, what);
            }
        }
    }
}
