using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 100 (documents and configuration, sixth pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview100Tests
    {
        private static Dictionary<string, string?> D(params (string Key, string? Value)[] pairs)
        {
            var d = new Dictionary<string, string?>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        // ---------------- PdfWriter ----------------

        // A column sized to its text wraps at that width again: a one-word cell prints as that one word. Taking the
        // padding back off landed one ulp short about 7% of the time and split the word ('flq' at Helvetica 15.6).
        public void Pdf_AnAutoSizedColumnNeverSplitsAWordThatFits()
        {
            var rng = new Random(10001);
            var tj = new Regex(@"\(([^()\\]*)\) Tj");
            foreach (PdfFont font in new[] { PdfFont.Helvetica, PdfFont.TimesRoman, PdfFont.Courier })
                for (int trial = 0; trial < 600; trial++)
                {
                    double size = Math.Round(6 + rng.NextDouble() * 10, 1);
                    var word = new StringBuilder();
                    int len = rng.Next(1, 12);
                    for (int i = 0; i < len; i++) word.Append((char)('a' + rng.Next(26)));
                    byte[] pdf = new PdfWriter(PdfPageSize.Letter, 54, font, size)
                        .Table(new[] { "H" }, new[] { new[] { word.ToString() } }).Build();
                    var shown = new List<string>();
                    foreach (Match m in tj.Matches(Encoding.GetEncoding("ISO-8859-1").GetString(pdf))) shown.Add(m.Groups[1].Value);
                    Check.True(shown.Contains(word.ToString()), $"{font} {size} '{word}': shown [{string.Join("|", shown)}]");
                }
        }

        // ---------------- ConfigBinder ----------------

        public sealed class WithLists
        {
            public int[] Ports { get; set; } = { 9 };
            public List<string> Hosts { get; set; } = new List<string> { "d" };
        }

        // A null item is unset, as a null key is: the list keeps its default, or ends before it.
        public void Binder_ANullListItemIsUnset()
        {
            Check.Equal("9", string.Join(",", ConfigBinder.Bind<WithLists>(D(("Ports:0", null))).Ports));
            Check.Equal("d", string.Join(",", ConfigBinder.Bind<WithLists>(D(("Hosts:0", null))).Hosts));
            var r = ConfigBinder.Bind<WithLists>(D(("Ports:0", "1"), ("Ports:1", null), ("Hosts:0", "a"), ("Hosts:1", null)));
            Check.Equal("1", string.Join(",", r.Ports));
            Check.Equal("a", string.Join(",", r.Hosts));
        }

        // A null item before a value leaves a gap, which is reported as any gap is (not converted, not a null entry).
        public void Binder_ANullItemBeforeAValueIsAGap()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithLists>(D(("Hosts:0", null), ("Hosts:1", "b"))));
            Check.True(string.Join(" ", ex.Errors).Contains("'Hosts:1'"), string.Join(" ", ex.Errors));
            var ey = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithLists>(D(("Ports:0", null), ("Ports:1", "2"))));
            Check.False(string.Join(" ", ey.Errors).Contains("cannot convert"), string.Join(" ", ey.Errors));
        }

        // "Hosts:" (an empty index) was dropped without a word, alone or beside real items.
        public void Binder_AnEmptyListIndexIsReported()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithLists>(D(("Hosts:", "x"))));
            Check.True(string.Join(" ", ex.Errors).Contains("'Hosts:'"), string.Join(" ", ex.Errors));
            Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithLists>(D(("Hosts:0", "a"), ("Hosts:", "x"))));
            Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithLists>(D(("Ports:", "5"))));
            // Guard: an unset empty index is nothing to report.
            Check.Equal("d", string.Join(",", ConfigBinder.Bind<WithLists>(D(("Hosts:", null))).Hosts));
        }

        public sealed class Tree
        {
            public Tree? A { get; set; }
            public Tree? B { get; set; }
            public int V { get; set; }
        }

        // A self-containing type binds in time linear in the keys: each section scanned every key once per property, so
        // 8,000 keys took 29 s (and 2,000 took 3 s).
        public void Binder_ADeepTreeBindsInLinearTime()
        {
            var rng = new Random(10003);
            var keys = new Dictionary<string, string?>();
            while (keys.Count < 8000)
            {
                var k = new StringBuilder();
                int depth = rng.Next(1, 61);
                for (int i = 0; i < depth; i++) k.Append(rng.Next(2) == 0 ? "A:" : "B:");
                keys[k.Append('V').ToString()] = "1";
            }
            var sw = Stopwatch.StartNew();
            Tree t = ConfigBinder.Bind<Tree>(keys);
            sw.Stop();
            Check.True(sw.ElapsedMilliseconds < 4000, $"8,000 keys bound in {sw.ElapsedMilliseconds} ms");
            // Every configured leaf was bound.
            foreach (string key in keys.Keys)
            {
                Tree? n = t;
                string[] parts = key.Split(':');
                for (int i = 0; i < parts.Length - 1; i++) n = parts[i] == "A" ? n!.A : n!.B;
                Check.Equal(1, n!.V, key);
            }
        }

        public sealed class WithPort { public int Port { get; set; } = 7; }

        // "Port" and "PORT" with different values: one was kept by enumeration order, without a word.
        public void Binder_AKeyInTwoCasingsWithDifferentValuesIsReported()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithPort>(D(("Port", "1"), ("PORT", "2"))));
            Check.True(string.Join(" ", ex.Errors).Contains("'PORT'"), string.Join(" ", ex.Errors));
            // Guard: the same value twice is no conflict.
            Check.Equal(1, ConfigBinder.Bind<WithPort>(D(("Port", "1"), ("PORT", "1"))).Port);
        }

        public void Layers_AKeyInTwoCasingsWithDifferentValuesIsRefused()
        {
            var ex = Check.Throws<ArgumentException>(() => new ConfigLayers().Add("file", D(("Port", "1"), ("PORT", "2"))));
            Check.Equal("values", ex.ParamName);
            // Guard: the same value twice, or the casings in different layers, are fine.
            var layers = new ConfigLayers().Add("a", D(("Port", "1"), ("PORT", "1"))).Add("b", D(("port", "3")));
            Check.True(layers.TryGet("PORT", out string? v));
            Check.Equal("3", v);
        }

        // ---------------- EnvironmentVariables ----------------

        // The OS stops reading a name at NUL, so "X\0junk" read X; a name with '=' can't exist either.
        public void Env_ANameWithNulOrEqualsIsRefused()
        {
            var env = EnvironmentVariables.From(new Dictionary<string, string> { ["X"] = "real" });
            Check.Equal("name", Check.Throws<ArgumentException>(() => env.Get("X\0junk")).ParamName);
            Check.Equal("name", Check.Throws<ArgumentException>(() => env.Get("X=1")).ParamName);
            Check.Equal("name", Check.Throws<ArgumentException>(() => env.GetInt("X\0", 1)).ParamName);
            Check.Equal("real", env.Get("X"));
            Check.True(env.Get("=C:") == null);    // guard: a leading '=' is a Windows drive variable's name
        }

        // ---------------- SettingsStore ----------------

