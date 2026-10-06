// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ToolBelt.Quality
{
    /// <summary>The usual verdict on a measurement system, from the gauge's share of the study variation.</summary>
    public enum GaugeAcceptability
    {
        /// <summary>Gauge R&amp;R under 10% of the study variation.</summary>
        Acceptable,
        /// <summary>10–30%: may be acceptable depending on the application and the cost of the gauge.</summary>
        Marginal,
        /// <summary>Over 30%: the gauge cannot tell parts apart well enough.</summary>
        Unacceptable,
    }

    /// <summary>One row of the gauge study's two-way ANOVA table. <see cref="F"/> and <see cref="P"/> are NaN where no test applies.</summary>
    public sealed class GaugeAnovaRow
    {
        internal GaugeAnovaRow(string source, int df, double ss, double f, double p)
        {
            Source = source;
            DegreesOfFreedom = df;
            SumOfSquares = ss;
            MeanSquare = df > 0 ? ss / df : double.NaN;
            F = f;
            P = p;
        }

        public string Source { get; }
        public int DegreesOfFreedom { get; }
        public double SumOfSquares { get; }
        public double MeanSquare { get; }
        public double F { get; }
        public double P { get; }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0,-16} {1,4} {2,12:G6} {3,12:G6} {4,9:G4} {5,8:G3}",
            Source, DegreesOfFreedom, SumOfSquares, MeanSquare, F, P);
    }

    /// <summary>One variance component of a gauge study, with the standard ways of expressing it.</summary>
    public sealed class GaugeComponent
    {
        internal GaugeComponent(string name, double variance, double totalVariance, double multiplier, double? tolerance)
        {
            Name = name;
            Variance = variance;
            StdDev = Math.Sqrt(variance);
            StudyVariation = multiplier * StdDev;
            PercentContribution = 100 * variance / totalVariance;
            PercentStudyVariation = 100 * StdDev / Math.Sqrt(totalVariance);
            PercentTolerance = tolerance.HasValue ? 100 * StudyVariation / tolerance.Value : double.NaN;
        }

        public string Name { get; }

        /// <summary>The estimated variance component (σ²); negative ANOVA estimates are reported as 0.</summary>
        public double Variance { get; }

        public double StdDev { get; }

        /// <summary>StdDev × the study-variation multiplier (6 by default: the spread covering ~99.73% of readings).</summary>
        public double StudyVariation { get; }

        /// <summary>Share of the total <em>variance</em>, in percent; these add up to 100.</summary>
        public double PercentContribution { get; }

        /// <summary>Share of the total <em>standard deviation</em>, in percent (the figure the 10%/30% rules use); these do not add up to 100.</summary>
        public double PercentStudyVariation { get; }

        /// <summary>Study variation as a percentage of the tolerance width (NaN when no tolerance was given).</summary>
        public double PercentTolerance { get; }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0,-16} {1,12:G6} {2,8:F2} {3,12:G6} {4,8:F2} {5,8:F2}",
            Name, Variance, PercentContribution, StudyVariation, PercentStudyVariation, PercentTolerance);
    }

    /// <summary>The outcome of <see cref="GaugeRR.Analyze(double[,,], double?, double, double)"/>.</summary>
    public sealed class GaugeRRResult
    {
        internal GaugeRRResult(IReadOnlyList<GaugeAnovaRow> anova, double interactionP, bool interactionRemoved,
            GaugeComponent grr, GaugeComponent repeat, GaugeComponent reprod, GaugeComponent op, GaugeComponent partOp,
            GaugeComponent part, GaugeComponent total, int ndc, double multiplier, double? tolerance)
        {
            Anova = anova;
            InteractionP = interactionP;
            InteractionRemoved = interactionRemoved;
            TotalGaugeRR = grr;
            Repeatability = repeat;
            Reproducibility = reprod;
            Operator = op;
            PartByOperator = partOp;
            PartToPart = part;
            TotalVariation = total;
            DistinctCategories = ndc;
            StudyVarianceMultiplier = multiplier;
            Tolerance = tolerance;
        }

        /// <summary>The ANOVA table actually used: with the Part × Operator row, or without it when the interaction was pooled into repeatability.</summary>
        public IReadOnlyList<GaugeAnovaRow> Anova { get; }

        /// <summary>P-value of the Part × Operator interaction in the full model.</summary>
        public double InteractionP { get; }

        /// <summary>True when the interaction was not significant and was pooled into the error term.</summary>
        public bool InteractionRemoved { get; }

        /// <summary>Repeatability + reproducibility: the measurement system's own variation.</summary>
        public GaugeComponent TotalGaugeRR { get; }

        /// <summary>Equipment variation: the same operator measuring the same part again.</summary>
        public GaugeComponent Repeatability { get; }

        /// <summary>Appraiser variation: operator + part × operator.</summary>
        public GaugeComponent Reproducibility { get; }

        public GaugeComponent Operator { get; }

        /// <summary>Operators measuring some parts differently from others (0 when the interaction was removed).</summary>
        public GaugeComponent PartByOperator { get; }

        /// <summary>The real differences between parts — what the gauge is supposed to see.</summary>
        public GaugeComponent PartToPart { get; }

        public GaugeComponent TotalVariation { get; }

        /// <summary>
        /// Number of distinct categories the gauge can resolve, ⌊1.41 × σ(part) / σ(gauge)⌋ (at least 1; int.MaxValue for a
        /// gauge with no variation). Five or more is the usual requirement.
        /// </summary>
        public int DistinctCategories { get; }

        public double StudyVarianceMultiplier { get; }

        public double? Tolerance { get; }

        /// <summary>The 10% / 30% verdict on <see cref="TotalGaugeRR"/>'s <see cref="GaugeComponent.PercentStudyVariation"/>.</summary>
        public GaugeAcceptability Acceptability
            => TotalGaugeRR.PercentStudyVariation < 10 ? GaugeAcceptability.Acceptable
             : TotalGaugeRR.PercentStudyVariation <= 30 ? GaugeAcceptability.Marginal
             : GaugeAcceptability.Unacceptable;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Source              DF           SS           MS         F        P");
            foreach (var row in Anova) sb.AppendLine(row.ToString());
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Interaction p = {0:G3} ({1})", InteractionP, InteractionRemoved ? "removed" : "kept"));
            sb.AppendLine();
            sb.AppendLine("Source               Variance  %Contrib    StudyVar  %StudyVar %Tolerance");
            foreach (var c in new[] { TotalGaugeRR, Repeatability, Reproducibility, Operator, PartByOperator, PartToPart, TotalVariation })
                sb.AppendLine(c.ToString());
            sb.Append(string.Format(CultureInfo.InvariantCulture, "Distinct categories = {0}; {1}", DistinctCategories, Acceptability));
            return sb.ToString();
        }
    }

    /// <summary>
    /// Gauge repeatability &amp; reproducibility (crossed design, ANOVA method): every operator measures every part the same
    /// number of times, and the two-way ANOVA splits the observed variation into repeatability (the instrument),
    /// reproducibility (the operators) and part-to-part variation. A non-significant Part × Operator interaction is pooled
    /// into repeatability (p above <c>alphaToRemoveInteraction</c>, 0.05 by default; some guides, e.g. AIAG, use 0.25).
    /// This is the method Minitab's "Gage R&amp;R Study (Crossed)" uses, preferred over the older average-and-range method
    /// because it estimates the interaction.
    /// </summary>
    public static class GaugeRR
    {
        /// <summary>
        /// Analyses <paramref name="measurements"/>[part, operator, trial]: at least 2 parts, 2 operators and 2 trials,
        /// every cell filled. <paramref name="tolerance"/> (upper − lower specification limit) adds %Tolerance figures.
        /// </summary>
        public static GaugeRRResult Analyze(double[,,] measurements, double? tolerance = null, double studyVarianceMultiplier = 6, double alphaToRemoveInteraction = 0.05)
        {
            if (measurements is null) throw new ArgumentNullException(nameof(measurements));
            int p = measurements.GetLength(0), o = measurements.GetLength(1), r = measurements.GetLength(2);
            if (p < 2 || o < 2 || r < 2) throw new ArgumentException("A gauge study needs at least 2 parts, 2 operators and 2 trials.", nameof(measurements));
            if (tolerance.HasValue && !(tolerance.Value > 0 && !double.IsInfinity(tolerance.Value)))
                throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be positive and finite.");
            if (!(studyVarianceMultiplier > 0) || double.IsInfinity(studyVarianceMultiplier))
                throw new ArgumentOutOfRangeException(nameof(studyVarianceMultiplier), studyVarianceMultiplier, "The multiplier must be positive and finite.");
            if (!(alphaToRemoveInteraction >= 0 && alphaToRemoveInteraction <= 1))
                throw new ArgumentOutOfRangeException(nameof(alphaToRemoveInteraction), alphaToRemoveInteraction, "Alpha must be in [0, 1].");

            var cell = new double[p, o];
            var partMean = new double[p];
            var opMean = new double[o];
            double grand = 0;
            for (int i = 0; i < p; i++)
                for (int j = 0; j < o; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < r; k++)
                    {
                        double v = measurements[i, j, k];
                        if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException($"Measurement [{i}, {j}, {k}] is not finite.", nameof(measurements));
                        sum += v;
                    }
                    cell[i, j] = sum / r;
                    partMean[i] += cell[i, j] / o;
                    opMean[j] += cell[i, j] / p;
                    grand += cell[i, j] / (p * o);
                }

            double ssPart = 0, ssOp = 0, ssInt = 0, ssRep = 0;
            for (int i = 0; i < p; i++) ssPart += Sq(partMean[i] - grand);
            ssPart *= o * r;
            for (int j = 0; j < o; j++) ssOp += Sq(opMean[j] - grand);
            ssOp *= p * r;
            for (int i = 0; i < p; i++)
                for (int j = 0; j < o; j++)
                {
                    ssInt += Sq(cell[i, j] - partMean[i] - opMean[j] + grand);
                    for (int k = 0; k < r; k++) ssRep += Sq(measurements[i, j, k] - cell[i, j]);
                }
            ssInt *= r;
            double ssTotal = ssPart + ssOp + ssInt + ssRep;

            int dfPart = p - 1, dfOp = o - 1, dfInt = (p - 1) * (o - 1), dfRep = p * o * (r - 1);
            double msPart = ssPart / dfPart, msOp = ssOp / dfOp, msInt = ssInt / dfInt, msRep = ssRep / dfRep;

            var (fInt, pInt) = FTest(msInt, msRep, dfInt, dfRep);
            bool removed = !(pInt <= alphaToRemoveInteraction);       // NaN (no variation at all) counts as "no evidence"

            List<GaugeAnovaRow> table;
            double vRepeat, vOp, vInt, vPart;
            if (!removed)
            {
                var (fP, pP) = FTest(msPart, msInt, dfPart, dfInt);
                var (fO, pO) = FTest(msOp, msInt, dfOp, dfInt);
                table = new List<GaugeAnovaRow>
                {
                    new GaugeAnovaRow("Part", dfPart, ssPart, fP, pP),
                    new GaugeAnovaRow("Operator", dfOp, ssOp, fO, pO),
                    new GaugeAnovaRow("Part * Operator", dfInt, ssInt, fInt, pInt),
                    new GaugeAnovaRow("Repeatability", dfRep, ssRep, double.NaN, double.NaN),
                };
                vRepeat = msRep;
                vInt = Math.Max(0, (msInt - msRep) / r);
                vOp = Math.Max(0, (msOp - msInt) / (p * r));
                vPart = Math.Max(0, (msPart - msInt) / (o * r));
            }
            else
            {
                int dfPooled = dfInt + dfRep;
                double msPooled = (ssInt + ssRep) / dfPooled;
                var (fP, pP) = FTest(msPart, msPooled, dfPart, dfPooled);
                var (fO, pO) = FTest(msOp, msPooled, dfOp, dfPooled);
                table = new List<GaugeAnovaRow>
                {
                    new GaugeAnovaRow("Part", dfPart, ssPart, fP, pP),
                    new GaugeAnovaRow("Operator", dfOp, ssOp, fO, pO),
                    new GaugeAnovaRow("Repeatability", dfPooled, ssInt + ssRep, double.NaN, double.NaN),
                };
                vRepeat = msPooled;
                vInt = 0;
                vOp = Math.Max(0, (msOp - msPooled) / (p * r));
                vPart = Math.Max(0, (msPart - msPooled) / (o * r));
            }
            table.Add(new GaugeAnovaRow("Total", p * o * r - 1, ssTotal, double.NaN, double.NaN));

            double vReprod = vOp + vInt, vGrr = vRepeat + vReprod, vTotal = vGrr + vPart;
            double m = studyVarianceMultiplier;
            GaugeComponent C(string name, double v) => new GaugeComponent(name, v, vTotal, m, tolerance);

            int ndc;
            if (vGrr == 0) ndc = int.MaxValue;
            else
            {
                double raw = Math.Floor(1.41 * Math.Sqrt(vPart) / Math.Sqrt(vGrr));
                ndc = raw >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)raw);
            }

            return new GaugeRRResult(table, pInt, removed,
                C("Total Gauge R&R", vGrr), C("Repeatability", vRepeat), C("Reproducibility", vReprod), C("Operator", vOp),
                C("Part * Operator", vInt), C("Part-to-Part", vPart), C("Total Variation", vTotal), ndc, m, tolerance);
        }

        /// <summary>
        /// Analyses long-format records (one row per reading). Parts and operators may be any keys; the design must be
        /// balanced — every part measured by every operator the same number of times (at least twice).
        /// </summary>
        public static GaugeRRResult Analyze<TPart, TOperator>(IEnumerable<(TPart Part, TOperator Operator, double Value)> measurements,
            double? tolerance = null, double studyVarianceMultiplier = 6, double alphaToRemoveInteraction = 0.05)
            where TPart : notnull where TOperator : notnull
        {
            if (measurements is null) throw new ArgumentNullException(nameof(measurements));
            var parts = new Dictionary<TPart, int>();
            var ops = new Dictionary<TOperator, int>();
            var cells = new Dictionary<(int, int), List<double>>();
            foreach (var (part, op, value) in measurements)
            {
                if (part is null || op is null) throw new ArgumentException("Part and operator keys must not be null.", nameof(measurements));
                if (!parts.TryGetValue(part, out int pi)) parts[part] = pi = parts.Count;
                if (!ops.TryGetValue(op, out int oi)) ops[op] = oi = ops.Count;
                if (!cells.TryGetValue((pi, oi), out var list)) cells[(pi, oi)] = list = new List<double>();
                list.Add(value);
            }
            if (parts.Count == 0) throw new ArgumentException("No measurements.", nameof(measurements));
            int trials = cells.Values.First().Count;
            if (cells.Count != parts.Count * ops.Count || cells.Values.Any(c => c.Count != trials))
                throw new ArgumentException("The study must be balanced: every operator measures every part the same number of times.", nameof(measurements));

            var data = new double[parts.Count, ops.Count, trials];
            foreach (var kv in cells)
                for (int k = 0; k < trials; k++) data[kv.Key.Item1, kv.Key.Item2, k] = kv.Value[k];
            return Analyze(data, tolerance, studyVarianceMultiplier, alphaToRemoveInteraction);
        }

        private static double Sq(double x) => x * x;

        private static (double F, double P) FTest(double numerator, double denominator, int df1, int df2)
        {
            if (denominator == 0) return numerator == 0 ? (double.NaN, double.NaN) : (double.PositiveInfinity, 0);
            double f = numerator / denominator;
            // P(F > f) = I_{df2/(df2 + df1 f)}(df2/2, df1/2).
            return (f, RegularizedBetaI(df2 / (df2 + df1 * f), df2 / 2.0, df1 / 2.0));
        }

        // ---- regularized incomplete beta, duplicated privately (no leaf-to-leaf dependency; see Numerics/SpecialFunctions.cs) ----

        private const double Tiny = 1e-300, Eps = 1e-15;
        private const int MaxIter = 10_000;

        private static readonly double[] LanczosG =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        };

        private static double LnGamma(double x)
        {
            double a = LanczosG[0], t = x + 6.5, xm1 = x - 1;
            for (int i = 1; i < LanczosG.Length; i++) a += LanczosG[i] / (xm1 + i);
            return 0.5 * Math.Log(2 * Math.PI) + (xm1 + 0.5) * Math.Log(t) - t + Math.Log(a);
        }

        private static double RegularizedBetaI(double x, double a, double b)
        {
            if (x <= 0) return 0.0;
            if (x >= 1) return 1.0;
            double front = Math.Exp(LnGamma(a + b) - LnGamma(a) - LnGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
            if (x < (a + 1.0) / (a + b + 2.0)) return front * BetaCf(x, a, b) / a;
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
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1.0) < Eps) break;
            }
            return h;
        }
    }
}
