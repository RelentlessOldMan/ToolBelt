#if !NETFRAMEWORK   // SettingsStore is net8.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ToolBelt.Configuration;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions found in round 155 (by the checked build's run of a round-151 test): each failed before its fix.</summary>
    public sealed class DocumentsConfigReview155Tests
    {
        public sealed class S { public int N { get; set; } }

        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r155-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // The temp name a save of the given file writes: another store's save no longer delays this file's load.
        internal static string SaveTemp(string dir, string fileName)
        {
            uint h = 2166136261;
            foreach (char ch in fileName.ToUpperInvariant()) h = unchecked((h ^ ch) * 16777619);
            return Path.Combine(dir, ".tb-" + h.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N").Substring(8) + ".tmp");
        }

        private static string FreshTemp(string dir)
        {
            string temp = SaveTemp(dir, "s.json");
            File.WriteAllText(temp, "{\"N\":5}");
            return temp;
        }

        // A load during another save waited 50 rounds of 10 ms (about half a second); a saver held up longer on a busy
        // machine, and the load took defaults with no warning, so the next save overwrote the user's settings.
        public void Settings_ASlowSaveIsWaitedForOrWarnedAbout()
        {
            string dir = NewDir();
            try
            {
                // The file comes back after a second: the load waits for it.
                string path = Path.Combine(dir, "s.json");
                FreshTemp(dir);
                var writer = new Thread(() => { Thread.Sleep(1000); File.WriteAllText(path, "{\"N\":5}"); });
                writer.Start();
                var store = SettingsStore<S>.LoadFrom(path);
                writer.Join();
                Check.Equal(5, store.Value.N);
                Check.True(store.LoadWarning is null);

                // It never comes back while the save looks under way (or one crashed): defaults, with a warning.
                string dir2 = NewDir();
                try
                {
                    string path2 = Path.Combine(dir2, "s.json");
                    FreshTemp(dir2);
                    var clock = Stopwatch.StartNew();
                    var waited = SettingsStore<S>.LoadFrom(path2);
                    Check.True(clock.ElapsedMilliseconds >= 1900, "waited " + clock.ElapsedMilliseconds + " ms");
                    Check.Equal(0, waited.Value.N);
                    Check.True(waited.LoadWarning != null && waited.LoadWarning.Contains("under way"), waited.LoadWarning ?? "no warning");
                    waited.Update(v => v.N = 1);   // a crashed save's temp must not block saving
                    Check.Equal(1, SettingsStore<S>.LoadFrom(path2).Value.N);
                }
                finally { Directory.Delete(dir2, true); }

                // Guards: a temp file more than a minute old is no save under way, so a missing file is "no file yet" at once.
                string dir3 = NewDir();
                try
                {
                    string path3 = Path.Combine(dir3, "s.json");
                    File.SetLastWriteTimeUtc(FreshTemp(dir3), DateTime.UtcNow.AddMinutes(-2));
                    var clock = Stopwatch.StartNew();
                    var none = SettingsStore<S>.LoadFrom(path3);
                    Check.True(clock.ElapsedMilliseconds < 1000, "took " + clock.ElapsedMilliseconds + " ms");
                    Check.True(none.LoadWarning is null);
                    none.Update(v => v.N = 3);
                    Check.Equal(3, SettingsStore<S>.LoadFrom(path3).Value.N);
                }
                finally { Directory.Delete(dir3, true); }
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
#endif
