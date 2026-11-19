using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 160 (documents and configuration, tenth pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview160Tests
    {
        public sealed class Counter
        {
            public int N { get; set; }
        }

        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r160-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // Holds the file open (no sharing but reads) for a fifth of a second, from another thread.
        private static Thread HoldFor(string path, FileShare share, ManualResetEventSlim held)
        {
            var t = new Thread(() =>
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, share))
                {
                    held.Set();
                    Thread.Sleep(200);
                }
            });
            t.Start();
            held.Wait();
            return t;
        }

        // ---------------- PdfWriter and DocxWriter ----------------

#if !NETFRAMEWORK
        // The temp file was the target's name plus 13 characters, so a legal 249-character name failed with IOException and
        // nothing was written.
        public void Save_ALongFileNameIsWritten()
        {
            string dir = NewDir();
            try
            {
                string pdf = Path.Combine(dir, new string('n', 245) + ".pdf");
                new PdfWriter().Paragraph("x").Save(pdf);
                Check.True(File.ReadAllBytes(pdf).Length > 0);
                string docx = Path.Combine(dir, new string('n', 244) + ".docx");
                new DocxWriter().Paragraph("x").Save(docx);
                Check.True(File.ReadAllBytes(docx).Length > 0);
                Check.Equal(2, Directory.GetFiles(dir).Length);
            }
            finally { Directory.Delete(dir, true); }
        }
#endif

        // A reader holding the old file (a viewer, File.ReadAllBytes: no FileShare.Delete) made the replace fail at once.
        public void Save_WaitsForAReaderToLetGo()
        {
            string dir = NewDir();
            try
            {
                foreach (bool docx in new[] { false, true })
                {
                    string path = Path.Combine(dir, docx ? "a.docx" : "a.pdf");
                    Action<string> save = text =>
                    {
                        if (docx) new DocxWriter().Paragraph(text).Save(path);
                        else new PdfWriter().Paragraph(text).Save(path);
                    };
                    save("old");
                    byte[] before = File.ReadAllBytes(path);
                    using (var held = new ManualResetEventSlim(false))
                    {
                        var t = HoldFor(path, FileShare.Read, held);
                        save("new text");
                        t.Join();
                    }
                    Check.False(File.ReadAllBytes(path).SequenceEqual(before), docx ? "docx" : "pdf");
                }
                Check.Equal(2, Directory.GetFiles(dir).Length);
            }
            finally { Directory.Delete(dir, true); }
        }

#if !NETFRAMEWORK
        // ---------------- SettingsStore ----------------

        // A path ending in a separator loaded as "no file yet"; Save then made the folder and failed with an access error.
        public void SettingsStore_APathEndingInASeparatorIsRefused()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "settings") + Path.DirectorySeparatorChar;
                Check.Equal("path", Check.Throws<ArgumentException>(() => SettingsStore<Counter>.LoadFrom(path)).ParamName);
                Check.Equal("path", Check.Throws<ArgumentException>(() => SettingsStore<Counter>.LoadFrom(path + "x" + Path.AltDirectorySeparatorChar)).ParamName);
                Check.Equal(0, Directory.GetFileSystemEntries(dir).Length);
            }
            finally { Directory.Delete(dir, true); }
        }

        // A load while another instance's save held the file returned defaults and refused to save until Reset.
        public void SettingsStore_ALoadWaitsForASaveToLetGo()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "settings.json");
                var store = SettingsStore<Counter>.LoadFrom(path);
                store.Value.N = 7;
                store.Save();
                using (var held = new ManualResetEventSlim(false))
                {
                    var t = HoldFor(path, FileShare.None, held);
                    var loaded = SettingsStore<Counter>.LoadFrom(path);
                    t.Join();
                    Check.True(loaded.LoadWarning is null, loaded.LoadWarning ?? "");
                    Check.Equal(7, loaded.Value.N);
                    loaded.Value.N = 8;
                    loaded.Save();
                }
                Check.Equal(8, SettingsStore<Counter>.LoadFrom(path).Value.N);
            }
            finally { Directory.Delete(dir, true); }
        }

        // Guard: a file held for longer than the wait still loads as defaults with saving refused (the file may be fine).
        public void SettingsStore_AFileHeldForLongStillRefusesToSave()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "settings.json");
                var store = SettingsStore<Counter>.LoadFrom(path);
                store.Value.N = 3;
                store.Save();
                SettingsStore<Counter> loaded;
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    loaded = SettingsStore<Counter>.LoadFrom(path);
                Check.True(loaded.LoadWarning != null && loaded.LoadWarning.Contains("saving is refused"), loaded.LoadWarning ?? "");
                Check.Throws<InvalidOperationException>(() => loaded.Save());
                Check.Equal(3, SettingsStore<Counter>.LoadFrom(path).Value.N);
            }
            finally { Directory.Delete(dir, true); }
        }

        // A file there for File.Exists but gone for the read (another save's replace, three times running) loaded as "no
        // file yet" without asking whether a save was under way, so the next save could put defaults over the user's
        // settings (about one run in four of the round 151 test). A link to a file not made yet takes the same path every
        // time, which makes it testable: a fresh temp of the file's own save is in the folder, so the load must wait and
        // warn.
        public void SettingsStore_AFileThatVanishesMidReadAsksWhetherASaveIsUnderWay()
        {
            string dir = NewDir();
            try
            {
                string target = Path.Combine(dir, "real.json"), link = Path.Combine(dir, "settings.json");
                try { File.CreateSymbolicLink(link, target); }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException) { return; }   // no privilege here
                File.WriteAllText(DocumentsConfigReview155Tests.SaveTemp(dir, "real.json"), "{}");
                var loaded = SettingsStore<Counter>.LoadFrom(link);
                Check.True(loaded.LoadWarning != null && loaded.LoadWarning.Contains("under way"), loaded.LoadWarning ?? "no warning");
            }
            finally { Directory.Delete(dir, true); }
        }

        // Guard: a link to a file not made yet, with no save under way, is no file yet: defaults, no warning, no wait, and
        // the first save writes the target.
        public void SettingsStore_ALinkToAFileNotMadeYetLoadsDefaults()
        {
            string dir = NewDir();
            try
            {
                string target = Path.Combine(dir, "real.json"), link = Path.Combine(dir, "settings.json");
                try { File.CreateSymbolicLink(link, target); }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException) { return; }   // no privilege here
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var loaded = SettingsStore<Counter>.LoadFrom(link);
                Check.True(clock.ElapsedMilliseconds < 1000, clock.ElapsedMilliseconds + " ms");
                Check.True(loaded.LoadWarning is null, loaded.LoadWarning ?? "");
                loaded.Update(v => v.N = 4);
                Check.Equal(4, SettingsStore<Counter>.LoadFrom(target).Value.N);
            }
            finally { Directory.Delete(dir, true); }
        }

