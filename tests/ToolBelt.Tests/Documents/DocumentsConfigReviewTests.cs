using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.IO;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 7 (documents and configuration): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReviewTests
    {
        // ---------------- HtmlReport ----------------

        public void Html_CodeBlockLanguageCannotBreakOutOfTheAttribute()
        {
            string html = new HtmlReport().CodeBlock("x", "c\" onclick=\"alert(1)").ToFragment();
            Check.False(html.Contains("\" onclick"), html);
            Check.True(html.Contains("&quot;"), html);
        }

        public void Html_TableToleratesNullAndRaggedRows()
        {
            string html = new HtmlReport().Table(new[] { "a", "b" },
                new IReadOnlyList<string>[] { null!, new[] { "1" }, new[] { "1", "2", "3" } }).ToFragment();
            Check.Equal(6, Regex.Matches(html, "<td>").Count);
            Check.False(html.Contains(">3<"), "extra cells are dropped");
        }

        // ---------------- MarkdownReport ----------------

        public void Markdown_CodeSpanUsesALongerDelimiter()
        {
            Check.Equal("``a`b``", MarkdownReport.Code("a`b"));
            Check.Equal("`` `x ``", MarkdownReport.Code("`x"));
            Check.Equal("`plain`", MarkdownReport.Code("plain"));
        }

        public void Markdown_CodeFenceOutgrowsBackticksInTheCode()
        {
            string md = new MarkdownReport().CodeBlock("x\n```\ny", "c`s\nharp").ToString();
            Check.True(md.Contains("````cs harp\nx\n```\ny\n````"), md);
        }

        public void Markdown_CellsEscapeHtmlAndEveryLineEnding()
        {
            string md = new MarkdownReport().Table(new[] { "h" }, new[] { new[] { "<b>&x\ra" } }).ToString();
            Check.True(md.Contains("\\<b\\>\\&x<br>a"), md);
            Check.False(md.Contains('\r'), "a lone CR would end the row");
        }

        // ---------------- ReportBuilder ----------------

        public void ReportMarkdown_EscapesTextBlocks()
        {
            string md = new ReportBuilder("*T*")
                .Heading("a_b")
                .Paragraph("# not a heading *x*\n- not a bullet\n1. not a list")
                .BulletList(new[] { "<i>" })
                .Callout(CalloutKind.Note, "---")
                .ToMarkdown();
            Check.True(md.Contains("# \\*T\\*"), md);
            Check.True(md.Contains("a\\_b"), md);
            Check.True(md.Contains("\\# not a heading \\*x\\*"), md);
            Check.True(md.Contains("\n\\- not a bullet"), md);
            Check.True(md.Contains("\n1\\. not a list"), md);
            Check.True(md.Contains("\\<i\\>"), md);
            Check.True(md.Contains("> \\---"), md);
        }

        public void ReportAnchors_NeverCollide()
        {
            string html = new ReportBuilder("R").Heading("x-1").Heading("x").Heading("x").ToHtml();
            var ids = Regex.Matches(html, "<h\\d id=\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Check.Equal(ids.Count, ids.Distinct().Count());
            Check.True(ids.Contains("x-2"), string.Join(",", ids));
        }

        public void ReportTemplates_NoNegativeZero()
        {
            string md = ReportTemplates.Comparison("C", "a", "b", new[] { new ComparisonRow("m", 1, 0.999) }, numberFormat: "0.##").ToMarkdown();
            Check.False(Regex.IsMatch(md, "-0(?![\\d.])"), md);
        }

        // ---------------- PdfWriter ----------------

        public void Pdf_ConstructorRejectsNonFiniteSizes()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(margin: double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(margin: double.PositiveInfinity));
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(fontSize: double.PositiveInfinity));
        }

        public void Pdf_ShortIhdrIsArgumentException()
        {
            Check.Throws<ArgumentException>(() => new PdfWriter().ImagePng(ShortIhdrPng()));
        }

        public void Pdf_MeasuresTabsControlsAndSurrogatesLikeTheirGlyphs()
        {
            var w = new PdfWriter(font: PdfFont.Helvetica);
            Check.Equal(w.Measure(" ", 10), w.Measure("\t", 10));
            Check.Equal(w.Measure("?", 10), w.Measure("\u007f", 10));
            Check.Equal(w.Measure("?", 10), w.Measure("\U0001F600", 10));
        }

        public void Pdf_HardSplitOfALongWordIsLinear()
        {
            string blob = new string('a', 1_200_000);
            var sw = Stopwatch.StartNew();
            new PdfWriter(font: PdfFont.Helvetica).Paragraph(blob).Build();
            Check.True(sw.Elapsed.TotalSeconds < 4, "took " + sw.Elapsed);
        }

        public void Pdf_LargeOutlineIsLinear()
        {
            var w = new PdfWriter();
            for (int i = 0; i < 60_000; i++) w.Heading("h" + i, level: 1 + i % 3);
            var sw = Stopwatch.StartNew();
            w.Build();
            Check.True(sw.Elapsed.TotalSeconds < 4, "took " + sw.Elapsed);
        }

        // ---------------- DocxWriter ----------------

        public void Docx_ImageRejectsInfiniteAndHugeWidths()
        {
            byte[] png = PngWriter.Encode(new ImageBuffer(4, 2, new Rgba(0, 0, 0)));
            Check.Throws<ArgumentOutOfRangeException>(() => new DocxWriter().Image(png, double.PositiveInfinity));
            Check.Throws<ArgumentOutOfRangeException>(() => new DocxWriter().Image(png, 1e12));
        }

        public void Docx_TableHasGridAndHeadingsShrinkPerLevel()
        {
            var d = new DocxWriter().Table(new[] { "a", "b" }, new[] { new[] { "1", "2" } });
            for (int l = 1; l <= 6; l++) d.Heading("h" + l, l);
            string xml = DocumentXml(d.Build());
            Check.Equal(2, Regex.Matches(xml, "<w:gridCol ").Count);
            var sizes = Regex.Matches(xml, "<w:sz w:val=\"(\\d+)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().Count();
            Check.True(sizes >= 6, "distinct heading sizes: " + sizes);
        }

        // ---------------- ConfigBinder ----------------

        private enum Mode { Off, On }
        [Flags] private enum Access { None = 0, Read = 1, Write = 2, Exec = 4 }
        private sealed class Inner { public string? Host { get; set; } public int Port { get; set; } }
        private abstract class Shape { public int Sides { get; set; } }
        private sealed class Opts
        {
            public string? Name { get; set; }
            public Uri? Endpoint { get; set; }
            public Inner Primary { get; set; } = new Inner { Host = "h", Port = 80 };
            public Inner ReadOnly { get; } = new Inner();
            public Guid Id { get; set; }
            public DateTimeOffset When { get; set; }
            public bool Flag { get; set; }
            public Mode Mode { get; set; }
            public Access Access { get; set; }
            public TimeSpan Timeout { get; set; }
            public IReadOnlyList<int>? Ports { get; set; }
            public List<string>? Tags { get; set; }
            public Shape? Shape { get; set; }
        }

        private static Dictionary<string, string?> D(params (string, string?)[] pairs)
            => pairs.ToDictionary(p => p.Item1, p => p.Item2);

        public void Binder_UnconfiguredClassPropertiesAreLeftAlone()
        {
            var o = ConfigBinder.Bind<Opts>(D(("Name", "n")));
            Check.Equal(null, o.Endpoint);
            Check.Equal(80, o.Primary.Port);
        }

        public void Binder_BindsIntoTheExistingInstance()
        {
            var o = ConfigBinder.Bind<Opts>(D(("Primary:Host", "x"), ("ReadOnly:Port", "5")));
            Check.Equal("x", o.Primary.Host);
            Check.Equal(80, o.Primary.Port);
            Check.Equal(5, o.ReadOnly.Port);
        }

        public void Binder_GuidsDatesBoolsAndFlags()
        {
            var g = Guid.NewGuid();
            var o = ConfigBinder.Bind<Opts>(D(("Id", g.ToString()), ("When", "2024-05-06T07:08:09+02:00"),
                ("Flag", "yes"), ("Access", "Read, Exec")));
            Check.Equal(g, o.Id);
            Check.Equal(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)), o.When);
            Check.True(o.Flag);
            Check.Equal(Access.Read | Access.Exec, o.Access);
        }

        public void Binder_RejectsUndefinedEnumsAndBareDurations()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Opts>(D(("Mode", "7"), ("Access", "8"), ("Timeout", "30"))));
            Check.Equal(3, ex.Errors.Count);
            Check.Equal(TimeSpan.FromSeconds(30), ConfigBinder.Bind<Opts>(D(("Timeout", "30s"))).Timeout);
            Check.Equal(TimeSpan.FromMinutes(5), ConfigBinder.Bind<Opts>(D(("Timeout", "00:05:00"))).Timeout);
        }

        public void Binder_InterfaceListsAndIndexGaps()
        {
            var o = ConfigBinder.Bind<Opts>(D(("Ports:0", "1"), ("Ports:1", "2")));
            Check.True(o.Ports != null && o.Ports.SequenceEqual(new[] { 1, 2 }));
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Opts>(D(("Tags:0", "a"), ("Tags:2", "c"))));
            Check.True(ex.Errors.Single().Contains("Tags:2"), ex.Message);
        }

        public void Binder_UnconstructibleSectionIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Opts>(D(("Shape:Sides", "3"))));
            Check.True(ex.Errors.Single().Contains("Shape"), ex.Message);
        }

        // ---------------- EnvironmentVariables / SettingsStore ----------------

        public void Env_BareNumberIsNotADurationAndFlagsCombine()
        {
            var env = EnvironmentVariables.From(new Dictionary<string, string> { ["T"] = "30", ["A"] = "Read, Exec" });
            Check.Throws<FormatException>(() => env.GetTimeSpan("T"));
            Check.Equal(Access.Read | Access.Exec, env.GetEnum("A", Access.None));
        }

        public sealed class FloatSettings { public double Gain { get; set; } = double.NaN; }

        public void Settings_NaNRoundTrips()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r7-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(dir, "s.json");
                SettingsStore<FloatSettings>.LoadFrom(path).Save();
                Check.True(double.IsNaN(SettingsStore<FloatSettings>.LoadFrom(path).Value.Gain));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        public void Settings_LockedFileYieldsDefaultsWithAWarning()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r7-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "s.json");
                File.WriteAllText(path, "{\"Gain\":2}");
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var s = SettingsStore<FloatSettings>.LoadFrom(path);
                    Check.True(s.LoadWarning != null);
                }
                Check.True(File.Exists(path), "an unreadable file is left in place");
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---------------- helpers ----------------

        private static string DocumentXml(byte[] docx)
        {
            using var zip = new ZipArchive(new MemoryStream(docx));
            using var r = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
            return r.ReadToEnd();
        }

        // A PNG whose IHDR chunk is 5 bytes long (valid CRC), followed by IEND.
        private static byte[] ShortIhdrPng()
        {
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            Chunk(ms, "IHDR", new byte[] { 0, 0, 0, 1, 0 });
            Chunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        private static void Chunk(Stream s, string kind, byte[] body)
        {
            byte[] k = Encoding.ASCII.GetBytes(kind);
            WriteBE(s, (uint)body.Length);
            s.Write(k, 0, 4);
            s.Write(body, 0, body.Length);
            WriteBE(s, Crc(k.Concat(body).ToArray()));
        }

        private static void WriteBE(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
        }

        private static uint Crc(byte[] data)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                c ^= b;
                for (int i = 0; i < 8; i++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return c ^ 0xFFFFFFFF;
        }
    }
}
