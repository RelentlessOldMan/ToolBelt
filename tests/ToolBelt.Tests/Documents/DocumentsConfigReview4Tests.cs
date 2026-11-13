using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 63 (documents and configuration, fourth pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview4Tests
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private static Dictionary<string, string?> D(params (string Key, string? Value)[] pairs)
        {
            var d = new Dictionary<string, string?>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        // ---------------- ConfigBinder ----------------

        public sealed class Node { public int Value { get; set; } public Node? Next { get; set; } }
        public sealed class Ports { public List<int> List { get; set; } = new List<int>(); }
        public sealed class Hosts { public List<string> Names { get; set; } = new List<string>(); }
        public sealed class Scalars
        {
            public int Port { get; set; }
            public char Sep { get; set; }
            public char? MaybeSep { get; set; }
            public DateTimeOffset When { get; set; }
            public IntPtr Handle { get; set; }
            public UIntPtr Size { get; set; }
        }

        public void Binder_DeepNestingIsAnErrorNotAStackOverflow()
        {
            // About 2,000 levels overflowed the stack, which kills the process (run under a timeout in the proof).
            string key = string.Join(":", Enumerable.Repeat("Next", 5000)) + ":Value";
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Node>(D((key, "1"))));
            Check.True(ex.Errors.Any(e => e.Contains("deep")), string.Join(" / ", ex.Errors));
            // Within the limit, every level is still bound.
            string ok = string.Join(":", Enumerable.Repeat("Next", 10)) + ":Value";
            var n = ConfigBinder.Bind<Node>(D((ok, "7")));
            for (int i = 0; i < 10; i++) n = n.Next!;
            Check.Equal(7, n.Value);
        }

        public void Binder_AFailedListItemIsNotAlsoAGap()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Ports>(D(("List:0", "x"), ("List:1", "2"))));
            Check.Equal(1, ex.Errors.Count, string.Join(" / ", ex.Errors));
            Check.True(ex.Errors[0].Contains("List:0"), ex.Errors[0]);
        }

        public void Binder_ANestedKeyUnderAScalarListItemIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Hosts>(D(("Names:0:Name", "a"))));
            Check.True(ex.Errors.Single().Contains("Names:0:Name"), ex.Errors.Single());
        }

        public void Binder_ANestedKeyUnderAScalarIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Scalars>(D(("Port:0", "5"))));
            Check.True(ex.Errors.Single().Contains("Port"), ex.Errors.Single());
        }

        public void Binder_ACharKeepsWhitespace()
        {
            Check.Equal(' ', ConfigBinder.Bind<Scalars>(D(("Sep", " "))).Sep);
            Check.Equal('\t', ConfigBinder.Bind<Scalars>(D(("Sep", "\t"))).Sep);
            Check.Equal(';', ConfigBinder.Bind<Scalars>(D(("Sep", ";"))).Sep);
            Check.Equal((char?)null, ConfigBinder.Bind<Scalars>(D(("MaybeSep", ""))).MaybeSep);
            Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Scalars>(D(("Sep", "ab"))));
        }

        public void Binder_ADateTimeOffsetWithoutAnOffsetIsUtc()
        {
            var when = ConfigBinder.Bind<Scalars>(D(("When", "2026-01-02T03:04:05"))).When;
            Check.Equal(TimeSpan.Zero, when.Offset);
            Check.Equal(new DateTime(2026, 1, 2, 3, 4, 5), when.DateTime);
            Check.Equal(TimeSpan.FromHours(2), ConfigBinder.Bind<Scalars>(D(("When", "2026-01-02T03:04:05+02:00"))).When.Offset);
        }

        public void Binder_PointerSizedIntegersBind()
        {
            var s = ConfigBinder.Bind<Scalars>(D(("Handle", "-5"), ("Size", "42")));
            Check.Equal(new IntPtr(-5), s.Handle);
            Check.Equal(new UIntPtr(42), s.Size);
            Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Scalars>(D(("Size", "-1"))));
        }

        // ---------------- EnvironmentVariables ----------------

        public void Env_AnOverflowingDoubleIsAnErrorOnEveryTarget()
        {
            var vars = new Dictionary<string, string?> { ["A"] = "1e400", ["B"] = "Infinity", ["C"] = "-1e400" };
            var env = new EnvironmentVariables(n => vars.TryGetValue(n, out var v) ? v : null);
            Check.Throws<FormatException>(() => env.GetDouble("A"));
            Check.Throws<FormatException>(() => env.GetDouble("C"));
            Check.Equal(double.PositiveInfinity, env.GetDouble("B"));   // spelled out: allowed
        }

        public void Env_RequireAllNullNamesItsParameter()
        {
            var ex = Check.Throws<ArgumentNullException>(() => new EnvironmentVariables(_ => null).RequireAll(null!));
            Check.Equal("names", ex.ParamName);
        }

        // ---------------- SettingsStore ----------------

