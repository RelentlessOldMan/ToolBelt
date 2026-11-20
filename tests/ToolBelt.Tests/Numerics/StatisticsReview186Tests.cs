using System;
using System.Linq;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 186 (statistics, eleventh pass): each failed on the code before its fix.
    public sealed class StatisticsReview186Tests
    {
        // Integers below 2^53 (exact as doubles and as BigIntegers), mixing small values with ones near 1.7e15, where
        // distinct mean differences sit within 1e-12 of each other.
        private static double[] RandomIntegers(Random rng, int n)
        {
            var v = new double[n];
            for (int i = 0; i < n; i++)
                v[i] = rng.Next(3) == 0 ? 1720000000000000 + rng.Next(16) : rng.Next(16);
            return v;
        }

        private static int BitCount(int v) { int c = 0; for (; v != 0; v &= v - 1) c++; return c; }

        private static string Show(double[] v) => "[" + string.Join(", ", v.Select(x => x.ToString("R"))) + "]";

        // The 1e-12 relative tie tolerance called means 36 ulps apart ties: {5, 1720000000000004} against {2} gave p = 1
        // for an exact 2/3. Graded against enumeration in BigInteger: mean(A) - mean(B) rises with sum(A) (the pooled
        // total is fixed), so a split is as extreme above when its exact sum(A) is at least the observed one.
        public void TwoSample_ExactPath_MatchesBigIntegerEnumeration()
        {
            Check.Equal(2.0 / 3, PermutationTest.TwoSample(new double[] { 5, 1720000000000004 }, new double[] { 2 }, new Random(1)).PValue);
            Check.Equal(1.0 / 3, PermutationTest.TwoSample(new double[] { 1e13, 1 }, new double[] { 0, 0 }, new Random(1)).PValue);
            var rng = new Random(186);
            int bad = 0;
            string first = "";
            for (int trial = 0; trial < 2000; trial++)
            {
                int na = 1 + rng.Next(4), nb = 1 + rng.Next(4), n = na + nb;
                double[] a = RandomIntegers(rng, na), b = RandomIntegers(rng, nb);
                double[] pooled = a.Concat(b).ToArray();
                BigInteger obs = a.Aggregate(BigInteger.Zero, (s, v) => s + new BigInteger(v));
                int greater = 0, less = 0, total = 0;
                for (int mask = 0; mask < 1 << n; mask++)
                {
                    if (BitCount(mask) != na) continue;
                    BigInteger s = 0;
                    for (int i = 0; i < n; i++) if ((mask >> i & 1) != 0) s += new BigInteger(pooled[i]);
                    total++;
                    if (s >= obs) greater++;
                    if (s <= obs) less++;
                }
                double want = Math.Min(1, 2 * Math.Min((double)greater / total, (double)less / total));
                var got = PermutationTest.TwoSample(a, b, new Random(trial), permutations: 1000);
                Check.True(got.IsExact, "exact path");
                if (got.PValue != want && bad++ == 0) first = $"a={Show(a)} b={Show(b)}: p {got.PValue:R}, exact {want:R}";
            }
            Check.Equal(0, bad, first);
        }

        // The Monte Carlo path ranked by the same tolerance. Replays the generator's partial Fisher-Yates and counts the
        // drawn splits by their exact sum(A).
        public void TwoSample_MonteCarloPath_CountsByExactSums()
        {
            var rng = new Random(1860);
            int bad = 0;
            string first = "";
            for (int trial = 0; trial < 300; trial++)
            {
                int na = 4 + rng.Next(4), nb = 4 + rng.Next(4), n = na + nb, m = 20 + rng.Next(20);
                double[] a = RandomIntegers(rng, na), b = RandomIntegers(rng, nb);
                var work = a.Concat(b).Select(v => new BigInteger(v)).ToArray();
                BigInteger obs = a.Aggregate(BigInteger.Zero, (s, v) => s + new BigInteger(v));
                var replay = new Random(trial);
                int greater = 0, less = 0;
                for (int r = 0; r < m; r++)
                {
                    for (int i = 0; i < na; i++)
                    {
                        int j = i + replay.Next(n - i);
                        (work[i], work[j]) = (work[j], work[i]);
                    }
                    BigInteger s = 0;
                    for (int i = 0; i < na; i++) s += work[i];
                    if (s >= obs) greater++;
                    if (s <= obs) less++;
                }
                double want = Math.Min(1, 2 * Math.Min((greater + 1.0) / (m + 1), (less + 1.0) / (m + 1)));
                var got = PermutationTest.TwoSample(a, b, new Random(trial), permutations: m);
                Check.True(!got.IsExact, "Monte Carlo path");
                if (got.PValue != want && bad++ == 0) first = $"a={Show(a)} b={Show(b)} m={m}: p {got.PValue:R}, want {want:R}";
            }
            Check.Equal(0, bad, first);
        }

        // Values near +-MaxValue: one of one sign and the rest of the other, or a random mix.
        private static double[] Huge(Random rng, int n)
        {
            var v = new double[n];
            bool mix = rng.Next(2) == 0;
            for (int i = 0; i < n; i++)
            {
                double mag = double.MaxValue * (0.3 + 0.7 * rng.NextDouble());
                v[i] = mix ? (rng.Next(2) == 0 ? mag : -mag) : (i == 0 ? mag : -mag);
            }
            return v;
        }

        // The limits scale exactly with the data, so the result for the data over 16 (where nothing overflows), times 16,
        // is the reference: equal, or both infinite with the same sign.
        private static bool Matches(double got, double reference16) =>
            got == reference16 * 16 || (double.IsInfinity(got) && double.IsInfinity(reference16 * 16) && Math.Sign(got) == Math.Sign(reference16));

        // z*sd overflowed before the bias was added: nine -MaxValue and one +MaxValue against zeros gave an upper limit of
        // +inf for a true 7.9e307.
        public void BlandAltman_HugeDifferences_LimitsOverflowOnlyWhenTheyMust()
        {
            var a = Enumerable.Repeat(-double.MaxValue, 9).Concat(new[] { double.MaxValue }).ToArray();
            double want = double.MaxValue * (-0.8 + 1.96 * Math.Sqrt(0.4));
            double upper = MeasurementAgreement.BlandAltman(a, new double[10]).UpperLimit;
            Check.True(Math.Abs(upper - want) <= 1e-12 * want, $"upper {upper:R}, want {want:R}");
            var rng = new Random(186);
            int bad = 0;
            string first = "";
            for (int trial = 0; trial < 3000; trial++)
            {
                int n = 2 + rng.Next(11);
                double[] x = Huge(rng, n), zero = new double[n];
                double z = 0.5 + 3 * rng.NextDouble();
                var got = MeasurementAgreement.BlandAltman(x, zero, z);
                var r16 = MeasurementAgreement.BlandAltman(x.Select(v => v / 16).ToArray(), zero, z);
                if ((!Matches(got.UpperLimit, r16.UpperLimit) || !Matches(got.LowerLimit, r16.LowerLimit)) && bad++ == 0)
                    first = $"{Show(x)} z={z:R}: [{got.LowerLimit:R}, {got.UpperLimit:R}], reference [{r16.LowerLimit * 16:R}, {r16.UpperLimit * 16:R}]";
            }
            Check.Equal(0, bad, first);
        }

        // 2.66 * (mean moving range) overflowed before the mean was added, so a finite UCL came out +inf.
        public void Individuals_HugeData_LimitsOverflowOnlyWhenTheyMust()
        {
            var rng = new Random(1861);
            int bad = 0, reached = 0;
            string first = "";
            for (int trial = 0; trial < 3000; trial++)
            {
                double[] x = Huge(rng, 2 + rng.Next(11));
                var got = ControlChart.Individuals(x);
                var r16 = ControlChart.Individuals(x.Select(v => v / 16).ToArray());
                double spread16 = r16.UpperControlLimit - r16.CenterLine;
                if (double.IsInfinity(spread16 * 16) && !double.IsInfinity(r16.UpperControlLimit * 16)) reached++;
                if ((!Matches(got.UpperControlLimit, r16.UpperControlLimit) || !Matches(got.LowerControlLimit, r16.LowerControlLimit)
                     || got.CenterLine != r16.CenterLine * 16) && bad++ == 0)
                    first = $"{Show(x)}: UCL {got.UpperControlLimit:R}, reference {r16.UpperControlLimit * 16:R}";
            }
            Check.True(reached > 0, "the trials reach a finite limit whose spread overflows");
            Check.Equal(0, bad, first);
        }

        // ln(1 + d) by its series, for |d| <= 1e-3: accurate to an ulp of itself, unlike ln v - ln max.
        private static double Log1pSeries(double d)
        {
            double term = d, sum = 0;
            for (int k = 1; k <= 12; k++) { sum += term / k; term *= -d; }
            return sum;
        }

        // Logs rounded to an ulp of ln x, and a log-likelihood that cancelled terms of size n*k*ln(lambda): at CV 1e-10
        // the shape and log-likelihood came out 4e-6 off. Graded against an independent solve: logs by series about the
        // maximum, and the shape equation sum(w c) / sum(w) = 1/k (c the centred logs, w = e^(k c)) by bisection.
        public void FitWeibull_TightData_KeepsShapeAndLogLikelihood()
        {
            var rng = new Random(186);
            double worstK = 0, worstLl = 0;
            string atK = "", atLl = "";
            foreach (double cv in new[] { 1e-4, 1e-6, 1e-8, 1e-10, 1e-12 })
                for (int trial = 0; trial < 20; trial++)
                {
                    double centre = Math.Exp(rng.NextDouble() * 20 - 5);
                    double[] x = Enumerable.Range(0, 20).Select(_ => centre * (1 + cv * (rng.NextDouble() - 0.5))).ToArray();
                    double max = x.Max();
                    int n = x.Length;
                    double[] l = x.Select(v => Log1pSeries((v - max) / max)).ToArray();
                    double meanL = l.Average();
                    double[] c = l.Select(v => v - meanL).ToArray();
                    double cmax = c.Max();
                    double G(double kk)
                    {
                        double s0 = 0, s1 = 0;
                        foreach (double ci in c) { double w = Math.Exp(kk * (ci - cmax)); s0 += w; s1 += w * ci; }
                        return s1 / s0 - 1 / kk;
                    }
                    double lo = 1e-3, hi = 1e3;
                    while (G(hi) < 0) { lo = hi; hi *= 10; }
                    for (int i = 0; i < 400 && hi / lo > 1 + 1e-15; i++) { double mid = Math.Sqrt(lo * hi); if (G(mid) < 0) lo = mid; else hi = mid; }
                    double k = Math.Sqrt(lo * hi);
                    double lnMean = Math.Log(l.Sum(v => Math.Exp(k * v)) / n);
                    double ll = n * Math.Log(k) - n * Math.Log(max) + l.Sum(v => -v + (k * v - lnMean) - Math.Exp(k * v - lnMean));

                    var fit = DistributionFit.FitWeibull(x);
                    double ek = Math.Abs(fit.Parameters["shape"] / k - 1), el = Math.Abs(fit.LogLikelihood - ll) / Math.Max(1, Math.Abs(ll));
                    if (ek > worstK) { worstK = ek; atK = $"cv {cv:R}: shape {fit.Parameters["shape"]:R}, reference {k:R}"; }
                    if (el > worstLl) { worstLl = el; atLl = $"cv {cv:R}: ll {fit.LogLikelihood:R}, reference {ll:R}"; }
                }
            Check.True(worstK <= 1e-9, "shape " + worstK.ToString("E2") + " off at " + atK);
            Check.True(worstLl <= 1e-11, "log-likelihood " + worstLl.ToString("E2") + " off at " + atLl);
        }

        // {1, 1 + 4eps, 1 + 8eps} are distinct, and FitNormal accepts them; FitWeibull said "All values are equal".
        public void FitWeibull_DistinctValuesAFewUlpsApart_AreNotCalledEqual()
        {
            double e = Math.Pow(2, -52);
            var fit = DistributionFit.FitWeibull(new[] { 1, 1 + 4 * e, 1 + 8 * e });
            double k = fit.Parameters["shape"];
            Check.True(k > 1e14 && !double.IsInfinity(k), "shape " + k.ToString("R"));
            var ex = Check.Throws<ArgumentException>(() => DistributionFit.FitWeibull(new[] { 3.0, 3.0, 3.0 }));
            Check.True(ex.Message.StartsWith("All values are equal", StringComparison.Ordinal), ex.Message);
        }
    }
}
