using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 109 (documents and configuration, seventh pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview109Tests
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public enum Color { Red = 1, Green = 2, Blue = 3 }
        public enum Mode { Off, On }
        [Flags] public enum Bits { None = 0, A = 1, B = 2 }
        public sealed class EnumOpts { public Color C { get; set; } = Color.Red; public Mode M { get; set; } public Bits F { get; set; } }
        public sealed class Srv { public int Port { get; set; } = 99; }
        public sealed class NestedOpts { public int Port { get; set; } = 7; public Srv? Server { get; set; } }
        public sealed class DurationOpts { public TimeSpan T { get; set; } }

        private static IReadOnlyDictionary<string, string?> D(params (string k, string? v)[] kv)
            => kv.ToDictionary(p => p.k, p => p.v);

        // ---------------- ConfigBinder / EnvironmentVariables ----------------

        public void Bind_ACommaCombinationOfANonFlagsEnumIsRefused()
        {
            Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<EnumOpts>(D(("C", "Red, Green"))));
            Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<EnumOpts>(D(("M", "Off, On"))));
            Check.Equal(Bits.A | Bits.B, ConfigBinder.Bind<EnumOpts>(D(("F", "A, B"))).F);     // a flags enum still combines
        }

        public void Env_ACommaCombinationOfANonFlagsEnumIsRefused()
        {
            var env = EnvironmentVariables.From(new Dictionary<string, string> { ["C"] = "Red, Green", ["M"] = "Off,On", ["F"] = "A,B" });
            Check.Throws<FormatException>(() => env.GetEnum("C", Color.Red));
            Check.Throws<FormatException>(() => env.GetEnum("M", Mode.Off));
            Check.Equal(Bits.A | Bits.B, env.GetEnum("F", Bits.None));
        }

        public void Bind_ANullNestedValueIsUnset()
        {
            Check.Equal(7, ConfigBinder.Bind<NestedOpts>(D(("Port:0", null))).Port);
            Check.True(ConfigBinder.Bind<NestedOpts>(D(("Server:Port", null))).Server is null, "the section stays unset");
            Check.Equal(5, ConfigBinder.Bind<NestedOpts>(D(("Server:Port", "5"))).Server!.Port);
        }

        private sealed class NullKeySource : IReadOnlyDictionary<string, string?>
        {
            private readonly List<KeyValuePair<string, string?>> _items = new List<KeyValuePair<string, string?>> { new KeyValuePair<string, string?>(null!, "v") };
            public string? this[string key] => throw new KeyNotFoundException();
            public IEnumerable<string> Keys => _items.Select(p => p.Key);
            public IEnumerable<string?> Values => _items.Select(p => p.Value);
            public int Count => _items.Count;
            public bool ContainsKey(string key) => false;
            public bool TryGetValue(string key, out string? value) { value = null; return false; }
            public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() => _items.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public void Bind_ANullKeyNamesTheSource()
            => Check.Equal("values", Check.Throws<ArgumentException>(() => ConfigBinder.Bind<NestedOpts>(new NullKeySource())).ParamName);

        public void Duration_BothReadersShareOneSyntax()
        {
            var env = EnvironmentVariables.From(new Dictionary<string, string> { ["A"] = "1m 30s", ["B"] = "1.5h", ["C"] = "5 s" });
            Check.Equal(TimeSpan.FromSeconds(90), env.GetTimeSpan("A"));
            Check.Equal(TimeSpan.FromMinutes(90), ConfigBinder.Bind<DurationOpts>(D(("T", "1.5h"))).T);
            Check.Equal(TimeSpan.FromSeconds(5), ConfigBinder.Bind<DurationOpts>(D(("T", "5 s"))).T);
            Check.Equal(TimeSpan.FromMinutes(-90), ConfigBinder.Bind<DurationOpts>(D(("T", "-1h 30m"))).T);   // ConfigBinder keeps its sign

            // Differential: random term lists, graded against an exact decimal sum.
            var rng = new Random(109);
            string[] units = { "d", "h", "m", "s", "ms", "H", "MS" };
            long[] per = { TimeSpan.TicksPerDay, TimeSpan.TicksPerHour, TimeSpan.TicksPerMinute, TimeSpan.TicksPerSecond, TimeSpan.TicksPerMillisecond, TimeSpan.TicksPerHour, TimeSpan.TicksPerMillisecond };
            for (int trial = 0; trial < 300; trial++)
            {
                var sb = new StringBuilder();
                decimal ticks = 0;
                int terms = rng.Next(1, 4);
                for (int k = 0; k < terms; k++)
                {
                    int u = rng.Next(units.Length);
                    decimal n = rng.Next(0, 1000) + (rng.Next(2) == 0 ? 0 : rng.Next(0, 100) / 100m);
                    ticks += n * per[u];
                    if (k > 0) sb.Append(' ');
                    sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(rng.Next(3) == 0 ? " " : "").Append(units[u]);
                }
                string text = sb.ToString();
                var expected = TimeSpan.FromTicks((long)decimal.Truncate(ticks));
                Check.Equal(expected, EnvironmentVariables.From(new Dictionary<string, string> { ["X"] = text }).GetTimeSpan("X"));
                Check.Equal(expected, ConfigBinder.Bind<DurationOpts>(D(("T", text))).T);
            }
        }

        // ---------------- MarkdownReport ----------------

        private static bool IsThematicBreak(string line)
        {
            string t = new string(line.Where(c => c != ' ' && c != '\t').ToArray());
            return t.Length >= 3 && "-*_".IndexOf(t[0]) >= 0 && t.All(c => c == t[0]);
        }

        public void Markdown_AnItemNeverCompletesAThematicBreak()
        {
            foreach (string item in new[] { "--", "- -", "**", "* *", "__", "-", "*" })
            {
                string md = new MarkdownReport().BulletList(new[] { "x", item }).BulletList(new[] { "y", item }).ToString();
                foreach (string line in md.Split('\n'))
                    Check.False(IsThematicBreak(line), $"item '{item}' made a rule: {md}");
            }
        }

        public void Markdown_ARawListEndingInAContinuationStaysSeparate()
        {
            foreach (string raw in new[] { "- a\n  b", "- a\nlazy", "-" })
                Check.True(new MarkdownReport().Raw(raw).BulletList(new[] { "x" }).ToString().Contains("\n* x\n"), raw);
        }

        // ---------------- ReportBuilder / ReportTemplates ----------------

        public void Figure_AnSvgOfOnlyADeclarationIsRefused()
        {
            Check.Equal("svg", Check.Throws<ArgumentException>(() => new ReportBuilder("T").Figure("cap", svg: "<?xml version=\"1.0\"?>")).ParamName);
            Check.Equal("svg", Check.Throws<ArgumentException>(() => new ReportFigure("c", svg: "<?xml version='1.0' encoding='utf-8'?>\n")).ParamName);
            Check.True(new ReportBuilder("T").Figure("cap", svg: "<?xml version=\"1.0\"?><svg/>").ToHtml().Contains("<svg"), "real markup still works");
        }

        public void RunSummary_ANullMetadataKeyNamesMetadata()
            => Check.Equal("metadata", Check.Throws<ArgumentException>(() => ReportTemplates.RunSummary("t", true,
                new[] { new KeyValuePair<string, string>(null!, "v") }, new KeyValuePair<string, string>[0])).ParamName);

        public void Comparison_AnIntegerOnlyFormatIsRefusedUpFront()
            => Check.Equal("numberFormat", Check.Throws<ArgumentException>(() => ReportTemplates.Comparison("t", "a", "b",
                new[] { new ComparisonRow("m", 1, 2) }, numberFormat: "X")).ParamName);

        // ---------------- PdfWriter / DocxWriter ----------------

        private static string PdfText(PdfWriter w) => Latin1.GetString(w.Build());

        public void Pdf_ASizeTooSmallToWriteIsRefused()
        {
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(fontSize: 1e-11)).ParamName);
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(() => new ReportBuilder("T").ToPdf(fontSize: 1e-11)).ParamName);
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter().Heading("H", size: 1e-9)).ParamName);
            string pdf = PdfText(new PdfWriter().Table(new[] { "h" }, new[] { new[] { "x" } }, fontSize: 1.5e-7));
            Check.True(pdf.Contains(" 0.00000015 Tf"), "a size near the floor is written as itself");
        }

        public void Pdf_CodeKeepsASurrogatePairWholeAtOneCharALine()
        {
            string pdf = PdfText(new PdfWriter().Code("\U0001F600\U0001F600", size: 900));
            int count = 0;
            for (int i = pdf.IndexOf("(?)", StringComparison.Ordinal); i >= 0; i = pdf.IndexOf("(?)", i + 1, StringComparison.Ordinal)) count++;
            Check.Equal(2, count);
        }

        public void Pdf_MeasureValidatesTheSize()
        {
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter().Measure("abc", double.NaN)).ParamName);
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter().Measure("abc", -1)).ParamName);
        }

        public void Docx_ACarriageReturnInTheTitleSurvives()
        {
            byte[] docx = new DocxWriter { Title = "a\rb\tc", Author = "x\ry" }.Paragraph("p").Build();
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            string raw;
            using (var r = new StreamReader(zip.GetEntry("docProps/core.xml")!.Open())) raw = r.ReadToEnd();
            Check.False(raw.Contains('\r'), "no raw CR, which a conforming reader turns into LF");
            // XDocument reads through XmlReader.Create, which normalizes line ends as the XML spec requires.
            var xml = System.Xml.Linq.XDocument.Parse(raw);
            System.Xml.Linq.XNamespace dc = "http://purl.org/dc/elements/1.1/";
            Check.Equal("a\rb\tc", xml.Descendants(dc + "title").Single().Value);
            Check.Equal("x\ry", xml.Descendants(dc + "creator").Single().Value);
        }

        public void Docx_AnEmptyFooterTextHasNoSeparator()
        {
            byte[] docx = new DocxWriter().Footer("", pageNumbers: true).Paragraph("p").Build();
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            string footer;
            using (var r = new StreamReader(zip.GetEntry("word/footer1.xml")!.Open())) footer = r.ReadToEnd();
            Check.False(footer.Contains("·"), footer);
            Check.True(footer.Contains("Page "), "the page number is still there");
        }

        // ---------------- SettingsStore ----------------