#endif

        // ---------------- ReportBuilder ----------------

        // The builder's own escaper wrote lone surrogates, NUL and other controls raw (HtmlReport's was fixed in round 151),
        // so the title, metadata, contents, headings, properties, captions and callouts could not be written as UTF-8.
        public void ReportBuilder_HtmlReplacesControlsAndLoneSurrogates()
        {
            string bad = "\uD800\u0001\u0000\u0085\uDC00";
            string html = new ReportBuilder("T" + bad + "x").Metadata("k" + bad, "v" + bad).TableOfContents()
                .Heading("H" + bad).Properties(("p" + bad, "q" + bad))
                .Table(new[] { "a" }, new[] { new[] { "1" } }, "cap" + bad)
                .Callout(CalloutKind.Note, "co" + bad).Figure("fig" + bad, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>").ToHtml();
            new UTF8Encoding(false, true).GetBytes(html);                     // throws on a lone surrogate
            foreach (char ch in html)
                Check.False(ch < ' ' && ch != '\n' && ch != '\r' && ch != '\t' || ch >= '\u007f' && ch <= '\u009f', "raw U+" + ((int)ch).ToString("X4"));
            Check.True(html.Contains("T&#xFFFD;&#xFFFD;&#xFFFD;&#xFFFD;&#xFFFD;x"), html);
            // A pair is kept.
            Check.True(new ReportBuilder("a😀b").ToHtml().Contains("a😀b"));
        }

        // .NET Framework's casing tables lack mappings .NET 8 (and GitHub) has: the Kelvin sign kept its case on net48, so
        // the anchor differed from GitHub's. One letter from each missing range, upper and lower.
        public void ReportBuilder_SlugLowersAsGitHubDoesOnEveryRuntime()
        {
            var pairs = new (int Upper, int Lower)[]
            {
                (0x212A, 'k'), (0x2126, 0x3C9), (0x212B, 0xE5), (0x1C5, 0x1C6), (0x1F2, 0x1F3), (0x37F, 0x3F3), (0x3F4, 0x3B8),
                (0x524, 0x525), (0x52E, 0x52F), (0x10C7, 0x2D27), (0x13A0, 0xAB70), (0x13EF, 0xABBF), (0x13F5, 0x13FD),
                (0x1C90, 0x10D0), (0x1CBF, 0x10FF), (0x1E9E, 0xDF), (0x2C70, 0x252), (0x2C7F, 0x240), (0x2CF2, 0x2CF3),
                (0xA698, 0xA699), (0xA78D, 0x265), (0xA7AB, 0x25C), (0xA7B3, 0xAB53), (0xA7C6, 0x1D8E), (0xA7F5, 0xA7F6),
                (0x104B0, 0x104D8), (0x10C80, 0x10CC0), (0x118A0, 0x118C0), (0x16E40, 0x16E60), (0x1E900, 0x1E922),
            };
            foreach (var (u, l) in pairs)
                Check.Equal(ReportBuilder.Slug("x" + char.ConvertFromUtf32(l)), ReportBuilder.Slug("x" + char.ConvertFromUtf32(u)), "U+" + u.ToString("X4"));
            Check.Equal("kelvin", ReportBuilder.Slug("Kelvin"));
        }

        // ---------------- ConfigBinder and EnvironmentVariables ----------------

        public sealed class Numbers
        {
            public double D { get; set; }
            public float F { get; set; }
        }

        private static bool NegZero(double v) => v == 0 && double.IsNegativeInfinity(1 / v);

        // .NET Framework refused "infinity" and "nan" in any case but one and read "-0" as +0; "∞" was refused on both,
        // though the overflow check exempted it. Each now binds to the value it names on every runtime.
        public void Numbers_BindTheSameOnEveryRuntime()
        {
            var cases = new (string Text, double Value)[]
            {
                ("infinity", double.PositiveInfinity), ("-INFINITY", double.NegativeInfinity), ("+Infinity", double.PositiveInfinity),
                (" infinity ", double.PositiveInfinity), ("∞", double.PositiveInfinity), ("-∞", double.NegativeInfinity),
                ("nan", double.NaN), ("NAN", double.NaN), ("-nan", double.NaN), ("-0", -0.0), ("-0.0e5", -0.0), ("0", 0.0), ("1.5", 1.5),
            };
            foreach (var (text, value) in cases)
            {
                var n = ConfigBinder.Bind<Numbers>(new Dictionary<string, string?> { ["D"] = text, ["F"] = text });
                double env = EnvironmentVariables.From(new Dictionary<string, string> { ["X"] = text }).GetDouble("X", 99);
                foreach (double got in new[] { n.D, (double)n.F, env })
                {
                    if (double.IsNaN(value)) Check.True(double.IsNaN(got), text);
                    else Check.Equal(value, got, text);
                    Check.Equal(NegZero(value), NegZero(got), "sign of " + text);
                }
            }
            // Guard: overflow and junk are still refused.
            foreach (string text in new[] { "1e400", "-1e400", "infinityx", "in", "--0", "1,5" })
                Check.Throws<Exception>(() => ConfigBinder.Bind<Numbers>(new Dictionary<string, string?> { ["D"] = text }), text);
            Check.Throws<Exception>(() => ConfigBinder.Bind<Numbers>(new Dictionary<string, string?> { ["F"] = "1e39" }));
        }

        public class BaseOpts
        {
            public int A { get; set; }
        }

        public sealed class DerivedOpts : BaseOpts
        {
            public int B { get; set; }
        }

        public sealed class Holder
        {
            public BaseOpts Opts { get; set; } = new DerivedOpts();
        }

        // A section was bound into the property's declared type, so a derived instance's own keys were dropped silently.
        public void ConfigBinder_BindsIntoTheInstancesOwnType()
        {
            var h = ConfigBinder.Bind<Holder>(new Dictionary<string, string?> { ["Opts:A"] = "1", ["Opts:B"] = "2" });
            Check.Equal(1, h.Opts.A);
            Check.Equal(2, ((DerivedOpts)h.Opts).B);
        }

#if !NETFRAMEWORK
        // ---------------- SettingsStore temp files ----------------

        // Any store's recent temp file in the folder made a load of a file not yet written wait 2 s and warn that a save
        // was under way.
        public void SettingsStore_AnotherFilesSaveDoesNotDelayALoad()
        {
            string dir = NewDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, ".tb-" + Guid.NewGuid().ToString("N") + ".tmp"), "{}");
                var other = SettingsStore<Counter>.LoadFrom(Path.Combine(dir, "state.json"));
                other.Value.N = 1;
                other.Save();
                File.WriteAllText(Path.Combine(dir, ".tb-" + Guid.NewGuid().ToString("N") + ".tmp"), "{}");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var store = SettingsStore<Counter>.LoadFrom(Path.Combine(dir, "settings.json"));
                Check.True(clock.ElapsedMilliseconds < 1000, clock.ElapsedMilliseconds + " ms");
                Check.True(store.LoadWarning is null, store.LoadWarning ?? "");
                store.Save();
            }
            finally { Directory.Delete(dir, true); }
        }
#endif
    }
}
