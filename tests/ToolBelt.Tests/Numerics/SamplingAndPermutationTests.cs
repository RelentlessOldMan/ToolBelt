using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>Poisson and gamma sampling, and permutation tests.</summary>
    public sealed class SamplingAndPermutationTests
    {
        // ---------- Poisson ----------

        public void Poisson_MeanAndVarianceAcrossBothAlgorithms()
        {
            var rng = new DeterministicRandom(1);
            foreach (double lambda in new[] { 0.5, 4, 29.9, 30, 50, 1000 })        // Knuth below 30, PTRS from 30
            {
                const int n = 40000;
                int[] k = Enumerable.Range(0, n).Select(_ => rng.NextPoisson(lambda)).ToArray();
                double mean = k.Average(), var = k.Select(v => (v - mean) * (v - mean)).Sum() / (n - 1);
                Check.True(Math.Abs(mean - lambda) < 5 * Math.Sqrt(lambda / n), $"λ={lambda}: mean {mean}");
                Check.True(Math.Abs(var / lambda - 1) < 0.06, $"λ={lambda}: variance {var}");
            }
            Check.Equal(0, rng.NextPoisson(0));
        }

        public void Poisson_MatchesThePmf()
        {
            var rng = new DeterministicRandom(2);
            foreach (double lambda in new[] { 4.0, 50.0 })
            {
                const int n = 50000;
                int lo = (int)Math.Max(0, lambda - 3 * Math.Sqrt(lambda)), hi = (int)(lambda + 3 * Math.Sqrt(lambda));
                var observed = new double[hi - lo + 3];                              // [<lo] [lo..hi] [>hi]
                for (int i = 0; i < n; i++)
                {
                    int v = rng.NextPoisson(lambda);
                    observed[v < lo ? 0 : (v > hi ? observed.Length - 1 : v - lo + 1)]++;
                }
                var expected = new double[observed.Length];
                double cum = 0;
                for (int v = lo; v <= hi; v++)
                {
                    double pmf = Math.Exp(-lambda + v * Math.Log(lambda) - LogFactorial(v));
                    expected[v - lo + 1] = pmf * n;
                    cum += pmf;
                }
                double below = 0;
                for (int v = 0; v < lo; v++) below += Math.Exp(-lambda + v * Math.Log(lambda) - LogFactorial(v));
                expected[0] = below * n;
                expected[expected.Length - 1] = (1 - cum - below) * n;
                // Drop empty bins (e.g. "below range" when the range starts at 0).
                var keep = Enumerable.Range(0, expected.Length).Where(i => expected[i] > 0).ToArray();
                var r = HypothesisTests.ChiSquareGoodnessOfFit(keep.Select(i => observed[i]).ToArray(), keep.Select(i => expected[i]).ToArray());
                Check.True(r.PValue > 0.001, $"λ={lambda}: chi-square p {r.PValue}");
            }
        }

        // ---------- gamma ----------

        public void Gamma_MomentsAndDistribution()
        {
            var rng = new DeterministicRandom(3);
            foreach (double shape in new[] { 0.5, 2.0, 9.0 })
            {
                const double scale = 3;
                double[] x = Enumerable.Range(0, 30000).Select(_ => rng.NextGamma(shape, scale)).ToArray();
                double mean = x.Average(), var = x.Select(v => (v - mean) * (v - mean)).Sum() / (x.Length - 1);
                Check.Close(shape * scale, mean, 0.04 * shape * scale + 0.05, $"k={shape} mean");
                Check.Close(shape * scale * scale, var, 0.08 * shape * scale * scale, $"k={shape} variance");
                var ks = HypothesisTests.KolmogorovSmirnovOneSample(x, v => v <= 0 ? 0 : Distributions.ChiSquareCdf(2 * v / scale, 2 * shape));
                Check.True(ks.PValue > 0.001, $"k={shape}: KS p {ks.PValue}");
            }
        }

        // ---------- permutation tests ----------

        public void TwoSample_ExactSmallCase()
        {
            // C(6,3) = 20 splits; only the two extreme splits reach |mean difference| = 3.
            var r = PermutationTest.TwoSample(new double[] { 1, 2, 3 }, new double[] { 4, 5, 6 }, new DeterministicRandom(1));
            Check.True(r.IsExact);
            Check.Equal(20, r.Permutations);
            Check.Close(-3, r.Observed);
            Check.Close(0.1, r.PValue, 1e-15);
            Check.Close(0.05, PermutationTest.TwoSample(new double[] { 1, 2, 3 }, new double[] { 4, 5, 6 }, new DeterministicRandom(1),
                alternative: Alternative.Less).PValue, 1e-15);
            Check.Close(1, PermutationTest.TwoSample(new double[] { 1, 2, 3 }, new double[] { 4, 5, 6 }, new DeterministicRandom(1),
                alternative: Alternative.Greater).PValue, 1e-15);
        }

        public void Paired_ExactSignFlips()
        {
            // 2³ sign patterns; |mean| = 2 is reached by all-plus and all-minus only.
            var r = PermutationTest.Paired(new double[] { 2, 4, 6 }, new double[] { 1, 2, 3 }, new DeterministicRandom(1));
            Check.True(r.IsExact);
            Check.Equal(8, r.Permutations);
            Check.Close(0.25, r.PValue, 1e-15);
        }

        public void MonteCarlo_PValueNeverZeroAndCustomStatistic()
        {
            var a = Enumerable.Range(0, 30).Select(i => 100.0 + i).ToArray();
            var b = Enumerable.Range(0, 30).Select(i => (double)i).ToArray();
            var r = PermutationTest.TwoSample(a, b, new DeterministicRandom(5), permutations: 999);
            Check.False(r.IsExact);
            Check.Close(1.0 / 1000, r.PValue, 1e-15);                                 // (0 + 1) / (999 + 1)
            double Median(System.Collections.Generic.IReadOnlyList<double> v) { var s = v.OrderBy(t => t).ToArray(); return s[s.Length / 2]; }
            var med = PermutationTest.TwoSample(a, b, new DeterministicRandom(5), 499, (x, y) => Median(x) - Median(y));
            Check.True(med.IsSignificant(0.01));
        }

        public void FalsePositiveRateUnderTheNull()
        {
            var rng = new DeterministicRandom(77);
            int rejects = 0;
            const int trials = 1000;
            for (int t = 0; t < trials; t++)
            {
                double[] a = Enumerable.Range(0, 8).Select(_ => rng.NextGaussian()).ToArray();
                double[] b = Enumerable.Range(0, 8).Select(_ => rng.NextGaussian()).ToArray();
                if (PermutationTest.TwoSample(a, b, rng, permutations: 199).IsSignificant(0.05)) rejects++;
            }
            double rate = (double)rejects / trials;
            Check.True(rate > 0.03 && rate < 0.07, $"false-positive rate {rate}");
        }

        public void Validation()
        {
            var rng = new DeterministicRandom(1);
            Check.Throws<ArgumentOutOfRangeException>(() => rng.NextPoisson(-1));
            Check.Throws<ArgumentOutOfRangeException>(() => rng.NextGamma(0));
            Check.Throws<ArgumentException>(() => PermutationTest.TwoSample(Array.Empty<double>(), new double[] { 1 }, rng));
            Check.Throws<ArgumentException>(() => PermutationTest.Paired(new double[] { 1 }, new double[] { 1, 2 }, rng));
            Check.Throws<ArgumentOutOfRangeException>(() => PermutationTest.TwoSample(new double[] { 1 }, new double[] { 2 }, rng, 0));
        }

        private static double LogFactorial(int k)
        {
            double s = 0;
            for (int i = 2; i <= k; i++) s += Math.Log(i);
            return s;
        }
    }
}