#if !NETFRAMEWORK   // SettingsStore is net8.0-only
        public sealed class Plain { public int X { get; set; } }
        public sealed class GetOnlyUri { public Uri Home { get; } = new Uri("http://example.test/"); public int X { get; set; } }
        public sealed class Inner { public int X { get; set; } }
        public sealed class GetOnlyNull { public Inner? Child { get; } public int X { get; set; } }
        public sealed class GetOnlyArray { public string[] Tags { get; } = { "a", "b" }; public int X { get; set; } }
        public sealed class GetOnlyInterface { public ICollection<string> Tags { get; } = new List<string>(); }

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r63-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void Settings_AnUnreadableFileIsNotOverwrittenBySave()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                File.WriteAllText(path, "{\"X\":7}");
                SettingsStore<Plain> store;
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    store = SettingsStore<Plain>.LoadFrom(path);
                Check.True(store.LoadWarning != null, "warning");
                Check.Throws<InvalidOperationException>(() => store.Save());
                Check.Throws<InvalidOperationException>(() => store.Update(v => v.X = 1));
                Check.Equal("{\"X\":7}", File.ReadAllText(path));        // the user's file survives
                store.Reset();                                             // deliberately replaces it
                Check.Equal(0, SettingsStore<Plain>.LoadFrom(path).Value.X);
                store.Save();                                              // and saving works again
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_ALongFileNameCanBeSaved()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, new string('s', 245) + ".json");
                var store = SettingsStore<Plain>.LoadFrom(path);
                store.Value.X = 5;
                store.Save();
                Check.Equal(5, SettingsStore<Plain>.LoadFrom(path).Value.X);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_ASymbolicLinkIsWrittenThrough()
        {
            string dir = TempDir();
            try
            {
                string target = Path.Combine(dir, "real.json"), link = Path.Combine(dir, "link.json");
                File.WriteAllText(target, "{\"X\":1}");
                try { File.CreateSymbolicLink(link, target); }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException) { return; }   // no privilege here
                var store = SettingsStore<Plain>.LoadFrom(link);
                store.Value.X = 9;
                store.Save();
                Check.True(new FileInfo(link).LinkTarget != null, "the link is still a link");
                Check.Equal(9, SettingsStore<Plain>.LoadFrom(target).Value.X);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_SaveWaitsForABriefReader()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<Plain>.LoadFrom(path);
                store.Save();
                var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var release = new Thread(() => { Thread.Sleep(150); reader.Dispose(); });
                release.Start();
                store.Value.X = 3;
                store.Save();
                release.Join();
                Check.Equal(3, SettingsStore<Plain>.LoadFrom(path).Value.X);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_AGetOnlyUriDoesNotBreakSaveAndLoad()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<GetOnlyUri>.LoadFrom(path);
                store.Value.X = 4;
                store.Save();
                var again = SettingsStore<GetOnlyUri>.LoadFrom(path);
                Check.Equal(null, again.LoadWarning);
                Check.Equal(4, again.Value.X);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_ANullGetOnlyObjectLoadsBack()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<GetOnlyNull>.LoadFrom(path);
                store.Value.X = 6;
                store.Save();
                var again = SettingsStore<GetOnlyNull>.LoadFrom(path);
                Check.Equal(null, again.LoadWarning);
                Check.Equal(6, again.Value.X);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_AGetOnlyArrayKeepsItsDefaultAndLoads()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<GetOnlyArray>.LoadFrom(path);
                store.Value.X = 8;
                store.Save();
                var again = SettingsStore<GetOnlyArray>.LoadFrom(path);
                Check.Equal(null, again.LoadWarning);
                Check.Equal(8, again.Value.X);
                Check.Equal("a|b", string.Join("|", again.Value.Tags));
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_AGetOnlyInterfaceCollectionLoadsBack()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<GetOnlyInterface>.LoadFrom(path);
                store.Value.Tags.Add("x");
                store.Value.Tags.Add("y");
                store.Save();
                Check.Equal("x|y", string.Join("|", SettingsStore<GetOnlyInterface>.LoadFrom(path).Value.Tags));
            }
            finally { Directory.Delete(dir, true); }
        }
