// ToolBelt drop-in — also copy Documents/ReportBuilder.cs (and what it needs).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Documents
{
    /// <summary>A rendered figure to place in a template: SVG markup and/or PNG bytes, plus a caption.</summary>
    public sealed class ReportFigure
    {
        public ReportFigure(string caption, string? svg = null, byte[]? png = null)
        {
            Caption = caption ?? throw new ArgumentNullException(nameof(caption));
            if (string.IsNullOrWhiteSpace(svg) && (png is null || png.Length == 0))
                throw new ArgumentException("A figure needs SVG markup or PNG bytes.");
            Svg = svg;
            Png = png;
        }

        public string Caption { get; }
        public string? Svg { get; }
        public byte[]? Png { get; }
    }

    /// <summary>One metric compared between two runs in <see cref="ReportTemplates.Comparison"/>.</summary>
    public sealed class ComparisonRow
    {
        public ComparisonRow(string metric, double a, double b, string unit = "")
        {
            Metric = metric ?? throw new ArgumentNullException(nameof(metric));
            A = a;
            B = b;
            Unit = unit ?? "";
        }

        public string Metric { get; }
        public double A { get; }
        public double B { get; }
        public string Unit { get; }

        /// <summary>B − A.</summary>
        public double Delta => B - A;

        /// <summary>(B − A) / |A| × 100, or NaN when A is zero.</summary>
        public double PercentChange => A == 0 ? double.NaN : (B - A) / Math.Abs(A) * 100;
    }

    /// <summary>
    /// One check in <see cref="ReportTemplates.PassFailMatrix"/>. Built straight from a tolerance evaluation, e.g.
    /// <c>new PassFailRow("Rail 3V3", "within 3.2–3.4 V", r.Pass, $"worst {r.WorstDeviation:G4} V", "3.2 – 3.4 V")</c>
    /// for a <c>ToleranceCheck</c> result <c>r</c>.
    /// </summary>
    public sealed class PassFailRow
    {
        public PassFailRow(string item, string check, bool passed, string measured = "", string limits = "", string note = "")
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Check = check ?? throw new ArgumentNullException(nameof(check));
            Passed = passed;
            Measured = measured ?? "";
            Limits = limits ?? "";
            Note = note ?? "";
        }

        public string Item { get; }
        public string Check { get; }
        public bool Passed { get; }
        public string Measured { get; }
        public string Limits { get; }
        public string Note { get; }
    }

    /// <summary>
    /// Ready-made report shapes so a caller does not start from a blank page. Each returns a
    /// <see cref="ReportBuilder"/> that can be extended (add sections, figures, notes) before rendering to HTML,
    /// Markdown, PDF or Word. The verdict always appears first, as a callout, so it is the first thing a reader sees.
    /// </summary>
    public static class ReportTemplates
    {
        /// <summary>
        /// A single run: verdict, run metadata, a results table, optional figures and notes.
        /// </summary>
        public static ReportBuilder RunSummary(
            string title,
            bool passed,
            IEnumerable<KeyValuePair<string, string>> metadata,
            IEnumerable<KeyValuePair<string, string>> results,
            IEnumerable<ReportFigure>? figures = null,
            string? notes = null)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));
            if (results is null) throw new ArgumentNullException(nameof(results));
            var report = new ReportBuilder(title);
            foreach (var kv in metadata) report.Metadata(kv.Key, kv.Value);
            report.Callout(passed ? CalloutKind.Success : CalloutKind.Error, passed ? "Run PASSED." : "Run FAILED.");
            report.TableOfContents();
            report.Heading("Results").Properties(results);
            AddFigures(report, figures);
            if (!string.IsNullOrWhiteSpace(notes)) report.Heading("Notes").Paragraph(notes!);
            return report;
        }

        /// <summary>
        /// Two runs side by side: a table of A, B, Δ and Δ% per metric, and a summary naming the largest relative
        /// change. Metrics whose |Δ%| reaches <paramref name="flagPercent"/> are flagged in a warning callout.
        /// </summary>
        public static ReportBuilder Comparison(
            string title,
            string labelA,
            string labelB,
            IEnumerable<ComparisonRow> rows,
            double flagPercent = 5,
            string numberFormat = "G6")
        {
            if (labelA is null) throw new ArgumentNullException(nameof(labelA));
            if (labelB is null) throw new ArgumentNullException(nameof(labelB));
            if (rows is null) throw new ArgumentNullException(nameof(rows));
            if (!(flagPercent >= 0)) throw new ArgumentOutOfRangeException(nameof(flagPercent), flagPercent, "Must be non-negative.");
            var list = new List<ComparisonRow>();
            foreach (var r in rows) list.Add(r ?? throw new ArgumentException("Rows must not contain null.", nameof(rows)));

            var report = new ReportBuilder(title).Metadata("Baseline (A)", labelA).Metadata("Candidate (B)", labelB);

            var flagged = new List<string>();
            ComparisonRow? largest = null;
            foreach (var r in list)
            {
                double p = r.PercentChange;
                if (!double.IsNaN(p) && (largest is null || Math.Abs(p) > Math.Abs(largest.PercentChange))) largest = r;
                if (!double.IsNaN(p) && Math.Abs(p) >= flagPercent && flagPercent > 0)
                    flagged.Add(r.Metric + " (" + Signed(p, "0.##") + "%)");
            }

            if (list.Count == 0)
                report.Callout(CalloutKind.Note, "No metrics to compare.");
            else if (flagged.Count > 0)
                report.Callout(CalloutKind.Warning, flagged.Count.ToString(CultureInfo.InvariantCulture) + " metric(s) changed by "
                    + flagPercent.ToString("0.##", CultureInfo.InvariantCulture) + "% or more: " + string.Join(", ", flagged) + ".");
            else
                report.Callout(CalloutKind.Success, "No metric changed by " + flagPercent.ToString("0.##", CultureInfo.InvariantCulture) + "% or more.");

            if (largest != null)
                report.Paragraph("Largest relative change: " + largest.Metric + ", " + Signed(largest.PercentChange, "0.##") + "%.");

            var table = new List<IReadOnlyList<string>>();
            foreach (var r in list)
            {
                string u = r.Unit.Length == 0 ? "" : " " + r.Unit;
                table.Add(new[]
                {
                    r.Metric,
                    r.A.ToString(numberFormat, CultureInfo.InvariantCulture) + u,
                    r.B.ToString(numberFormat, CultureInfo.InvariantCulture) + u,
                    Signed(r.Delta, numberFormat) + u,
                    double.IsNaN(r.PercentChange) ? "n/a" : Signed(r.PercentChange, "0.##") + "%",
                });
            }
            report.Heading("Metrics").Table(new[] { "Metric", labelA, labelB, "Δ", "Δ%" }, table);
            return report;
        }

        /// <summary>
        /// A pass/fail matrix: a verdict with counts, every check in a table, and the failures repeated in their own
        /// section so they can't be missed in a long list.
        /// </summary>
        public static ReportBuilder PassFailMatrix(string title, IEnumerable<PassFailRow> checks, IEnumerable<KeyValuePair<string, string>>? metadata = null)
        {
            if (checks is null) throw new ArgumentNullException(nameof(checks));
            var list = new List<PassFailRow>();
            foreach (var c in checks) list.Add(c ?? throw new ArgumentException("Checks must not contain null.", nameof(checks)));

            var report = new ReportBuilder(title);
            if (metadata != null) foreach (var kv in metadata) report.Metadata(kv.Key, kv.Value);

            int failed = 0;
            foreach (var c in list) if (!c.Passed) failed++;
            int passed = list.Count - failed;
            string counts = passed.ToString(CultureInfo.InvariantCulture) + " of " + list.Count.ToString(CultureInfo.InvariantCulture) + " checks passed";
            if (list.Count == 0) report.Callout(CalloutKind.Note, "No checks were run.");
            else if (failed == 0) report.Callout(CalloutKind.Success, "PASS - " + counts + ".");
            else report.Callout(CalloutKind.Error, "FAIL - " + counts + "; " + failed.ToString(CultureInfo.InvariantCulture) + " failed.");

            var headers = new[] { "Result", "Item", "Check", "Measured", "Limits", "Note" };
            var rows = new List<IReadOnlyList<string>>();
            foreach (var c in list) rows.Add(Row(c));
            report.Heading("All checks").Table(headers, rows);

            if (failed > 0)
            {
                var failures = new List<IReadOnlyList<string>>();
                foreach (var c in list) if (!c.Passed) failures.Add(Row(c));
                report.Heading("Failures").Table(headers, failures);
            }
            return report;

            static IReadOnlyList<string> Row(PassFailRow c)
                => new[] { c.Passed ? "PASS" : "FAIL", c.Item, c.Check, c.Measured, c.Limits, c.Note };
        }

        private static void AddFigures(ReportBuilder report, IEnumerable<ReportFigure>? figures)
        {
            if (figures is null) return;
            bool any = false;
            foreach (var f in figures)
            {
                if (f is null) continue;
                if (!any) { report.Heading("Figures"); any = true; }
                report.Figure(f.Caption, f.Svg, f.Png);
            }
        }

        private static string Signed(double v, string format)
            => (v > 0 ? "+" : "") + v.ToString(format, CultureInfo.InvariantCulture);
    }
}
