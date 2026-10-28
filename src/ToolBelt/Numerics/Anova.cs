// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// The one-way ANOVA table: the F statistic and its p-value, the between- and within-group degrees of
    /// freedom, and the sum-of-squares / mean-square breakdown.
    /// </summary>
    public readonly struct AnovaResult
    {
        public AnovaResult(
            double fStatistic, double pValue, double dfBetween, double dfWithin,
            double ssBetween, double ssWithin, double msBetween, double msWithin, double grandMean)
        {
            FStatistic = fStatistic;
            PValue = pValue;
            DfBetween = dfBetween;
            DfWithin = dfWithin;
            SumSquaresBetween = ssBetween;
            SumSquaresWithin = ssWithin;
            MeanSquareBetween = msBetween;
            MeanSquareWithin = msWithin;
            GrandMean = grandMean;
        }

        /// <summary>The F statistic (MS between / MS within).</summary>
        public double FStatistic { get; }

        /// <summary>Upper-tail p-value P(F &gt; <see cref="FStatistic"/>) under the null of equal group means.</summary>
        public double PValue { get; }

        /// <summary>Between-group degrees of freedom (number of groups − 1).</summary>
        public double DfBetween { get; }

        /// <summary>Within-group (residual) degrees of freedom (total observations − number of groups).</summary>
        public double DfWithin { get; }

        public double SumSquaresBetween { get; }
        public double SumSquaresWithin { get; }
        public double MeanSquareBetween { get; }
        public double MeanSquareWithin { get; }
        public double GrandMean { get; }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "F({0:0.###}, {1:0.###}) = {2:0.######}, p = {3:0.######}",
            DfBetween, DfWithin, FStatistic, PValue);
    }

    /// <summary>
    /// Analysis of variance. <see cref="OneWay(double[][])"/> tests whether several groups share a common
    /// mean (the F-test), and <see cref="FUpperTailProbability(double, double, double)"/> exposes the
    /// underlying F-distribution tail. Built on the regularized incomplete beta function (duplicated
    /// privately so the file stands alone; see <see cref="SpecialFunctions"/>).
    /// </summary>
    public static class Anova
    {
        /// <summary>
        /// One-way (single-factor) ANOVA across the given <paramref name="groups"/>. Requires at least two
        /// groups, each with at least one observation, and more observations in total than groups (so the
        /// residual degrees of freedom are positive).
        /// </summary>
        public static AnovaResult OneWay(params double[][] groups)
        {
            if (groups is null) throw new ArgumentNullException(nameof(groups));
            if (groups.Length < 2)
                throw new ArgumentException("ANOVA needs at least two groups.", nameof(groups));

            int k = groups.Length;
            int total = 0;
            double maxAbs = 0;
            for (int i = 0; i < k; i++)
            {
                double[] g = groups[i] ?? throw new ArgumentException($"Group {i} is null.", nameof(groups));
                if (g.Length == 0)
                    throw new ArgumentException($"Group {i} is empty.", nameof(groups));
                total += g.Length;
                for (int j = 0; j < g.Length; j++)
                {
                    // A NaN slipped past every comparison and came back as F = NaN; an infinity as F = NaN too.
                    if (double.IsNaN(g[j]) || double.IsInfinity(g[j]))
                        throw new ArgumentException($"Group {i} value [{j}] is not finite.", nameof(groups));
                    maxAbs = Math.Max(maxAbs, Math.Abs(g[j]));
                }
            }
            if (total <= k)
                throw new ArgumentException("Total observations must exceed the number of groups (need positive residual df).", nameof(groups));

            // Work in units of the largest magnitude (a power of two, so normal-range data give the same bits): squared
            // outright, data near 1e-160 underflowed to zero sums of squares and data near 1e+160 overflowed.
            double s = Pow2Scale(maxAbs);
            // Means as offsets from the first value c: summed outright, data on a large offset (1e9 + small effects) lost
            // the effects to rounding in every sum, and the group-minus-grand difference cancelled what was left.
            double c = groups[0][0] / s;
            double grandSum = 0;
            for (int i = 0; i < k; i++)
                for (int j = 0; j < groups[i].Length; j++) grandSum += groups[i][j] / s - c;
            double grandOffset = grandSum / total;
            double ssBetween = 0, ssWithin = 0;
            for (int i = 0; i < k; i++)
            {
                double[] g = groups[i];
                double groupSum = 0;
                for (int j = 0; j < g.Length; j++) groupSum += g[j] / s - c;
                double groupOffset = groupSum / g.Length;
                double d = groupOffset - grandOffset;
                ssBetween += g.Length * d * d;
                for (int j = 0; j < g.Length; j++)
                {
                    double e = g[j] / s - c - groupOffset;
                    ssWithin += e * e;
                }
            }
            double grandMean = c + grandOffset;

            double dfB = k - 1;
            double dfW = total - k;
            double msB = ssBetween / dfB;
            double msW = ssWithin / dfW;

            // Sums of squares at rounding level are zero: data like {0.1, 0.1, 0.1} leave ~1e-34 of round-off in the
            // group mean's residuals, and a ratio of two such crumbs is a meaningless F (and p).
            double scaledMax = maxAbs / s;
            double floor = total * (1e-14 * scaledMax) * (1e-14 * scaledMax);
            if (ssWithin <= floor) { ssWithin = 0; msW = 0; }
            if (ssBetween <= floor) { ssBetween = 0; msB = 0; }

            double f, p;
            if (msW == 0)
            {
                // No within-group variation: the F-test degenerates.
                if (ssBetween == 0) { f = 0; p = 1; }      // every value identical → no evidence of difference
                else { f = double.PositiveInfinity; p = 0; } // groups differ with zero noise
            }
            else
            {
                f = msB / msW;
                p = FUpperTailProbability(f, dfB, dfW);
            }
            return new AnovaResult(f, p, dfB, dfW, Unscale(ssBetween, s), Unscale(ssWithin, s), Unscale(msB, s), Unscale(msW, s), grandMean * s);
        }

        // The power of two at or below m (1 for 0): dividing by it is exact.
        // A sum of squares back in the data's units: zero stays zero (0 · s² was NaN once s² overflowed, for data near
        // 1e308), and s is applied twice so a small sum is not lost to an s² that alone over- or underflows.
        private static double Unscale(double v, double s) => v == 0 ? 0 : v * s * s;

        private static double Pow2Scale(double m)
        {
            if (m == 0 || double.IsInfinity(m)) return 1;
            double p = Math.Pow(2, Math.Floor(Math.Log(m) / Math.Log(2)));
            // The logarithm can land a hair either side of an integer; settle on exactly the power at or below m.
            while (p > m) p /= 2;
            while (p * 2 <= m) p *= 2;
            return p;
        }

        /// <summary>
        /// Upper-tail probability P(F &gt; <paramref name="f"/>) for an F-distribution with
        /// (<paramref name="df1"/>, <paramref name="df2"/>) degrees of freedom. This is the F-distribution's
        /// survival function; one minus it is the CDF.
        /// </summary>
        public static double FUpperTailProbability(double f, double df1, double df2)
        {
            if (!(df1 > 0) || double.IsInfinity(df1))
                throw new ArgumentOutOfRangeException(nameof(df1), df1, "Degrees of freedom must be positive and finite.");
            if (!(df2 > 0) || double.IsInfinity(df2))
                throw new ArgumentOutOfRangeException(nameof(df2), df2, "Degrees of freedom must be positive and finite.");
            if (double.IsNaN(f)) return double.NaN;
            if (f <= 0) return 1.0;
            if (double.IsPositiveInfinity(f)) return 0.0;
            // P(F > f) = 1 − I_y(df1/2, df2/2) with y = df1·f/(df1·f + df2), the complement computed directly.
            double num = df1 * f, den = num + df2;
            BetaI(num / den, df2 / den, df1 / 2, df2 / 2, out _, out double upper);
            return upper;
        }

        // ---- special functions: the same code as Distributions.cs, duplicated privately (no leaf-to-leaf dependency) ----

        private const double LnSqrt2Pi = 0.91893853320467274178;

        private static readonly double[] Lanczos =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        };

        private static double LnGamma(double x)
        {
            if (double.IsPositiveInfinity(x)) return x;
            if (x < 0.5)
            {
                // Γ(x) = Γ(x + 1)/x for 0 < x < ½; the reflection formula's π/sin(πx) overflows for x below ~1e-308.
                if (x > 0) return LnGamma(x + 1) - Math.Log(x);
                return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LnGamma(1 - x); // reflection
            }
            // Near its zeros at 1 and 2 ln Γ is tiny, and the Lanczos sum, accurate only absolutely, kept ~1e-5 of it.
            if (x >= 0.8 && x <= 1.2) return LnGammaNearOne(x - 1);
            if (x >= 1.8 && x <= 2.2) return Math.Log(x - 1) + LnGammaNearOne(x - 2);   // x − 1 is exact
            double a = Lanczos[0];
            double t = x + 6.5;
            double xm1 = x - 1;
            for (int i = 1; i < Lanczos.Length; i++) a += Lanczos[i] / (xm1 + i);
            return LnSqrt2Pi + (xm1 + 0.5) * Math.Log(t) - t + Math.Log(a);
        }

        // ζ(2) … ζ(25), for the series of ln Γ(1 + z).
        private static readonly double[] Zeta =
        {
            1.6449340668482264, 1.2020569031595942, 1.0823232337111381, 1.03692775514337, 1.0173430619844492,
            1.0083492773819229, 1.0040773561979444, 1.0020083928260821, 1.000994575127818, 1.0004941886041194,
            1.000246086553308, 1.0001227133475785, 1.0000612481350588, 1.000030588236307, 1.0000152822594086,
            1.0000076371976379, 1.000003817293265, 1.0000019082127165, 1.0000009539620338, 1.0000004769329869,
            1.0000002384505027, 1.0000001192199259, 1.0000000596081891, 1.0000000298035034,
        };

        // ln Γ(1 + z) = −γz + Σₖ₌₂ ζ(k)(−z)ᵏ/k for |z| <= 0.2 (the terms fall by 5× each, 24 of them reach 1e-18 relative).
        private static double LnGammaNearOne(double z)
        {
            double w = -z, wk = w, sum = 0.57721566490153286 * w;
            for (int k = 2; k < Zeta.Length + 2; k++)
            {
                wk *= w;
                sum += Zeta[k - 2] * wk / k;
            }
            return sum;
        }

        private const double Tiny = 1e-300;
        private const double Eps = 1e-16;

        /// <summary>Regularized incomplete gamma P(a, x) and its complement Q(a, x), each computed directly.</summary>
        private static void GammaPQ(double a, double x, out double p, out double q)
        {
            if (x <= 0) { p = 0; q = 1; return; }
            if (double.IsPositiveInfinity(x)) { p = 1; q = 0; return; }
            if (a >= TemmeMinShape) { GammaTemme(a, x, out p, out q); return; }
            double lnPre = LnPrefix(a, x);
            if (x < a + 1)
            {
                if (a < 1e-300)
                {
                    // The series' 1/a overflows below ~5.6e-309 (P came out NaN at a = 5e-324); Q directly, P = 1 − Q.
                    q = SmallShapeQ(a, x);
                    p = 1 - q;
                    return;
                }
                double ap = a, sum = 1.0 / a, del = sum;
                for (int n = 0; n < 100000; n++)
                {
                    ap += 1;
                    del *= x / ap;
                    sum += del;
                    if (Math.Abs(del) < Math.Abs(sum) * Eps) break;
                }
                p = sum * Math.Exp(lnPre);
                if (p > 1) p = 1;
                q = 1 - p;
                // P from the series carries lnΓ(a)·eps through its prefix (690 eps at a = 1e-300); 1 − Q is exact to eps.
                if (a < 1 && p > 0.5) { q = SmallShapeQ(a, x); p = 1 - q; }
                return;
            }
            q = GammaFront(a, x) * GammaCf(a, x);
            if (q > 1) q = 1;
            p = 1 - q;
        }

        // A small shape puts P near 1, and 1 − P kept few of Q's digits (3e-8 relative at a = 5e-9). Directly:
        // Q = 1 − xᵃ/Γ(a + 1) − a·xᵃ/Γ(a + 1)·Σₙ₌₁ (−x)ⁿ/(n!(a + n)), the first difference by expm1.
        // lnΓ(1 + a) from a itself: a + 1 rounds away ~1e-16 of a, a relative 2e-8 of a shape near 5e-9. For x < a + 1.
        private static double SmallShapeQ(double a, double x)
        {
            double u = a * Math.Log(x) - (a <= 0.2 ? LnGammaNearOne(a) : LnGamma(a + 1)), term = 1, s = 0;
            for (int n = 1; n < 200; n++)
            {
                term *= -x / n;
                double t = term / (a + n);
                s += t;
                if (Math.Abs(t) <= Eps * Math.Abs(s)) break;
            }
            return -Expm1(u) - a * Math.Exp(u) * s;
        }

        // Continued fraction for Q(a, x) without its prefix x^a e^(−x)/Γ(a) (modified Lentz), for x >= a + 1.
        private static double GammaCf(double a, double x)
        {
            double b = x + 1 - a, c = 1 / Tiny, d = 1 / b, h = d;
            for (int i = 1; i < 100000; i++)
            {
                double an = -i * (i - a);
                b += 2;
                d = an * d + b; if (Math.Abs(d) < Tiny) d = Tiny;
                c = b + an / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1) < Eps) break;
            }
            return h;
        }

        // e^(z²)·erfc(z) for z >= 0, without the underflow of erfc past z ≈ 27.
        private static double Erfcx(double z)
        {
            if (z < 1.5)
            {
                GammaPQ(0.5, z * z, out _, out double q);
                return Math.Exp(z * z) * q;
            }
            return z / 1.7724538509055160 * GammaCf(0.5, z * z); // erfc(z) = (z e^(−z²)/√π)·CF
        }

        // The series and continued fraction above need ~10·√a terms, so past a ≈ 1e8 (chi-square df 2e8) they hit their
        // iteration cap and return silently wrong values. From a = 1e7 up, Temme's uniform asymptotic expansion
        // (DLMF 8.12.7–8.12.10) is used instead, with two correction terms (truncation ~ 4e-3/a², under 1e-16 here):
        //   Q(a, x) = ½·erfc(η·√(a/2)) + e^(−aη²/2)/√(2πa) · (c₀(η) + c₁(η)/a),  ½η² = λ − 1 − ln λ,  λ = x/a,
        // and P by the mirror formula for η < 0, so the small tail is always computed directly. Measured against 40-digit
        // references for a from 1e5 to 1e9 out to ±30σ: below 1.3e-13 relative (the residue is the conditioning of x).
        private const double TemmeMinShape = 1e7;

        // Taylor coefficients of c₀(η) and c₁(η) at η = 0 (exact rationals from series reversion, rounded), used where the
        // closed forms below cancel.
        private static readonly double[] TemmeC0 =
        {
            -0.3333333333333333, 0.08333333333333333, -0.014814814814814815, 0.0011574074074074073, 0.0003527336860670194,
            -0.0001787551440329218, 3.919263178522438e-05, -2.185448510679992e-06, -1.85406221071516e-06,
            8.296711340953087e-07, -1.7665952736826078e-07, 6.707853543401498e-09, 1.0261809784240309e-08,
            -4.382036018453353e-09, 9.14769958223679e-10, -2.5514193994946248e-11,
        };

        private static readonly double[] TemmeC1 =
        {
            -0.001851851851851852, -0.003472222222222222, 0.0026455026455026454, -0.0009902263374485596,
            0.00020576131687242798, -4.018775720164609e-07, -1.8098550334489977e-05, 7.64916091608111e-06,
            -1.6120900894563446e-06, 4.647127802807434e-09, 1.378633446915721e-07, -5.752545603517705e-08,
            1.1951628599778148e-08, -1.7543241719747647e-11,
        };

        private static void GammaTemme(double a, double x, out double p, out double q)
        {
            double t = (x - a) / a, lm1 = t;            // λ − 1
            double y = -a * Log1pmx(t);                  // aη²/2 = a(λ − 1 − ln λ), cancellation-free
            double eta = Math.Sqrt(2 * y / a);
            if (t < 0) eta = -eta;
            double c0 = Math.Abs(eta) < 0.5 ? Horner(TemmeC0, eta) : 1 / lm1 - 1 / eta;
            double c1 = Math.Abs(eta) < 1 ? Horner(TemmeC1, eta)
                : 1 / (eta * eta * eta) - 1 / (lm1 * lm1 * lm1) - 1 / (lm1 * lm1) - 1 / (12 * lm1);
            double r = Math.Exp(-y) / Math.Sqrt(2 * Math.PI * a) * (c0 + c1 / a);
            GammaPQ(0.5, y, out _, out double erfcHalf);
            double half = 0.5 * erfcHalf;                // ½·erfc(|η|·√(a/2))
            if (eta >= 0) { q = half + r; p = 1 - q; }
            else { p = half - r; q = 1 - p; }
        }

        private static double Horner(double[] c, double x)
        {
            double r = 0;
            for (int i = c.Length - 1; i >= 0; i--) r = r * x + c[i];
            return r;
        }

        /// <summary>
        /// Regularized incomplete beta I_x(a, b) and its complement, given x and 1 − x separately (so the caller can
        /// supply 1 − x without cancellation). Each value is computed directly in its own small regime.
        /// </summary>
        private static void BetaI(double x, double oneMinusX, double a, double b, out double ix, out double ixc)
        {
            // Each value is formed in its own regime and the other as 1 − it, which can step just outside [0, 1]
            // (1.0000000000000155 at shapes near 1e-100): clamped, since a probability is reported.
            BetaICore(x, oneMinusX, a, b, out ix, out ixc);
            ix = Math.Min(1, Math.Max(0, ix));
            ixc = Math.Min(1, Math.Max(0, ixc));
        }

        private static void BetaICore(double x, double oneMinusX, double a, double b, out double ix, out double ixc)
        {
            if (x <= 0) { ix = 0; ixc = 1; return; }
            if (oneMinusX <= 0) { ix = 1; ixc = 0; return; }
            if (double.IsInfinity(a + b))
            {
                // a + b overflows only when the smaller shape is above ~1e292, so the spread about the mode a/(a + b),
                // ~1/√min(a, b), is far below a double's resolution there: I is a step. x < a/(a + b) is x·b < (1 − x)·a,
                // which cannot overflow; λ and the forms below overflowed to NaN (I_0.5(MaxValue, MaxValue)).
                double below = x * b, above = oneMinusX * a;
                ix = below < above ? 0 : below > above ? 1 : 0.5;
                ixc = 1 - ix;
                return;
            }
            if (Math.Min(a, b) > 100)
            {
                // Near the mode with both parameters large (TOMS 708's region for BASYM): λ = a − (a+b)x, formed on the
                // side where it cancels least; λ >= 0 is the lower side.
                double lambda = a > b ? (a + b) * oneMinusX - b : a - (a + b) * x;
                double limit = 0.03 * Math.Min(a, b);
                if (lambda >= 0 && lambda <= limit) { ix = Basym(a, b, lambda); ixc = 1 - ix; return; }
                if (lambda < 0 && -lambda <= limit) { ixc = Basym(b, a, -lambda); ix = 1 - ixc; return; }
            }
            // Which side of (a + 1)/(a + b + 2) x lies on, decided by whichever of x and 1 − x is the smaller: at
            // a = 1e20 both x and the split round to 1, and the upper branch returned a negative upper tail.
            if (x <= 0.5 ? x < (a + 1) / (a + b + 2) : oneMinusX > (b + 1) / (a + b + 2))
            {
                if (UseBgrat(a, b) && Bgrat(x, oneMinusX, a, b, out ix, out ixc)) return;
                double front = BetaFront(x, oneMinusX, a, b, a);
                ix = front == 0 ? 0 : front * BetaCf(x, oneMinusX, a, b);
                ixc = 1 - ix;
                if (a < 1) SmallShapeTail(oneMinusX, x, b, a, ref ixc, ref ix);
            }
            else
            {
                if (UseBgrat(b, a) && Bgrat(oneMinusX, x, b, a, out ixc, out ix)) return;
                double front = BetaFront(x, oneMinusX, a, b, b);
                ixc = front == 0 ? 0 : front * BetaCf(oneMinusX, x, b, a);
                ix = 1 - ixc;
                if (b < 1) SmallShapeTail(x, oneMinusX, a, b, ref ix, ref ixc);
            }
        }

        // I_x(a, b) for b < 1 above the split, where it is the small tail (a small b puts the mass near 1), so 1 − the
        // continued fraction's I_(1−x)(b, a) kept only its absolute accuracy: I_0.7(1, 1e-20) = 1.2e-20 came out 2e-14.
        // Directly, by
        //   I_x(a, b) = x^a (1 − x)^b / (a·B(a, b)) · Σₙ (a+b)ₙ/(a+1)ₙ · xⁿ,
        // whose terms are positive and fall by at least x each (b < 1). Where that is too slow (x near 1) the first n
        // terms are summed and the rest, I_x(a + n, b), comes from BGRAT at a + n >= 1000 (TOMS 708's BUP and BGRAT).
        // The thousands of terms are summed with compensation (Neumaier). The result replaces the pair only where it is
        // the small tail (below 0.25, where 1 − the big tail would lose 2 bits or more); nothing changes if BGRAT fails.
        private static void SmallShapeTail(double x, double oneMinusX, double a, double b, ref double small, ref double big)
        {
            double front = BetaFront(x, oneMinusX, a, b, a);
            // The terms fall below 1e-17 of the first after ~39/(−ln x) of them.
            bool direct = -LnOf(x, oneMinusX) * 4000 >= 40;
            int n = direct ? 4000 : (int)Math.Max(0, Math.Ceiling(1000 - a));
            double sum = 0, comp = 0, term = 1;
            for (int i = 0; i < n; i++)
            {
                double t = sum + term;
                comp += Math.Abs(sum) >= Math.Abs(term) ? (sum - t) + term : (term - t) + sum;
                sum = t;
                term *= (a + b + i) / (a + 1 + i) * x;
                if (direct && term <= 1e-17 * sum) break;
            }
            double value = front * (sum + comp);
            if (!direct)
            {
                if (!Bgrat(x, oneMinusX, a + n, b, out double rest, out _)) return;
                value += rest;
            }
            if (!(value < 0.25)) return;
            small = value;
            big = 1 - value;
        }

        // x^a (1 − x)^b / (B(a, b)·s), s being a or b. With p = min(a, b) below 1, 1/B(a, b) carries a factor p (lnΓ(p) =
        // lnΓ(1 + p) − ln p), applied outside the exponential as p/s <= 1: inside, the rounding of ln p (690 at p = 1e-300)
        // put up to ~250 eps on the tail, and at a = b = 5e-324 dividing by s after the exponential underflowed (I_x came
        // out 0, not ½). When the whole underflows the tail is 0 and the continued fraction is not formed.
        private static double BetaFront(double x, double oneMinusX, double a, double b, double s)
        {
            double p = Math.Min(a, b), ln = BetaLnFront(x, oneMinusX, a, b);
            if (p < 1) return Math.Exp(ln) * (p / s);
            return ln < -700 ? Math.Exp(ln - Math.Log(s)) : Math.Exp(ln) / s;
        }

        // ln( x^a (1 − x)^b / B(a, b) ). For a, b >= 10 the three terms are each ~(a + b) and cancel to a small number, so
        // with Stirling for ln B they are recombined as a·(ln(1 + e₁) − e₁) + b·(ln(1 + e₂) − e₂) + ½ln(ab/(a+b)) − ½ln 2π
        // − corr, where x = x₀(1 + e₁), 1 − x = y₀(1 + e₂) about the mode x₀ = a/(a+b) (a·e₁ + b·e₂ = 0 exactly), the
        // same decomposition as TOMS 708's BRCOMP. The direct form was off by ~1e-11 relative at a = b = 1e6.
        // For min(a, b) < 1 it is ln( x^a (1 − x)^b / (p·B(a, b)) ), p = min(a, b) (see BetaFront).
        private static double BetaLnFront(double x, double oneMinusX, double a, double b)
        {
            double p = Math.Min(a, b), q = Math.Max(a, b);
            if (q < 10) return a * LnOf(x, oneMinusX) + b * LnOf(oneMinusX, x) - (p < 1 ? LnPBeta(p, q) : LnBeta(a, b));
            if (p < 10)
            {
                // ln B = lnΓ(p) + lnΓ(q) − lnΓ(p+q), the last two by Stirling: corr + p − p·ln(p+q) + (q − ½)·ln(1 − p/(p+q)).
                // p·ln(p+q) cancels against p·ln x_p, x_p being the variable that carries p (FCdf(0.5, 5, 1e300): two
                // terms of ~1700 left 1e-13 relative), so they are merged as p·ln(x_p·(p+q)), the product formed first.
                bool aSmall = a <= b;
                double xp = aSmall ? x : oneMinusX;
                double lnXq = aSmall ? LnOf(oneMinusX, x) : LnOf(x, oneMinusX);
                double cq = StirlingCorr(q) - StirlingCorr(p + q);
                double lnGp = p < 1 ? LnGamma1p(p) : LnGamma(p);
                return p * Math.Log(xp * (p + q)) + q * lnXq - lnGp - cq - p - (q - 0.5) * Log1p(-p / (p + q));
            }
            double ab = a + b;
            // λ = a − (a+b)x = (a+b)(1 − x) − b, formed on the side where it cancels least (TOMS 708); e₁ = −λ/a, e₂ = λ/b.
            double lambda = a > b ? ab * oneMinusX - b : a - ab * x;
            // Far from the mode (|e| > 0.6) ln(1 + e) is taken as ln(x/x₀) directly, as BRCOMP does.
            double e1 = -lambda / a, e2 = lambda / b;
            double u = Math.Abs(e1) > 0.6 ? Math.Log(x / (a / ab)) - e1 : Log1pmx(e1);
            double v = Math.Abs(e2) > 0.6 ? Math.Log(oneMinusX / (b / ab)) - e2 : Log1pmx(e2);
            double corr = StirlingCorr(a) + StirlingCorr(b) - StirlingCorr(ab);
            return a * u + b * v + 0.5 * Math.Log(a / ab * b) - LnSqrt2Pi - corr;
        }

        // The continued fraction's relative error grows like 1e-17 × the larger parameter (its odd terms approach −1), so
        // at F's denominator df of 1e8–1e9 it is off by ~1e-8. There the large-parameter expansion takes over; measured
        // against 40-digit references it stays near 1e-14 while small² ≤ big (it diverges once small² ≫ big).
        private static bool UseBgrat(double big, double small) => big >= 1000 && small * small <= big;

        // Didonato & Morris' asymptotic expansion (TOMS 708, BGRAT) for I_x(a, b) with a large and b small:
        //   I_x(a, b) = R·(Q(b, z) + r·Σ dₙJₙ),  ν = a + (b − 1)/2,  z = −ν·ln x,  R = Γ(a+b)/(Γ(a)·ν^b),
        //   r = z^b·e^(−z)/Γ(b). The complement 1 − I is formed from its own pieces, P(b, z) − (R − 1)·Q − R·r·Σ, so
        // it stays accurate too (both callers have z ≳ b + 1, where P is not small). Returns false if it fails to
        // converge, and the caller falls back to the continued fraction.
        private static bool Bgrat(double x, double oneMinusX, double a, double b, out double ix, out double ixc)
        {
            ix = ixc = double.NaN;
            double bm1 = b - 1, nu = a + 0.5 * bm1;
            double lnx = oneMinusX > 0.375 ? Math.Log(x) : Log1p(-oneMinusX);
            double z = -nu * lnx;
            if (!(z > 0)) return false;
            // ln R = lnΓ(a+b) − lnΓ(a) − b·ln ν, from Stirling with the a·ln a pieces cancelled analytically (a >= 1000).
            double lnR = (a + b - 0.5) * Log1p(b / a) - b - b * Log1p(bm1 / (2 * a)) + StirlingCorr(a + b) - StirlingCorr(a);
            double rr = Math.Exp(lnR), rm1 = Expm1(lnR);
            GammaPQ(b, z, out double p, out double q);
            double r = GammaFront(b, z);
            double sum = 0;
            if (r > 0)
            {
                double v = 0.25 / (nu * nu), t2 = 0.25 * lnx * lnx;
                double j = q / r, t = 1, cn = 1, scale = j;
                var c = new double[30];
                var d = new double[30];
                int n2 = 0;
                bool converged = false;
                for (int n = 1; n <= 30; n++)
                {
                    double bp2n = b + n2;
                    j = (bp2n * (bp2n + 1) * j + (z + bp2n + 1) * t) * v;
                    n2 += 2;
                    t *= t2;
                    cn /= n2 * (n2 + 1.0);
                    c[n - 1] = cn;
                    double s = 0, coef = b - n;
                    for (int i = 1; i < n; i++) { s += coef * c[i - 1] * d[n - i - 1]; coef += b; }
                    d[n - 1] = bm1 * cn + s / n;
                    double dj = d[n - 1] * j;
                    sum += dj;
                    if (double.IsNaN(sum) || scale + sum <= 0) return false;
                    if (Math.Abs(dj) <= 1e-17 * (scale + sum)) { converged = true; break; }
                }
                if (!converged) return false;
            }
            ix = rr * (q + r * sum);
            ixc = p - rm1 * q - rr * r * sum;
            return ix >= 0 && ixc >= 0;
        }

        // Didonato & Morris' asymptotic expansion (TOMS 708, BASYM) for I_x(a, b) with a and b both large and x at or
        // below the mode, λ = a − (a+b)x in [0, 0.03·min(a, b)]. The continued fraction's rounding error grows with the
        // parameters there (2.6e-13 at I_½(1e6, 1e6), worse beyond); this is exact at the mode and otherwise within the
        // conditioning of x (measured to 1e9 against 40-digit quadrature).
        private static double Basym(double a, double b, double lambda)
        {
            const double e0 = 1.1283791670955126, e1 = 0.35355339059327376; // 2/√π, 2^(−3/2)
            const int num = 20;
            var a0 = new double[num + 1];
            var b0 = new double[num + 1];
            var c = new double[num + 1];
            var d = new double[num + 1];
            double h, r0, r1, w0;
            if (a < b) { h = a / b; r0 = 1 / (h + 1); r1 = (b - a) / b; w0 = 1 / Math.Sqrt(a * (h + 1)); }
            else { h = b / a; r0 = 1 / (h + 1); r1 = (b - a) / a; w0 = 1 / Math.Sqrt(b * (h + 1)); }
            double f = -a * Log1pmx(-lambda / a) - b * Log1pmx(lambda / b);
            double t = Math.Exp(-f);
            if (t == 0) return 0;
            double z0 = Math.Sqrt(f), z = 0.5 * (z0 / e1), z2 = f + f;
            a0[0] = 2.0 / 3 * r1;
            c[0] = -0.5 * a0[0];
            d[0] = -c[0];
            double j0 = 0.5 / e0 * Erfcx(z0), j1 = e1;
            double sum = j0 + d[0] * w0 * j1;
            double s = 1, h2 = h * h, hn = 1, w = w0, znm1 = z, zn = z2;
            for (int n = 2; n <= num; n += 2)
            {
                hn *= h2;
                a0[n - 1] = 2 * r0 * (1 + h * hn) / (n + 2);
                int np1 = n + 1;
                s += hn;
                a0[np1 - 1] = 2 * r1 * s / (n + 3);
                for (int i = n; i <= np1; i++)
                {
                    double r = -0.5 * (i + 1);
                    b0[0] = r * a0[0];
                    for (int m = 2; m <= i; m++)
                    {
                        double bsum = 0;
                        for (int j = 1; j < m; j++) bsum += (j * r - (m - j)) * a0[j - 1] * b0[m - j - 1];
                        b0[m - 1] = r * a0[m - 1] + bsum / m;
                    }
                    c[i - 1] = b0[i - 1] / (i + 1);
                    double dsum = 0;
                    for (int j = 1; j < i; j++) dsum += d[i - j - 1] * c[j - 1];
                    d[i - 1] = -(dsum + c[i - 1]);
                }
                j0 = e1 * znm1 + (n - 1) * j0;
                j1 = e1 * zn + n * j1;
                znm1 *= z2;
                zn *= z2;
                w *= w0;
                double t0 = d[n - 1] * w * j0;
                w *= w0;
                double t1 = d[np1 - 1] * w * j1;
                sum += t0 + t1;
                if (Math.Abs(t0) + Math.Abs(t1) <= Eps * sum) break;
            }
            double u = Math.Exp(-(StirlingCorr(a) + StirlingCorr(b) - StirlingCorr(a + b)));
            return e0 * t * u * sum;
        }

        // Continued fraction for I_x(a, b) (modified Lentz).
        private static double BetaCf(double x, double oneMinusX, double a, double b)
        {
            double qab = a + b, qap = a + 1, qam = a - 1;
            // 1 − (a+b)x/(a+1) = ((a+1) − (a+b)x)/(a+1) = (1 − b + (a+b)(1 − x))/(a+1). Each form subtracts numbers as large
            // as its biggest term, so take the one whose terms are smaller: the second for x near 1 with a large, the first
            // for x small with b large (F's lower tail at a big denominator df, where the second lost 1e-8).
            double c = 1;
            double d = Math.Max(qap, qab * x) <= Math.Max(Math.Abs(1 - b), qab * oneMinusX)
                ? (qap - qab * x) / qap
                : (1 - b + qab * oneMinusX) / qap;
            if (Math.Abs(d) < Tiny) d = Tiny;
            d = 1 / d;
            double h = d;
            for (int m = 1; m <= 100000; m++)
            {
                int m2 = 2 * m;
                // Each term as a product of bounded ratios ((b − m)x and (a + b + m)x are at most ~a + m on this side of
                // the split): multiplied out first, m(b − m) and (a + m)(a + b + m) overflowed at shapes near 1e300 and
                // the fraction became NaN (I_5e-324(2, MaxValue); F's CDF at d1 = 1e10, d2 = 1e300).
                double aa = m / (a + m2) * ((b - m) * x / (qam + m2));
                d = 1 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1 / d;
                h *= d * c;
                aa = -(a + m) / (a + m2) * ((qab + m) * x / (qap + m2));
                d = 1 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1) < Eps) break;
            }
            return h;
        }

        // log(1 + x) accurate to a few ulps everywhere (Goldberg): corrects for the rounding of 1 + x.
        private static double Log1p(double x)
        {
            if (double.IsPositiveInfinity(x)) return x;          // ln(∞)·∞/∞ would be NaN
            double u = 1 + x;
            return u == 1 ? x : Math.Log(u) * x / (u - 1);
        }

        // e^x − 1 accurate to a few ulps (Goldberg / Kahan).
        private static double Expm1(double x)
        {
            double u = Math.Exp(x);
            if (u == 1) return x;
            double um1 = u - 1;
            return um1 == -1 ? -1 : um1 * x / Math.Log(u);
        }

        // log(1 + t) − t without cancellation for small t (the "bd0" trick behind accurate gamma tails).
        private static double Log1pmx(double t)
        {
            if (Math.Abs(t) >= 0.5) return Log1p(t) - t;
            double term = t, sum = 0;
            for (int n = 2; n < 200; n++)
            {
                term *= -t;
                double add = term / n;
                sum += add;
                if (Math.Abs(add) <= 1e-17 * Math.Abs(sum)) break;
            }
            return sum;
        }

        // ln(y) given y and 1 − y (both supplied accurately): the log of a value near 1 goes through log1p.
        private static double LnOf(double y, double oneMinusY) => y < 0.5 ? Math.Log(y) : Log1p(-oneMinusY);

        // Stirling series correction: lnΓ(x) = (x − ½)ln x − x + ½ln 2π + StirlingCorr(x), for x ≥ 10.
        private static double StirlingCorr(double x)
        {
            double r = 1 / x, r2 = r * r;
            return r * (1.0 / 12 + r2 * (-1.0 / 360 + r2 * (1.0 / 1260 + r2 * (-1.0 / 1680 + r2 * (1.0 / 1188
                 + r2 * (-691.0 / 360360 + r2 * (1.0 / 156 + r2 * (-3617.0 / 122400))))))));
        }

        // ln B(a, b) without the catastrophic lnΓ(a+b) − lnΓ(a) − lnΓ(b) cancellation for large arguments
        // (the same decomposition R's lbeta uses).
        private static double LnBeta(double a, double b)
        {
            double p = Math.Min(a, b), q = Math.Max(a, b);
            if (p >= 10)
            {
                double corr = StirlingCorr(p) + StirlingCorr(q) - StirlingCorr(p + q);
                return -0.5 * Math.Log(q) + LnSqrt2Pi + corr + (p - 0.5) * Math.Log(p / (p + q)) + q * Log1p(-p / (p + q));
            }
            if (q >= 10)
            {
                double corr = StirlingCorr(q) - StirlingCorr(p + q);
                return LnGamma(p) + corr + p - p * Math.Log(p + q) + (q - 0.5) * Log1p(-p / (p + q));
            }
            return LnGamma(p) + LnGamma(q) - LnGamma(p + q);
        }

        // x^a e^(−x) / Γ(a). For a < 1 as a·x^a e^(−x)/Γ(a + 1): lnΓ(a) = lnΓ(a + 1) − ln a is ~690 at a = 1e-300, and its
        // rounding inside the exponential put up to ~250 eps on Q(a, x) for x >= a + 1 and on BGRAT's terms.
        private static double GammaFront(double a, double x) =>
            a < 1 ? a * Math.Exp(a * Math.Log(x) - x - LnGamma1p(a)) : Math.Exp(LnPrefix(a, x));

        // lnΓ(1 + a) for a >= 0, from a itself near 0 (1 + a rounds away a's low digits).
        private static double LnGamma1p(double a) => a <= 0.2 ? LnGammaNearOne(a) : LnGamma(1 + a);

        // ln( x^a e^(−x) / Γ(a) ), stable for large a where a·ln x, x and lnΓ(a) are huge and nearly cancel.
        private static double LnPrefix(double a, double x)
        {
            if (a < 10) return a * Math.Log(x) - x - LnGamma(a);
            // a·ln x − x − lnΓ(a) = a·(ln r − (r − 1)) + ½ln a − ½ln 2π − corr(a), r = x/a. Near r = 1 use the
            // cancellation-free log1p(t) − t series; far from it take ln r directly (1 + t would round away digits).
            double r = x / a;
            double core = r < 0.5 || r > 2 ? Math.Log(r) - (r - 1) : Log1pmx((x - a) / a);
            return a * core + 0.5 * Math.Log(a) - LnSqrt2Pi - StirlingCorr(a);
        }

        // ln(p·B(p, q)) for p < 1 and q < 10, with ln p left out of lnΓ(p) (and of lnΓ(q) when q < 1 too; see BetaFront).
        private static double LnPBeta(double p, double q)
        {
            if (q < 1) return LnGamma1p(p) + LnGamma1p(q) - LnGamma1p(p + q) + Log1p(p / q);
            return LnGamma1p(p) + LnGamma(q) - LnGamma(p + q);
        }
    }
}
