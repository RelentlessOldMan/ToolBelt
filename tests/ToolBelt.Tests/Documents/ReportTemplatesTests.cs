using System;
using System.Collections.Generic;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    public sealed class ReportTemplatesTests
    {
        private static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v);

        public void RunSummary_VerdictFirst_ResultsFiguresNotes()
        {
            string html = ReportTemplates.RunSummary("Run 7", passed: true,
                new[] { KV("Station", "3") },
                new[] { KV("Yield", "98.2 %"), KV("Cycle time", "41 s") },
                new[] { new ReportFigure("Throughput", svg: "<svg xmlns=\"http://www.w3.org/2000/svg\"/>") },
                notes: "Nominal.").ToHtml();
            int verdict = html.IndexOf("Run PASSED.", StringComparison.Ordinal);
            Check.True(verdict > 0 && verdict < html.IndexOf("<h2", StringComparison.Ordinal), "verdict precedes the sections");
            Check.True(html.Contains("callout success"));
            Check.True(html.Contains("<th>Yield</th><td>98.2 %</td>"));
            Check.True(html.Contains("Figure 1: Throughput"));
            Check.True(html.Contains(">Notes</h2>") && html.Contains("Nominal."));

            string failed = ReportTemplates.RunSummary("Run 8", false, new KeyValuePair<string, string>[0], new KeyValuePair<string, string>[0]).ToHtml();
            Check.True(failed.Contains("callout error") && failed.Contains("Run FAILED."));
            Check.False(failed.Contains(">Figures</h2>"));
            Check.False(failed.Contains(">Notes</h2>"));
        }

        public void Comparison_DeltasFlagsAndLargestChange()
        {
            var rows = new[]
            {
                new ComparisonRow("throughput", 100, 110, "u/h"),
                new ComparisonRow("latency", 50, 50.5, "ms"),
                new ComparisonRow("errors", 0, 3),
            };
            ReportBuilder report = ReportTemplates.Comparison("A vs B", "v1.0", "v1.1", rows, flagPercent: 5);
            string md = report.ToMarkdown();
            Check.True(md.Contains("> [!WARNING]\n> 1 metric(s) changed by 5% or more: throughput (+10%)."), md);
            Check.True(md.Contains("Largest relative change: throughput, +10%."));
            Check.True(md.Contains("| throughput | 100 u/h | 110 u/h | +10 u/h | +10% |"), md);
            Check.True(md.Contains("| latency | 50 ms | 50.5 ms | +0.5 ms | +1% |"));
            Check.True(md.Contains("| errors | 0 | 3 | +3 | n/a |"));
            Check.True(md.Contains("| Metric | v1.0 | v1.1 | Change | Change % |"));

            Check.Close(10, rows[0].PercentChange);
            Check.True(double.IsNaN(rows[2].PercentChange));
        }

        public void Comparison_NoFlagsAndEmpty()
        {
            string quiet = ReportTemplates.Comparison("q", "a", "b", new[] { new ComparisonRow("m", 10, 10.1) }).ToMarkdown();
            Check.True(quiet.Contains("> [!TIP]\n> No metric changed by 5% or more."));
            string empty = ReportTemplates.Comparison("e", "a", "b", new ComparisonRow[0]).ToMarkdown();
            Check.True(empty.Contains("No metrics to compare."));
            Check.True(ReportTemplates.Comparison("n", "a", "b", new[] { new ComparisonRow("m", -20, -10) }).ToMarkdown().Contains("+50%"));
        }

        public void PassFailMatrix_CountsAndFailuresSection()
        {
            var checks = new[]
            {
                new PassFailRow("Rail 3V3", "within limits", true, "3.31 V", "3.2 – 3.4 V"),
                new PassFailRow("Rail 5V", "within limits", false, "5.41 V", "4.8 – 5.2 V", "worst +0.21 V"),
                new PassFailRow("Ripple", "< 50 mV", true, "12 mV"),
            };
            string md = ReportTemplates.PassFailMatrix("Power-on test", checks, new[] { KV("Board", "SN-0012") }).ToMarkdown();
            Check.True(md.Contains("> [!CAUTION]\n> FAIL - 2 of 3 checks passed; 1 failed."), md);
            Check.True(md.Contains("**Board:** SN-0012"));
            Check.True(md.Contains("## All checks") && md.Contains("## Failures"));
            Check.True(md.Contains("| FAIL | Rail 5V | within limits | 5.41 V | 4.8 – 5.2 V | worst +0.21 V |"));
            int failuresAt = md.IndexOf("## Failures", StringComparison.Ordinal);
            Check.False(md.Substring(failuresAt).Contains("Rail 3V3"), "passing checks are not repeated under Failures");
        }

        public void PassFailMatrix_AllPassAndNone()
        {
            string ok = ReportTemplates.PassFailMatrix("t", new[] { new PassFailRow("a", "b", true) }).ToMarkdown();
            Check.True(ok.Contains("> [!TIP]\n> PASS - 1 of 1 checks passed."));
            Check.False(ok.Contains("## Failures"));
            Check.True(ReportTemplates.PassFailMatrix("t", new PassFailRow[0]).ToMarkdown().Contains("No checks were run."));
        }

        public void Templates_AreExtendable()
        {
            ReportBuilder r = ReportTemplates.PassFailMatrix("t", new[] { new PassFailRow("a", "b", true) });
            int before = r.Count;
            r.Heading("Appendix").Paragraph("Raw data attached.");
            Check.Equal(before + 2, r.Count);
            Check.True(r.ToHtml().Contains("Raw data attached."));
        }

        public void Validation()
        {
            Check.Throws<ArgumentException>(() => new ReportFigure("x"));
            Check.Throws<ArgumentNullException>(() => ReportTemplates.Comparison("t", null!, "b", new ComparisonRow[0]));
            Check.Throws<ArgumentOutOfRangeException>(() => ReportTemplates.Comparison("t", "a", "b", new ComparisonRow[0], flagPercent: -1));
            Check.Throws<ArgumentException>(() => ReportTemplates.PassFailMatrix("t", new PassFailRow[] { null! }));
            Check.Throws<ArgumentNullException>(() => new PassFailRow(null!, "c", true));
        }
    }
}
