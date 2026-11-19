using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>
    /// Regressions from review round 162 (tenth pass over the numerics core): each test failed before its fix, except
    /// the guards, which say what the fix keeps. Graded against exact BigInteger arithmetic.
    /// </summary>
    public sealed class NumericsCoreReview162Tests
    {
        // ---- exact references ----

        // A double as m·2^k exactly.
        private static (BigInteger M, int K) Exact(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int be = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else m |= 1L << 52;
            return (v < 0 ? -(BigInteger)m : m, be - 1075);
        }

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

        // The determinant of a small matrix, exactly, as a fraction over a power of two.
        private static double ExactDeterminant(double[,] a)
        {
            int n = a.GetLength(0);
            var m = new BigInteger[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    var (v, k) = Exact(a[i, j]);
                    m[i, j] = v << (k + 1074); // every double is a whole multiple of 2^-1074
                }
            BigInteger sum = 0;
            foreach (int[] p in Permutations(n))
            {
                BigInteger prod = 1;
                for (int i = 0; i < n; i++) prod *= m[i, p[i]];
                int inv = 0;
                for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) if (p[i] > p[j]) inv++;
                sum += inv % 2 == 0 ? prod : -prod;
            }
            return Nearest(sum, BigInteger.One << (1074 * n));
        }

        private static string Show(double[,] a)
        {
            int n = a.GetLength(0);
            return "{" + string.Join(", ", Enumerable.Range(0, n).Select(i => "{" + string.Join(", ", Enumerable.Range(0, n).Select(j => a[i, j].ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "}")) + "}";
        }

        private static IEnumerable<int[]> Permutations(int n)
        {
            if (n == 1) { yield return new[] { 0 }; yield break; }
            foreach (int[] p in Permutations(n - 1))
                for (int at = 0; at < n; at++)
                {
                    var q = new List<int>(p);
                    q.Insert(at, n - 1);
                    yield return q.ToArray();
                }
        }

        private static double Scale(double x, int k)
        {
            for (; k > 0; k -= Math.Min(k, 1000)) x *= Math.Pow(2, Math.Min(k, 1000));
            for (; k < 0; k += Math.Min(-k, 1000)) x /= Math.Pow(2, Math.Min(-k, 1000));
            return x;
        }

        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        // ---- LinearAlgebra ----

        // Rows alone were scaled: {{1e305, 1e-223}, {1e239, 1e-287}} lost its small entries (determinant 0, Solve and
        // Inverse "singular"), and the multiplier 1e-108 / 1e233 underflowed to 0 (determinant −1e9, not 1e104).
        public void LinearAlgebra_MatricesSpanningTheWholeRangeKeepTheirSmallEntries()
        {
            foreach (var a in new[]
            {
                new double[,] { { 1e305, 1e-223 }, { 1e239, 1e-287 } },
                new double[,] { { 1e-108, 1e-224 }, { 1e233, 1e212 } },
            })
            {
                double det = ExactDeterminant(a);
                Rel(det, LinearAlgebra.Determinant(a), 1e-12, "determinant");
                // Cramer's rule, exactly: x = (a11 − a01, a00 − a10) / det for b = (1, 1).
                double x0 = (a[1, 1] - a[0, 1]) / det, x1 = (a[0, 0] - a[1, 0]) / det;
                double[] x = LinearAlgebra.Solve(a, new[] { 1.0, 1.0 });
                Rel(x0, x[0], 1e-12, "x0");
                Rel(x1, x[1], 1e-12, "x1");
                double[,] inv = LinearAlgebra.Inverse(a);
                Rel(a[1, 1] / det, inv[0, 0], 1e-12, "inverse[0, 0]");
                Rel(a[0, 0] / det, inv[1, 1], 1e-12, "inverse[1, 1]");
            }
            // Randomized: entries anywhere in the double range, graded against the exact determinant.
            var rng = new DeterministicRandom(162);
            int bad = 0;
            string first = "";
            for (int t = 0; t < 3000; t++)
            {
                int n = 1 + rng.Next(4);
                var a = new double[n, n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++) a[i, j] = Scale(rng.NextDouble() * 2 - 1, rng.Next(-1074, 1023));
                double want = ExactDeterminant(a), got = LinearAlgebra.Determinant(a);
                bool ok = Math.Abs(got - want) <= 1e-6 * Math.Abs(want) || (double.IsInfinity(want) && got == want)
                          || (want == 0 && Math.Abs(got) < 1e-300);
                if (!ok && bad++ == 0) first = $"{Show(a)}: got {got:R}, want {want:R}";
            }
            Check.Equal(0, bad, first);
        }

        // The rounding left in the last pivot of an exactly singular matrix passed as a pivot: {{6, 7, -9}, {-5, -6, 4},
        // {-3, -4, -6}} solved to x ≈ 3e15, and 721 of 20,000 random rank-deficient integer matrices were accepted.
        public void LinearAlgebra_ExactlySingularMatricesAreRejected()
        {
            var s = new double[,] { { 6, 7, -9 }, { -5, -6, 4 }, { -3, -4, -6 } };
            Check.Equal(0.0, ExactDeterminant(s));
            Check.Throws<InvalidOperationException>(() => LinearAlgebra.Solve(s, new[] { 1.0, 2, 3 }));
            Check.Throws<InvalidOperationException>(() => LinearAlgebra.Inverse(s));

            var rng = new DeterministicRandom(1621);
            int accepted = 0, scaledAccepted = 0;
            const int Trials = 20000;
            for (int t = 0; t < Trials; t++)
            {
                int n = 2 + rng.Next(5);
                var a = new double[n, n];
                for (int i = 0; i < n - 1; i++)
                    for (int j = 0; j < n; j++) a[i, j] = rng.Next(19) - 9;
                var w = new int[n - 1];
                for (int i = 0; i < n - 1; i++) w[i] = rng.Next(7) - 3;
                for (int j = 0; j < n; j++)
                {
                    double sum = 0;
                    for (int i = 0; i < n - 1; i++) sum += w[i] * a[i, j];
                    a[n - 1, j] = sum;
                }
                int swap = rng.Next(n);
                for (int j = 0; j < n; j++) (a[swap, j], a[n - 1, j]) = (a[n - 1, j], a[swap, j]);
                if (Accepts(a)) accepted++;
                // The same matrix with rows and columns scaled by powers of two (exact): still singular.
                for (int i = 0; i < n; i++) { int e = rng.Next(600) - 300; for (int j = 0; j < n; j++) a[i, j] = Scale(a[i, j], e); }
                for (int j = 0; j < n; j++) { int e = rng.Next(600) - 300; for (int i = 0; i < n; i++) a[i, j] = Scale(a[i, j], e); }
                if (Accepts(a)) scaledAccepted++;
            }
            // Not none: a pivot of a few hundred ε against its bound is as likely real as rounding (Hilbert 10's is).
            Check.True(accepted <= Trials / 500, $"accepted {accepted} of {Trials}");
            Check.True(scaledAccepted <= Trials / 500, $"scaled: accepted {scaledAccepted} of {Trials}");
        }

        private static bool Accepts(double[,] a)
        {
            try { LinearAlgebra.Solve(a, new double[a.GetLength(0)]); return true; }
            catch (InvalidOperationException) { return false; }
        }

        // Guard: ill-conditioned but regular matrices still solve: random integer ones, Gaussian ones to 60×60, Hilbert
        // matrices to 11×11, and the scalings the earlier rounds fixed.
        public void LinearAlgebra_RegularMatricesStillSolve()
        {
            var rng = new DeterministicRandom(1622);
            for (int t = 0; t < 5000; t++)
            {
                int n = 2 + rng.Next(4);
                var a = new double[n, n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++) a[i, j] = rng.Next(19) - 9;
                if (ExactDeterminant(a) == 0) continue;
                Check.True(Accepts(a), "integer matrix rejected");
            }
            for (int t = 0; t < 200; t++)
            {
                int n = 2 + rng.Next(59);
                var a = new double[n, n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++) a[i, j] = rng.NextDouble() * 2 - 1;
                var ones = Enumerable.Repeat(1.0, n).ToArray();
                double[] x = LinearAlgebra.Solve(a, LinearAlgebra.Multiply(a, ones));
                Check.True(x.All(v => Math.Abs(v - 1) < 1e-8), "Gaussian " + n);
            }
            for (int n = 1; n <= 11; n++)
            {
                var h = new double[n, n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++) h[i, j] = 1.0 / (i + j + 1);
                Check.True(Accepts(h), "Hilbert " + n);
            }
            Rel(1, LinearAlgebra.Determinant(new double[,] { { 1e-20, 1e20 }, { 2e-20, 3e20 } }), 1e-14, "mixed units");
            Rel(1e20, LinearAlgebra.Determinant(new double[,] { { 1e20, 0 }, { 0, 1 } }), 1e-15, "diag(1e20, 1)");
            Rel(1e-200, LinearAlgebra.Determinant(new double[,] { { 1e-100, 0 }, { 0, 1e-100 } }), 1e-15, "uniformly tiny");
        }

        // ---- UnitConvert ----

        private static readonly Dictionary<string, (string Category, string Factor)> Factors = new Dictionary<string, (string, string)>
        {
            ["m"] = ("length", "1"), ["km"] = ("length", "1000"), ["cm"] = ("length", "0.01"), ["mm"] = ("length", "0.001"),
            ["um"] = ("length", "0.000001"), ["mi"] = ("length", "1609.344"), ["yd"] = ("length", "0.9144"),
            ["ft"] = ("length", "0.3048"), ["in"] = ("length", "0.0254"), ["nmi"] = ("length", "1852"),
            ["kg"] = ("mass", "1"), ["g"] = ("mass", "0.001"), ["mg"] = ("mass", "0.000001"), ["t"] = ("mass", "1000"),
            ["lb"] = ("mass", "0.45359237"), ["oz"] = ("mass", "0.028349523125"),
            ["s"] = ("time", "1"), ["ms"] = ("time", "0.001"), ["us"] = ("time", "0.000001"), ["ns"] = ("time", "0.000000001"),
            ["min"] = ("time", "60"), ["h"] = ("time", "3600"), ["d"] = ("time", "86400"), ["wk"] = ("time", "604800"),
            ["B"] = ("data", "1"), ["KB"] = ("data", "1000"), ["MB"] = ("data", "1000000"), ["GB"] = ("data", "1000000000"),
            ["TB"] = ("data", "1000000000000"), ["KiB"] = ("data", "1024"), ["MiB"] = ("data", "1048576"),
            ["GiB"] = ("data", "1073741824"), ["TiB"] = ("data", "1099511627776"),
        };

        private static (BigInteger N, BigInteger D) Decimal(string s)
        {
            int dot = s.IndexOf('.');
            if (dot < 0) return (BigInteger.Parse(s), 1);
            return (BigInteger.Parse(s.Remove(dot, 1)), BigInteger.Pow(10, s.Length - dot - 1));
        }

        // The rounded ratio times the value rounded twice: 9 m was 0.009000000000000001 km, and 9,361 of 49,600 whole-number
        // conversions were off. Each is value · from / to rounded once.
        public void UnitConvert_IsTheExactProductRoundedOnce()
        {
            Check.Equal(0.009, UnitConvert.Convert(9, "m", "km"));
            var rng = new DeterministicRandom(1623);
            var values = new List<double>();
            for (int v = 1; v <= 200; v++) values.Add(v);
            for (int t = 0; t < 200; t++) values.Add(Scale(rng.NextDouble() + 0.5, rng.Next(-800, 800)));
            for (int t = 0; t < 50; t++) values.Add(-rng.Next(1, 1_000_000) / 1000.0);
            int bad = 0;
            string first = "";
            foreach (var from in Factors)
                foreach (var to in Factors)
                {
                    if (from.Value.Category != to.Value.Category || from.Key == to.Key) continue;
                    var (fn, fd) = Decimal(from.Value.Factor);
                    var (tn, td) = Decimal(to.Value.Factor);
                    foreach (double v in values)
                    {
                        var (m, k) = Exact(v);
                        BigInteger n = m * fn * td, d = fd * tn;
                        if (k >= 0) n <<= k; else d <<= -k;
                        double want = Nearest(n, d), got = UnitConvert.Convert(v, from.Key, to.Key);
                        if (!want.Equals(got) && bad++ == 0) first = $"{v:R} {from.Key} → {to.Key}: got {got:R}, want {want:R}";
                    }
                }
            Check.Equal(0, bad, first);
        }

        // ---- EngineeringNotation ----

        // Past yocto–yotta, "G" rounded the binary value: 1.25e27 at 2 digits was "1.2E+27" though 1.25e24 is "1.3 Y".
        // Now rounded from the shortest digits, half away from zero, as in range.
        public void EngineeringNotation_OutOfRangeRoundsAsInRange()
        {
            Check.Equal("1.3 Y", EngineeringNotation.Format(1.25e24, 2));
            Check.Equal("1.3E+27", EngineeringNotation.Format(1.25e27, 2));
            Check.Equal("-1.3E+27", EngineeringNotation.Format(-1.25e27, 2));
            Check.Equal("3E-28", EngineeringNotation.Format(2.5e-28, 1));
            Check.Equal("1.3E-30", EngineeringNotation.Format(1.25e-30, 2));
            // Guard: the form "G" gave is kept.
            Check.Equal("1E+27", EngineeringNotation.Format(1e27, 3));
            Check.Equal("1.23E+300", EngineeringNotation.Format(1.234e300, 3));
            Check.Equal("5E-324", EngineeringNotation.Format(double.Epsilon, 15)); // its shortest digits, as in range
            Check.Equal("1.7976931348623157E+308", EngineeringNotation.Format(double.MaxValue, 17));
            Check.Equal("1E+27", EngineeringNotation.Format(9.99e26, 2));
            var rng = new DeterministicRandom(1624);
            for (int t = 0; t < 2000; t++)
            {
                double v = Scale(rng.NextDouble() + 0.5, rng.Next(2) == 0 ? rng.Next(90, 1023) : -rng.Next(90, 1074));
                Check.Equal(v, EngineeringNotation.Parse(EngineeringNotation.Format(v, 17)), "round trip"); // double.Parse is not exact on .NET Framework
            }
        }

        // ---- Polynomial ----

        // A root at zero came back as −0 from x, x² + x and x², and as +0 from x³.
        public void Polynomial_ARootAtZeroIsPositiveZero()
        {
            foreach (var p in new[]
            {
                new Polynomial(0, 1), new Polynomial(0, -2), new Polynomial(0, 1, 1), new Polynomial(0, 0, 1),
                new Polynomial(0, 0, 0, 1), new Polynomial(0, -1, 0, 1), new Polynomial(0, 0, 1, 1),
            })
                foreach (double r in p.RealRoots())
                    if (r == 0) Check.Equal(0L, BitConverter.DoubleToInt64Bits(r), "−0 root");
        }

        // ---- Integration ----

        // Past double range the running sum went to ∞ and the opposite panels after it gave NaN where Trapezoid gave 0.
        public void CumulativeTrapezoid_EndsWhereTrapezoidDoes()
        {
            var y = new[] { 1e308, 1e308, -1e308, -1e308 };
            double[] c = Integration.CumulativeTrapezoid(y, 10);
            Check.Equal(Integration.Trapezoid(y, 10), c[3]);
            Check.Equal(0.0, c[3]);
            Check.Equal(double.PositiveInfinity, c[1]);
            var z = new[] { 1e308, 1e308, -1e308, -1e308, 5 };
            Check.Equal(Integration.Trapezoid(z, 10), Integration.CumulativeTrapezoid(z, 10)[4]);
            // Guard: ordinary sums are unchanged.
            var w = new[] { 1.0, 2, 4, 8 };
            Check.True(Integration.CumulativeTrapezoid(w, 0.5).SequenceEqual(new[] { 0, 0.75, 2.25, 5.25 }));
        }

        // ∞ − ∞ of two halves past double range was NaN (x over ±1e200, integral 0), returned with no exception. A
        // cancellation that lands in range is that value; one left below the rounding of halves near 1e400 is noise
        // scaled back past the range, and throws rather than return ∞ or a value.
        public void AdaptiveSimpson_OppositeOverflowingHalvesCancel()
        {
            Check.Equal(0.0, Integration.AdaptiveSimpson(x => x * x * x, -1e100, 1e100));
            Check.Equal(0.0, Integration.AdaptiveSimpson(x => x, -1e308, 1e308));
            Rel(1e300, Integration.AdaptiveSimpson(x => x + 2.5e145, -2e154, 2e154), 1e-6, "x + c over ±2e154, halves ±2e308");
            var e = Check.Throws<InvalidOperationException>(() => Integration.AdaptiveSimpson(x => x, -1e200, 1e200));
            Check.True(e.Message.Contains("double precision"), e.Message);
            // An infinite half and a finite one: ∫ x over [−1e200, 2e200] is 1.5e400, past range.
            Check.Equal(double.PositiveInfinity, Integration.AdaptiveSimpson(x => x, -1e200, 2e200));
            // Guard: one that overflows stays signed.
            Check.Equal(double.NegativeInfinity, Integration.AdaptiveSimpson(x => -1e308, 0, 10));
        }

        // The quartered fallback was 1.5 times the sample of a constant, which overflowed for samples above 1.2e308.
        public void AdaptiveSimpson_LargeConstantsIntegrateInRange()
        {
            Rel(1.2e308, Integration.AdaptiveSimpson(x => 1.2e308, 0, 1), 1e-15, "1.2e308 over [0, 1]");
            Rel(double.MaxValue, Integration.AdaptiveSimpson(x => double.MaxValue, 0, 1), 1e-15, "MaxValue over [0, 1]");
            Rel(-1.5e308, Integration.AdaptiveSimpson(x => -1.5e308, 0, 1), 1e-15, "−1.5e308 over [0, 1]");
            Rel(1.5e8, Integration.AdaptiveSimpson(x => 1.5e308, 0, 1e-300), 1e-14, "over [0, 1e-300]");
            Rel(1.5e308 * 8 * double.Epsilon, Integration.AdaptiveSimpson(x => 1.5e308, 0, 8 * double.Epsilon), 1e-14, "over [0, 8ε]");
            Rel(1.5e308, Integration.AdaptiveSimpson(x => 1.5e308, -0.5, 0.5), 1e-15, "over [−0.5, 0.5]");
        }

        // ---- RootFinding ----

        // x1 − x0 overflowed for guesses far apart, so the step was ∞ and f(∞) failed as "f returned NaN".
        public void Secant_GuessesFarApartStepFromHalves()
        {
            RootResult r = RootFinding.Secant(x => 0.5 * x, 1e308, -1e308);
            Check.True(r.Converged && r.Root == 0, r.Message + " " + r.Root);
            r = RootFinding.Secant(x => x - 1, 1.7e308, -1.7e308);
            Check.True(r.Converged && Math.Abs(r.Root - 1) < 1e-12, r.Message + " " + r.Root);
            r = RootFinding.Secant(Math.Atan, 1.7e308, -1.7e308);
            Check.False(r.Message.Contains("NaN"), r.Message);
            // Guard: ordinary guesses take the same steps.
            r = RootFinding.Secant(x => x * x - 2, 1, 2);
            Check.True(r.Converged && Math.Abs(r.Root - Math.Sqrt(2)) < 1e-12, r.Message);
        }

        // ---- Angle ----

        // An infinite t threw only when the ends differed; with equal ends 0 · ∞ was NaN.
        public void LerpDegrees_InfiniteTThrowsForEqualEndsToo()
        {
            foreach (double t in new[] { double.PositiveInfinity, double.NegativeInfinity })
            {
                Check.Equal("t", Check.Throws<ArgumentOutOfRangeException>(() => Angle.LerpDegrees(10, 10, t)).ParamName);
                Check.Equal("t", Check.Throws<ArgumentOutOfRangeException>(() => Angle.LerpDegrees(10, 20, t)).ParamName);
            }
            Check.Equal(15.0, Angle.LerpDegrees(10, 20, 0.5));
        }

        // ---- BaseConverter ----

        // Digits are UTF-16 code units: "a" and an emoji made base 3 and printed 1 as a lone surrogate, and two emoji were
        // refused as "repeats the digit '?'".
        public void BaseConverter_SurrogateDigitsAreRefused()
        {
            foreach (string alphabet in new[] { "a\U0001F600", "\U0001F600\U0001F601", "ab\uD800", "\uDC00ab" })
            {
                var e = Check.Throws<ArgumentException>(() => BaseConverter.ToBase(1, alphabet));
                Check.Equal("alphabet", e.ParamName);
                Check.True(e.Message.Contains("surrogate"), e.Message);
                Check.Equal("alphabet", Check.Throws<ArgumentException>(() => BaseConverter.FromBase("a", alphabet)).ParamName);
            }
            Check.Equal("ba", BaseConverter.ToBase(2, "ab")); // guard
        }
    }
}
