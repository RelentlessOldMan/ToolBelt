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
            double grandSum = 0;
            for (int i = 0; i < k; i++)
            {
                double[] g = groups[i] ?? throw new ArgumentException($"Group {i} is null.", nameof(groups));
                if (g.Length == 0)
                    throw new ArgumentException($"Group {i} is empty.", nameof(groups));
                total += g.Length;
                for (int j = 0; j < g.Length; j++) grandSum += g[j];
            }
            if (total <= k)
                throw new ArgumentException("Total observations must exceed the number of groups (need positive residual df).", nameof(groups));

            double grandMean = grandSum / total;
            double ssBetween = 0, ssWithin = 0;
            for (int i = 0; i < k; i++)
            {
                double[] g = groups[i];
                double groupSum = 0;
                for (int j = 0; j < g.Length; j++) groupSum += g[j];
                double groupMean = groupSum / g.Length;
                double d = groupMean - grandMean;
                ssBetween += g.Length * d * d;
                for (int j = 0; j < g.Length; j++)
                {
                    double e = g[j] - groupMean;
                    ssWithin += e * e;
                }
            }

            double dfB = k - 1;
            double dfW = total - k;
            double msB = ssBetween / dfB;
            double msW = ssWithin / dfW;

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
            return new AnovaResult(f, p, dfB, dfW, ssBetween, ssWithin, msB, msW, grandMean);
        }

        /// <summary>
        /// Upper-tail probability P(F &gt; <paramref name="f"/>) for an F-distribution with
        /// (<paramref name="df1"/>, <paramref name="df2"/>) degrees of freedom. This is the F-distribution's
        /// survival function; one minus it is the CDF.
        /// </summary>
        public static double FUpperTailProbability(double f, double df1, double df2)
        {
            if (df1 <= 0)
                throw new ArgumentOutOfRangeException(nameof(df1), df1, "Degrees of freedom must be positive.");
            if (df2 <= 0)
                throw new ArgumentOutOfRangeException(nameof(df2), df2, "Degrees of freedom must be positive.");
            if (f <= 0) return 1.0;
            // P(F > f) = I_{df2/(df2 + df1 f)}(df2/2, df1/2).
            double x = df2 / (df2 + df1 * f);
            return RegularizedBetaI(x, df2 / 2.0, df1 / 2.0);
        }

        // ---- regularized incomplete beta, duplicated privately (no leaf-to-leaf dependency; see SpecialFunctions.cs) ----

        private const double Tiny = 1e-300;
        private const double Eps = 1e-15;
        private const int MaxIter = 300;

        private static readonly double[] LanczosG =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        };

        private static double LnGamma(double x)
        {
            double a = LanczosG[0];
            double t = x + 6.5;
            double xm1 = x - 1;
            for (int i = 1; i < LanczosG.Length; i++) a += LanczosG[i] / (xm1 + i);
            return 0.5 * Math.Log(2 * Math.PI) + (xm1 + 0.5) * Math.Log(t) - t + Math.Log(a);
        }

        private static double RegularizedBetaI(double x, double a, double b)
        {
            if (x <= 0) return 0.0;
            if (x >= 1) return 1.0;
            double front = Math.Exp(LnGamma(a + b) - LnGamma(a) - LnGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
            if (x < (a + 1.0) / (a + b + 2.0))
                return front * BetaCf(x, a, b) / a;
            return 1.0 - front * BetaCf(1 - x, b, a) / b;
        }

        private static double BetaCf(double x, double a, double b)
        {
            double qab = a + b, qap = a + 1.0, qam = a - 1.0;
            double c = 1.0, d = 1.0 - qab * x / qap;
            if (Math.Abs(d) < Tiny) d = Tiny;
            d = 1.0 / d;
            double h = d;
            for (int m = 1; m <= MaxIter; m++)
            {
                double m2 = 2.0 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1.0 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1.0 / d; h *= d * c;
                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1.0 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1.0 / d;
                double del = d * c; h *= del;
                if (Math.Abs(del - 1.0) < Eps) break;
            }
            return h;
        }
    }
}
