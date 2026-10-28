using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades the regularized incomplete beta and gamma functions against 50-digit mpmath references over a grid of shapes
    /// from 1e-300 to 1e20 (Data/incomplete-*.tsv, from scripts/gen-incomplete-references.py). The error is measured in
    /// units of eps·(1 + κ), κ being the function's condition number: input rounding alone moves the value by ~eps·κ, so a
    /// backward-stable algorithm stays within a few units. Anything over 16 fails.
    /// </summary>
    public sealed class IncompleteFunctionsGradedTests
    {
        private const double Eps = 2.220446049250313e-16, Limit = 16;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string DataDir()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string d = Path.Combine(dir.FullName, "tests", "ToolBelt.MathChecks", "Data");
                if (Directory.Exists(d)) return d;
            }
            throw new DirectoryNotFoundException("tests/ToolBelt.MathChecks/Data not found above " + AppContext.BaseDirectory);
        }

        private static IEnumerable<string[]> Rows(string file) =>
            File.ReadLines(Path.Combine(DataDir(), file)).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t'));

        private static double D(string s) => double.Parse(s, Inv);

        // Relative error; references below the normal range compare absolutely at 1e-300 (subnormals carry few digits).
        private static double Err(double want, double got) => double.IsNaN(got) ? double.NaN
            : want == 0 ? (Math.Abs(got) < 1e-300 ? 0 : double.PositiveInfinity)
            : Math.Abs(got - want) / Math.Max(Math.Abs(want), 1e-300);

        /// <summary>Fails on rows over the limit, and lists rows over 16 eps without a κ (graded at κ = 0) in a need file
        /// for the generator's kappa step.</summary>
        private static void Grade(string name, List<(string what, string key, double want, double got, string kappa)> res)
        {
            var need = res.Where(t => t.kappa.Length == 0 && !(Err(t.want, t.got) <= Limit * Eps)).Select(t => t.key).ToList();
            string needFile = Path.Combine(DataDir(), name + ".need.tsv");
            if (need.Count > 0) File.WriteAllLines(needFile, need); else if (File.Exists(needFile)) File.Delete(needFile);
            var bad = res.Select(t => (t, units: Err(t.want, t.got) / (Eps * (1 + (t.kappa.Length == 0 ? 0 : D(t.kappa))))))
                .Where(u => !(u.units <= Limit)).OrderByDescending(u => double.IsNaN(u.units) ? double.MaxValue : u.units).ToList();
            Check.True(bad.Count == 0, $"{bad.Count} of {res.Count} values over {Limit} units" + (need.Count > 0
                ? $" ({need.Count} without a kappa: run scripts/gen-incomplete-references.py kappa)" : "") + ":" + Environment.NewLine
                + string.Join(Environment.NewLine, bad.Take(20).Select(u =>
                    $"  {u.t.what} want={u.t.want:R} got={u.t.got:R} kappa={(u.t.kappa.Length == 0 ? "?" : u.t.kappa)} units={u.units:G3}")));
        }

        public void RegularizedBetaI_WithinSixteenUnits()
        {
            var res = new List<(string, string, double, double, string)>();
            foreach (var p in Rows("incomplete-beta.tsv"))
            {
                double x = D(p[0]), a = D(p[1]), b = D(p[2]);
                res.Add(($"I({p[0]}, {p[1]}, {p[2]})", string.Join("\t", p, 0, 3), D(p[3]),
                    SpecialFunctions.RegularizedBetaI(x, a, b), p.Length > 4 ? p[4] : ""));
            }
            Check.True(res.Count > 5000, "beta grid has " + res.Count + " rows");
            Grade("incomplete-beta", res);
        }

        public void RegularizedGammaPQ_WithinSixteenUnits()
        {
            var res = new List<(string, string, double, double, string)>();
            foreach (var p in Rows("incomplete-gamma.tsv"))
            {
                double a = D(p[0]), x = D(p[1]);
                res.Add(($"P({p[0]}, {p[1]})", p[0] + "\t" + p[1] + "\tP", D(p[2]), SpecialFunctions.RegularizedGammaP(a, x), p.Length > 4 ? p[4] : ""));
                res.Add(($"Q({p[0]}, {p[1]})", p[0] + "\t" + p[1] + "\tQ", D(p[3]), SpecialFunctions.RegularizedGammaQ(a, x), p.Length > 5 ? p[5] : ""));
            }
            Check.True(res.Count > 800, "gamma grid has " + res.Count + " values");
            Grade("incomplete-gamma", res);
        }
    }
}
