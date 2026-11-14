using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 76 (documents and configuration, fifth pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview5Tests
    {
        private static Dictionary<string, string?> D(params (string Key, string? Value)[] pairs)
        {
            var d = new Dictionary<string, string?>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        // ---------------- ConfigBinder ----------------

        public sealed class Server { public int Port { get; set; } }
        public sealed class WithServer { public Server Server { get; set; } = new Server(); }
        public sealed class WithHosts { public List<string> Hosts { get; set; } = new List<string>(); }
        public sealed class WithSecret { public string Secret { get; private set; } = "built-in"; public int Port { get; set; } }
        public sealed class WithDefaults { public string Name { get; set; } = "default"; public int Count { get; set; } = 3; }

        public void Binder_ASectionsOwnValueBesideItsKeysIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithServer>(D(("Server", "localhost"), ("Server:Port", "5"))));
            Check.True(ex.Errors.Any(e => e.Contains("'Server'")), string.Join(" / ", ex.Errors));
            // An empty value beside the keys is nothing to drop.
            Check.Equal(5, ConfigBinder.Bind<WithServer>(D(("Server", ""), ("Server:Port", "5"))).Server.Port);
        }

        public void Binder_AListsWholeValueBesideIndexedKeysIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithHosts>(D(("Hosts", "a,b"), ("Hosts:0", "x"))));
            Check.True(ex.Errors.Any(e => e.Contains("'Hosts'")), string.Join(" / ", ex.Errors));
            Check.Equal("x", string.Join(",", ConfigBinder.Bind<WithHosts>(D(("Hosts", ""), ("Hosts:0", "x"))).Hosts));
        }

        public void Binder_APrivateSetterIsReadOnly()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithSecret>(D(("Secret", "s"))));
            Check.True(ex.Errors.Single().Contains("read-only"), ex.Errors.Single());
            Check.Equal("built-in", ConfigBinder.Bind<WithSecret>(D(("Port", "1"))).Secret);
        }

        public void Binder_ANullValueIsUnsetForEveryType()
        {
            var r = ConfigBinder.Bind<WithDefaults>(D(("Name", null), ("Count", null)));
            Check.Equal("default", r.Name);
            Check.Equal(3, r.Count);
            Check.Equal("", ConfigBinder.Bind<WithDefaults>(D(("Name", ""))).Name);   // empty is still a value for a string
        }

        // ---------------- EnvironmentVariables ----------------

        public void Env_ANullNameIsArgumentNull()
        {
            var env = new EnvironmentVariables(_ => "x");
            Check.Equal("name", Check.Throws<ArgumentNullException>(() => env.Get(null!)).ParamName);
            Check.Equal("name", Check.Throws<ArgumentException>(() => env.Get("")).ParamName);
        }

        public void Env_RequireAllNamesItsOwnParameterForABadElement()
        {
            var env = new EnvironmentVariables(_ => "x");
            Check.Equal("names", Check.Throws<ArgumentException>(() => env.RequireAll("A", null!)).ParamName);
            Check.Equal("names", Check.Throws<ArgumentException>(() => env.RequireAll("A", "")).ParamName);
            env.RequireAll("A", "B");
        }

        public void Env_BooleansAreExactlyTheDocumentedWords()
        {
            var vars = new Dictionary<string, string?> { ["Y"] = "y", ["N"] = "N", ["YES"] = "Yes", ["OFF"] = "off" };
            var env = new EnvironmentVariables(n => vars.TryGetValue(n, out var v) ? v : null);
            Check.Throws<FormatException>(() => env.GetBool("Y"));
            Check.Throws<FormatException>(() => env.GetBool("N"));
            Check.True(env.GetBool("YES"));
            Check.False(env.GetBool("OFF", true));
        }

        public void Env_WithPrefixReportsVariablesThatStripToOneKey()
        {
            MethodInfo m = typeof(EnvironmentVariables).GetMethod("WithPrefix", BindingFlags.NonPublic | BindingFlags.Static)
                           ?? throw new InvalidOperationException("no testable WithPrefix");
            var unix = new Hashtable { ["APP_x"] = "1", ["app_x"] = "2", ["APP_y"] = "3" };
            var ex = Check.Throws<TargetInvocationException>(() => m.Invoke(null, new object[] { unix, false, "APP_", true }));
            Check.True(ex.InnerException is InvalidOperationException, ex.InnerException?.GetType().Name ?? "");
            // Not stripped, the names stay apart; distinct keys are fine.
            var kept = (IReadOnlyDictionary<string, string>)m.Invoke(null, new object[] { unix, false, "APP_", false })!;
            Check.Equal(3, kept.Count);
            var fine = (IReadOnlyDictionary<string, string>)m.Invoke(null, new object[] { new Hashtable { ["APP_x"] = "1", ["app_y"] = "2" }, false, "APP_", true })!;
            Check.Equal("1", fine["x"]);
            Check.Equal("2", fine["y"]);
        }

        // ---------------- MarkdownReport ----------------

        public void Markdown_InlineCodeLinksAndAltTextStayOnOneLine()
        {
            foreach (string br in new[] { "\n\n", "\r\n\r\n", "\r\r", ((char)0x2028).ToString(), ((char)0x2029).ToString() })
            {
                string code = MarkdownReport.Code("a" + br + "b"), link = MarkdownReport.Link("a" + br + "- b", "u");
                string image = new MarkdownReport().Image("x" + br + "y", "u").ToString();
                foreach (string s in new[] { code, link, image.TrimEnd('\n') })
                    Check.False(s.IndexOfAny(new[] { '\n', '\r', (char)0x2028, (char)0x2029 }) >= 0, s);
            }
            Check.Equal("`a  b`", MarkdownReport.Code("a\n\nb"));
        }

        // ---------------- ReportBuilder ----------------

        public void Report_MarkdownAnchorsFollowGitHubForAnAllPunctuationHeading()
        {
            string md = new ReportBuilder("T").TableOfContents().Heading("!!!").Heading("Section").ToMarkdown();
            // GitHub gives "!!!" the empty slug, so a later "Section" is plain "section".
            Check.True(md.Contains("](#section)"), md);
            Check.False(md.Contains("](#section-1)"), md);
            // HTML ids can't be empty: there it is still "section", and the real one is "section-1".
            string html = new ReportBuilder("T").Heading("!!!").Heading("Section").ToHtml();
            Check.True(html.Contains("id=\"section\"") && html.Contains("id=\"section-1\""), html);
            Check.Equal("section", ReportBuilder.Slug("!!!"));
        }

        // ---------------- DocxWriter ----------------

        public void Docx_AltTextKeepsItsLineBreaksAndTabs()
        {
            string alt = "line one\nline two\ttabbed\r\nend";
            byte[] docx = new DocxWriter().Image(SamplePng(), altText: alt).Build();
            using var zip = new ZipArchive(new MemoryStream(docx));
            using var r = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
            XElement root = XDocument.Parse(r.ReadToEnd()).Root!;
            var descrs = root.Descendants().Select(e => e.Attribute("descr")).Where(a => a != null).Select(a => a!.Value).ToList();
            Check.Equal(2, descrs.Count);
            foreach (string d in descrs) Check.Equal(alt, d);
        }

        // ---------------- PdfWriter ----------------

        private static byte[] SamplePng(int w = 40, int h = 20)
        {
            var img = new ImageBuffer(w, h, new Rgba(240, 240, 255));
            img.DrawLine(0, 0, w - 1, h - 1, new Rgba(200, 0, 0));
            return PngWriter.Encode(img);
        }

        public void Pdf_WrappedLinesNeverPassTheRightMargin()
        {
            // Differential: every line Wrap gives is measured by the writer's own Measure; an indented first word ran over.
            var rng = new Random(7601);
            MethodInfo wrap = typeof(PdfWriter).GetMethod("Wrap", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (PdfFont font in new[] { PdfFont.Courier, PdfFont.Helvetica, PdfFont.HelveticaBold, PdfFont.TimesRoman })
            {
                var pdf = new PdfWriter(PdfPageSize.Letter, 54, font, 11);
                for (int trial = 0; trial < 400; trial++)
                {
                    double width = 20 + rng.NextDouble() * 480, size = 5 + rng.NextDouble() * 20;
                    bool bold = rng.Next(2) == 0;
                    var sb = new StringBuilder(new string(' ', rng.Next(4) == 0 ? 0 : rng.Next(120)));
                    int words = 1 + rng.Next(8);
                    for (int i = 0; i < words; i++)
                    {
                        if (i > 0) sb.Append(' ', 1 + (rng.Next(5) == 0 ? 1 : 0));
                        int len = 1 + rng.Next(rng.Next(3) == 0 ? 120 : 12);
                        for (int k = 0; k < len; k++) sb.Append((char)('a' + rng.Next(26)));
                    }
                    string text = sb.ToString();
                    var lines = (List<string>)wrap.Invoke(pdf, new object[] { text, width, size, bold })!;
                    foreach (string line in lines)
                        Check.True(line.TrimEnd().Length <= 1 || pdf.Measure(line, size, bold) <= width * (1 + 1e-12),
                            $"{font} w={width} s={size}: '{line}' is {pdf.Measure(line, size, bold)} wide");
                    // Nothing but spaces is lost.
                    Check.Equal(text.Replace(" ", ""), string.Concat(lines).Replace(" ", ""), text);
                    // An indent that fits beside the first word is kept whole.
                    int lead = text.Length - text.TrimStart(' ').Length;
                    string first = text.TrimStart(' ').Split(' ')[0];
                    if (pdf.Measure(new string(' ', lead) + first, size, bold) <= width)
                        Check.True(lines[0].StartsWith(new string(' ', lead) + first, StringComparison.Ordinal), lines[0]);
                }
            }
        }

        public void Pdf_AHardSplitFirstWordKeepsItsIndent()
        {
            var pdf = new PdfWriter(PdfPageSize.Letter, 54, PdfFont.Courier, 10);
            MethodInfo wrap = typeof(PdfWriter).GetMethod("Wrap", BindingFlags.NonPublic | BindingFlags.Instance)!;
            // Courier 10: 6 points a character, so 60 points hold 10.
            var lines = (List<string>)wrap.Invoke(pdf, new object[] { "    " + new string('x', 25), 60.0, 10.0, false })!;
            Check.Equal("    xxxxxx|xxxxxxxxxx|xxxxxxxxx", string.Join("|", lines));
        }

        public void Pdf_AFullHeightImageAtTheTopUsesOnePage()
        {
            foreach (double margin in new[] { 0.2, 0.7, 1.3, 7.7, 54, 54.3 })
            {
                var pdf = new PdfWriter(PdfPageSize.Letter, margin);
                pdf.Image(1, 2000, Enumerable.Repeat((byte)255, 8000).ToArray());
                Check.Equal(1, pdf.PageCount, "margin " + margin.ToString(CultureInfo.InvariantCulture));
            }
        }

        public void Pdf_CodeBarsStayAboveTheBottomMargin()
        {
            var rng = new Random(7602);
            var bar = new Regex(@"0\.75 g (\S+) (\S+) 2 (\S+) re f");
            for (int trial = 0; trial < 40; trial++)
            {
                double margin = 10 + rng.NextDouble() * 80, size = 4 + rng.NextDouble() * 30;
                var pdf = new PdfWriter(PdfPageSize.Letter, margin);
                pdf.Code(string.Join("\n", Enumerable.Range(0, 60 + rng.Next(80)).Select(i => "line " + i)), size);
                string content = Encoding.GetEncoding(28591).GetString(pdf.Build());
                foreach (Match m in bar.Matches(content))
                {
                    double y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    Check.True(y >= margin - 0.01, $"margin {margin}, size {size}: a bar starts at {y}");
                }
            }
        }

        public void Pdf_CorruptPngDataIsAnArgumentException()
        {
            byte[] png = SamplePng();
            int idat = Encoding.ASCII.GetString(png).IndexOf("IDAT", StringComparison.Ordinal);
            for (int i = idat + 4 + 2; i < idat + 4 + 12; i++) png[i] = 0xFF;   // past the zlib header: an invalid block type
            var ex = Check.Throws<ArgumentException>(() => new PdfWriter().ImagePng(png));
            Check.Equal("png", ex.ParamName);
            // ReportBuilder still skips such a figure rather than failing the document.
            new ReportBuilder("T").Figure("f", png: png).ToPdf();
        }

        // ---------------- SettingsStore ----------------

#if !NETFRAMEWORK   // SettingsStore is net8.0-only
        public abstract class Animal { public string Name { get; set; } = ""; }
        public sealed class WithAbstract { public Animal? Pet { get; set; } public int X { get; set; } }
        public sealed class Plain { public int X { get; set; } public string Name { get; set; } = ""; }
        public sealed class Collections
        {
            public Queue<int> Queue { get; } = new Queue<int>();
            public Stack<int> Stack { get; } = new Stack<int>();
            public ConcurrentQueue<int> CQueue { get; } = new ConcurrentQueue<int>();
            public ConcurrentStack<int> CStack { get; } = new ConcurrentStack<int>();
            public IEnumerable<string> Seq { get; } = new List<string>();
            public IReadOnlyList<string> RoList { get; } = new List<string>();
            public IReadOnlyCollection<string> RoColl { get; } = new HashSet<string>();
            public IReadOnlyDictionary<string, int> RoMap { get; } = new Dictionary<string, int>();
            public Stack<int> SettableStack { get; set; } = new Stack<int>();
            public ConcurrentStack<string> SettableCStack { get; set; } = new ConcurrentStack<string>();
        }
        public sealed class PrivateSet
        {
            public string Name { get; private set; } = "default";
            public int Count { get; private set; }
            public void Change(string name, int count) { Name = name; Count = count; }
        }
        public sealed class Lossy
        {
            private string _name = "";
            public string Name { get => _name; set => _name = value.ToUpperInvariant(); }
            public void SetRaw(string name) => _name = name;
        }
        public sealed class Validating
        {
            private int _port = 80;
            public int Port { get => _port; set => _port = value <= 1000 ? value : throw new ArgumentOutOfRangeException(nameof(value)); }
            public void SetRaw(int port) => _port = port;
        }
        public sealed class Texts { public string Name { get; set; } = ""; public Dictionary<string, int> Map { get; set; } = new Dictionary<string, int>(); }
        private sealed class CallerFault : Exception { }

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r76-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Clean(string dir)
        {
            foreach (string f in Directory.GetFiles(dir)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }

        public void Settings_ATypeTheSerializerCannotLoadThrowsAndKeepsTheFile()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                File.WriteAllText(path, "{\"Pet\":{\"Name\":\"Rex\"},\"X\":1}");
                Check.Throws<NotSupportedException>(() => SettingsStore<WithAbstract>.LoadFrom(path));
                Check.True(File.Exists(path), "the file was moved aside");
                Check.Equal(1, Directory.GetFiles(dir).Length);
                // Bad data is still moved aside: a number where a string goes.
                File.WriteAllText(path, "{\"Name\":5}");
                var store = SettingsStore<Plain>.LoadFrom(path);
                Check.True(store.LoadWarning != null && store.LoadWarning.Contains("moved to"), store.LoadWarning ?? "");
                Check.False(File.Exists(path), "a corrupt file stays");
            }
            finally { Clean(dir); }
        }

        public void Settings_GetOnlyCollectionsOfEveryKindReload()
        {
            var rng = new Random(7603);
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                for (int trial = 0; trial < 20; trial++)
                {
                    var store = SettingsStore<Collections>.LoadFrom(path);
                    var v = store.Value;
                    int[] nums = Enumerable.Range(0, rng.Next(6)).Select(_ => rng.Next(100)).ToArray();
                    string[] words = Enumerable.Range(0, rng.Next(6)).Select(i => "w" + i + "-" + rng.Next(100)).ToArray();
                    v.Queue.Clear(); v.Stack.Clear(); v.CQueue.Clear(); v.CStack.Clear();
                    ((List<string>)v.Seq).Clear(); ((List<string>)v.RoList).Clear(); ((HashSet<string>)v.RoColl).Clear();
                    ((Dictionary<string, int>)v.RoMap).Clear();
                    v.SettableStack = new Stack<int>(); v.SettableCStack = new ConcurrentStack<string>();
                    foreach (int n in nums) { v.Queue.Enqueue(n); v.Stack.Push(n); v.CQueue.Enqueue(n); v.CStack.Push(n); v.SettableStack.Push(n); }
                    foreach (string w in words)
                    {
                        ((List<string>)v.Seq).Add(w); ((List<string>)v.RoList).Add(w); ((HashSet<string>)v.RoColl).Add(w);
                        ((Dictionary<string, int>)v.RoMap)[w] = w.Length; v.SettableCStack.Push(w);
                    }
                    store.Save();
                    var back = SettingsStore<Collections>.LoadFrom(path).Value;
                    Check.Equal(string.Join(",", v.Queue), string.Join(",", back.Queue), "Queue");
                    Check.Equal(string.Join(",", v.Stack), string.Join(",", back.Stack), "Stack");
                    Check.Equal(string.Join(",", v.CQueue), string.Join(",", back.CQueue), "ConcurrentQueue");
                    Check.Equal(string.Join(",", v.CStack), string.Join(",", back.CStack), "ConcurrentStack");
                    Check.Equal(string.Join(",", v.Seq), string.Join(",", back.Seq), "IEnumerable");
                    Check.Equal(string.Join(",", v.RoList), string.Join(",", back.RoList), "IReadOnlyList");
                    Check.Equal(string.Join(",", v.RoColl.OrderBy(s => s, StringComparer.Ordinal)), string.Join(",", back.RoColl.OrderBy(s => s, StringComparer.Ordinal)), "IReadOnlyCollection");
                    Check.Equal(string.Join(",", v.RoMap.OrderBy(p => p.Key, StringComparer.Ordinal)), string.Join(",", back.RoMap.OrderBy(p => p.Key, StringComparer.Ordinal)), "IReadOnlyDictionary");
                    Check.Equal(string.Join(",", v.SettableStack), string.Join(",", back.SettableStack), "settable Stack");
                    Check.Equal(string.Join(",", v.SettableCStack), string.Join(",", back.SettableCStack), "settable ConcurrentStack");
                }
            }
            finally { Clean(dir); }
        }

        public void Settings_APrivateSetterLoads()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<PrivateSet>.LoadFrom(path);
                store.Value.Change("custom", 4);
                store.Save();
                var back = SettingsStore<PrivateSet>.LoadFrom(path);
                Check.Equal("custom", back.Value.Name);
                Check.Equal(4, back.Value.Count);
                Check.True(back.LoadWarning == null, back.LoadWarning ?? "");
            }
            finally { Clean(dir); }
        }

        public void Settings_UpdateKeepsTheCallersExceptionAndNeverRestoresALossyCopy()
        {
            string dir = TempDir();
            try
            {
                // The copy would differ (the setter upper-cases): Value stays, and saving is refused until Reset.
                var lossy = SettingsStore<Lossy>.LoadFrom(Path.Combine(dir, "l.json"));
                lossy.Value.SetRaw("abc");
                Lossy before = lossy.Value;
                Check.Throws<CallerFault>(() => lossy.Update(_ => throw new CallerFault()));
                Check.True(ReferenceEquals(before, lossy.Value), "Value was replaced");
                Check.Equal("abc", lossy.Value.Name);
                Check.Throws<InvalidOperationException>(() => lossy.Save());
                Check.Throws<InvalidOperationException>(() => lossy.Update(v => v.Name = "x"));
                lossy.Reset();
                lossy.Save();

                // The copy can't even be made (the setter rejects the value): still the caller's exception.
                var strict = SettingsStore<Validating>.LoadFrom(Path.Combine(dir, "v.json"));
                strict.Value.SetRaw(5000);
                Check.Throws<CallerFault>(() => strict.Update(_ => throw new CallerFault()));
                Check.Equal(5000, strict.Value.Port);
                Check.Throws<InvalidOperationException>(() => strict.Save());

                // An exact copy is still restored.
                var plain = SettingsStore<Plain>.LoadFrom(Path.Combine(dir, "p.json"));
                plain.Value.X = 3;
                Check.Throws<CallerFault>(() => plain.Update(v => { v.X = 9; throw new CallerFault(); }));
                Check.Equal(3, plain.Value.X);
                plain.Save();
            }
            finally { Clean(dir); }
        }

        public void Settings_UpdateAndResetLeaveValueAloneWhenTheyCannotSave()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                File.WriteAllText(path, "{\"X\":7}");
                SettingsStore<Plain> store;
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    store = SettingsStore<Plain>.LoadFrom(path);
                bool ran = false;
                Check.Throws<InvalidOperationException>(() => store.Update(v => { ran = true; v.X = 99; }));
                Check.False(ran, "the change ran");
                Check.Equal(0, store.Value.X);

                // A save that fails (a read-only file): Update rolls back, Reset keeps the current settings.
                string ro = Path.Combine(dir, "ro.json");
                var s2 = SettingsStore<Plain>.LoadFrom(ro);
                s2.Value.X = 5;
                s2.Save();
                File.SetAttributes(ro, FileAttributes.ReadOnly);
                Check.Throws<UnauthorizedAccessException>(() => s2.Update(v => v.X = 9));
                Check.Equal(5, s2.Value.X);
                Check.Throws<UnauthorizedAccessException>(() => s2.Reset());
                Check.Equal(5, s2.Value.X);
            }
            finally { Clean(dir); }
        }

        public void Settings_ACorruptFileWithALongNameIsMovedAside()
        {
            foreach (string stem in new[] { new string('s', 245), new string('é', 120), string.Concat(Enumerable.Repeat("😀", 60)) })
            {
                string dir = TempDir();
                try
                {
                    string path = Path.Combine(dir, stem + ".json");
                    File.WriteAllText(path, "{ not json");
                    var store = SettingsStore<Plain>.LoadFrom(path);
                    Check.True(store.LoadWarning != null && store.LoadWarning.Contains("moved to"), store.LoadWarning ?? "");
                    Check.False(File.Exists(path), "still in place");
                    string aside = Directory.GetFiles(dir).Single();
                    Check.True(Path.GetFileName(aside).Length <= 255 && Path.GetFileName(aside).Contains(".corrupt-"), aside);
                    Check.Equal("{ not json", File.ReadAllText(aside));
                }
                finally { Clean(dir); }
            }
        }

        public void Settings_SaveRemovesOnlyStaleTempFiles()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                string stale = Path.Combine(dir, ".tb-" + Guid.NewGuid().ToString("N") + ".tmp");
                string fresh = Path.Combine(dir, ".tb-" + Guid.NewGuid().ToString("N") + ".tmp");
                string other = Path.Combine(dir, ".tb-mine.tmp");
                foreach (string f in new[] { stale, fresh, other }) File.WriteAllText(f, "x");
                File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));
                File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddHours(-2));
                SettingsStore<Plain>.LoadFrom(path).Save();
                Check.False(File.Exists(stale), "the stale temp file is left");
                Check.True(File.Exists(fresh) && File.Exists(other), "a file that isn't a stale temp was removed");
            }
            finally { Clean(dir); }
        }

        public void Settings_ALoneSurrogateIsRefusedNotReplaced()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<Texts>.LoadFrom(path);
                store.Value.Name = "ok";
                store.Save();
                string saved = File.ReadAllText(path);
                foreach (string bad in new[] { "a\uD800b", "\uDC00", "x\uD83D" })
                {
                    store.Value.Name = bad;
                    Check.Throws<ArgumentException>(() => store.Save());
                    store.Value.Name = "ok";
                    store.Value.Map[bad] = 1;
                    Check.Throws<ArgumentException>(() => store.Save());
                    store.Value.Map.Clear();
                    Check.Equal(saved, File.ReadAllText(path));
                }
                // A pair is fine, as a value and as a key.
                store.Value.Name = "😀";
                store.Value.Map["😀"] = 2;
                store.Save();
                var back = SettingsStore<Texts>.LoadFrom(path).Value;
                Check.Equal("😀", back.Name);
                Check.Equal(2, back.Map["😀"]);
            }
            finally { Clean(dir); }
        }

        public void Settings_SaveKeepsHiddenAndFailsFastWhenReadOnly()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<Plain>.LoadFrom(path);
                store.Save();
                File.SetAttributes(path, FileAttributes.Hidden);
                store.Value.X = 2;
                store.Save();
                Check.True((File.GetAttributes(path) & FileAttributes.Hidden) != 0, "no longer hidden");
                Check.Equal(2, SettingsStore<Plain>.LoadFrom(path).Value.X);

                File.SetAttributes(path, FileAttributes.ReadOnly);
                var sw = Stopwatch.StartNew();
                Check.Throws<UnauthorizedAccessException>(() => store.Save());
                Check.True(sw.ElapsedMilliseconds < 250, sw.ElapsedMilliseconds + " ms");
                Check.Equal(0, Directory.GetFiles(dir, ".tb-*").Length);
            }
            finally { Clean(dir); }
        }
#endif
    }
}
