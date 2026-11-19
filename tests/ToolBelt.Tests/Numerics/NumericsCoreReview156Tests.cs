using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Round 156 (numerics core, ninth pass): each regression failed on the code before its fix.
    public sealed class NumericsCoreReview156Tests
    {
        // ---------------- EmpiricalDistribution.Quantile agrees with Percentile at fractional ranks ----------------

        public void Quantile_FractionalRanksMatchPercentile()
        {
            var tens = Enumerable.Range(0, 198).Select(i => i * 1e6).ToArray();
            Check.Equal(137900000.0, new EmpiricalDistribution(tens).Quantile(0.7));
            Check.Equal(137900000.0, Percentile.Compute(tens, 70));
            var small = Enumerable.Range(0, 11).Select(i => (double)i).ToArray();
            Check.Equal(0.7, new EmpiricalDistribution(small).Quantile(0.07));

            var rng = new Random(156);
            int bad = 0;
            for (int trial = 0; trial < 3000; trial++)
            {
                int n = rng.Next(2, 200);
                var data = Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 200 - 100).ToArray();
                var e = new EmpiricalDistribution(data);
                // A whole percentile, or one of two decimals (parsed, as typed: 12.34 and 0.1234).
                int hundredths = rng.Next(0, 10001);
                double pct = double.Parse((hundredths / 100m).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                double p = double.Parse((hundredths / 10000m).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                if (e.Quantile(p) != Percentile.Compute(data, pct)) bad++;
                int j = rng.Next(0, 101);
                if (e.Quantile(j / 100.0) != Percentile.Compute(data, j)) bad++;
            }
            Check.Equal(0, bad);
        }

        // The rank of a decimal is the decimal's, rounded once: graded against exact decimal arithmetic on whole data,
        // where the type-7 value is a decimal too.
        public void Quantile_DecimalRanksAreRoundedOnce()
        {
            var rng = new Random(1561);
            for (int trial = 0; trial < 3000; trial++)
            {
                int n = rng.Next(2, 400);
                var data = Enumerable.Range(0, n).Select(i => (double)(i * 7)).ToArray();
                int thousandths = rng.Next(0, 1001);
                decimal pd = thousandths / 1000m;
                double p = double.Parse(pd.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                decimal exact = pd * (n - 1) * 7;   // data i·7 is linear, so the value is 7·rank
                double want = (double)exact;
                double got = new EmpiricalDistribution(data).Quantile(p);
                Check.True(Math.Abs(got - want) <= Math.Abs(want) * 2.3e-16, $"n {n}, p {pd}: {got:R} vs {want:R}");
            }
        }

        // ---------------- AkimaSpline: scaled slope terms no longer overflow ----------------

        private static double Hermite(AkimaSpline s, double[] x, double[] y, double q)
        {
            int i = 0;
            while (q > x[i + 1]) i++;
            double h = x[i + 1] - x[i], t = (q - x[i]) / h;
            double h10 = t * (1 - t) * (1 - t), h01 = t * t * (3 - 2 * t), h11 = t * t * (t - 1);
            return y[i] + h01 * (y[i + 1] - y[i]) + h * h10 * s.Slopes[i] + h * h11 * s.Slopes[i + 1];
        }

        public void Akima_WideXWithTinyYStaysFinite()
        {
            var x = new[] { 0, 1e-150, 1e200 };
            var y = new[] { 0, 1e-200, 0 };
            foreach (bool modified in new[] { false, true })
            {
                var s = new AkimaSpline(x, y, modified);
                double got = s.Interpolate(2.5e199), want = Hermite(s, x, y, 2.5e199);
                Check.True(!double.IsInfinity(got) && Math.Abs(got - want) <= 1e-12 * Math.Abs(want), $"{got:R} vs {want:R}");
            }

            var rng = new Random(1562);
            int checkedCount = 0;
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = rng.Next(3, 7);
                var xs = new double[n];
                var ys = new double[n];
                var mags = Enumerable.Range(0, n - 2).Select(_ => Math.Pow(10, rng.Next(-200, 150) + rng.NextDouble())).Distinct().OrderBy(v => v).ToList();
                if (mags.Count != n - 2) continue;
                for (int i = 1; i < n - 1; i++) xs[i] = mags[i - 1];
                xs[n - 1] = Math.Pow(10, rng.Next(160, 250) + rng.NextDouble());   // beyond 2^500
                for (int i = 0; i < n; i++) ys[i] = (rng.Next(2) * 2 - 1) * Math.Pow(10, rng.Next(-320, -160));   // below 2^-500
                var s = new AkimaSpline(xs, ys, rng.Next(2) == 0);
                for (int k = 0; k < 5; k++)
                {
                    int seg = rng.Next(0, n - 1);
                    double q = xs[seg] + rng.NextDouble() * (xs[seg + 1] - xs[seg]);
                    if (!(q > xs[seg] && q < xs[seg + 1])) continue;
                    // The reference reads the public slopes, which underflow where the stored (scaled) ones do not.
                    if (!(Math.Abs(s.Slopes[seg]) >= 2.2250738585072014e-308 && Math.Abs(s.Slopes[seg + 1]) >= 2.2250738585072014e-308)) continue;
                    double want = Hermite(s, xs, ys, q);
                    if (double.IsNaN(want) || double.IsInfinity(want) || Math.Abs(want) > 1e300) continue;
                    double got = s.Interpolate(q);
                    Check.True(!double.IsInfinity(got) && !double.IsNaN(got), $"trial {trial}: {got:R} vs {want:R}");
                    double t = (q - xs[seg]) / (xs[seg + 1] - xs[seg]), h = xs[seg + 1] - xs[seg];
                    double scale = Math.Abs(ys[seg]) + Math.Abs(ys[seg + 1]) + Math.Abs(h * t * s.Slopes[seg]) + Math.Abs(h * t * s.Slopes[seg + 1]);
                    Check.True(Math.Abs(got - want) <= 1e-12 * scale + 8 * double.Epsilon, $"trial {trial}: {got:R} vs {want:R} (segment {seg})");
                    checkedCount++;
                }
            }
            Check.True(checkedCount > 300, "only " + checkedCount + " checked");
        }

        // ---------------- ScatteredInterpolation: a far sample no longer rounds near distances to 0 ----------------

        private static double Idw(IEnumerable<(double D, double V)> near, double power)
        {
            double dmin = near.Min(s => s.D), num = 0, den = 0;
            foreach (var (d, v) in near) { double w = Math.Pow(dmin / d, power); num += w * v; den += w; }
            return num / den;
        }

        public void InverseDistance_AFarSampleLeavesSubnormalDistancesAlone()
        {
            const double eps = double.Epsilon;
            var far = (-1e308, 0.0, 1.0);
            Check.Close(4.4, ScatteredInterpolation.InverseDistance(new[] { far, (1e308, eps, 3.0), (1e308, 2 * eps, 10.0) }, 1e308, 0), 1e-12);
            Check.Close(7.846153846153847, ScatteredInterpolation.InverseDistance(new[] { far, (1e308, 3 * eps, 3.0), (1e308, 2 * eps, 10.0) }, 1e308, 0), 1e-12);
            // nearest and power count there too
            Check.Equal(3.0, ScatteredInterpolation.InverseDistance(new[] { far, (1e308, eps, 3.0), (1e308, 2 * eps, 10.0) }, 1e308, 0, nearest: 1));
            Check.Close(Idw(new[] { (1.0, 3.0), (2.0, 10.0) }, 1), ScatteredInterpolation.InverseDistance(new[] { far, (1e308, eps, 3.0), (1e308, 2 * eps, 10.0) }, 1e308, 0, power: 1), 1e-12);

            var rng = new Random(1563);
            for (int trial = 0; trial < 2000; trial++)
            {
                int m = rng.Next(1, 6);
                var near = new List<(double D, double V)>();
                var samples = new List<(double X, double Y, double Value)> { (-1e308, rng.NextDouble(), rng.NextDouble() * 10) };
                foreach (int k in Enumerable.Range(1, 40).OrderBy(_ => rng.Next()).Take(m))
                {
                    double v = rng.NextDouble() * 10;
                    near.Add((k, v));
                    samples.Insert(rng.Next(samples.Count + 1), (1e308, k * eps, v));
                }
                double power = 0.5 + rng.NextDouble() * 3;
                double want = Idw(near, power), got = ScatteredInterpolation.InverseDistance(samples, 1e308, 0, power);
                Check.True(Math.Abs(got - want) <= 1e-12 * 10, $"trial {trial}: {got:R} vs {want:R}");
            }
        }

        // A weight ratio below the double range was 0 even with a small power, where it is a real weight.
        public void InverseDistance_AnUnderflowingRatioWithASmallPowerStillWeighs()
        {
            var samples = new[] { (1e-200, 0.0, 0.0), (1e200, 0.0, 1.0) };
            double w = Math.Pow(10, -0.4);   // (1e-400)^0.001
            Check.Close(w / (1 + w), ScatteredInterpolation.InverseDistance(samples, 0, 0, power: 0.001), 1e-9);
            Check.Equal(0.0, ScatteredInterpolation.InverseDistance(samples, 0, 0, power: 2));
        }

        // ---------------- Angle.LerpDegrees: both ends exact ----------------

        public void LerpDegrees_BothEndsAreExact()
        {
            Check.Equal(193.9, Angle.LerpDegrees(-353.7, 193.9, 1));
            Check.Equal(0.2, Angle.LerpDegrees(359.9, 0.2, 1));
            var rng = new Random(1564);
            for (int trial = 0; trial < 20000; trial++)
            {
                double a = rng.Next(-7200, 7200) / 10.0, b = rng.Next(-7200, 7200) / 10.0;
                Check.Equal(Angle.NormalizeDegrees(a), Angle.LerpDegrees(a, b, 0));
                Check.Equal(Angle.NormalizeDegrees(b), Angle.LerpDegrees(a, b, 1));
                double t = rng.NextDouble();
                double want = Angle.NormalizeDegrees(a + Angle.DifferenceDegrees(a, b) * t);
                double got = Angle.LerpDegrees(a, b, t);
                Check.True(Math.Abs(Angle.DifferenceDegrees(want, got)) <= 1e-11, $"{a} to {b} at {t:R}: {got:R} vs {want:R}");
            }
        }

        // ---------------- RunningStatistics: a subnormal variance is rounded once ----------------

        // Samples {0, d} with d = M·2^-q and M² below 2^53: the internal sum of squares is exact, so the variance d²/4
        // should be that value rounded once. Graded against BigInteger rounding (half to even).
        public void RunningStatistics_ASubnormalVarianceIsRoundedOnce()
        {
            var rng = new Random(1565);
            int bad = 0, trials = 0;
            for (int trial = 0; trial < 20000; trial++)
            {
                long m = 67108864 + rng.Next(0, 27755000);          // [2^26, 2^26.5): M² has 53 bits
                if ((System.Numerics.BigInteger)m * m >= System.Numerics.BigInteger.One << 53) continue;
                int s = 2 * rng.Next(1, 26);                         // d²/4 in units of 2^-1074 is M² / 2^s
                int q = (s + 1072) / 2;
                double d = m * Math.Pow(2, -q);
                var sq = (System.Numerics.BigInteger)m * m;
                var floor = sq >> s;
                var rem = sq - (floor << s);
                var half = System.Numerics.BigInteger.One << (s - 1);
                if (rem > half || (rem == half && !floor.IsEven)) floor += 1;
                double want = (double)floor * double.Epsilon;
                var rs = new RunningStatistics();
                rs.Push(0);
                rs.Push(d);
                trials++;
                if (rs.PopulationVariance != want) bad++;
            }
            Check.True(trials > 10000, "only " + trials);
            Check.Equal(0, bad);
        }

        // ---------------- RandomUtils.NextExponential: never −0 ----------------

        private sealed class ZeroRandom : Random
        {
            protected override double Sample() => 0;
            public override double NextDouble() => 0;
        }

        public void NextExponential_ADrawOfZeroIsPositiveZero()
        {
            double x = new ZeroRandom().NextExponential(2);
            Check.Equal(0.0, x);
            Check.True(double.IsPositiveInfinity(1 / x), "−0");
        }

        // ---------------- ToleranceCheck: past an infinite limit the furthest sample is the worst ----------------

        public void Tolerance_PastAnInfiniteLimitTheFurthestSampleIsWorst()
        {
            Check.Equal(1, ToleranceCheck.Evaluate(new[] { 0, -1e308, 5 }, double.PositiveInfinity, double.PositiveInfinity).WorstViolationIndex);
            Check.Equal(2, ToleranceCheck.Evaluate(new[] { 0, 5, 1e308 }, double.NegativeInfinity, double.NegativeInfinity).WorstViolationIndex);
            Check.Equal(1, ToleranceCheck.Evaluate(new[] { 0, double.NaN, -1e308 }, double.PositiveInfinity, double.PositiveInfinity).WorstViolationIndex);
            // finite limits as before: a distance past the double range beats a finite one
            Check.Equal(1, ToleranceCheck.Evaluate(new[] { 0, -1.7e308, 1.75e308 }, 1e308, 1e308).WorstViolationIndex);
        }
    }
}

