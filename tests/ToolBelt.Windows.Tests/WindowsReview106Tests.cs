using System;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 106 (Windows satellite, pass 6) regressions.
    public sealed class WindowsReview106Tests
    {
        private const RegistryHive Hive = RegistryHive.CurrentUser;

        private static string NewKey() => @"Software\ToolBelt.Windows.Tests\R106." + Guid.NewGuid().ToString("N");

        private static void Cleanup(string sub)
        {
            try { RegistryUtils.DeleteKey(Hive, sub); } catch (Exception) { }
        }

        // ---------- RegistryUtils: NUL characters ----------

        // Win32 ends a key path at a NUL, so DeleteKey(@"...\victim" + "\0other") deleted all of victim, children included.
        public void Registry_DeleteKeyWithANul_IsRefusedAndDeletesNothing()
        {
            string sub = NewKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub + @"\victim\child", "v", "x");
                var ex = Check.Throws<ArgumentException>(() => RegistryUtils.DeleteKey(Hive, sub + @"\victim" + "\0other"));
                Check.Equal("subKey", ex.ParamName);
                Check.True(RegistryUtils.KeyExists(Hive, sub + @"\victim\child"), "victim's child");
                ex = Check.Throws<ArgumentException>(() => RegistryUtils.DeleteKey(Hive, sub + "\0"));
                Check.Equal("subKey", ex.ParamName);
                Check.True(RegistryUtils.KeyExists(Hive, sub), "the parent");
            }
            finally { Cleanup(sub); }
        }

        // Reads with a NUL in the path read the key named up to it: KeyExists("victim\0nosuch") was true.
        public void Registry_ReadsWithANul_SeeNothing()
        {
            string sub = NewKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub + @"\victim", "v", "x");
                Check.False(RegistryUtils.KeyExists(Hive, sub + @"\victim" + "\0nosuch"));
                Check.Equal(null, RegistryUtils.GetValue(Hive, sub + @"\victim" + "\0nosuch", "v"));
                Check.Equal(0, RegistryUtils.GetValueNames(Hive, sub + @"\victim" + "\0nosuch").Count);
                Check.Equal(0, RegistryUtils.GetSubKeyNames(Hive, sub + "\0x").Count);
                Check.Equal("d", RegistryUtils.GetValue<string>(Hive, sub + @"\victim", "v\0shadow", "d"));
            }
            finally { Cleanup(sub); }
        }

        // SetValue with subkey "a\0b" created "a"; with value name "real\0shadow" it overwrote "real".
        public void Registry_WritesWithANul_AreRefused()
        {
            string sub = NewKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub, "real", "R");
                var ex = Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub + @"\a" + "\0b", "v", "x"));
                Check.Equal("subKey", ex.ParamName);
                Check.False(RegistryUtils.KeyExists(Hive, sub + @"\a"), "a was created");
                ex = Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "real\0shadow", "S"));
                Check.Equal("name", ex.ParamName);
                Check.Equal("R", RegistryUtils.GetValue<string>(Hive, sub, "real"));
                ex = Check.Throws<ArgumentException>(() => RegistryUtils.DeleteValue(Hive, sub, "real\0shadow"));
                Check.Equal("name", ex.ParamName);
                Check.Equal("R", RegistryUtils.GetValue<string>(Hive, sub, "real"));
                ex = Check.Throws<ArgumentException>(() => RegistryUtils.DeleteValue(Hive, sub + "\0x", "real"));
                Check.Equal("subKey", ex.ParamName);
                Check.Equal("R", RegistryUtils.GetValue<string>(Hive, sub, "real"));
            }
            finally { Cleanup(sub); }
        }

        // ---------- SingleInstanceApp: case folding ----------

        // The gate folded the id with ToUpperInvariant but the pipe relied on the kernel's case table, which leaves U+017F
        // (long s) and Georgian alone: "...s" and "...ſ" shared a gate but not a pipe, so the second launch was lost.
        public void SingleInstanceApp_IdsThatFoldAlike_Forward()
        {
            foreach (var (first, second) in new[] { ("s", "ſ"), ("ა", "Ა"), ("ǅx", "Ǆx") })
            {
                string id = "TbR106." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".";
                using var primary = SingleInstanceApp.Start(id + first, Array.Empty<string>());
                Check.True(primary.IsPrimary);
                using var got = new System.Threading.ManualResetEventSlim();
                primary.SecondInstanceStarted += (_, __) => got.Set();
                using var other = SingleInstanceApp.Start(id + second, new[] { "x" }, TimeSpan.FromSeconds(5));
                Check.False(other.IsPrimary, "second became primary: " + second);
                Check.True(other.ForwardedToPrimary, "not forwarded: " + second);
                Check.True(got.Wait(TimeSpan.FromSeconds(10)), "not delivered: " + second);
            }
        }

        // A forward that failed and was retried after a take-over attempt got the whole timeout again, so a launch waited
        // twice the forward timeout. The gate is held with no listener, so both forwards fail.
        public void SingleInstanceApp_FailedForward_WaitsAtMostTheTimeout()
        {
            string id = "TbR106." + Guid.NewGuid().ToString("N").Substring(0, 12);
            string user;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent()) user = identity.User!.Value;
            using var gate = SingleInstance.TryAcquire(@"Local\ToolBelt.SingleInstance." + id.ToUpperInvariant() + "." + user);
            Check.True(gate.IsOwned);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var app = SingleInstanceApp.Start(id, new[] { "x" }, TimeSpan.FromSeconds(2));
            clock.Stop();
            Check.False(app.IsPrimary);
            Check.False(app.ForwardedToPrimary);
            Check.True(clock.Elapsed < TimeSpan.FromSeconds(3.5), "took " + clock.Elapsed);      // was about 4 s
        }

        // ---------- SingleInstance: long names ----------

        // Names past the kernel's limit were cut short, so two different ones shared a gate.
        public void SingleInstance_TooLongNames_AreRefused()
        {
            string stem = "TbR106." + Guid.NewGuid().ToString("N");
            string a = stem + new string('a', SingleInstance.MaxNameLength - stem.Length - 1);
            using (var g1 = SingleInstance.TryAcquire(a + "1"))
            using (var g2 = SingleInstance.TryAcquire(a + "2"))
            {
                Check.True(g1.IsOwned, "first at the limit");
                Check.True(g2.IsOwned, "second at the limit");
            }
            string longer = stem + new string('a', 33000);
            var ex = Check.Throws<ArgumentException>(() => SingleInstance.TryAcquire(longer + "1").Dispose());
            Check.Equal("name", ex.ParamName);
        }

        // ---------- RegistryUtils: culture ----------

        // SetValue wrote text in the current culture but GetValue<T> parses invariantly: 1.5 under de-DE read back as 15.
        public void Registry_NumbersAsText_RoundTripInAnyCulture()
        {
            string sub = NewKey();
            var saved = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                foreach (string culture in new[] { "de-DE", "fr-FR", "sv-SE" })
                {
                    System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
                    RegistryUtils.SetValue(Hive, sub, "d", 1.5);
                    RegistryUtils.SetValue(Hive, sub, "m", 1234567.25m);
                    RegistryUtils.SetValue(Hive, sub, "l", -5L);
                    RegistryUtils.SetValue(Hive, sub, "s", 2.5, RegistryValueKind.String);
                    RegistryUtils.SetValue(Hive, sub, "i", -7, RegistryValueKind.String);
                    Check.Equal(1.5, RegistryUtils.GetValue<double>(Hive, sub, "d"));
                    Check.Equal(1234567.25m, RegistryUtils.GetValue<decimal>(Hive, sub, "m"));
                    Check.Equal(-5L, RegistryUtils.GetValue<long>(Hive, sub, "l", 99));
                    Check.Equal(2.5, RegistryUtils.GetValue<double>(Hive, sub, "s"));
                    Check.Equal(-7, RegistryUtils.GetValue<int>(Hive, sub, "i", 99));
                    Check.Equal("1.5", RegistryUtils.GetValue<string>(Hive, sub, "d"));
                }
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = saved;
                Cleanup(sub);
            }
        }

        // ---------- ShortcutUtils ----------

        // An empty file (or one shorter than the link header) read as a shortcut with no target.
        public void Shortcut_EmptyOrShortFile_IsNotAShortcut()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TbR106." + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string empty = System.IO.Path.Combine(dir, "empty.lnk");
                System.IO.File.WriteAllBytes(empty, Array.Empty<byte>());
                Check.Throws<System.IO.InvalidDataException>(() => ShortcutUtils.Read(empty));
                string stub = System.IO.Path.Combine(dir, "stub.lnk");
                var header = new byte[40];
                header[0] = 0x4C;
                System.IO.File.WriteAllBytes(stub, header);
                Check.Throws<System.IO.InvalidDataException>(() => ShortcutUtils.Read(stub));
            }
            finally { System.IO.Directory.Delete(dir, recursive: true); }
        }

        // ---------- null arguments ----------

        public void NullArguments_ThrowArgumentNullException()
        {
            Check.Equal("privilegeName", Check.Throws<ArgumentNullException>(() => PrivilegeScope.Enable(null!)).ParamName);
            Check.Equal("privilegeName", Check.Throws<ArgumentNullException>(() => PrivilegeScope.IsHeld(null!)).ParamName);
            Check.Equal("privilegeName", Check.Throws<ArgumentNullException>(() => PrivilegeScope.IsEnabled(null!)).ParamName);
            Check.Equal("path", Check.Throws<ArgumentNullException>(() => StorageDeviceInfo.Describe(null!)).ParamName);
            Check.Equal("path", Check.Throws<ArgumentNullException>(() => StorageDeviceInfo.GetUncPath(null!)).ParamName);
            Check.Equal("appId", Check.Throws<ArgumentNullException>(() => SingleInstanceApp.Start(null!, Array.Empty<string>())).ParamName);
        }
    }
}
