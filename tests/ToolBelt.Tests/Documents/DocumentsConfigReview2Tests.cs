using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 26 (documents and configuration, second pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview2Tests
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        // ---------------- ConfigBinder ----------------

        public sealed class Numbers
        {
            public double Ratio { get; set; }
            public decimal Price { get; set; }
            public float F { get; set; }
        }

        public void Binder_RejectsADecimalCommaInsteadOfReadingThousands()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Numbers>(new Dictionary<string, string?>
                { ["Ratio"] = "1,5", ["Price"] = "2,50", ["F"] = "0,25" }));
            Check.Equal(3, ex.Errors.Count);
            var ok = ConfigBinder.Bind<Numbers>(new Dictionary<string, string?> { ["Ratio"] = "1.5", ["Price"] = "2.50", ["F"] = "-2.5e1" });
            Check.Equal(1.5, ok.Ratio);
            Check.Equal(2.50m, ok.Price);
            Check.Equal(-25f, ok.F);
        }

        public sealed class GetOnlyList
        {
            public List<string> Hosts { get; } = new List<string> { "default" };
        }

        public void Binder_FillsAGetOnlyListInPlace()
        {
            var o = ConfigBinder.Bind<GetOnlyList>(new Dictionary<string, string?> { ["Hosts:0"] = "a", ["Hosts:1"] = "b" });
            Check.Equal("a,b", string.Join(",", o.Hosts));
        }

        public sealed class Endpoint { public string Host { get; set; } = ""; }

        public sealed class Unsupported
        {
            public Uri? Url { get; set; }
            public Dictionary<string, string>? Tags { get; set; }
            public List<Endpoint>? Endpoints { get; set; }
            public KeyValuePair<string, int> Pair { get; set; }
        }

        public void Binder_ReportsConfiguredValuesOfUnsupportedTypes()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Unsupported>(new Dictionary<string, string?>
            {
                ["Url"] = "http://example.com",
                ["Tags:env"] = "prod",
                ["Endpoints:0:Host"] = "a",
                ["Pair:Key"] = "k",
            }));
            foreach (string key in new[] { "'Url'", "'Tags'", "'Endpoints'", "'Pair'" })
                Check.True(ex.Errors.Any(e => e.StartsWith(key, StringComparison.Ordinal)), key + ": " + string.Join(" | ", ex.Errors));
            // Unconfigured, they are left alone.
            Check.True(ConfigBinder.Bind<Unsupported>(new Dictionary<string, string?>()).Url == null);
        }

        public sealed class Validating
        {
            private int _port = 80;
            public int Port
            {
                get => _port;
                set => _port = value is > 0 and <= 65535 ? value : throw new ArgumentOutOfRangeException(nameof(Port), "Port must be 1-65535.");
            }
            public string Name { get; set; } = "";
        }

        public void Binder_ReportsAThrowingSetterAsABindingError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Validating>(new Dictionary<string, string?>
                { ["Port"] = "70000", ["Name"] = "x" }));
            Check.Equal(1, ex.Errors.Count);
            Check.True(ex.Errors[0].StartsWith("'Port'", StringComparison.Ordinal) && ex.Errors[0].Contains("1-65535"), ex.Errors[0]);
        }

        // ---------------- SettingsStore / EnvironmentVariables ----------------

        public void Settings_AValueTheTypeRejectsFallsBackToDefaults()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r26-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "settings.json");
                File.WriteAllText(path, "{ \"Port\": 70000 }");
                var store = SettingsStore<Validating>.LoadFrom(path);
                Check.Equal(80, store.Value.Port);
                Check.True(store.LoadWarning != null && store.LoadWarning.Contains("1-65535"), store.LoadWarning ?? "(null)");
                Check.False(File.Exists(path), "the rejected file is moved aside");
                Check.Equal(80, SettingsStore<Validating>.LoadFrom(path).Value.Port);       // the next launch is clean
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Env_GetListAcceptsNullSeparators()
        {
            var env = new EnvironmentVariables(n => n == "L" ? "a, b;c" : null);
            Check.Equal("a|b|c", string.Join("|", env.GetList("L", null!)));
        }

        // ---------------- PdfWriter ----------------

        private static string PdfText(PdfWriter w) => Latin1.GetString(w.Build());

        public void Pdf_ARowTallerThanAPageContinuesOnTheNext()
        {
            var cell = string.Join("\n", Enumerable.Range(0, 120).Select(i => "L" + i));
            var w = new PdfWriter(font: PdfFont.Helvetica).Table(new[] { "H" }, new[] { new[] { cell } });
            string pdf = PdfText(w);
            for (int i = 0; i < 120; i++) Check.True(pdf.Contains("(L" + i + ") Tj"), "line L" + i + " lost");
            Check.True(w.PageCount >= 2);
        }

        public void Pdf_AHeaderTallerThanAPageDoesNotSwallowTheRows()
        {
            var head = string.Join("\n", Enumerable.Range(0, 80).Select(i => "H" + i));
            string pdf = PdfText(new PdfWriter(font: PdfFont.Helvetica).Table(new[] { head }, new[] { new[] { "r1" }, new[] { "r2" } }));
            Check.True(pdf.Contains("(r1) Tj") && pdf.Contains("(r2) Tj"), "rows lost");
            for (int i = 0; i < 80; i++) Check.True(pdf.Contains("(H" + i + ") Tj"), "header line H" + i + " lost");
        }

        public void Pdf_HugeColumnWeightsStayFinite()
        {
            string pdf = PdfText(new PdfWriter().Table(new[] { "a", "b" }, new[] { new[] { "1", "2" } }, new[] { 1e308, 1e308 }));
            Check.False(pdf.Contains("NaN") || pdf.Contains("Infinity") || pdf.Contains("∞"), "non-finite number in the content");
            Check.True(pdf.Contains("(1) Tj") && pdf.Contains("(2) Tj"));
        }

        public void Pdf_WidthsMatchTheAdobeMetrics()
        {
            var helv = new PdfWriter(font: PdfFont.Helvetica);
            Check.Equal(584.0, helv.Measure("±", 1000));
            Check.Equal(584.0, helv.Measure("÷", 1000));
            Check.Equal(278.0, helv.Measure("·", 1000));
            Check.Equal(333.0, helv.Measure("¯", 1000));
            Check.Equal(556.0, helv.Measure("µ", 1000));
            Check.Equal(611.0, helv.Measure("µ", 1000, bold: true));
            var times = new PdfWriter(font: PdfFont.TimesRoman);
            Check.Equal(564.0, times.Measure("±", 1000));
            Check.Equal(570.0, times.Measure("÷", 1000, bold: true));
        }

        public void Pdf_UnpairedSurrogatesInMetadataBecomeReplacementCharacters()
        {
            var w = new PdfWriter { Title = "a\uD800b", Author = "\uDC00" };
            w.Heading("x\uD83D");
            string pdf = PdfText(w);
            Check.False(Regex.IsMatch(pdf, "<FEFF(?:[0-9A-F]{4})*?D[89A-F][0-9A-F]{2}"), "an unpaired surrogate was written");
            Check.True(pdf.Contains("<FEFF0061FFFD0062>"), "title");
            var pair = new PdfWriter { Title = "\U0001F600" };
            Check.True(PdfText(pair).Contains("<FEFFD83DDE00>"), "a valid pair is kept");
        }

        public void Pdf_CodeDoesNotSplitASurrogatePair()
        {
            string pdf = PdfText(new PdfWriter().Code(new string('x', 82) + "\U0001F600y"));
            Check.True(pdf.Contains("(" + new string('x', 82) + ") Tj"), "the first line stops before the emoji");
            Check.True(pdf.Contains("(?y) Tj"), "the emoji is one '?'");
        }

        // ---------------- DocxWriter ----------------

        private static string DocumentXml(byte[] docx)
        {
            using var zip = new ZipArchive(new MemoryStream(docx));
            using var r = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
            return r.ReadToEnd();
        }

        public void Docx_DefaultRunIsEmpty()
        {
            string xml = DocumentXml(new DocxWriter().RichParagraph(default(DocxRun), "x").Build());
            Check.True(xml.Contains(">x</w:t>"), xml);
        }

        public void Docx_ALoneCarriageReturnIsALineBreak()
        {
            string xml = DocumentXml(new DocxWriter().Paragraph("a\rb").Build());
            Check.False(xml.Contains("\r"), "raw CR in the XML");
            Check.True(xml.Contains(">a</w:t><w:br/><w:t xml:space=\"preserve\">b<"), xml);
        }

        // ---------------- MarkdownReport ----------------

        public void Markdown_QuoteTreatsALoneCarriageReturnAsALineBreak()
        {
            Check.Equal("> a\n> b\n", new MarkdownReport().Quote("a\rb").ToString());
        }

        public void Markdown_DestinationsWithSpacesOrParenthesesAreBracketed()
        {
            Check.Equal("![plot](<my plot (v2).png>)\n", new MarkdownReport().Image("plot", "my plot (v2).png").ToString());
            Check.Equal("[doc](<a b.html>)", MarkdownReport.Link("doc", "a b.html"));
            Check.Equal("[doc](<a\\>b>)", MarkdownReport.Link("doc", "a>b"));
            Check.Equal("[doc](https://x.org/a_(b))", MarkdownReport.Link("doc", "https://x.org/a_(b)"));     // balanced: as is
            Check.Equal("[doc](<a)b>)", MarkdownReport.Link("doc", "a)b"));
        }

        // ---------------- ReportBuilder ----------------

        public void Report_SlugFollowsGitHub()
        {
            Check.Equal("café", ReportBuilder.Slug("Café"));                       // combining mark kept
            Check.Equal("line-one-line-two", ReportBuilder.Slug("Line one\nline two"));
            Check.Equal("a₀_b", ReportBuilder.Slug("a₀_b"));                          // other number, connector
            Check.Equal("\U0001D400x", ReportBuilder.Slug("\U0001D400x!"));                     // a letter outside the BMP
        }

        public void Report_TocLinksAMultiLineHeadingToItsRenderedAnchor()
        {
            string md = new ReportBuilder("R").TableOfContents().Heading("Line one\nline two").ToMarkdown();
            Check.True(md.Contains("- [Line one line two](#line-one-line-two)\n"), md);
            Check.True(md.Contains("## Line one line two\n"), md);
        }

        public void Report_MarkdownMetadataAndCaptionsStayOnOneLine()
        {
            byte[] png = ToolBelt.Visualization.PngWriter.Encode(new ToolBelt.Visualization.ImageBuffer(1, 1, new ToolBelt.Visualization.Rgba(0, 0, 0)));
            string md = new ReportBuilder("R")
                .Metadata("Key", "v1\nv2")
                .Table(new[] { "A" }, new[] { new[] { "1" } }, caption: "cap\none")
                .Figure("fig\ntwo", png: png)
                .ToMarkdown();
            Check.True(md.Contains("v1 v2"), md);
            Check.True(md.Contains("**Table 1: cap one**"), md);
            Check.True(md.Contains("![Figure 1: fig two](data:"), md);
            Check.True(md.Contains("*Figure 1: fig two*"), md);
        }

        public void Report_IndentedParagraphLinesDoNotBecomeCode()
        {
            string md = new ReportBuilder("R").Paragraph("intro\n\n    indented\n\tTabbed").Callout(CalloutKind.Note, "a\n\n    b").ToMarkdown();
            Check.False(Regex.IsMatch(md, "\n(    |\t)"), md);
            Check.True(md.Contains("\nindented\nTabbed"), md);
        }

        public void Report_TableHeadersAreCapturedAtCallTime()
        {
            var h = new[] { "A", "B" };
            var rb = new ReportBuilder("R").Table(h, new[] { new[] { "1", "2" } });
            h[0] = "MUTATED";
            Check.False(rb.ToMarkdown().Contains("MUTATED"));
        }

        // ---------------- ReportTemplates ----------------

        public void Comparison_FlagPercentZeroFlagsAChangeFromAZeroBaseline()
        {
            string md = ReportTemplates.Comparison("C", "a", "b", new[] { new ComparisonRow("errors", 0, 5), new ComparisonRow("same", 0, 0) },
                flagPercent: 0).ToMarkdown();
            Check.True(md.Contains("> [!WARNING]\n> 1 metric(s) changed: errors (from 0)."), md);
            Check.False(md.Contains("No metric changed"), md);
        }
    }
}
