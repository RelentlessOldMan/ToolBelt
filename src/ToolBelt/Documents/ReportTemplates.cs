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
            // An XML declaration alone is no markup (as ReportBuilder.Figure judges it): it was accepted here and failed
            // only when the report was built as HTML.
            string? markup = svg?.TrimStart();
            if (markup != null && markup.StartsWith("<?xml", StringComparison.Ordinal) && markup.IndexOf("?>", StringComparison.Ordinal) is int end && end >= 0)
                markup = markup.Substring(end + 2);
            if (string.IsNullOrWhiteSpace(markup) && (png is null || png.Length == 0))
                throw new ArgumentException("A figure needs SVG markup or PNG bytes.", nameof(svg));
            Svg = svg;
            // A copy: a buffer the caller reused before the report was built changed the figure.
            Png = (byte[]?)png?.Clone();
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
        // B − A overflowed to ∞ for values of opposite sign near ±MaxValue (a finite 200%); then each is scaled first.
        public double PercentChange => A == 0 ? double.NaN
            : !double.IsInfinity(B - A) || double.IsInfinity(A) || double.IsInfinity(B) ? (B - A) / Math.Abs(A) * 100
            : (B / Math.Abs(A) - Math.Sign(A)) * 100;
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
            AddMetadata(report, metadata);
            report.Callout(passed ? CalloutKind.Success : CalloutKind.Error, passed ? "Run PASSED." : "Run FAILED.");
            report.TableOfContents();
            report.Heading("Results").Properties(results);
            AddFigures(report, figures);
            if (!string.IsNullOrWhiteSpace(notes)) report.Heading("Notes").Paragraph(notes!);
            return report;
        }

        // A null key named the builder's internal parameter ("key"); it names the caller's.
        private static void AddMetadata(ReportBuilder report, IEnumerable<KeyValuePair<string, string>> metadata)
        {
            foreach (var kv in metadata)
            {
                if (kv.Key is null) throw new ArgumentException("Metadata keys must not be null.", nameof(metadata));
                report.Metadata(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// Two runs side by side: a table of A, B, change and percent change per metric, and a summary naming the largest
        /// relative change. Metrics whose |change %| reaches <paramref name="flagPercent"/> are flagged in a warning callout;
        /// 0 flags any change at all, including one from a baseline of 0 (which has no percent change). (Columns are spelled out rather than "Δ", which the PDF's base fonts can't show.)
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
            // Infinity turned flagging off and claimed "No metric changed by Infinity% or more".
            if (!(flagPercent >= 0) || double.IsPositiveInfinity(flagPercent))
                throw new ArgumentOutOfRangeException(nameof(flagPercent), flagPercent, "Must be finite and non-negative.");
            if (numberFormat is null) throw new ArgumentNullException(nameof(numberFormat));
            // Tried up front: an integer-only format ("X", "D") threw a bare FormatException partway through the build.
            try { 1.5.ToString(numberFormat, CultureInfo.InvariantCulture); }
            catch (FormatException ex) { throw new ArgumentException($"'{numberFormat}' is not a numeric format for a double.", nameof(numberFormat), ex); }
            var list = new List<ComparisonRow>();
            foreach (var r in rows) list.Add(r ?? throw new ArgumentException("Rows must not contain null.", nameof(rows)));

            var report = new ReportBuilder(title).Metadata("Baseline (A)", labelA).Metadata("Candidate (B)", labelB);

            var flagged = new List<string>();
            ComparisonRow? largest = null;
            foreach (var r in list)
            {
                double p = r.PercentChange;
                if (!double.IsNaN(p) && (largest is null || Math.Abs(p) > Math.Abs(largest.PercentChange))) largest = r;
                // From a zero baseline there is no percent change, but any change is past every threshold: with a threshold
                // above 0 (the default 5 among them), 0 -> 100 was never flagged and the callout said nothing changed.
                if (r.A == 0 && r.B != 0 && !double.IsNaN(r.B)) flagged.Add(r.Metric + " (from 0)");
                else if (!double.IsNaN(p) && (flagPercent > 0 ? Math.Abs(p) >= flagPercent : p != 0))
                    flagged.Add(r.Metric + " (" + SignedPercent(p, flagPercent) + "%)");
            }

            if (list.Count == 0)
                report.Callout(CalloutKind.Note, "No metrics to compare.");
            else if (flagged.Count > 0)
                report.Callout(CalloutKind.Warning, flagged.Count.ToString(CultureInfo.InvariantCulture) + " metric(s) changed"
                    + (flagPercent > 0 ? " by " + ThresholdText(flagPercent) + "% or more" : "")
                    + ": " + string.Join(", ", flagged) + ".");
            else
                report.Callout(CalloutKind.Success, flagPercent > 0
                    ? "No metric changed by " + ThresholdText(flagPercent) + "% or more."
                    : "No metric changed.");

            if (largest != null)
                report.Paragraph("Largest relative change: " + largest.Metric + ", " + SignedPercent(largest.PercentChange, flagPercent) + "%.");

            var table = new List<IReadOnlyList<string>>();
            foreach (var r in list)
            {
                string u = r.Unit.Length == 0 ? "" : " " + r.Unit;
                table.Add(new[]
                {
                    r.Metric,
                    Plain(r.A, numberFormat) + u,
                    Plain(r.B, numberFormat) + u,
                    Signed(r.Delta, numberFormat) + u,
                    double.IsNaN(r.PercentChange) ? "n/a" : SignedPercent(r.PercentChange, flagPercent) + "%",
                });
            }
            report.Heading("Metrics").Table(new[] { "Metric", labelA, labelB, "Change", "Change %" }, table);
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
            if (metadata != null) AddMetadata(report, metadata);

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

        // "+1.5" / "-1.5"; a value that rounds to zero prints as "0", never "-0" or "+0".
        // Two decimals, but never 0 for a percentage that isn't: a threshold of 0.001 read "changed by 0% or more".
        // A million percent and more in three significant digits: "0.##" wrote every digit (303 of them for 1e-300 to 1).
        private static string Percent(double v)
        {
            double a = Math.Abs(v);
            return v.ToString(v != 0 && (a < 0.005 || a >= 1e6) ? "G3" : "0.##", CultureInfo.InvariantCulture);
        }

        private static string SignedPercent(double v, double threshold) => v == 0 ? "0" : (v > 0 ? "+" : "") + Percent(v, threshold);

        // Rounded, a change and the threshold could print in the wrong order: 4.996% printed "+5%" beside "No metric changed
        // by 5% or more", and a threshold of 0.014 printed "0.01%" beside an unflagged "+0.01%". The threshold prints in full
        // and a change takes the digits it needs to print on the same side of it as it is.
        private static string ThresholdText(double threshold)
        {
            string s = Percent(threshold);
            for (int g = 3; g <= 17 && Parsed(s) != threshold; g++) s = threshold.ToString("G" + g.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return s;
        }

        private static string Percent(double v, double threshold)
        {
            string s = Percent(v);
            if (!(threshold > 0) || double.IsInfinity(v) || double.IsNaN(v)) return s;
            bool reaches = Math.Abs(v) >= threshold;
            for (int g = 3; g <= 17 && (Math.Abs(Parsed(s)) >= threshold) != reaches; g++) s = v.ToString("G" + g.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return s;
        }

        private static double Parsed(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        // A value that rounds to zero prints as "0", never "-0" (-0.0, or -0.001 under "0.##"), as in the Change column.
        private static string Plain(double v, string format)
            => ShowsAsZero(v, format) ? 0.0.ToString(format, CultureInfo.InvariantCulture) : v.ToString(format, CultureInfo.InvariantCulture);

        private static string Signed(double v, string format)
            => ShowsAsZero(v, format) ? 0.0.ToString(format, CultureInfo.InvariantCulture) : (v > 0 ? "+" : "") + v.ToString(format, CultureInfo.InvariantCulture);

        // True when v prints as zero does: its size formatted like 0. Parsing the text back missed every format the parser
        // cannot read ("P0" gave "-0 %" and "+0 %", "C" gave "(0.00)", "0.0 ms" gave "-0.0 ms").
        private static bool ShowsAsZero(double v, string format)
            => !double.IsNaN(v) && Math.Abs(v).ToString(format, CultureInfo.InvariantCulture) == 0.0.ToString(format, CultureInfo.InvariantCulture);
    }
}
