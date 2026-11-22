using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Numerics;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 207 (Windows satellite, pass 12) regressions.
    public sealed class WindowsReview207Tests
    {
        private static T OnThread<T>(Func<T> f)
        {
            T result = default!;
            Exception? error = null;
            var t = new Thread(() => { try { result = f(); } catch (Exception ex) { error = ex; } });
            t.Start();
            t.Join();
            if (error != null) throw new InvalidOperationException("thread failed: " + error.Message, error);
            return result;
        }

        private static string NewAppId() => "TbR207." + Guid.NewGuid().ToString("N").Substring(0, 12);

        private static string Sid()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User!.Value;
        }

        private static string GateName(string appId) => @"Local\ToolBelt.SingleInstance." + appId.ToUpperInvariant() + "." + Sid();

        private static string PipeName(string appId)
        {
            using var self = Process.GetCurrentProcess();
            return "ToolBelt.SingleInstance.2." + appId + "." + self.SessionId + "." + Sid();
        }

        // ---------- SingleInstanceApp: one deadline for the whole forward (R193-2) ----------

        // A primary that takes the connection and stalls: the first forward spent the 1 s, then the takeover check's
        // second forward got its own 1 s floor, so the launch took 2 s (3 s under load).
        public void SingleInstanceApp_StalledPrimary_StaysWithinTheTimeout()
        {
            string id = NewAppId();
            using var gate = SingleInstance.TryAcquire(GateName(id));
            Check.True(gate.IsOwned);
            using var server = new NamedPipeServerStream(PipeName(id), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var accepted = server.WaitForConnectionAsync();                          // accepts, then never says anything
            var sw = Stopwatch.StartNew();
            bool forwarded = OnThread(() => { using var s = SingleInstanceApp.Start(id, new[] { "a" }, TimeSpan.FromSeconds(1)); return s.ForwardedToPrimary; });
            sw.Stop();
            Check.False(forwarded);
            Check.True(sw.ElapsedMilliseconds >= 900, $"gave up early: {sw.ElapsedMilliseconds} ms");
            Check.True(sw.ElapsedMilliseconds < 1600, $"a 1 s forward timeout took {sw.ElapsedMilliseconds} ms");
        }

        // A gate held with no listener: each connect attempt waited at least 1 s, so a 1.5 s timeout ran a last attempt
        // to 2 s, and the takeover check's second forward a third second.
        public void SingleInstanceApp_NoListener_StaysWithinTheTimeout()
        {
            string id = NewAppId();
            using var gate = SingleInstance.TryAcquire(GateName(id));
            Check.True(gate.IsOwned);
            var sw = Stopwatch.StartNew();
            bool forwarded = OnThread(() => { using var s = SingleInstanceApp.Start(id, new[] { "a" }, TimeSpan.FromMilliseconds(1500)); return s.ForwardedToPrimary; });
            sw.Stop();
            Check.False(forwarded);
            Check.True(sw.ElapsedMilliseconds >= 1400, $"gave up early: {sw.ElapsedMilliseconds} ms");
            Check.True(sw.ElapsedMilliseconds < 2000, $"a 1.5 s forward timeout took {sw.ElapsedMilliseconds} ms");
        }

        // A zero timeout still waits the 1 s floor, once.
        public void SingleInstanceApp_ZeroTimeout_WaitsTheFloorOnce()
        {
            string id = NewAppId();
            using var gate = SingleInstance.TryAcquire(GateName(id));
            Check.True(gate.IsOwned);
            var sw = Stopwatch.StartNew();
            bool forwarded = OnThread(() => { using var s = SingleInstanceApp.Start(id, new[] { "a" }, TimeSpan.Zero); return s.ForwardedToPrimary; });
            sw.Stop();
            Check.False(forwarded);
            Check.True(sw.ElapsedMilliseconds >= 900, $"gave up early: {sw.ElapsedMilliseconds} ms");
            Check.True(sw.ElapsedMilliseconds < 1600, $"a zero forward timeout took {sw.ElapsedMilliseconds} ms");
        }

        // ---------- RegistryUtils ----------

        private const RegistryHive Hive = RegistryHive.CurrentUser;

        private static string FreshSubKey() => @"Software\ToolBelt.Windows.Tests\" + Guid.NewGuid().ToString("N");

        private static void DeleteTree(string sub)
        {
            using RegistryKey cu = Registry.CurrentUser;
            try { cu.DeleteSubKeyTree(sub, throwOnMissingSubKey: false); } catch (Exception) { }
        }

        // Win32 opens at most 32 levels in one call: a key 40 levels below the test root (one built a level at a time) read
        // as absent, its value as the default, and DeleteKey left it, reporting it missing; a write 50 deep was refused.
        public void Registry_KeyDeeperThan32Levels_IsReadWrittenAndDeleted()
        {
            string root = FreshSubKey();
            try
            {
                string path = root;
                RegistryKey key = Registry.CurrentUser.CreateSubKey(root, writable: true);
                for (int i = 0; i < 40; i++)
                {
                    RegistryKey next = key.CreateSubKey("L" + i, writable: true);
                    key.Dispose();
                    key = next;
                    path += "\\L" + i;
                }
                key.SetValue("v", "deep");
                key.CreateSubKey("child").Dispose();
                key.Dispose();
                Check.True(RegistryUtils.KeyExists(Hive, path), "a key 43 levels deep exists");
                Check.Equal("deep", RegistryUtils.GetValue<string>(Hive, path, "v"));
                Check.Equal("deep", (string?)RegistryUtils.GetValue(Hive, path, "v"));
                Check.Equal("v", string.Join(",", RegistryUtils.GetValueNames(Hive, path)));
                Check.Equal("child", string.Join(",", RegistryUtils.GetSubKeyNames(Hive, path)));

                string deeper = path + "\\a\\b\\c\\d\\e\\f\\g\\h";
                RegistryUtils.SetValue(Hive, deeper, "n", 7);
                Check.Equal(7, RegistryUtils.GetValue<int>(Hive, deeper, "n"));
                RegistryUtils.SetValue(Hive, path, "v", "again");                                 // the key that exists
                Check.Equal("again", RegistryUtils.GetValue<string>(Hive, path, "v"));

                Check.True(RegistryUtils.DeleteValue(Hive, path, "v"), "the deep value was deleted");
                Check.Equal(0, RegistryUtils.GetValueNames(Hive, path).Count);
                Check.True(RegistryUtils.DeleteKey(Hive, path), "the deep key was deleted");
                Check.False(RegistryUtils.KeyExists(Hive, path), "the deep key is gone");
                Check.True(RegistryUtils.KeyExists(Hive, path.Substring(0, path.LastIndexOf('\\'))), "its parent is left");
                Check.False(RegistryUtils.DeleteKey(Hive, path), "a second delete finds nothing");
            }
            finally { DeleteTree(root); }
        }

        // No key can have a name over 255 characters: the readers said absent, the deletes threw ArgumentException although
        // they return false for a key that does not exist.
        public void Registry_DeleteWithOverLongSegment_ReturnsFalse()
        {
            string sub = FreshSubKey() + "\\" + new string('k', 300);
            Check.False(RegistryUtils.KeyExists(Hive, sub));
            Check.False(RegistryUtils.DeleteKey(Hive, sub), "DeleteKey");
            Check.False(RegistryUtils.DeleteValue(Hive, sub, "v"), "DeleteValue");
            Check.Equal("subKey", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "v", 1)).ParamName);
        }

        // A Complex was written "<1.5; -2>", which Complex.Parse refuses, so it read back as 0.
        public void Registry_Complex_RoundTrips()
        {
            string sub = FreshSubKey();
            try
            {
                foreach (var c in new[] { new Complex(1.5, -2), new Complex(-0.1, 1e300), new Complex(double.Epsilon, 0), Complex.Zero })
                {
                    RegistryUtils.SetValue(Hive, sub, "c", c);
                    Check.Equal(c, RegistryUtils.GetValue<Complex>(Hive, sub, "c"));
                }
            }
            finally { DeleteTree(sub); }
        }

        // ---------- PrivilegeScope: scopes for one privilege close in any order ----------

        // Closing the outer of two scopes turned the privilege off while the inner one was still open.
        public void PrivilegeScope_OuterDisposedFirst_KeepsThePrivilegeOn()
        {
            const string name = "SeShutdownPrivilege";   // held, and off until asked for, for ordinary and elevated accounts
            if (!PrivilegeScope.IsHeld(name) || PrivilegeScope.IsEnabled(name)) return;   // nothing to see on this account
            var outer = PrivilegeScope.Enable(name);
            var inner = PrivilegeScope.Enable(name);
            try
            {
                Check.False(outer.WasAlreadyEnabled);
                Check.True(inner.WasAlreadyEnabled);
                outer.Dispose();
                Check.True(PrivilegeScope.IsEnabled(name), "still on while the inner scope is open");
                inner.Dispose();
                Check.False(PrivilegeScope.IsEnabled(name), "off once both are closed");
                outer.Dispose();                                                                  // a second dispose does nothing
                Check.False(PrivilegeScope.IsEnabled(name));
                using (var again = PrivilegeScope.Enable(name)) Check.False(again.WasAlreadyEnabled);
                Check.False(PrivilegeScope.IsEnabled(name), "a later scope restores it too");
            }
            finally { inner.Dispose(); outer.Dispose(); }
        }

        // Name kept the caller's casing.
        public void PrivilegeScope_Name_IsWindowsSpelling()
        {
            using var scope = PrivilegeScope.Enable("sechangenotifyprivilege");   // every account holds it
            Check.Equal("SeChangeNotifyPrivilege", scope.Name);
        }

        // ---------- ResourceSampler: a process that has exited ----------

        // Just after the exit, the process can still be in the system's process list: it read 0% and a working set of tens
        // of kilobytes instead of throwing as documented.
        public void ResourceSampler_ExitedProcess_Throws()
        {
            int stale = 0;
            for (int i = 0; i < 60; i++)
            {
                using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
                using var sampler = new ResourceSampler(p);
                Check.True(p.WaitForExit(30000), "cmd exited");
                try { sampler.Sample(); stale++; }
                catch (InvalidOperationException) { }
            }
            Check.Equal(0, stale);
        }

        // ---------- JobObject.Start: bad arguments ----------

        // Each came back from CreateProcess as a Win32Exception naming only the file.
        public void JobObject_Start_BadArguments_AreRefused()
        {
            using var job = new JobObject();
            Check.Equal("fileName", Check.Throws<ArgumentException>(() => job.Start("   ")).ParamName);
            Check.Equal("workingDirectory", Check.Throws<ArgumentException>(() => job.Start("cmd.exe", "/c exit 0", "   ")).ParamName);
            string missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ToolBelt.NoSuchDir." + Guid.NewGuid().ToString("N"));
            Check.Throws<System.IO.DirectoryNotFoundException>(() => job.Start("cmd.exe", "/c exit 0", missing));
            Check.Equal("arguments", Check.Throws<ArgumentException>(() => job.Start("cmd.exe", "/c rem " + new string('x', 33000))).ParamName);
            // The longest command line still starts: "cmd.exe" quoted, a space, and the rest.
            // (cmd itself refuses a line over 8191 characters, so only the start is checked.)
            using var p = job.Start("cmd.exe", "/c rem " + new string('x', 32766 - 10 - 7));
            Check.True(p.WaitForExit(30000), "cmd exited");
        }

        // ---------- ClipboardUtils.DecodeDib: a header too short for its bit masks ----------

        // A 44-byte header with BI_BITFIELDS was accepted and the green mask's bytes decoded as the pixel.
        public void DecodeDib_BitfieldsHeaderBetween40And52_IsRefused()
        {
            byte[] Dib(int headerSize)
            {
                var dib = new byte[headerSize + 12 + 4 + 12];
                BitConverter.GetBytes(headerSize).CopyTo(dib, 0);
                BitConverter.GetBytes(1).CopyTo(dib, 4);
                BitConverter.GetBytes(1).CopyTo(dib, 8);
                BitConverter.GetBytes((short)1).CopyTo(dib, 12);
                BitConverter.GetBytes((short)32).CopyTo(dib, 14);
                BitConverter.GetBytes(3).CopyTo(dib, 16);                                       // BI_BITFIELDS
                BitConverter.GetBytes(0x00FF0000u).CopyTo(dib, 40);
                BitConverter.GetBytes(0x0000FF00u).CopyTo(dib, 44);
                BitConverter.GetBytes(0x000000FFu).CopyTo(dib, 48);
                return dib;
            }
            foreach (int size in new[] { 41, 44, 48, 51 })
                Check.Throws<InvalidOperationException>(() => ClipboardUtils.DecodeDib(Dib(size)));
            var ok = Dib(40);
            new byte[] { 1, 2, 3, 4 }.CopyTo(ok, 52);
            Check.Equal("1,2,3,255", string.Join(",", ClipboardUtils.DecodeDib(ok).Pixels));
            var v2 = Dib(52);
            new byte[] { 5, 6, 7, 8 }.CopyTo(v2, 52);
            Check.Equal("5,6,7,255", string.Join(",", ClipboardUtils.DecodeDib(v2).Pixels));
        }
    }
}
