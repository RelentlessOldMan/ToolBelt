using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>Multiple and robust regression, and maximum-likelihood distribution fitting.</summary>
    public sealed class RegressionAndFitTests
    {
        // ---------- multiple regression ----------

        public void ExactPlaneIsRecovered()
        {
            var rng = new DeterministicRandom(1);
            int n = 25;
            var x = new double[n, 2];
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                x[i, 0] = rng.NextDouble() * 10;
                x[i, 1] = rng.NextDouble() * 5;
                y[i] = 1 + 2 * x[i, 0] - 3 * x[i, 1];
            }
            RegressionResult r = MultipleRegression.Fit(x, y);
            Check.Close(1, r.Coefficients[0], 1e-10);
            Check.Close(2, r.Coefficients[1], 1e-10);
            Check.Close(-3, r.Coefficients[2], 1e-10);
            Check.Close(1, r.RSquared, 1e-12);
            Check.True(r.Residuals.All(e => Math.Abs(e) < 1e-9));
            Check.Close(1 + 2 * 4 - 3 * 2, r.Predict(new double[] { 4, 2 }), 1e-9);
        }

        public void OnePredictor_MatchesTheTextbookFormulas()
        {
            var rng = new DeterministicRandom(2);
            double[] xs = Enumerable.Range(0, 40).Select(i => i * 0.5).ToArray();
            double[] ys = xs.Select(v => 3 - 0.7 * v + rng.NextGaussian(0, 0.4)).ToArray();
            RegressionResult r = MultipleRegression.Fit(xs.Select(v => (System.Collections.Generic.IReadOnlyList<double>)new[] { v }).ToList(), ys);
            LinearFit simple = LinearRegression.Fit(xs, ys);
            Check.Close(simple.Intercept, r.Coefficients[0], 1e-10);
            Check.Close(simple.Slope, r.Coefficients[1], 1e-10);
            // SE(slope) = s / √Sxx, s = √(SSE / (n − 2)).
            double xbar = xs.Average(), sxx = xs.Sum(v => (v - xbar) * (v - xbar));
            double sse = r.Residuals.Sum(e => e * e), s = Math.Sqrt(sse / (xs.Length - 2));
            Check.Close(s / Math.Sqrt(sxx), r.StandardErrors[1], 1e-12);
            Check.Close(s, r.ResidualStandardError, 1e-12);
            Check.Equal(38, r.DegreesOfFreedom);
            Check.True(r.PValues[1] < 1e-10, "a strong slope is significant");
        }

        public void ThroughTheOrigin()
        {
            var x = new double[,] { { 1 }, { 2 }, { 3 }, { 4 } };
            RegressionResult r = MultipleRegression.Fit(x, new double[] { 3, 6, 9, 12 }, intercept: false);
            Check.Equal(1, r.Coefficients.Count);
            Check.Close(3, r.Coefficients[0], 1e-12);
            Check.False(r.HasIntercept);
        }

        public void CollinearAndUnderdeterminedAreRejected()
        {
            var collinear = new double[,] { { 1, 2 }, { 2, 4 }, { 3, 6 }, { 4, 8 } };
            Check.Throws<InvalidOperationException>(() => MultipleRegression.Fit(collinear, new double[] { 1, 2, 3, 4 }));
            Check.Throws<ArgumentException>(() => MultipleRegression.Fit(new double[,] { { 1, 2 } }, new double[] { 1 }));
            Check.Throws<ArgumentException>(() => MultipleRegression.Fit(new double[,] { { 1 }, { 2 } }, new double[] { 1 }));
        }

        public void IntervalsHaveNominalCoverage()
        {
            var rng = new DeterministicRandom(3);
            int coefHits = 0, predictionHits = 0, meanHits = 0;
            const int trials = 1000;
            for (int t = 0; t < trials; t++)
            {
                int n = 20;
                var x = new double[n, 2];
                var y = new double[n];
                for (int i = 0; i < n; i++)
                {
                    x[i, 0] = rng.NextDouble() * 4;
                    x[i, 1] = rng.NextDouble() * 4;
                    y[i] = 0.5 + 1.5 * x[i, 0] - x[i, 1] + rng.NextGaussian();
                }
                RegressionResult r = MultipleRegression.Fit(x, y);
                double tq = Distributions.StudentTQuantile(0.975, r.DegreesOfFreedom);
                if (Math.Abs(r.Coefficients[1] - 1.5) <= tq * r.StandardErrors[1]) coefHits++;
                var at = new double[] { 2, 1 };
                double truth = 0.5 + 1.5 * 2 - 1;
                var (plo, phi) = r.PredictionInterval(at);
                double fresh = truth + rng.NextGaussian();
                if (plo <= fresh && fresh <= phi) predictionHits++;
                var (clo, chi) = r.ConfidenceInterval(at);
                if (clo <= truth && truth <= chi) meanHits++;
            }
            foreach (var (name, hits) in new[] { ("coefficient", coefHits), ("prediction", predictionHits), ("mean", meanHits) })
            {
                double rate = (double)hits / trials;
                Check.True(rate > 0.93 && rate < 0.97, $"{name} interval coverage {rate}");
            }
        }

        public void TheilSen_ShrugsOffOutliers()
        {
            double[] x = Enumerable.Range(0, 50).Select(i => (double)i).ToArray();
            double[] y = x.Select(v => 2 + 0.5 * v).ToArray();
            var exact = RobustRegression.TheilSen(x, y);
            Check.Close(0.5, exact.Slope, 1e-12);
            Check.Close(2, exact.Intercept, 1e-12);

            for (int i = 0; i < 50; i += 5) y[i] += 200;                     // 20% gross outliers
            var robust = RobustRegression.TheilSen(x, y);
            Check.Close(0.5, robust.Slope, 0.05);
            Check.True(Math.Abs(LinearRegression.Fit(x, y).Intercept - 2) > 10, "least squares is dragged away (contrast)");
            Check.Throws<ArgumentException>(() => RobustRegression.TheilSen(new double[] { 1, 1 }, new double[] { 1, 2 }));
        }

        // ---------- distribution fitting ----------

        private static double[] Draw(int seed, int n, Func<Random, double> f)
        {
            var rng = new DeterministicRandom(seed);
            return Enumerable.Range(0, n).Select(_ => f(rng)).ToArray();
        }

        public void ClosedFormEstimates()
        {
            double[] x = { 1, 2, 3, 4, 10 };
            var normal = DistributionFit.FitNormal(x);
            Check.Close(4, normal.Parameters["mean"]);
            Check.Close(Math.Sqrt(50.0 / 5), normal.Parameters["sd"], 1e-12);   // MLE divides by n
            Check.Close(5.0 / 20, DistributionFit.FitExponential(x).Parameters["rate"], 1e-15);
            var ln = DistributionFit.FitLogNormal(x);
            Check.Close(x.Average(v => Math.Log(v)), ln.Parameters["mu"], 1e-12);
        }

        public void GammaAndWeibull_AreLikelihoodMaxima()
        {
            double[] g = Draw(4, 3000, r => r.NextGamma(2.5, 3));
            var gf = DistributionFit.FitGamma(g);
            double GammaLL(double k, double th) => g.Sum(v => (k - 1) * Math.Log(v) - v / th) - g.Length * (k * Math.Log(th) + LnGammaRef(k));
            AssertMaximum(GammaLL, gf.Parameters["shape"], gf.Parameters["scale"], gf.LogLikelihood);

            double[] w = Draw(5, 3000, r => 4 * Math.Pow(-Math.Log(1 - r.NextDouble()), 1 / 1.7));   // Weibull(1.7, 4) by inversion
            var wf = DistributionFit.FitWeibull(w);
            double WeibullLL(double k, double l) => w.Length * (Math.Log(k) - k * Math.Log(l)) + (k - 1) * w.Sum(v => Math.Log(v)) - w.Sum(v => Math.Pow(v / l, k));
            AssertMaximum(WeibullLL, wf.Parameters["shape"], wf.Parameters["scale"], wf.LogLikelihood);
        }

        private static void AssertMaximum(Func<double, double, double> ll, double a, double b, double reported)
        {
            double at = ll(a, b);
            Check.Close(at, reported, 1e-8 * Math.Abs(at));
            foreach (double h in new[] { 1e-3, 1e-2 })
            {
                Check.True(ll(a * (1 + h), b) <= at && ll(a * (1 - h), b) <= at, "first parameter is not at a maximum");
                Check.True(ll(a, b * (1 + h)) <= at && ll(a, b * (1 - h)) <= at, "second parameter is not at a maximum");
            }
        }

        public void ParametersAreRecovered()
        {
            var gf = DistributionFit.FitGamma(Draw(6, 20000, r => r.NextGamma(2.5, 3)));
            Check.Close(2.5, gf.Parameters["shape"], 0.08);
            Check.Close(3, gf.Parameters["scale"], 0.1);
            var wf = DistributionFit.FitWeibull(Draw(7, 20000, r => 4 * Math.Pow(-Math.Log(1 - r.NextDouble()), 1 / 1.7)));
            Check.Close(1.7, wf.Parameters["shape"], 0.04);
            Check.Close(4, wf.Parameters["scale"], 0.06);
        }

        public void FitAll_RanksTheTrueFamilyFirst()
        {
            Check.Equal(DistributionFamily.Normal, DistributionFit.FitAll(Draw(8, 5000, r => r.NextGaussian(50, 5))).First().Family);
            Check.Equal(DistributionFamily.LogNormal, DistributionFit.FitAll(Draw(9, 5000, r => Math.Exp(r.NextGaussian(1, 0.6)))).First().Family);
            Check.Equal(DistributionFamily.Gamma, DistributionFit.FitAll(Draw(10, 5000, r => r.NextGamma(2.5, 3))).First().Family);
            Check.Equal(DistributionFamily.Weibull, DistributionFit.FitAll(Draw(11, 5000, r => 4 * Math.Pow(-Math.Log(1 - r.NextDouble()), 1 / 1.7))).First().Family);
            Check.Equal(1, DistributionFit.FitAll(new double[] { -1, 2, 3 }).Count);   // only Normal applies to non-positive data
        }

        public void IsPlausible_SeparatesRightFromWrong()
        {
            Check.True(DistributionFit.FitNormal(Draw(12, 2000, r => r.NextGaussian(10, 2))).IsPlausible());
            Check.False(DistributionFit.FitNormal(Draw(13, 2000, r => r.NextExponential())).IsPlausible());
            var fit = DistributionFit.FitExponential(new double[] { 1, 2, 3 });
            Check.Close(1 - Math.Exp(-0.5), fit.Cdf(1), 1e-15);
            Check.True(fit.ToString().StartsWith("Exponential(rate=0.5", StringComparison.Ordinal), fit.ToString());
        }

        public void FitValidation()
        {
            Check.Throws<ArgumentException>(() => DistributionFit.FitGamma(new double[] { 1, -2 }));
            Check.Throws<ArgumentException>(() => DistributionFit.FitNormal(new double[] { 3, 3, 3 }));
            Check.Throws<ArgumentException>(() => DistributionFit.FitWeibull(new double[] { 2, 2 }));
            Check.Throws<ArgumentException>(() => DistributionFit.FitNormal(new double[] { 1, double.NaN }));
        }

        // An independent log-gamma (Stirling with corrections, shifted) for the test's own likelihood.
        private static double LnGammaRef(double x)
        {
            double shift = 0;
            while (x < 10) { shift -= Math.Log(x); x += 1; }
            return shift + (x - 0.5) * Math.Log(x) - x + 0.5 * Math.Log(2 * Math.PI) + 1 / (12 * x) - 1 / (360 * x * x * x) + 1 / (1260 * Math.Pow(x, 5));
        }
    }
}
