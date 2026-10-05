using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    public sealed class ReportBuilderTests
    {
        private const string Svg = "<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\"/></svg>";
        private static readonly byte[] Png = { 0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3 };

        private static ReportBuilder Sample() => new ReportBuilder("Run <42> Report")
            .Metadata("Operator", "J. Doe")
            .Metadata("Date", "2026-10-05")
            .TableOfContents()
            .Heading("Summary")
            .Paragraph("All <good> & fine.")
            .Callout(CalloutKind.Warning, "Fan speed near limit.")
            .Heading("Details", 2)
            .Properties(("Station", "3"), ("Firmware", "1.2.0"))
            .Table(new[] { "Name", "Value" }, new[] { new[] { "alpha", "1" }, new[] { "beta", "22" } }, "Readings")
            .Table(new[] { "X" }, new[] { new[] { "x1", "extra cell dropped" } }, "Second")
            .Figure("Trend", svg: Svg, png: Png)
            .Figure("Raster only", png: Png)
            .Code("line one\nline <two>", "text")
            .Heading("Summary");                    // duplicate heading text

        private static string DocxXml(byte[] docx)
        {
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            return reader.ReadToEnd();
        }

        // ---------- HTML ----------

        public void Html_StructureAndEscaping()
        {
            string html = Sample().ToHtml();
            Check.True(html.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal));
            Check.True(html.Contains("<title>Run &lt;42&gt; Report</title>"));
            Check.True(html.Contains("<h1 id=\"run-42-report\">Run &lt;42&gt; Report</h1>"));
            Check.True(html.Contains("<th>Operator</th><td>J. Doe</td>"));
            Check.True(html.Contains("All &lt;good&gt; &amp; fine."));
            Check.False(html.Contains("<good>"));
            Check.True(html.Contains("<div class=\"callout warning\"><strong>Warning:</strong> Fan speed near limit.</div>"));
            Check.True(html.Contains("<h2 id=\"summary\">Summary</h2>"));
            Check.True(html.Contains("<h3 id=\"details\">Details</h3>"));
            Check.True(html.Contains("<h2 id=\"summary-1\">Summary</h2>"));   // GitHub-style de-duplication
        }

        public void Html_TocLinksResolveToHeadings()
        {
            string html = Sample().ToHtml();
            var hrefs = Regex.Matches(html, "href=\"#([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
            Check.Equal("summary,details,summary-1", string.Join(",", hrefs));
            foreach (string id in hrefs) Check.True(html.Contains("id=\"" + id + "\""), $"no target for #{id}");
        }

        public void Html_NumbersTablesAndFigures_InlinesSvgElsePng()
        {
            string html = Sample().ToHtml();
            Check.True(html.Contains("Table 1: Readings") && html.Contains("Table 2: Second"));
            Check.True(html.Contains("<figcaption>Figure 1: Trend</figcaption>"));
            Check.True(html.Contains("<figcaption>Figure 2: Raster only</figcaption>"));
            Check.False(html.Contains("<?xml"));                                // declaration stripped for inlining
            Check.True(html.Contains("<figure><svg xmlns="));                   // SVG preferred when present
            Check.True(html.Contains("src=\"data:image/png;base64," + Convert.ToBase64String(Png) + "\""));
            Check.False(html.Contains("extra cell dropped"));                   // rows truncated to header width
        }

        // ---------- Markdown ----------

        public void Markdown_HeadingsTocAlertsFigures()
        {
            string md = Sample().ToMarkdown();
            Check.True(md.StartsWith("# Run", StringComparison.Ordinal));
            Check.True(md.Contains("**Operator:** J. Doe"));
            Check.True(md.Contains("- [Summary](#summary)"));
            Check.True(md.Contains("  - [Details](#details)"));                // nested by level
            Check.True(md.Contains("- [Summary](#summary-1)"));
            Check.True(md.Contains("## Summary") && md.Contains("### Details"));
            Check.True(md.Contains("> [!WARNING]\n> Fan speed near limit."));
            Check.True(md.Contains("**Table 1: Readings**"));
            // PNG preferred for Markdown even when SVG is also given.
            Check.True(md.Contains("(data:image/png;base64," + Convert.ToBase64String(Png) + ")"));
            Check.True(md.Contains("```text\nline one\nline <two>\n```"));
        }

        public void Markdown_SvgOnlyFigureUsesSvgDataUri()
        {
            string md = new ReportBuilder("R").Figure("Vector", svg: Svg).ToMarkdown();
            Check.True(md.Contains("data:image/svg+xml;base64,"));
            Check.True(md.Contains("*Figure 1: Vector*"));
        }

        public void Markdown_EscapesInlineSyntaxInMetadata()
        {
            string md = new ReportBuilder("R").Metadata("Tag", "a*b_c").ToMarkdown();
            Check.True(md.Contains("a\\*b\\_c"), md);
        }

        // ---------- PDF ----------

        public void Pdf_IsValidAndCarriesTheContent()
        {
            byte[] pdf = Sample().ToPdf();
            string text = Encoding.ASCII.GetString(pdf);
            Check.True(text.StartsWith("%PDF-", StringComparison.Ordinal));
            Check.True(text.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal));
            Check.True(text.Contains("Contents"));
            Check.True(text.Contains("Table 1: Readings"));
            Check.True(text.Contains("(alpha) Tj") && text.Contains("(1) Tj"));       // table cells
            Check.True(text.Contains("[Figure 1: Trend - graphics are not rendered in PDF output]"));
            Check.True(text.Contains("WARNING: Fan speed near limit."));
            Check.True(text.Contains("(Operator) Tj") && text.Contains("(J. Doe) Tj"));
        }

        // ---------- Word ----------

        public void Docx_IsAZipWithTheContent()
        {
            string xml = DocxXml(Sample().ToDocx());
            Check.True(xml.Contains("Run &lt;42&gt; Report"));
            Check.True(xml.Contains("Table 1: Readings"));
            Check.True(xml.Contains("[Figure 1: Trend - see the HTML version for graphics]"));
            Check.True(xml.Contains("Warning: Fan speed near limit."));
            Check.True(xml.Contains("<w:tbl>"));
            Check.True(xml.Contains("line &lt;two&gt;"));
        }

        // ---------- misc ----------

        public void Slug_GithubStyle()
        {
            Check.Equal("hello-world", ReportBuilder.Slug("Hello, World!"));
            Check.Equal("résumé-2", ReportBuilder.Slug("  Résumé 2  "));
            Check.Equal("a_b-c", ReportBuilder.Slug("A_b-C"));
            Check.Equal("section", ReportBuilder.Slug("!!!"));
        }

        public void Validation()
        {
            Check.Throws<ArgumentException>(() => new ReportBuilder("  "));
            Check.Throws<ArgumentNullException>(() => new ReportBuilder(null!));
            var r = new ReportBuilder("R");
            Check.Throws<ArgumentOutOfRangeException>(() => r.Heading("x", 5));
            Check.Throws<ArgumentException>(() => r.Figure("no content"));
            Check.Throws<ArgumentException>(() => r.Table(Array.Empty<string>(), Array.Empty<string[]>()));
            Check.Throws<ArgumentNullException>(() => r.Paragraph(null!));
            Check.Equal(0, r.Count);
        }
    }
}
