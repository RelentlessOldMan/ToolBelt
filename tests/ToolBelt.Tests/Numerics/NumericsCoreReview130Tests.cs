using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Round 130 (numerics core, eighth pass): each regression failed on the code before its fix.
    public sealed class NumericsCoreReview130Tests
    {
        // ---------------- Polynomial.Fit: the rank test no longer grows with n ----------------

        private static void FitsSine(int n, int degree)
        {
            var x = Enumerable.Range(0, n).Select(i => -1 + 2.0 * i / (n - 1)).ToArray();
            var y = x.Select(v => Math.Sin(3 * v)).ToArray();
            var p = Polynomial.Fit(x, y, degree);
            double worst = 0;
            for (int i = 0; i < n; i += Math.Max(1, n / 2000)) worst = Math.Max(worst, Math.Abs(p.Evaluate(x[i]) - y[i]));
            Check.True(worst < 1e-10, n + " points, degree " + degree + ": " + worst);
        }

        public void Fit_ManyDistinctPointsAreNotRefused()
        {
            FitsSine(100_000, 30);
            FitsSine(1_000_000, 25);
            FitsSine(31, 30);
        }

        public void Fit_RefusesExactlyWhenTooFewDistinctX()
        {
            var rng = new Random(130);
            for (int trial = 0; trial < 300; trial++)
            {
                int k = rng.Next(1, 12), n = k * rng.Next(1, 40) + rng.Next(0, k);
                var nodes = Enumerable.Range(0, k).Select(i => (double)rng.Next(-1000, 1000) * 0.37).Distinct().ToArray();
                k = nodes.Length;
                var x = Enumerable.Range(0, Math.Max(n, k)).Select(i => nodes[i % k]).OrderBy(_ => rng.Next()).ToArray();
                var y = x.Select(v => Math.Cos(v)).ToArray();
                Polynomial.Fit(x, y, k - 1);
                if (k <= x.Length - 1)
                {
                    var ex = Check.Throws<InvalidOperationException>(() => Polynomial.Fit(x, y, k));
                    Check.True(ex.Message.Contains("too few distinct x"), ex.Message);
                }
            }
        }

        // ---------------- Integration.Simpson: a running sum past double range ----------------

        private static BigInteger Exact(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            BigInteger r = (BigInteger)man << (exp - 1);
            return bits < 0 ? -r : r;
        }

        public void Simpson_ARunningSumPastRangeIsResummed()
        {
            var y = new[] { 6e307, 6e307, 6e307, 6e307, 6e307, -6e307, -6e307 };
            double got = Integration.Simpson(y, 1);
            Check.True(Math.Abs(got - 1.6e308) <= 1e293, got.ToString("R"));   // 1.2e308 + 1.2e308 - 0.8e308
            var rng = new Random(1301);
            for (int trial = 0; trial < 3000; trial++)
            {
                var s = new double[rng.Next(2, 12)];
                for (int i = 0; i < s.Length; i++) s[i] = (rng.NextDouble() * 2 - 1) * 1.7e308 / (1 << rng.Next(0, 3));
                double dx = rng.Next(2) == 0 ? 1 : rng.NextDouble() * 3;
                BigInteger num = 0;
                int intervals = s.Length - 1, even = intervals - intervals % 2;
                for (int i = 0; i < even; i += 2) num += 4 * (Exact(s[i]) + 4 * Exact(s[i + 1]) + Exact(s[i + 2]));
                if (even != intervals)
                {
                    int n = intervals;
                    num += n >= 2 ? 5 * Exact(s[n]) + 8 * Exact(s[n - 1]) - Exact(s[n - 2]) : 6 * (Exact(s[n - 1]) + Exact(s[n]));
                }
                // num / 12 · dx, with the samples and dx each exact times 2^1074.
                double exact = (double)((num * Exact(dx) / 12) >> 2148);
                double r = Integration.Simpson(s, dx);
                if (Math.Abs(Math.Abs(exact) - double.MaxValue) < 1e-9 * double.MaxValue) continue;   // either side of the edge
                if (double.IsInfinity(exact)) { Check.True(double.IsInfinity(r) && Math.Sign(r) == Math.Sign(exact), exact + " gave " + r); continue; }
                Check.True(Math.Abs(r - exact) <= 1e-12 * 1.7e308 * s.Length * Math.Max(dx, 1), string.Join(",", s.Select(v => v.ToString("R"))) + " dx " + dx + ": " + r + " vs " + exact);
            }
        }

        // ---------------- Lerp: equal infinite ends ----------------

        public void Lerp_EqualInfiniteEndsStayInfinite()
        {
            foreach (double inf in new[] { double.PositiveInfinity, double.NegativeInfinity })
                foreach (double t in new[] { 0.5, 0.25, 2, -3, 1e-300 })
                {
                    Check.Equal(inf, Interpolation.Lerp(inf, inf, t), inf + " " + t);
                    var v = ToolBelt.Numerics.Vector2.Lerp(new ToolBelt.Numerics.Vector2(inf, 1), new ToolBelt.Numerics.Vector2(inf, 3), t);
                    Check.Equal(inf, v.X, "Vector2 " + inf + " " + t);
                }
            Check.Equal(5.0, Interpolation.Lerp(5, 5, 0.3));
        }

        // ---------------- Rounding and EngineeringNotation: exact on every framework ----------------

        // .NET Framework's double.Parse misreads about 1.5% of short decimals at extreme exponents, and its "R" can miss the
        // shortest digits: RoundToSignificantDigits returned a neighbour of the nearest double there.
        public void RoundToSignificantDigits_IsTheNearestDoubleOnEveryFramework()
        {
            var rng = new Random(1302);
            for (int trial = 0; trial < 30_000; trial++)
            {
                BigInteger d = rng.Next(1_000_000, 10_000_000);
                int e = rng.Next(-322, 300), k = rng.Next(1, 7);
                double v = NearestRef(d, e);
                if (v == 0 || Math.Abs(v) < 1e-300) continue;   // subnormals hold fewer digits than 7
                bool negative = rng.Next(2) == 0;
                double got = Rounding.RoundToSignificantDigits(negative ? -v : v, k);
                BigInteger r = RoundRef(d, e, e + 7 - k);
                double expect = NearestRef(r, e + 7 - k);
                Check.Equal(negative ? -expect : expect, got, (negative ? "-" : "") + d + "E" + e + " to " + k);
            }
        }

        public void EngineeringNotation_DigitsAreTheShortestDecimalsOnEveryFramework()
        {
            // Texts .NET Framework's double.Parse reads an ulp off; Parse reads them exactly, and Format's output reads back.
            Check.Equal(NearestRef(539205, 247), EngineeringNotation.Parse("5.39205E252"));
            Check.Equal(NearestRef(6960980, -151), EngineeringNotation.Parse("6960980E-151"));
            Check.Equal(-NearestRef(9856352, -285), EngineeringNotation.Parse("-9856352e-285"));
            Check.Equal(NearestRef(5390153, -253 + 24), EngineeringNotation.Parse("5390153e-253 Y"));
            var rng = new Random(1303);
            for (int trial = 0; trial < 30_000; trial++)
            {
                BigInteger d = rng.Next(1_000_000, 10_000_000);
                int e = rng.Next(-30, 21), k = rng.Next(1, 9);
                double v = NearestRef(d, e);
                string text = EngineeringNotation.Format(v, k);
                BigInteger r = k >= 7 ? d : RoundRef(d, e, e + 7 - k);
                string want = r.ToString(CultureInfo.InvariantCulture).TrimEnd('0');
                string shown = new string(text.TakeWhile(c => c != ' ' && c != 'E').Where(char.IsDigit).ToArray()).TrimStart('0').TrimEnd('0');
                Check.Equal(want, shown, d + "E" + e + " at " + k + ": " + text);
                string full = EngineeringNotation.Format(v, 17);
                Check.Equal(v, EngineeringNotation.Parse(full), full);
            }
        }

        // ---------------- Uncertainty: the same digits round the same way at every magnitude ----------------

        // The double nearest digits·10^exp (ties to even), checked exactly against both neighbours: double.Parse alone is
        // not correctly rounded on .NET Framework (5.39205e252 is read one ulp low).
        private static double NearestRef(BigInteger digits, int exp)
        {
            double d = double.Parse(digits.ToString(CultureInfo.InvariantCulture) + "E" + exp.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            BigInteger target = digits * BigInteger.Pow(10, Math.Max(exp, 0)) << 1074, scale = BigInteger.Pow(10, Math.Max(-exp, 0));
            BigInteger Dist(double c) => BigInteger.Abs(target - Exact(c) * scale);
            while (true)
            {
                double up = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(d) + 1);
                double down = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(d) - 1);
                if (!double.IsInfinity(up) && Dist(up) < Dist(d)) { d = up; continue; }
                if (d > 0 && Dist(down) < Dist(d)) { d = down; continue; }
                return d;
            }
        }

        // digits·10^exp rounded half away from zero at 10^place, as digits·10^place.
        private static BigInteger RoundRef(BigInteger digits, int exp, int place)
        {
            if (exp >= place) return digits * BigInteger.Pow(10, exp - place);
            var den = BigInteger.Pow(10, place - exp);
            return (2 * digits + den) / (2 * den);
        }

        public void Uncertainty_RoundsTheShortestDecimalAtEveryMagnitude()
        {
            Check.Equal("1.01 ± 0.01", Uncertainty.Format(1.005, 0.01, rule: UncertaintyRule.OneSignificantDigit));
            Check.Equal("(1.01 ± 0.01)e-9", Uncertainty.Format(1.005e-9, 1e-11, rule: UncertaintyRule.OneSignificantDigit));
            Check.Equal(1.01, Uncertainty.Round(1.005, 0.01, UncertaintyRule.OneSignificantDigit).Value);
            Check.Equal(0.0015, Uncertainty.Round(5, 0.00145, UncertaintyRule.TwoSignificantDigits).Uncertainty);
            Check.Equal(0.015, Uncertainty.Round(5, 0.0145, UncertaintyRule.TwoSignificantDigits).Uncertainty);

            // .NET Framework's double.Parse reads 5.3921e252 and 5.39205e252 one ulp off; the result is still the nearest double.
            Check.Equal(-NearestRef(53921, 248), Uncertainty.Round(-NearestRef(539205, 247), NearestRef(3605, 246), UncertaintyRule.TwoSignificantDigits).Value);

            var rng = new Random(1300);
            var rules = new[] { UncertaintyRule.ParticleDataGroup, UncertaintyRule.TwoSignificantDigits, UncertaintyRule.OneSignificantDigit };
            for (int trial = 0; trial < 40_000; trial++)
            {
                int scale = rng.Next(-300, 300);
                // Ties are the point: short decimals ending in 5. Each is the double's shortest decimal (at most 7 digits).
                BigInteger vd = rng.Next(1, 100000) * 10 + (rng.Next(2) == 0 ? 5 : rng.Next(10)), ud = rng.Next(1, 1000) * 10 + (rng.Next(2) == 0 ? 5 : rng.Next(10));
                int ve = scale - rng.Next(0, 8), ue = scale - rng.Next(3, 12);
                bool negative = rng.Next(2) == 0;
                double v = NearestRef(vd, ve), u = NearestRef(ud, ue);
                if (negative) v = -v;
                if (u < Math.Abs(v) * 1e-17) continue;
                var rule = rules[rng.Next(3)];
                var (rv, ru, decimals) = Uncertainty.Round(v, u, rule);
                string what = (negative ? "-" : "") + vd + "E" + ve + " ± " + ud + "E" + ue + " " + rule;
                BigInteger rvd = RoundRef(vd, ve, -decimals);
                double expectV = rvd.IsZero ? 0 : NearestRef(rvd, -decimals);
                Check.Equal(negative ? -expectV : expectV, rv, what + " value");
                Check.Equal(NearestRef(RoundRef(ud, ue, -decimals), -decimals), ru, what + " uncertainty");
            }
        }
    }
}