#if !NETFRAMEWORK   // SettingsStore is net8.0-only
        public sealed class Plain { public int X { get; set; } public string Name { get; set; } = ""; public int Count { get; set; } }

        public sealed class Range
        {
            private int _lo, _hi = 10;
            public string Theme { get; set; } = "light";
            public int Lo { get => _lo; set { if (value > _hi) throw new ArgumentOutOfRangeException(nameof(Lo)); _lo = value; } }
            public int Hi { get => _hi; set { if (value < _lo) throw new ArgumentOutOfRangeException(nameof(Hi)); _hi = value; } }
        }

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r109-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void Settings_AValueTheLoadWouldRejectIsNotSaved()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<Range>.LoadFrom(path);
                store.Value.Theme = "dark";
                store.Value.Hi = 30;
                store.Value.Lo = 20;              // valid in memory, but Lo loads before Hi
                Check.Throws<InvalidOperationException>(() => store.Save());
                Check.False(File.Exists(path), "nothing written that the next launch would move aside");
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_ANestedUpdateIsRefused()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<Plain>.LoadFrom(path);
                Check.Throws<InvalidOperationException>(() => store.Update(v =>
                {
                    v.Name = "outer";
                    store.Update(w => w.Count = 5);
                    throw new FormatException();
                }));
                Check.Throws<InvalidOperationException>(() => store.Update(v => { v.Name = "outer"; store.Save(); }));
                Check.Throws<InvalidOperationException>(() => store.Update(v => store.Reset()));
                Check.False(File.Exists(path), "no half-done change reached the file");
                Check.Equal("", store.Value.Name);
                store.Update(v => v.Count = 2);   // and Update still works
                Check.Equal(2, SettingsStore<Plain>.LoadFrom(path).Value.Count);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_ACorruptLinkedFileIsMovedAsideAtItsTarget()
        {
            string dir = TempDir();
            try
            {
                string target = Path.Combine(dir, "real.json"), link = Path.Combine(dir, "link.json");
                File.WriteAllText(target, "{ not json");
                try { File.CreateSymbolicLink(link, target); }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException) { return; }   // no privilege here
                var store = SettingsStore<Plain>.LoadFrom(link);
                Check.True(store.LoadWarning != null, "warning");
                Check.True(new FileInfo(link).LinkTarget != null, "the link is still a link");
                Check.False(File.Exists(target), "the corrupt target was moved");
                Check.Equal(1, Directory.GetFiles(dir, "real.json.corrupt-*").Length);
                store.Value.X = 4;
                store.Save();
                Check.True(new FileInfo(link).LinkTarget != null, "still a link after the save");
                Check.Equal(4, SettingsStore<Plain>.LoadFrom(target).Value.X);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_AFileThatIsNotUtf8IsSetAside()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                File.WriteAllBytes(path, Latin1.GetBytes("{ \"Name\": \"café\" }"));
                var store = SettingsStore<Plain>.LoadFrom(path);
                Check.True(store.LoadWarning != null, "warning");
                Check.Equal("", store.Value.Name);
                Check.Equal(1, Directory.GetFiles(dir, "s.json.corrupt-*").Length);
                // Valid UTF-8, with or without a BOM, still loads.
                File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{ \"Name\": \"café\" }")).ToArray());
                Check.Equal("café", SettingsStore<Plain>.LoadFrom(path).Value.Name);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Settings_ADirectoryPathIsRefused()
        {
            string dir = TempDir();
            try { Check.Equal("path", Check.Throws<ArgumentException>(() => SettingsStore<Plain>.LoadFrom(dir)).ParamName); }
            finally { Directory.Delete(dir, true); }
        }
#endif
    }
}
