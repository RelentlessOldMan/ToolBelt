using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using Vector2 = ToolBelt.Numerics.Vector2;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>
    /// Regressions from review round 177 (eleventh pass over the numerics core): each test failed before its fix.
    /// Graded against exact BigInteger arithmetic over randomized trials where a reference exists.
    /// </summary>
    public sealed class NumericsCoreReview177Tests
    {
        // ---- exact references ----

        // A double as a whole number of 2^-1074 units.
        private static BigInteger Units(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int be = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else m |= 1L << 52;
            BigInteger u = (BigInteger)m << (be - 1);
            return v < 0 ? -u : u;
        }

        private static readonly BigInteger One = BigInteger.One << 1074;

        // n / d rounded to the nearest double, ties to even (d > 0).
        private static double Nearest(BigInteger n, BigInteger d)
        {
            if (n.IsZero) return 0;
            bool neg = n.Sign < 0;
            n = BigInteger.Abs(n);
            int e = (int)Math.Floor(BigInteger.Log(n, 2) - BigInteger.Log(d, 2));
            while (e >= 0 ? n < d << e : n << -e < d) e--;
            while (e + 1 >= 0 ? n >= d << (e + 1) : n << -(e + 1) >= d) e++;
            if (e > 1023) return neg ? double.NegativeInfinity : double.PositiveInfinity;
            int sh = 52 - Math.Max(e, -1022);
            BigInteger num = sh >= 0 ? n << sh : n, den = sh >= 0 ? d : d << -sh;
            BigInteger q = BigInteger.DivRem(num, den, out BigInteger rem);
            int c = (rem * 2).CompareTo(den);
            if (c > 0 || (c == 0 && !q.IsEven)) q++;
            double r = (double)q;
            for (int s = sh; s > 0; s -= Math.Min(s, 1000)) r /= Math.Pow(2, Math.Min(s, 1000)); // exact: q has 53 bits
            for (int s = -sh; s > 0; s -= Math.Min(s, 1000)) r *= Math.Pow(2, Math.Min(s, 1000));
            return neg ? -r : r;
        }

        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a + 0.0) == BitConverter.DoubleToInt64Bits(b + 0.0);

        // A sample that is huge (cancelling neighbours), ordinary, or tiny (down to subnormal).
        private static double Mixed(Random rng)
        {
            double sign = rng.Next(2) == 0 ? -1 : 1;
            switch (rng.Next(4))
            {
                case 0: return sign * (0.5 + rng.NextDouble()) * 1e308;
                case 1: return sign * rng.NextDouble() * Math.Pow(10, rng.Next(-20, 20));
                case 2: return sign * rng.NextDouble() * Math.Pow(10, -rng.Next(290, 320));
                default: return sign * rng.Next(1, 100);
            }
        }

        // ---- MovingAverage ----

        // Once the window's plain sum overflowed, every sample was summed scaled by 2^-32, which rounded tiny ones
        // (with 1e308, 1e308, -1e308, -1e308 in the window, 3e-300 averaged 38 ulps off, two 1e-320s averaged 0).
        public void MovingAverage_TinySamplesBesideCancelledHugeOnesStayExact()
        {
            var ma = new MovingAverage(5);
            foreach (double v in new[] { 1e308, 1e308, -1e308, -1e308, 3e-300 }) ma.Add(v);
            Check.Equal(R(3e-300 / 5), R(ma.Average));
            var mb = new MovingAverage(6);
            foreach (double v in new[] { 1e308, 1e308, -1e308, -1e308, 1e-320, 1e-320 }) mb.Add(v);
            Check.Equal(R(2e-320 / 6), R(mb.Average));

            var rng = new Random(177);
            int bad = 0, checkedCount = 0;
            string? first = null;
            for (int trial = 0; trial < 400; trial++)
            {
                int w = rng.Next(2, 9);
                var avg = new MovingAverage(w);
                var seen = new List<double>();
                for (int i = 0; i < 40; i++)
                {
                    double v = Mixed(rng);
                    seen.Add(v);
                    avg.Add(v);
                    double got = avg.Average;
                    var window = seen.Skip(Math.Max(0, seen.Count - w)).ToList();
                    BigInteger sum = 0;
                    foreach (double s in window) sum += Units(s);
                    double total = Nearest(sum, One);
                    if (double.IsInfinity(total)) continue; // a sum beyond the range is averaged scaled
                    double expected = total / window.Count;
                    checkedCount++;
                    if (!Same(got, expected))
                    {
                        bad++;
                        first ??= "window " + string.Join(", ", window.Select(R)) + ": expected " + R(expected) + ", got " + R(got);
                    }
                }
            }
            Check.True(checkedCount > 5000, "too few finite windows: " + checkedCount);
            Check.Equal(0, bad, first);
        }

        // ---- RunningStatistics ----

        // The sum was the plain running sum, with a double-double copy read only after it overflowed: 1e308, 61, -1e308
        // summed to 0, and so did 1.7e308, 3e307, 61, -1.7e308, -3e307.
        public void RunningStatistics_SumIsExactAndRoundedOnce()
        {
            var a = new RunningStatistics();
            foreach (double v in new[] { 1e308, 61, -1e308 }) a.Push(v);
            Check.Equal(61.0, a.Sum);
            var b = new RunningStatistics();
            foreach (double v in new[] { 1.7e308, 3e307, 61, -1.7e308, -3e307 }) b.Push(v);
            Check.Equal(61.0, b.Sum);

            var rng = new Random(1770);
            int bad = 0;
            string? first = null;
            for (int trial = 0; trial < 3000; trial++)
            {
                var rs = new RunningStatistics();
                BigInteger sum = 0;
                var values = new List<double>();
                int n = rng.Next(1, 12);
                for (int i = 0; i < n; i++)
                {
                    double v = Mixed(rng);
                    values.Add(v);
                    rs.Push(v);
                    sum += Units(v);
                }
                double expected = Nearest(sum, One), got = rs.Sum;
                if (!Same(got, expected))
                {
                    bad++;
                    first ??= string.Join(", ", values.Select(R)) + ": expected " + R(expected) + ", got " + R(got);
                }
            }
            Check.Equal(0, bad, first);
        }

        // ---- MonotoneCubic ----

        // The cubic was evaluated in plain doubles, a few ulps off, so on increasing data it went down by an ulp inside
        // a segment. Walked ulp by ulp through random segments, it never steps back.
        public void MonotoneCubic_NeverStepsBackInsideASegment()
        {
            var rng = new Random(17700);
            int bad = 0;
            string? first = null;
            for (int trial = 0; trial < 400; trial++)
            {
                int n = rng.Next(3, 8);
                double scale = Math.Pow(10, rng.Next(-3, 6));
                bool up = rng.Next(2) == 0;
                var x = new double[n];
                var y = new double[n];
                double xs = rng.NextDouble() * 5, ys = rng.NextDouble() * scale;
                for (int i = 0; i < n; i++)
                {
                    xs += 0.01 + rng.NextDouble() * 3;
                    ys += rng.Next(5) == 0 ? 0 : rng.NextDouble() * scale;
                    x[i] = xs;
                    y[i] = up ? ys : -ys;
                }
                var mc = new MonotoneCubic(x, y);
                for (int seg = 0; seg < 3; seg++)
                {
                    int k = rng.Next(n - 1);
                    double at = x[k] + rng.NextDouble() * (x[k + 1] - x[k]);
                    double prev = mc.Interpolate(at);
                    for (int step = 0; step < 600; step++)
                    {
                        at = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(at) + 1);
                        double v = mc.Interpolate(at);
                        if (up ? v < prev : v > prev)
                        {
                            bad++;
                            first ??= "x {" + string.Join(", ", x.Select(R)) + "}, y {" + string.Join(", ", y.Select(R)) + "}: " + R(prev) + " then " + R(v) + " at " + R(at);
                        }
                        prev = v;
                    }
                }
            }
            Check.Equal(0, bad, first);
        }

        // ---- StreamingQuantile ----

        // The estimate past five values was the middle marker, which starts at the median whatever p is and moves one
        // place a sample: on 1..6, p = 0.99 went from 4.96 to 3. Sorted data keeps the markers on the data, so the
        // estimate is the exact quantile.
        public void StreamingQuantile_DoesNotJumpAtTheSixthValue()
        {
            foreach (double p in new[] { 0.01, 0.1, 0.25, 0.5, 0.75, 0.9, 0.99 })
            {
                var sq = new StreamingQuantile(p);
                for (int n = 1; n <= 60; n++)
                {
                    sq.Add(n);
                    double exact = 1 + (n - 1) * p;
                    Check.True(Math.Abs(sq.Estimate - exact) <= 1e-9 * n, "p = " + R(p) + ", 1.." + n + ": expected " + R(exact) + ", got " + R(sq.Estimate));
                }
            }
        }

        // ---- Angle ----

        // to % 360 - from % 360 was rounded at the size of the difference (up to 720), not of the result:
        // DifferenceDegrees(359, 1e-14) gave 1, not 1.00000000000001.
        public void Angle_DifferenceDegreesIsRoundedOnce()
        {
            Check.Equal(R(1.00000000000001), R(Angle.DifferenceDegrees(359, 1e-14)));
            var rng = new Random(177000);
            BigInteger full = 360 * One, half = 180 * One;
            int bad = 0;
            string? first = null;
            for (int trial = 0; trial < 20000; trial++)
            {
                double from = Pick(rng), to = Pick(rng);
                BigInteger d = (Units(to) - Units(from)) % full;
                if (d > half) d -= full;
                else if (d <= -half) d += full;
                double expected = Nearest(d, One);
                if (expected == -180) expected = 180;
                double got = Angle.DifferenceDegrees(from, to);
                if (!Same(got, expected))
                {
                    bad++;
                    first ??= "(" + R(from) + ", " + R(to) + "): expected " + R(expected) + ", got " + R(got);
                }
            }
            Check.Equal(0, bad, first);
        }

        private static double Pick(Random rng)
        {
            double sign = rng.Next(2) == 0 ? -1 : 1;
            switch (rng.Next(5))
            {
                case 0: return sign * rng.NextDouble() * 720;
                case 1: return sign * rng.NextDouble() * Math.Pow(10, -rng.Next(1, 20));
                case 2: return sign * rng.NextDouble() * Math.Pow(10, rng.Next(3, 20));
                case 3: return sign * (180 - rng.NextDouble() * 1e-12);
                default: return sign * rng.Next(0, 721);
            }
        }

        // ---- RandomUtils ----

        // Below shape 1 the scale went into the Gamma(k + 1) draw before U^(1/k) shrank it, so a large scale overflowed
        // draws far below double.MaxValue: NextGamma(0.5, double.MaxValue) overflowed far more often than
        // P(Gamma(0.5) > 1) = erfc(1), about 0.157, and NextGamma(0.001, double.MaxValue) gave NaN.
        public void NextGamma_ShapeBelowOneTakesAHugeScale()
        {
            var rng = new Random(17);
            int inf = 0, nan = 0;
            for (int i = 0; i < 4000; i++)
            {
                double g = rng.NextGamma(0.5, double.MaxValue);
                if (double.IsNaN(g)) nan++;
                else if (double.IsInfinity(g)) inf++;
            }
            Check.Equal(0, nan);
            Check.True(inf > 4000 * 0.12 && inf < 4000 * 0.20, "shape 0.5 overflow rate " + inf / 4000.0);
            int bad = 0, inf2 = 0;
            for (int i = 0; i < 4000; i++)
            {
                double g = rng.NextGamma(0.001, double.MaxValue);
                if (double.IsNaN(g) || g < 0) bad++;
                else if (double.IsInfinity(g)) inf2++;
            }
            Check.Equal(0, bad);
            Check.True(inf2 < 20, "shape 0.001 overflowed " + inf2 + " times (P(Gamma(0.001) > 1) is about 2e-4)");
            // Scale enters only as a factor: Gamma(0.5, 1e300) / 1e300 has mean 0.5.
            double sum = 0;
            for (int i = 0; i < 20000; i++) sum += rng.NextGamma(0.5, 1e300) / 1e300;
            Check.Close(0.5, sum / 20000, 0.03);
        }

        // ---- Lerp ----

        // a + (b - a)*t was inf + (-inf)*t for an infinite first end: Lerp(inf, 1, 0.5) was NaN though Lerp(1, inf, 0.5)
        // was inf. Lerp(a, b, t) is Lerp(b, a, 1 - t) for every finite t.
        public void Lerp_InfiniteEndsAreSymmetric()
        {
            Check.Equal(double.PositiveInfinity, Interpolation.Lerp(double.PositiveInfinity, 1, 0.5));
            Check.Equal(double.NegativeInfinity, Vector2.Lerp(new Vector2(double.NegativeInfinity, 0), new Vector2(1, 0), 0.5).X);
            double[] ends = { double.PositiveInfinity, double.NegativeInfinity, 1, -3, 0 };
            double[] ts = { -2, -0.5, 0.25, 0.5, 0.75, 1.5, 3 }; // 1 - t is exact for each
            foreach (double a in ends)
                foreach (double b in ends)
                    foreach (double t in ts)
                    {
                        if (!double.IsInfinity(a) && !double.IsInfinity(b)) continue;
                        if (a == b) { Check.Equal(a, Interpolation.Lerp(a, b, t)); continue; } // equal ends are that end
                        double l = Interpolation.Lerp(a, b, t), m = Interpolation.Lerp(b, a, 1 - t);
                        double expected = (1 - t) * a + t * b;
                        string what = "Lerp(" + R(a) + ", " + R(b) + ", " + R(t) + ")";
                        Check.Equal(R(expected), R(l), what);
                        Check.Equal(R(l), R(m), what + " against its mirror");
                        Check.Equal(R(l), R(Vector2.Lerp(new Vector2(a, 0), new Vector2(b, 0), t).X), what + " as a Vector2");
                    }
        }

        // ---- Differentiation ----

        // x was never checked: Derivative(f, inf, 1) blamed h, and without a step a non-finite x came back NaN.
        public void Derivative_RejectsANonFiniteX()
        {
            Func<double, double> f = v => v * v;
            foreach (double x in new[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN })
            {
                Check.Equal("x", Check.Throws<ArgumentOutOfRangeException>(() => Differentiation.Derivative(f, x)).ParamName);
                Check.Equal("x", Check.Throws<ArgumentOutOfRangeException>(() => Differentiation.Derivative(f, x, 1)).ParamName);
                Check.Equal("x", Check.Throws<ArgumentOutOfRangeException>(() => Differentiation.RichardsonDerivative(f, x)).ParamName);
                Check.Equal("x", Check.Throws<ArgumentOutOfRangeException>(() => Differentiation.RichardsonDerivative(f, x, 1)).ParamName);
            }
        }

        // ---- ToString ----

        // A decimal string read as the nearest double, exactly (.NET Framework's double.Parse is not correctly rounded:
        // it reads "1.817281725805849E-162" an ulp off).
        private static double ExactParse(string s)
        {
            bool neg = s.StartsWith("-", StringComparison.Ordinal);
            if (neg) s = s.Substring(1);
            int exp = 0, at = s.IndexOf('E');
            if (at >= 0) { exp = int.Parse(s.Substring(at + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); s = s.Substring(0, at); }
            int dot = s.IndexOf('.');
            if (dot >= 0) { exp -= s.Length - dot - 1; s = s.Remove(dot, 1); }
            BigInteger digits = BigInteger.Parse(s, CultureInfo.InvariantCulture);
            double v = exp >= 0 ? Nearest(digits * BigInteger.Pow(10, exp), 1) : Nearest(digits, BigInteger.Pow(10, -exp));
            return neg ? -v : v;
        }

        // double.ToString() writes 15 digits on .NET Framework, so Vector2 and Polynomial came out "(0.3,
        // 0.333333333333333)" there and did not read back. They write the shortest round-trip form, as .NET Core does.
        public void ToString_IsTheShortestRoundTripOnEveryFramework()
        {
            Check.Equal("(0.30000000000000004, 0.3333333333333333)", new Vector2(0.1 + 0.2, 1.0 / 3).ToString());
            Check.Equal("(1E+17, -1E-05)", new Vector2(1e17, -1e-5).ToString());
            Check.Equal("(123456789012345.6, 0.0001)", new Vector2(123456789012345.6, 1e-4).ToString());
            Check.Equal("(85947878093434430, 1.2345678901234568E+17)", new Vector2(85947878093434430, 1.2345678901234568E+17).ToString());
            Check.True(new Polynomial(0.1 + 0.2, 1.0 / 3).ToString().Contains("0.30000000000000004"), new Polynomial(0.1 + 0.2, 1.0 / 3).ToString());
            var rng = new Random(1777);
            for (int i = 0; i < 20000; i++)
            {
                double x = i % 2 == 0
                    ? rng.NextDouble() * Math.Pow(10, rng.Next(-30, 30))
                    : BitConverter.Int64BitsToDouble((long)(rng.NextDouble() * long.MaxValue));
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                string s = new Vector2(x, -x).ToString();
                string[] parts = s.Substring(1, s.Length - 2).Split(new[] { ", " }, StringSplitOptions.None);
                Check.Equal(R(x), R(ExactParse(parts[0])), s);
                Check.Equal(R(-x), R(ExactParse(parts[1])), s);
                string p = new Polynomial(1, x).ToString();
                Check.True(p.Contains(parts[0]), p + " against " + parts[0]);
#if !NETFRAMEWORK
                Check.Equal(x.ToString(CultureInfo.InvariantCulture), parts[0], "as .NET Core writes it");
                Check.Equal((-x).ToString(CultureInfo.InvariantCulture), parts[1], "as .NET Core writes it");
#endif
            }
        }
    }
}
