using System;
using System.Collections.Generic;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 140 (statistics, eighth pass): each failed on the code before its fix.
    public sealed class StatisticsReview140Tests
    {
        // y near 1e300 over x near 1e-300: the scale ratio ys/xs overflowed and a zero slope read 0 · ∞ = NaN.
        public void LinearRegression_ZeroSlopeAcrossExtremeScales()
        {
            LinearFit fit = LinearRegression.Fit(new[] { -1e-300, 0, 1e-300 }, new[] { 1e300, 0, 1e300 });
            Check.Equal(0.0, fit.Slope);
            Check.True(Math.Abs(fit.Intercept - 2e300 / 3) <= 1e-15 * 2e300 / 3, fit.Intercept.ToString("R"));
            // The same zero slope with the scales the other way round already held; a guard.
            Check.Equal(0.0, LinearRegression.Fit(new[] { -1e300, 0, 1e300 }, new[] { 1e-300, 0, 1e-300 }).Slope);
        }

        // The same data: the slope's rounding crumb (1e-17 in scaled units) times 2^2000 is past MaxValue, but the
        // intercept was ∞·0 = NaN through it.
        public void MultipleRegression_InterceptAcrossExtremeScales()
        {
            RegressionResult fit = MultipleRegression.Fit(new double[,] { { -1e-300 }, { 0 }, { 1e-300 } }, new[] { 1e300, 0, 1e300 });
            Check.True(Math.Abs(fit.Coefficients[0] - 2e300 / 3) <= 1e-15 * 2e300 / 3, fit.Coefficients[0].ToString("R"));
            Check.False(double.IsNaN(fit.Coefficients[1]), "slope is NaN");
            Check.False(double.IsNaN(fit.StandardErrors[1]), "standard error is NaN");
            Check.Equal(2e300 / 3, fit.Predict(new[] { 0.0 }), "prediction at the centre");
        }

        // A coefficient truly past MaxValue made the intercept ∞ (b0 − ∞·c) though the intercept itself is finite.
        public void MultipleRegression_InterceptStaysFiniteBesideAnOverflowingSlope()
        {
            double[] xs = { -1e-300, 0, 1e-300, 3e-300 }, ys = { 1e300, -1e300, 1e300, -1e300 };
            var x = new double[4, 1];
            for (int i = 0; i < 4; i++) x[i, 0] = xs[i];
            RegressionResult fit = MultipleRegression.Fit(x, ys);
            // The same data in moderate units: intercept and slope·xbar scale by 1e300, so the intercept does too.
            var xm = new double[4, 1];
            for (int i = 0; i < 4; i++) xm[i, 0] = xs[i] * 1e300;
            RegressionResult moderate = MultipleRegression.Fit(xm, new[] { 1.0, -1, 1, -1 });
            double expected = moderate.Coefficients[0] * 1e300;
            Check.True(Math.Abs(fit.Coefficients[0] - expected) <= 1e-12 * Math.Abs(expected), $"{fit.Coefficients[0]:R} vs {expected:R}");
        }

        // The intercept from the halves can itself pass MaxValue: ∞ came back though the method refuses an overflow.
        public void TheilSen_RefusesAnInfiniteIntercept()
        {
            var ex = Check.Throws<ArgumentException>(() => RobustRegression.TheilSen(new[] { 1.0, 2, 3 }, new[] { 1e308, 0, -1e308 }));
            Check.Equal("y", ex.ParamName);
            var ok = RobustRegression.TheilSen(new[] { 1.0, 2, 3 }, new[] { 1e307, 0, -1e307 });   // guard: in range
            Check.Equal(-1e307, ok.Slope);
        }

        // {0, 5e-324} has a spread that rounds to 0, but the values are not equal.
        public void FitNormal_ATooSmallSpreadIsNotCalledEqual()
        {
            var ex = Check.Throws<ArgumentException>(() => DistributionFit.FitNormal(new[] { 0, double.Epsilon }));
            Check.Equal("sample", ex.ParamName);
            Check.False(ex.Message.Contains("equal"), ex.Message);
            var same = Check.Throws<ArgumentException>(() => DistributionFit.FitNormal(new[] { 2.0, 2.0 }));
            Check.True(same.Message.Contains("equal"), same.Message);
        }

        // The default mean difference summed raw values, so on data with an offset a rearrangement holding exactly the
        // observed values (1000.3 and 1000.7 swapped for their twins) came out past the tie tolerance and was not counted.
        public void PermutationTwoSample_ExactTiesOnOffsetData()
        {
            var r = PermutationTest.TwoSample(new[] { 1000.7, 1000.3, 1000.1 }, new[] { 1000.3, 1000.7, 1000.2 }, new Random(1), 100000);
            Check.True(r.IsExact, "exact");
            Check.Equal(1.0, r.PValue);

            // Against an exact count over the same doubles: each double is an integer times 2^-1074, and a split is as
            // extreme when nb·ΣA − na·ΣB compares with the observed one so, in BigInteger.
            var rng = new Random(140);
            double[] fractions = { 0.1, 0.2, 0.3, 0.7, 1.1 };
            for (int trial = 0; trial < 200; trial++)
            {
                double off = Math.Pow(10, 3 + rng.Next(10));
                int na = 2 + rng.Next(3), nb = 2 + rng.Next(3);
                var a = new double[na];
                var b = new double[nb];
                for (int i = 0; i < na; i++) a[i] = off + fractions[rng.Next(fractions.Length)];
                for (int i = 0; i < nb; i++) b[i] = off + fractions[rng.Next(fractions.Length)];
                var got = PermutationTest.TwoSample(a, b, new Random(1), 100000);
                Check.True(got.IsExact, "exact");
                double expected = ExactTwoSidedP(a, b);
                Check.True(Math.Abs(got.PValue - expected) < 1e-12, $"trial {trial}: {got.PValue:R} vs {expected:R} at offset {off:R}");
            }
        }

        private static BigInteger Units(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp != 0) mant |= 1L << 52; else exp = 1;
            BigInteger m = new BigInteger(mant) << (exp - 1);    // v = m · 2^-1074
            return bits < 0 ? -m : m;
        }

        private static double ExactTwoSidedP(double[] a, double[] b)
        {
            int na = a.Length, n = na + b.Length, nb = b.Length;
            var pooled = new BigInteger[n];
            BigInteger total = 0;
            for (int i = 0; i < n; i++) { pooled[i] = Units(i < na ? a[i] : b[i - na]); total += pooled[i]; }
            BigInteger Stat(BigInteger sa) => nb * sa - na * (total - sa);
            BigInteger obsA = 0;
            for (int i = 0; i < na; i++) obsA += pooled[i];
            BigInteger obs = Stat(obsA);
            int greater = 0, less = 0, count = 0;
            for (int mask = 0; mask < 1 << n; mask++)
            {
                int bitsSet = 0;
                BigInteger sa = 0;
                for (int i = 0; i < n; i++) if ((mask >> i & 1) != 0) { bitsSet++; sa += pooled[i]; }
                if (bitsSet != na) continue;
                count++;
                BigInteger s = Stat(sa);
                if (s >= obs) greater++;
                if (s <= obs) less++;
            }
            return Math.Min(1, 2 * Math.Min((double)greater / count, (double)less / count));
        }
    }
}
