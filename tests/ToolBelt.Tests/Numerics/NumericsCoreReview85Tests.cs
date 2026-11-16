using System;
using System.Globalization;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 85 (Numerics core, fifth pass).
    public sealed class NumericsCoreReview85Tests
    {
        // ---------- UnitConvert.TryParse ----------

        // An overflowing number parsed as ∞ on .NET 8 and failed on .NET Framework.
        public void UnitParseRefusesAnOverflowingNumberEverywhere()
        {
            Check.False(UnitConvert.TryParse("1e400 m", out double v, out string unit));
            Check.Equal(0.0, v);
            Check.Equal("", unit);
            Check.False(UnitConvert.TryParse("-1e309 kg", out _, out _));
            Check.True(UnitConvert.TryParse("1.7e308 m", out v, out _));
            Check.Equal(1.7e308, v);
        }

        // ---------- Uncertainty.Format ----------

        // The exponent form printed 17 or more digits with "F": .NET Framework padded zeros after 15 digits and .NET 8
        // showed binary noise, so the text differed by framework.
        public void UncertaintyFormatIsTheSameOnEveryFramework()
        {
            Check.Equal("(3.33333333333333300 ± 0.00000000000000030)e-1", Uncertainty.Format(1.0 / 3, 3e-17));
            Check.Equal("1.70000000000000000(10)e308", Uncertainty.Format(1.7e308, 1e292, UncertaintyStyle.Parenthetical));
            Check.Equal("(-1.23456789012345690 ± 0.00000000000000012)e-200",
                Uncertainty.Format(-1.2345678901234568e-200, 1.2e-216));
            // Unchanged where few digits are shown.
            Check.Equal("(1.235 ± 0.030)e-7", Uncertainty.Format(1.23456e-7, 3e-9));
            Check.Equal("1.235(30)e-7", Uncertainty.Format(1.23456e-7, 3e-9, UncertaintyStyle.Parenthetical));
            Check.Equal("(1.23457 ± 0.00023)e7", Uncertainty.Format(12345678.9, 2345));
            Check.Equal("(0.0 ± 1.2)e-300", Uncertainty.Format(-1e-320, 1.2e-300));
        }

#if NET
        // Graded against System.Decimal rounding of the shortest round-trip text (.NET's "R" is shortest from .NET Core 3.0).
        public void UncertaintyFormatAgreesWithDecimalRounding()
        {
            var rng = new Random(8501);
            int checkedCount = 0;
            for (int trial = 0; trial < 20000; trial++)
            {
                double value = (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(-8, 12));
                double u = Math.Abs(value) * Math.Pow(10, -rng.Next(1, 16)) * (0.5 + rng.NextDouble());
                if (u == 0) continue;
                string text = Uncertainty.Format(value, u, UncertaintyStyle.PlusMinus, UncertaintyRule.TwoSignificantDigits);
                if (!text.StartsWith("(", StringComparison.Ordinal)) continue;        // the plain form is not changed
                var (_, ru, decimals) = Uncertainty.Round(value, u, UncertaintyRule.TwoSignificantDigits);
                int e = int.Parse(text.Substring(text.IndexOf(")e", StringComparison.Ordinal) + 2), CultureInfo.InvariantCulture);
                int d = Math.Max(decimals + e, 0);
                if (decimals > 27 || e - d < -28 || e > 27) continue;                 // outside decimal's range
                decimal dv = decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
                decimal du = decimal.Parse(ru.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
                decimal scale = 1;
                for (int i = 0; i < Math.Abs(e); i++) scale *= 10;
                decimal mv = e >= 0 ? dv / scale : dv * scale, mu = e >= 0 ? du / scale : du * scale;
                string sv = Math.Round(mv, d, MidpointRounding.AwayFromZero).ToString("F" + d, CultureInfo.InvariantCulture);
                if (sv.StartsWith("-", StringComparison.Ordinal) && sv.Trim('-', '0', '.').Length == 0) sv = sv.Substring(1);
                string su = Math.Round(mu, d, MidpointRounding.AwayFromZero).ToString("F" + d, CultureInfo.InvariantCulture);
                Check.Equal("(" + sv + " ± " + su + ")e" + e.ToString(CultureInfo.InvariantCulture), text, $"{value:R} ± {u:R}");
                checkedCount++;
            }
            Check.True(checkedCount > 5000, "checked " + checkedCount);
        }
#endif

        // ---------- Polynomial ----------

        // A NaN leading coefficient threw ArithmeticException (Math.Sign(NaN)); an infinite one gave −∞ as a root.
        public void PolynomialRootsNeedFiniteCoefficients()
        {
            foreach (var c in new[] { new[] { 1.0, 2, 3, double.NaN }, new[] { double.NaN, 1, 1 }, new[] { 1.0, double.PositiveInfinity, 1, 1 },
                                      new[] { 1.0, 2, double.NegativeInfinity }, new[] { double.NaN, 1.0 } })
                Check.Throws<InvalidOperationException>(() => new Polynomial(c).RealRoots(), string.Join(",", c));
            Check.Close(1.0, new Polynomial(-1, 0, 0, 1).RealRoots().Single(), 1e-15);
        }

        // Newton polishing at a double root jumped to the simple root: (x − 15)²(x − 19) gave 19 twice and lost 15.
        public void CubicKeepsEveryRootOfExactDoubleRootCubics()
        {
            var got = new Polynomial(-4275, 795, -49, 1).RealRoots();
            Check.True(got.Any(r => Math.Abs(r - 15) < 1e-6) && got.Any(r => Math.Abs(r - 19) < 1e-6), string.Join(", ", got));
            // Random cubics with exact eighth-integer roots, one of them repeated: every root found, nothing else returned.
            var rng = new Random(8503);
            for (int trial = 0; trial < 20000; trial++)
            {
                double r1 = rng.Next(-200, 201) / 8.0, r2 = rng.Next(4) == 0 ? r1 : rng.Next(-200, 201) / 8.0, r3 = rng.Next(-200, 201) / 8.0;
                double lead = rng.Next(1, 9) * (rng.Next(2) == 0 ? 1 : -1);
                // lead·(x − r1)(x − r2)(x − r3), exact in doubles at these sizes.
                var p = new Polynomial(-lead * r1 * r2 * r3, lead * (r1 * r2 + r1 * r3 + r2 * r3), -lead * (r1 + r2 + r3), lead);
                var roots = p.RealRoots();
                var expected = new[] { r1, r2, r3 };
                string what = $"roots {r1}, {r2}, {r3} lead {lead}: got {string.Join(", ", roots.Select(r => r.ToString("R", CultureInfo.InvariantCulture)))}";
                foreach (double r in expected)
                    Check.True(roots.Any(x => Math.Abs(x - r) <= 1e-6 * Math.Max(1, Math.Abs(r))), what);
                foreach (double x in roots)
                    Check.True(expected.Any(r => Math.Abs(x - r) <= 1e-6 * Math.Max(1, Math.Abs(r))), what);
            }
        }

        // Distinct subnormal x halved to the same value, so the scale was 0 and the fit "had too few distinct x".
        public void PolynomialFitTakesDistinctSubnormalX()
        {
            const double eps = double.Epsilon;
            var p = Polynomial.Fit(new[] { 0.0, eps }, new[] { 1.0, 1.0 }, 1);
            Check.Close(1.0, p.Evaluate(0), 1e-15);
            p = Polynomial.Fit(new[] { -eps, eps }, new[] { 2.0, 2.0 }, 1);
            Check.Close(2.0, p.Evaluate(eps), 1e-15);
            p = Polynomial.Fit(new[] { -eps, 0, eps }, new[] { 3.0, 3.0, 3.0 }, 0);
            Check.Close(3.0, p.Evaluate(0), 1e-15);
        }

        // ---------- MovingAverage ----------

        // After huge samples overflowed the sum, the window stayed scaled by 2⁻³² until a lap rebuild, which kept it
        // scaled while they were in the window, so tiny samples after them were flushed to 0.
        public void MovingAverageLeavesScaledModeWhenTheHugeSamplesLeave()
        {
            var ma = new MovingAverage(4);
            ma.Add(1e308); ma.Add(1e308);
            Check.Equal(1e308, ma.Average);
            for (int i = 0; i < 4; i++) ma.Add(1e-320);
            Check.Equal(1e-320, ma.Average);
            // Graded against an exact window mean of tiny samples after huge ones, over random windows.
            var rng = new Random(8502);
            for (int trial = 0; trial < 300; trial++)
            {
                int w = rng.Next(1, 12);
                var m = new MovingAverage(w);
                var all = new System.Collections.Generic.List<double>();
                int n = rng.Next(1, 40);
                for (int i = 0; i < n; i++)
                {
                    double v = rng.Next(5) == 0 ? (rng.Next(2) == 0 ? 1.5e308 : -1.2e308) : (rng.NextDouble() + 1) * 1e-310;
                    all.Add(v);
                    m.Add(v);
                }
                var window = all.Skip(Math.Max(0, all.Count - w)).ToArray();
                if (window.Any(v => Math.Abs(v) > 1)) continue;
                // Subnormals are integer multiples of ε, so their sum in units of ε is exact.
                double units = window.Sum(v => v / double.Epsilon);
                double expected = units / window.Length * double.Epsilon;
                Check.True(Math.Abs(m.Average - expected) <= 2 * double.Epsilon, $"trial {trial}: {m.Average:R} vs {expected:R}");
            }
        }

        // ---------- ToleranceCheck ----------

        // Deviations past double.MaxValue overflowed to ∞ and tied, so the first such sample was reported as the worst.
        public void ToleranceWorstSampleIsTheFurthestEvenPastTheDoubleRange()
        {
            var r = ToleranceCheck.Evaluate(new[] { -1e308, -1.7e308 }, 1e308, 1.5e308);
            Check.Equal(1, r.WorstViolationIndex);
            Check.Equal(double.PositiveInfinity, r.WorstDeviation);
            r = ToleranceCheck.Evaluate(new[] { 1.7e308, -1.7e308, -1.6e308 }, -1e308, -1e308 + 1e292);
            Check.Equal(0, r.WorstViolationIndex);
            r = ToleranceCheck.Evaluate(new[] { -1.7e308, double.NaN }, 1e308, 1.5e308);
            Check.Equal(1, r.WorstViolationIndex);
            r = ToleranceCheck.Evaluate(new[] { 0.0, 5, -7, 6 }, 0, 1);
            Check.Equal(2, r.WorstViolationIndex);
            Check.Equal(7.0, r.WorstDeviation);
        }

        // ---------- MinMaxScaler ----------

        public void MinMaxScalerRefusesNonFiniteTargetBounds()
        {
            var data = new[] { 1.0, 2, 3 };
            Check.Equal("newMax", Check.Throws<ArgumentOutOfRangeException>(() => MinMaxScaler.ToRange(data, 0, double.PositiveInfinity)).ParamName);
            Check.Equal("newMin", Check.Throws<ArgumentOutOfRangeException>(() => MinMaxScaler.ToRange(data, double.NegativeInfinity, 0)).ParamName);
            Check.Equal("newMin", Check.Throws<ArgumentOutOfRangeException>(() => MinMaxScaler.ToRange(data, double.NaN, 1)).ParamName);
            Check.Equal("newMax", Check.Throws<ArgumentOutOfRangeException>(() => MinMaxScaler.Scale(2, 1, 3, 0, double.NaN)).ParamName);
            Check.Equal(0.5, MinMaxScaler.Scale(2, 1, 3, 0, 1));
        }

        // ---------- Vector2 ----------

        public void NormalizingANaNVectorGivesNaN()
        {
            foreach (var v in new[] { new Vector2(double.PositiveInfinity, double.NaN), new Vector2(double.NaN, double.NegativeInfinity), new Vector2(double.NaN, 0), new Vector2(3, double.NaN) })
            {
                var n = v.Normalized();
                Check.True(double.IsNaN(n.X) && double.IsNaN(n.Y), v.ToString());
            }
            var inf = new Vector2(double.PositiveInfinity, 3).Normalized();
            Check.Equal(1.0, inf.X);
            Check.Equal(0.0, inf.Y);
        }

        // ---------- LinearAlgebra ----------

        // A matrix with non-zero lower bounds was indexed from 0 and threw IndexOutOfRangeException.
        public void LinearAlgebraRefusesMatricesNotIndexedFromZero()
        {
            var a = (double[,])Array.CreateInstance(typeof(double), new[] { 2, 2 }, new[] { 1, 1 });
            a[1, 1] = 2; a[2, 2] = 3;
            Check.Equal("a", Check.Throws<ArgumentException>(() => LinearAlgebra.Determinant(a)).ParamName);
            Check.Equal("a", Check.Throws<ArgumentException>(() => LinearAlgebra.Transpose(a)).ParamName);
            Check.Equal("a", Check.Throws<ArgumentException>(() => LinearAlgebra.Solve(a, new[] { 1.0, 1 })).ParamName);
            Check.Equal("a", Check.Throws<ArgumentException>(() => LinearAlgebra.Inverse(a)).ParamName);
            Check.Equal("a", Check.Throws<ArgumentException>(() => LinearAlgebra.Multiply(a, new[] { 1.0, 1 })).ParamName);
            var ok = new double[,] { { 2, 0 }, { 0, 3 } };
            Check.Equal("b", Check.Throws<ArgumentException>(() => LinearAlgebra.Multiply(ok, a)).ParamName);
            Check.Equal("a", Check.Throws<ArgumentException>(() => LinearAlgebra.Multiply(a, ok)).ParamName);
            Check.Equal(6.0, LinearAlgebra.Determinant(ok));
        }

        // ---------- ScatteredInterpolation ----------

        // A NaN or infinite query point gave NaN, which here means "no samples within the radius".
        public void ScatteredInterpolationRefusesNonFinitePoints()
        {
            var samples = new[] { (0.0, 0.0, 1.0), (1.0, 1.0, 2.0) };
            Check.Equal("x", Check.Throws<ArgumentOutOfRangeException>(() => ScatteredInterpolation.InverseDistance(samples, double.NaN, 0)).ParamName);
            Check.Equal("y", Check.Throws<ArgumentOutOfRangeException>(() => ScatteredInterpolation.InverseDistance(samples, 0, double.PositiveInfinity)).ParamName);
            Check.Equal("xMin", Check.Throws<ArgumentOutOfRangeException>(() => ScatteredInterpolation.ToGrid(samples, double.NegativeInfinity, 1, 3, 0, 1, 3)).ParamName);
            Check.Equal("yMax", Check.Throws<ArgumentOutOfRangeException>(() => ScatteredInterpolation.ToGrid(samples, 0, 1, 3, 0, double.NaN, 3)).ParamName);
            Check.Equal(1.0, ScatteredInterpolation.InverseDistance(samples, 0, 0));
        }
            // ---------- Integration ----------

        // Five samples that agree by aliasing were accepted: sin²(πx) is 0 at all of them on [0, 4].
        public void AdaptiveSimpsonDoesNotTrustTheFirstFiveSamples()
        {
            Check.Close(2.0, Integration.AdaptiveSimpson(x => Math.Sin(Math.PI * x) * Math.Sin(Math.PI * x), 0, 4), 1e-8);
            Check.Close(1.0, Integration.AdaptiveSimpson(x => 1 - Math.Cos(2 * Math.PI * x), 0, 1), 1e-8);
            Check.Close(Math.E - 1, Integration.AdaptiveSimpson(Math.Exp, 0, 1), 1e-12);
        }

        // ---------- MonotoneCubic and AkimaSpline ----------

        // Scaling x to bring 1e308 under 2^500 flushed 1e-310 to 0: the knots collapsed, the curve went NaN and the middle
        // knot was missed.
        public void SplinesKeepTinyKnotsBesideHugeOnes()
        {
            var mc = new MonotoneCubic(new[] { 0.0, 1e-310, 1e308 }, new[] { 0.0, 1, 2 });
            Check.Equal(1.0, mc.Interpolate(1e-310));
            double mid = mc.Interpolate(5e307);
            Check.True(mid >= 1 && mid <= 2, mid.ToString("R", CultureInfo.InvariantCulture));
            double step = mc.Interpolate(0.5e-310);
            Check.True(step >= 0 && step <= 1, step.ToString("R", CultureInfo.InvariantCulture));
            var ak = new AkimaSpline(new[] { 0.0, 1e-310, 1e308, 1.5e308 }, new[] { 0.0, 1, 2, 3 });
            Check.Equal(1.0, ak.Interpolate(1e-310));
            Check.False(double.IsNaN(ak.Interpolate(5e307)));
            Check.False(double.IsNaN(ak.Interpolate(0.5e-310)));
            Check.False(double.IsNaN(ak.Derivative(5e307)));
            // A span past double.MaxValue still works.
            var wide = new MonotoneCubic(new[] { -1.5e308, 0, 1.5e308 }, new[] { 0.0, 1, 2 });
            Check.Close(1.5, wide.Interpolate(0.75e308), 0.2);
            Check.Equal(2.0, wide.Interpolate(1.5e308));
        }

        // h00·y0 + h01·y1 rounded off a flat segment: 0.1 came back 0.10000000000000002 between equal knots.
        public void SplinesReproduceFlatSegmentsExactly()
        {
            var mc = new MonotoneCubic(new[] { 0.0, 1, 2, 3 }, new[] { 0.0, 0.1, 0.1, 0.3 });
            var ak = new AkimaSpline(new[] { 0.0, 1, 2, 3 }, new[] { 0.1, 0.1, 0.1, 0.1 });
            var mk = new AkimaSpline(new[] { 0.0, 1, 2, 3, 4 }, new[] { 0.0, 0.1, 0.1, 0.1, 0.5 }, modified: true);
            for (int i = 0; i <= 1000; i++)
            {
                double t = i / 1000.0;
                Check.Equal(0.1, mc.Interpolate(1 + t));
                Check.Equal(0.1, ak.Interpolate(3 * t));
                Check.Equal(0.1, mk.Interpolate(1 + t));
            }
            // Monotone data never leaves a segment's range, over random data.
            var rng = new Random(8504);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = rng.Next(2, 10);
                var x = new double[n];
                var y = new double[n];
                for (int i = 0; i < n; i++)
                {
                    x[i] = i == 0 ? rng.NextDouble() : x[i - 1] + rng.NextDouble() + 1e-3;
                    y[i] = i == 0 ? rng.NextDouble() : y[i - 1] + (rng.Next(3) == 0 ? 0 : rng.NextDouble());
                }
                var c = new MonotoneCubic(x, y);
                for (int i = 0; i < n - 1; i++)
                    for (int k = 1; k < 20; k++)
                    {
                        double v = c.Interpolate(x[i] + (x[i + 1] - x[i]) * k / 20);
                        Check.True(v >= y[i] && v <= y[i + 1], $"trial {trial} segment {i}: {v:R} outside [{y[i]:R}, {y[i + 1]:R}]");
                    }
            }
        }

        // ---------- Means ----------

        // sum / n rounds twice, so the mean of {0.1, 0.1, 0.1} was 0.10000000000000002, above the maximum.
        public void ArithmeticMeanStaysBetweenTheExtremes()
        {
            Check.Equal(0.1, Means.Arithmetic(new[] { 0.1, 0.1, 0.1 }));
            Check.Equal(6.913138992797181, Means.Arithmetic(Enumerable.Repeat(6.913138992797181, 6).ToArray()));
            var rng = new Random(8505);
            for (int trial = 0; trial < 20000; trial++)
            {
                int n = rng.Next(1, 12);
                double c = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-5, 6));
                var v = Enumerable.Range(0, n).Select(_ => rng.Next(2) == 0 ? c : c + c * 1e-15 * rng.Next(3)).ToArray();
                double m = Means.Arithmetic(v);
                Check.True(m >= v.Min() && m <= v.Max(), $"trial {trial}: {m:R}");
            }
        }

        // ---------- UnitConvert ----------

        // π/180 over π/200 in doubles: 90 deg came out 99.99999999999999 grad.
        public void AngleConversionsBetweenDegreesGradiansAndTurnsAreExact()
        {
            Check.Equal(100.0, UnitConvert.Convert(90, "deg", "grad"));
            Check.Equal(400.0, UnitConvert.Convert(1, "turn", "grad"));
            Check.Equal(0.25, UnitConvert.Convert(90, "deg", "turn"));
            for (int v = -3600; v <= 3600; v++)
            {
                Check.Equal(RatioToDouble(v * 10, 9), UnitConvert.Convert(v, "deg", "grad"), "deg " + v);
                Check.Equal(RatioToDouble(v * 9, 10), UnitConvert.Convert(v, "grad", "deg"), "grad " + v);
                Check.Equal(RatioToDouble(v, 360), UnitConvert.Convert(v, "deg", "turn"), "turn " + v);
            }
            Check.Close(Math.PI / 2, UnitConvert.Convert(90, "deg", "rad"), 1e-15);
        }

        // n/d rounded once to the nearest double, half to even (the cast from decimal is not correctly rounded).
        private static double RatioToDouble(System.Numerics.BigInteger n, System.Numerics.BigInteger d)
        {
            if (n.IsZero) return 0;
            int sign = n.Sign * d.Sign;
            n = System.Numerics.BigInteger.Abs(n); d = System.Numerics.BigInteger.Abs(d);
            int k = 64 - (Bits(n) - Bits(d));
            if (k >= 0) n <<= k; else d <<= -k;
            var q = System.Numerics.BigInteger.DivRem(n, d, out var r);
            int shift = Bits(q) - 53;
            var low = q & ((System.Numerics.BigInteger.One << shift) - 1);
            var half = System.Numerics.BigInteger.One << (shift - 1);
            q >>= shift;
            if (low > half || (low == half && (!r.IsZero || !q.IsEven))) q += 1;
            return sign * (double)q * Math.Pow(2, shift - k);
        }

        private static int Bits(System.Numerics.BigInteger x)
        {
            int b = 0;
            for (; x > 0; x >>= 1) b++;
            return b;
        }

        // ---------- MinMaxScaler ----------

        // newMin + 1·(newMax − newMin) does not round back to newMax: [0.6, 1.61] mapped the maximum to 1.6100000000000003.
        public void MinMaxScalerHitsTheTargetBoundsExactly()
        {
            var r = MinMaxScaler.ToRange(new[] { 0.0, 0.5, 1 }, 0.6, 1.61);
            Check.Equal(1.61, r[2]);
            var rng = new Random(8506);
            for (int trial = 0; trial < 20000; trial++)
            {
                double lo = rng.NextDouble() * 10 - 5, hi = lo + rng.NextDouble() * 10;
                var data = Enumerable.Range(0, rng.Next(2, 8)).Select(_ => rng.NextDouble() * 100).ToArray();
                var s = MinMaxScaler.ToRange(data, lo, hi);
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == data.Max() && data.Max() != data.Min()) Check.Equal(hi, s[i], "max, trial " + trial);
                    if (data[i] == data.Min()) Check.Equal(lo, s[i], "min, trial " + trial);
                    Check.True(s[i] >= lo && s[i] <= hi, $"trial {trial}: {s[i]:R} outside [{lo:R}, {hi:R}]");
                }
            }
        }

        // ---------- Differentiation ----------

        // The Lagrange weights and the one-sided end formulas left round-off on constant data.
        public void DerivativeOfConstantDataIsZero()
        {
            foreach (double d in Differentiation.Sample(new[] { 0.0, 0.1, 0.3, 0.7 }, new[] { 0.1, 0.1, 0.1, 0.1 })) Check.Equal(0.0, d);
            foreach (double d in Differentiation.Sample(new[] { 1e6, 1e6 + 0.1, 1e6 + 0.3 }, new[] { 5.0, 5, 5 })) Check.Equal(0.0, d);
            foreach (double d in Differentiation.Sample(new[] { 1e8 + 0.1, 1e8 + 0.1, 1e8 + 0.1 }, 0.1)) Check.Equal(0.0, d);
            // Quadratics are still exact (to round-off), over random uneven grids.
            var rng = new Random(8507);
            for (int trial = 0; trial < 500; trial++)
            {
                double a = rng.NextDouble() * 4 - 2, b = rng.NextDouble() * 4 - 2, c = rng.NextDouble() * 4 - 2;
                var x = new double[6];
                for (int i = 0; i < 6; i++) x[i] = (i == 0 ? -1 : x[i - 1]) + 0.05 + rng.NextDouble();
                var d = Differentiation.Sample(x, x.Select(t => (a * t + b) * t + c).ToArray());
                for (int i = 0; i < 6; i++) Check.Close(2 * a * x[i] + b, d[i], 1e-9, $"trial {trial} at {i}");
            }
        }

        // Found while testing the above: positions spread past double.MaxValue overflowed every spacing, so the ends were NaN.
        public void DerivativeOverASpanPastTheDoubleRange()
        {
            var w = Differentiation.Sample(new[] { -1e308, 0, 1e308 }, new[] { 1.0, 0, 1 });
            Check.Close(0.0, w[1], 1e-300);
            Check.True(w[0] < 0 && w[2] > 0, string.Join(", ", w));
        }

        // ---------- Vector2 ----------

        public void InfiniteVectorsAreApproximatelyEqualToThemselves()
        {
            foreach (var v in new[] { new Vector2(double.PositiveInfinity, 0), new Vector2(1, double.NegativeInfinity), new Vector2(double.NaN, 2) })
                Check.True(v.ApproximatelyEquals(v), v.ToString());
            Check.False(new Vector2(double.PositiveInfinity, 0).ApproximatelyEquals(new Vector2(double.NegativeInfinity, 0)));
            Check.True(new Vector2(1, 2).ApproximatelyEquals(new Vector2(1 + 1e-12, 2)));
        }

        // ---------- RootFinding ----------

        public void BisectionFailureRootStaysInsideAHugeBracket()
        {
            var r = RootFinding.Bisection(x => x - 1.6e308, 1e308, 1.7e308, 1e-12, maxIterations: 10);
            Check.False(r.Converged);
            Check.True(r.Root >= 1e308 && r.Root <= 1.7e308, r.Root.ToString("R", CultureInfo.InvariantCulture));
        }

        public void RootFindersRefuseNoIterations()
        {
            Func<double, double> f = x => x - 0.5;
            Check.Equal("maxIterations", Check.Throws<ArgumentOutOfRangeException>(() => RootFinding.Bisection(f, 0, 1, 1e-12, 0)).ParamName);
            Check.Equal("maxIterations", Check.Throws<ArgumentOutOfRangeException>(() => RootFinding.Brent(f, 0, 1, 1e-12, -5)).ParamName);
            Check.Equal("maxIterations", Check.Throws<ArgumentOutOfRangeException>(() => RootFinding.Newton(f, 1, null, 1e-12, 0)).ParamName);
            Check.Equal("maxIterations", Check.Throws<ArgumentOutOfRangeException>(() => RootFinding.Secant(f, 0, 1, 1e-12, 0)).ParamName);
            Check.True(RootFinding.Bisection(f, 0, 1, 1e-12, 1).Iterations <= 1);
        }

        // ---------- Uncertainty.Round ----------

        // A +1e-9 fudge on the shifted double made leading digits 354.999… count as 355 (one digit, not two).
        public void UncertaintyLeadDigitsAreReadExactly()
        {
            Check.Equal(0.035, Uncertainty.Round(1, 0.0354999999999).Uncertainty);
            var (_, u, decimals) = Uncertainty.Round(1, 354.9999999999);
            Check.Equal(350.0, u);
            Check.Equal(-1, decimals);
            Check.Equal(0.04, Uncertainty.Round(1, 0.0355).Uncertainty);
            Check.Equal(0.10, Uncertainty.Round(1, 0.096).Uncertainty);
            Check.Equal(0.94, Uncertainty.Round(1, 0.94, UncertaintyRule.TwoSignificantDigits).Uncertainty);
#if NET
            // Graded against the leading digits of the shortest round-trip text ("R" is shortest from .NET Core 3.0): 0.355
            // is read as 355, though the double is 0.35499999999999998.
            var rng = new Random(8508);
            for (int trial = 0; trial < 20000; trial++)
            {
                double un = (100 + rng.Next(900) + (rng.Next(2) == 0 ? 0 : 1 - Math.Pow(10, -rng.Next(1, 12)))) * Math.Pow(10, rng.Next(-16, 20));
                decimal dv = decimal.Parse(un.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
                string digitsText = dv.ToString(CultureInfo.InvariantCulture).Replace(".", "").TrimStart('0') + "00";
                int lead = int.Parse(digitsText.Substring(0, 3), CultureInfo.InvariantCulture);
                int e0 = 0;
                for (decimal p = 1; p * 10 <= dv; p *= 10) e0++;
                for (decimal p = 1; p > dv; p /= 10) e0--;
                int digits = lead <= 354 ? 2 : lead <= 949 ? 1 : 2;
                var (_, ru, dec) = Uncertainty.Round(0, un);
                int e = e0 + (lead >= 950 ? 1 : 0);
                Check.Equal(digits - 1 - e, dec, $"{un:R}");
            }
#endif
        }
    }
}
