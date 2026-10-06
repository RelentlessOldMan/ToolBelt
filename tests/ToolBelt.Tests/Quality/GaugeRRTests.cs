using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Quality
{
    public sealed class GaugeRRTests
    {
        private static void Rel(double expected, double actual, double tol, string what)
            => Check.Close(expected, actual, tol * Math.Max(1e-12, Math.Abs(expected)) + 1e-15, what);

        public void MatchesIndependentLeastSquaresReference()
        {
            int n = 0;
            foreach (var c in GaugeRRReferenceData.Cases)
            {
                var res = GaugeRR.Analyze(c.Data, alphaToRemoveInteraction: c.Alpha);
                string tag = $"case {n++}";
                Check.Equal(c.InteractionRemoved, res.InteractionRemoved, tag);
                Rel(c.InteractionP, res.InteractionP, 1e-8, tag + " interaction p");
                var part = res.Anova.Single(r => r.Source == "Part");
                var op = res.Anova.Single(r => r.Source == "Operator");
                var rep = res.Anova.Single(r => r.Source == "Repeatability");
                Rel(c.SsPart, part.SumOfSquares, 1e-9, tag + " SS part");
                Rel(c.SsOperator, op.SumOfSquares, 1e-9, tag + " SS operator");
                Rel(c.PartP, part.P, 1e-8, tag + " part p");
                Rel(c.OperatorP, op.P, 1e-8, tag + " operator p");
                if (c.InteractionRemoved)
                    Rel(c.SsInteraction + c.SsRepeatability, rep.SumOfSquares, 1e-9, tag + " pooled SS");
                else
                {
                    Rel(c.SsInteraction, res.Anova.Single(r => r.Source == "Part * Operator").SumOfSquares, 1e-9, tag + " SS interaction");
                    Rel(c.SsRepeatability, rep.SumOfSquares, 1e-9, tag + " SS repeatability");
                }
                Rel(c.VarRepeatability, res.Repeatability.Variance, 1e-9, tag + " var repeat");
                Rel(c.VarOperator, res.Operator.Variance, 1e-8, tag + " var operator");
                Rel(c.VarInteraction, res.PartByOperator.Variance, 1e-8, tag + " var interaction");
                Rel(c.VarPart, res.PartToPart.Variance, 1e-9, tag + " var part");
                Check.Equal(c.Ndc, res.DistinctCategories, tag + " ndc");
            }
        }

        public void ComponentsAreConsistent()
        {
            var res = GaugeRR.Analyze(GaugeRRReferenceData.Cases[1].Data, tolerance: 4);
            Check.Close(100, res.TotalGaugeRR.PercentContribution + res.PartToPart.PercentContribution, 1e-9);
            Check.Close(res.Repeatability.Variance + res.Reproducibility.Variance, res.TotalGaugeRR.Variance, 1e-15);
            Check.Close(res.Operator.Variance + res.PartByOperator.Variance, res.Reproducibility.Variance, 1e-15);
            Check.Close(6 * res.TotalGaugeRR.StdDev, res.TotalGaugeRR.StudyVariation, 1e-12);
            Check.Close(100 * res.TotalGaugeRR.StudyVariation / 4, res.TotalGaugeRR.PercentTolerance, 1e-9);
            Check.Close(100, res.TotalVariation.PercentStudyVariation, 1e-12);
            double ssSum = res.Anova.Where(r => r.Source != "Total").Sum(r => r.SumOfSquares);
            Check.Close(res.Anova.Last().SumOfSquares, ssSum, 1e-9);
            Check.Equal(10 * 3 * 2 - 1, res.Anova.Last().DegreesOfFreedom);
            Check.True(double.IsNaN(GaugeRR.Analyze(GaugeRRReferenceData.Cases[1].Data).TotalGaugeRR.PercentTolerance));
            Check.True(res.ToString().Contains("Part * Operator") && res.ToString().Contains("Distinct categories"));
        }

        public void EstimatesAreUnbiasedOverManyStudies()
        {
            // Known truth: σ²part = 1, σ²operator = 0.09, σ²interaction = 0.04, σ²repeat = 0.0625. With the interaction always
            // kept (alpha = 1) the ANOVA estimators are unbiased (truncation at 0 is rare at these sizes).
            var rng = new Random(87);
            const int studies = 3000;
            double sumPart = 0, sumOp = 0, sumInt = 0, sumRep = 0;
            for (int s = 0; s < studies; s++)
            {
                var data = Simulate(rng, 10, 5, 3, 1.0, 0.3, 0.2, 0.25);
                var res = GaugeRR.Analyze(data, alphaToRemoveInteraction: 1);
                Check.False(res.InteractionRemoved);
                sumPart += res.PartToPart.Variance; sumOp += res.Operator.Variance;
                sumInt += res.PartByOperator.Variance; sumRep += res.Repeatability.Variance;
            }
            Check.Close(1.0, sumPart / studies, 0.05, "part");
            Check.Close(0.09, sumOp / studies, 0.01, "operator");
            Check.Close(0.04, sumInt / studies, 0.004, "interaction");
            Check.Close(0.0625, sumRep / studies, 0.002, "repeatability");
        }

        public void InteractionIsPooledWhenNotSignificant()
        {
            // No true interaction, so most seeds give p > 0.05; take the first one that does.
            double[,,] data = null!;
            GaugeRRResult res = null!;
            for (int seed = 3; res is null || res.InteractionP <= 0.05; seed++)
            {
                data = Simulate(new Random(seed), 10, 3, 3, 1.0, 0.1, 0.0, 0.2);
                res = GaugeRR.Analyze(data, alphaToRemoveInteraction: 0.05);
            }
            Check.True(res.InteractionRemoved);
            Check.Equal(0.0, res.PartByOperator.Variance);
            Check.False(res.Anova.Any(r => r.Source == "Part * Operator"));
            Check.Equal(4, res.Anova.Count);
            Check.Equal(2 * 9 + 10 * 3 * 2, res.Anova.Single(r => r.Source == "Repeatability").DegreesOfFreedom);
            var kept = GaugeRR.Analyze(data, alphaToRemoveInteraction: 1);
            Check.False(kept.InteractionRemoved);
            Check.Equal(5, kept.Anova.Count);
        }

        public void RecordsOverloadMatchesArray()
        {
            var data = GaugeRRReferenceData.Cases[0].Data;
            var records = new List<(string, string, double)>();
            // Shuffled order, string keys: grouping must not depend on the order of the rows.
            var order = new List<(int, int, int)>();
            for (int i = 0; i < data.GetLength(0); i++)
                for (int j = 0; j < data.GetLength(1); j++)
                    for (int k = 0; k < data.GetLength(2); k++) order.Add((i, j, k));
            foreach (var (i, j, k) in order.OrderBy(t => t.Item3).ThenByDescending(t => t.Item2))
                records.Add(("P" + i, "Op" + j, data[i, j, k]));
            var a = GaugeRR.Analyze(data);
            var b = GaugeRR.Analyze(records);
            Check.Close(a.TotalGaugeRR.Variance, b.TotalGaugeRR.Variance, 1e-12);
            Check.Close(a.PartToPart.Variance, b.PartToPart.Variance, 1e-12);
            Check.Close(a.InteractionP, b.InteractionP, 1e-12);

            records.RemoveAt(0);
            Check.Throws<ArgumentException>(() => GaugeRR.Analyze(records));
        }

        public void VerdictsAndDistinctCategories()
        {
            // A precise gauge on widely spread parts.
            var good = GaugeRR.Analyze(Simulate(new Random(1), 10, 3, 3, 5.0, 0.01, 0, 0.05));
            Check.Equal(GaugeAcceptability.Acceptable, good.Acceptability);
            Check.True(good.DistinctCategories >= 5, good.DistinctCategories.ToString());
            // A noisy gauge on near-identical parts.
            var bad = GaugeRR.Analyze(Simulate(new Random(2), 10, 3, 3, 0.05, 0.3, 0, 1.0));
            Check.Equal(GaugeAcceptability.Unacceptable, bad.Acceptability);
            Check.Equal(1, bad.DistinctCategories);
            // A gauge that reads every part identically on every trial: zero gauge variation.
            var perfect = new double[3, 2, 2];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++) for (int k = 0; k < 2; k++) perfect[i, j, k] = i;
            var res = GaugeRR.Analyze(perfect);
            Check.Equal(0.0, res.TotalGaugeRR.Variance);
            Check.Equal(int.MaxValue, res.DistinctCategories);
            Check.Equal(GaugeAcceptability.Acceptable, res.Acceptability);
        }

        public void RejectsBadInput()
        {
            Check.Throws<ArgumentNullException>(() => GaugeRR.Analyze((double[,,])null!));
            Check.Throws<ArgumentException>(() => GaugeRR.Analyze(new double[1, 3, 3]));
            Check.Throws<ArgumentException>(() => GaugeRR.Analyze(new double[3, 1, 3]));
            Check.Throws<ArgumentException>(() => GaugeRR.Analyze(new double[3, 3, 1]));
            var data = new double[2, 2, 2];
            Check.Throws<ArgumentOutOfRangeException>(() => GaugeRR.Analyze(data, tolerance: 0));
            Check.Throws<ArgumentOutOfRangeException>(() => GaugeRR.Analyze(data, studyVarianceMultiplier: -1));
            Check.Throws<ArgumentOutOfRangeException>(() => GaugeRR.Analyze(data, alphaToRemoveInteraction: 2));
            data[1, 1, 1] = double.NaN;
            Check.Throws<ArgumentException>(() => GaugeRR.Analyze(data));
            Check.Throws<ArgumentException>(() => GaugeRR.Analyze(new List<(int, int, double)>()));
        }

        private static double[,,] Simulate(Random rng, int p, int o, int r, double sdPart, double sdOp, double sdInt, double sdRep)
        {
            double N() => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
            var part = Enumerable.Range(0, p).Select(_ => sdPart * N()).ToArray();
            var op = Enumerable.Range(0, o).Select(_ => sdOp * N()).ToArray();
            var data = new double[p, o, r];
            for (int i = 0; i < p; i++)
                for (int j = 0; j < o; j++)
                {
                    double inter = sdInt * N();
                    for (int k = 0; k < r; k++) data[i, j, k] = 10 + part[i] + op[j] + inter + sdRep * N();
                }
            return data;
        }
    }
}