#if !NETFRAMEWORK   // SettingsStore is net8.0-only
        public sealed class WithChars
        {
            public char C { get; set; } = 'a';
            public List<char> Cs { get; set; } = new List<char>();
            public char[] Arr { get; set; } = Array.Empty<char>();
        }

        public sealed class WithReadOnly
        {
            public ReadOnlyCollection<string> Names { get; } = new ReadOnlyCollection<string>(new List<string> { "a" });
            public ReadOnlyDictionary<string, int> Map { get; } = new ReadOnlyDictionary<string, int>(new Dictionary<string, int> { ["k"] = 1 });
            public int N { get; set; }
        }

        // A get-only ReadOnlyCollection or ReadOnlyDictionary keeps its default, as documented: once saved, every load threw.
        public void Settings_AGetOnlyReadOnlyCollectionKeepsItsDefault()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r100-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<WithReadOnly>.LoadFrom(path);
                store.Value.N = 5;
                store.Save();
                var back = SettingsStore<WithReadOnly>.LoadFrom(path);
                Check.True(back.LoadWarning == null, back.LoadWarning ?? "");
                Check.Equal(5, back.Value.N);
                Check.Equal("a", string.Join(",", back.Value.Names));
                Check.Equal(1, back.Value.Map["k"]);
            }
            finally { Directory.Delete(dir, true); }
        }

        public interface IPlugin { string Id { get; } }
        public sealed class Plug : IPlugin { public string Id { get; set; } = "p"; }
        public sealed class WithPlugin { public IPlugin? Plugin { get; set; } public int N { get; set; } }

        // A value no load can read back is refused at the save, not at every launch after it.
        public void Settings_AValueThatCannotLoadBackIsNotSaved()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r100-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<WithPlugin>.LoadFrom(path);
                store.Value.N = 3;
                store.Save();                                  // guard: null is saved and loads
                string before = File.ReadAllText(path);
                Check.Equal(3, SettingsStore<WithPlugin>.LoadFrom(path).Value.N);
                Check.Throws<NotSupportedException>(() => store.Update(v => { v.Plugin = new Plug(); v.N = 7; }));
                Check.Equal(before, File.ReadAllText(path));
                Check.Equal(3, SettingsStore<WithPlugin>.LoadFrom(path).Value.N);
                Check.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
            }
            finally { Directory.Delete(dir, true); }
        }

        // A save keeps the file's own ACL: the move gave an owner-only file the folder's inherited entries.
        public void Settings_ASaveKeepsTheFilesAcl()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = Path.Combine(Path.GetTempPath(), "tb-r100-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<WithPort>.LoadFrom(path);
                store.Save();
                Icacls($"\"{path}\" /inheritance:r /grant:r *S-1-3-4:F");
                string before = Icacls($"\"{path}\"");
                Check.False(before.Contains("(I)"), before);
                store.Update(v => v.Port = 9);
                string after = Icacls($"\"{path}\"");
                Check.False(after.Contains("(I)"), after);
                Check.Equal(9, SettingsStore<WithPort>.LoadFrom(path).Value.Port);
            }
            finally { Directory.Delete(dir, true); }
        }

        private static string Icacls(string args)
        {
            var psi = new ProcessStartInfo("icacls", args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Check.Equal(0, p.ExitCode, output);
            return output;
        }

        // A char that is half a surrogate pair is refused on save, as a string holding one is; it was written as U+FFFD.
        public void Settings_ALoneSurrogateCharIsRefused()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r100-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<WithChars>.LoadFrom(path);
                store.Value.C = 'é';
                store.Value.Cs.Add('z');
                store.Save();
                string before = File.ReadAllText(path);
                Check.Equal('é', SettingsStore<WithChars>.LoadFrom(path).Value.C);

                store.Value.C = '\uD800';
                Check.Throws<ArgumentException>(() => store.Save());
                store.Value.C = 'b';
                store.Value.Cs.Add('\uDC00');
                Check.Throws<ArgumentException>(() => store.Save());
                store.Value.Cs.RemoveAt(1);
                store.Value.Arr = new[] { 'q', '\uDBFF' };
                Check.Throws<ArgumentException>(() => store.Save());
                Check.Equal(before, File.ReadAllText(path));
            }
            finally { Directory.Delete(dir, true); }
        }
#endif
    }
}