#endif

        // ---------------- MarkdownReport ----------------

        public void Markdown_ConsecutiveListsStaySeparate()
        {
            string md = new MarkdownReport().BulletList(new[] { "a", "b" }).BulletList(new[] { "c" }).ToString();
            Check.Equal("- a\n- b\n\n* c\n", md);
            string nums = new MarkdownReport().NumberedList(new[] { "x" }).NumberedList(new[] { "z" }).NumberedList(new[] { "w" }).ToString();
            Check.Equal("1. x\n\n1) z\n\n1. w\n", nums);
            // After a Raw list, too.
            Check.Equal("- t\n\n* u\n", new MarkdownReport().Raw("- t").BulletList(new[] { "u" }).ToString());
            // Separated by a paragraph, the usual marker.
            Check.Equal("- a\n\np\n\n- b\n", new MarkdownReport().BulletList(new[] { "a" }).Paragraph("p").BulletList(new[] { "b" }).ToString());
        }

        public void Markdown_InlineSpansHaveNoLineBreaks()
        {
            Check.Equal("**a - b**", MarkdownReport.Bold("a\n- b"));
            Check.False(MarkdownReport.Bold("a\n- b").Contains('\n'), "bold");
            Check.False(MarkdownReport.Italic("a\r\n\r\nb").Contains('\n'), "italic");
            Check.False(MarkdownReport.Italic("a\rb").Contains('\r'), "lone CR");
        }

        public void Markdown_EntitiesInADestinationAreEscaped()
        {
            Check.Equal("[t](http://x/?q=\\&amp;r=\\&#60;&s)", MarkdownReport.Link("t", "http://x/?q=&amp;r=&#60;&s"));
            Check.Equal("[t](http://x/?a=1&b=2)", MarkdownReport.Link("t", "http://x/?a=1&b=2"));   // a plain '&' is left alone
            Check.Equal("![i](<a b\\&lt;.png>)\n", new MarkdownReport().Image("i", "a b&lt;.png").ToString());
        }

        // ---------------- ReportBuilder / ReportTemplates ----------------

        public void Report_ABulletListAfterTheContentsIsItsOwnList()
        {
            string md = new ReportBuilder("T").TableOfContents().BulletList(new[] { "p", "q" }).Heading("One").BulletList(new[] { "r" }).BulletList(new[] { "s" }).ToMarkdown();
            Check.True(md.Contains("- [One](#one)"), md);
            Check.True(md.Contains("* p\n* q"), md);
            Check.True(md.Contains("- r\n\n* s"), md);
        }

        public void Report_ToPdfRejectsAFontSizeItsTitleCannotUse()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => new ReportBuilder("T").ToPdf(fontSize: 6000));
            Check.Equal("fontSize", ex.ParamName);
            new ReportBuilder("T").ToPdf(fontSize: 5000);
        }

        public void Report_AFigureKeepsItsOwnPng()
        {
            byte[] png = PngWriter.Encode(new ImageBuffer(4, 4, Rgba.White));
            string before = Convert.ToBase64String(png);
            var report = new ReportBuilder("T").Figure("f", png: png);
            Array.Clear(png, 0, png.Length);
            Check.True(report.ToHtml().Contains(before), "the figure changed with the caller's buffer");
        }

        public void Templates_PercentChangeDoesNotOverflow()
        {
            Check.Close(200, new ComparisonRow("m", -1e308, 1e308).PercentChange, 1e-9);
            Check.Close(-200, new ComparisonRow("m", 1e308, -1e308).PercentChange, 1e-9);
            Check.Close(50, new ComparisonRow("m", 2, 3).PercentChange, 1e-12);
            Check.Equal(double.PositiveInfinity, new ComparisonRow("m", 1, double.PositiveInfinity).PercentChange);
        }

        // ---------------- PdfWriter ----------------

        private static string PdfText(PdfWriter w) => Latin1.GetString(w.Build());

        public void Pdf_ALongTinyLineWrapsInLinearTime()
        {
            string text = string.Join(" ", Enumerable.Repeat("word", 100_000));
            var sw = Stopwatch.StartNew();
            new PdfWriter(fontSize: 0.01).Paragraph(text);
            Check.True(sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms");   // about 24 s before
        }

        public void Pdf_WrappingIsUnchangedAtNormalSizes()
        {
            // Differential: every wrapped line fits, and adding the next word would not have.
            var rng = new Random(63);
            foreach (PdfFont font in new[] { PdfFont.Courier, PdfFont.Helvetica, PdfFont.TimesRoman })
            {
                var w = new PdfWriter(font: font, fontSize: 11);
                var words = Enumerable.Range(0, 400).Select(_ => new string('a', 1 + rng.Next(12)).Replace('a', (char)('a' + rng.Next(26)))).ToList();
                string pdf = PdfText(new PdfWriter(font: font, fontSize: 11).Paragraph(string.Join(" ", words)));
                var lines = pdf.Split('\n').Where(l => l.EndsWith(") Tj ET")).Select(l => l.Substring(l.IndexOf(" Td (") + 5)).Select(l => l.Substring(0, l.Length - 7)).ToList();
                Check.Equal(string.Join(" ", words), string.Join(" ", lines));
                double usable = 612 - 2 * 54;
                for (int i = 0; i < lines.Count; i++)
                {
                    Check.True(w.Measure(lines[i], 11) <= usable, lines[i]);
                    if (i + 1 < lines.Count)
                        Check.True(w.Measure(lines[i] + " " + lines[i + 1].Split(' ')[0], 11) > usable, "greedy: " + lines[i]);
                }
            }
        }

        public void Pdf_AnOversizedLineLeavesNoBlankPages()
        {
            Check.Equal(1, new PdfWriter().Paragraph("A").PageCount);
            var big = new PdfWriter(fontSize: 1000).Paragraph("A");
            Check.Equal(1, big.PageCount);
            Check.Equal(1, new PdfWriter().Heading("H", size: 900).PageCount);
            Check.Equal(2, new PdfWriter(fontSize: 1000).Paragraph("A").Paragraph("B").PageCount);
        }

        public void Pdf_ATinyFontSizeIsNotWrittenAsZero()
        {
            string pdf = PdfText(new PdfWriter().Table(new[] { "h" }, new[] { new[] { "x" } }, fontSize: 0.0001));
            Check.False(pdf.Contains(" 0 Tf"), "a zero font size");
            Check.True(pdf.Contains(" 0.0001 Tf"), "the size");
        }

        public void Pdf_ImageNamesTheBadSide()
        {
            Check.Equal("height", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter().Image(2, 0, new byte[0])).ParamName);
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter().Image(0, 2, new byte[0])).ParamName);
        }

        public void Pdf_ABadListLevelWritesNothing()
        {
            var w = new PdfWriter();
            Check.Throws<ArgumentOutOfRangeException>(() => w.BulletList(new[] { (0, "first-item"), (-1, "bad") }));
            Check.False(PdfText(w).Contains("first-item"), "a half-written list");
        }

        // ---------------- DocxWriter / HtmlReport ----------------

        private static IEnumerable<IReadOnlyList<string>> ThrowingRows()
        {
            yield return new[] { "row-one" };
            throw new InvalidOperationException("source failed");
        }

        private static IEnumerable<string> ThrowingItems()
        {
            yield return "item-one";
            throw new InvalidOperationException("source failed");
        }

        private static string DocumentXml(byte[] docx)
        {
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            return reader.ReadToEnd();
        }

        public void Docx_AFailedTableOrListLeavesNothingBehind()
        {
            var d = new DocxWriter();
            Check.Throws<InvalidOperationException>(() => d.Table(new[] { "h" }, ThrowingRows()));
            Check.Throws<InvalidOperationException>(() => d.BulletList(ThrowingItems()));
            Check.Throws<ArgumentOutOfRangeException>(() => d.List(new[] { (0, "lvl-ok"), (9, "bad") }, numbered: true));
            d.Paragraph("after");
            string xml = DocumentXml(d.Build());
            System.Xml.Linq.XDocument.Parse(xml);                                   // well-formed
            Check.False(xml.Contains("<w:tbl>"), "a half-written table");
            Check.False(xml.Contains("row-one") || xml.Contains("item-one") || xml.Contains("lvl-ok"), "half-written content");
            Check.True(xml.Contains("after"), "later content");
        }

        public void Docx_AnImageKeepsItsOwnBytes()
        {
            byte[] png = PngWriter.Encode(new ImageBuffer(4, 4, Rgba.White));
            byte[] copy = (byte[])png.Clone();
            var d = new DocxWriter().Image(png);
            Array.Clear(png, 0, png.Length);
            using var zip = new ZipArchive(new MemoryStream(d.Build()), ZipArchiveMode.Read);
            using var ms = new MemoryStream();
            using (var s = zip.GetEntry("word/media/image1.png")!.Open()) s.CopyTo(ms);
            Check.True(ms.ToArray().SequenceEqual(copy), "the image changed with the caller's buffer");
        }

        public void Html_AFailedTableOrListLeavesNothingBehind()
        {
            var h = new HtmlReport();
            Check.Throws<InvalidOperationException>(() => h.Table(new[] { "h" }, ThrowingRows()));
            Check.Throws<InvalidOperationException>(() => h.BulletList(ThrowingItems()));
            h.Paragraph("after");
            Check.Equal("<p>after</p>\n", h.ToFragment());
        }
    }
}
