using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    /// <summary>PrivilegeScope, PowerScheme, DpiInfo, StorageDeviceInfo, JobObject limits, clipboard files/images, SingleInstanceApp.</summary>
    public sealed class WindowsWaveTests
    {
        // ---------- privileges ----------

        public void Privilege_EnableAndRestore()
        {
            const string name = "SeShutdownPrivilege";                               // held by ordinary users, disabled by default
            if (!PrivilegeScope.IsHeld(name)) return;
            bool before = PrivilegeScope.IsEnabled(name);
            using (var scope = PrivilegeScope.Enable(name))
            {
                Check.True(PrivilegeScope.IsEnabled(name), "enabled inside the scope");
                Check.Equal(before, scope.WasAlreadyEnabled);
            }
            Check.Equal(before, PrivilegeScope.IsEnabled(name));
            Check.True(PrivilegeScope.List().Any(p => p.Name == "SeChangeNotifyPrivilege" && p.Enabled), "bypass-traverse is always enabled");
        }

        public void Privilege_NotHeldOrUnknown()
        {
            if (!PrivilegeScope.IsHeld("SeTcbPrivilege"))                            // "act as part of the OS": not held outside SYSTEM
                Check.Throws<UnauthorizedAccessException>(() => PrivilegeScope.Enable("SeTcbPrivilege"));
            Check.Throws<ArgumentException>(() => PrivilegeScope.Enable("SeNoSuchPrivilege"));
        }

        // ---------- power plans ----------

        public void Power_ActivePlanIsListed()
        {
            PowerPlan active = PowerScheme.Active();
            Check.True(active.Name.Length > 0 && active.Id != Guid.Empty);
            var all = PowerScheme.All();
            Check.Equal(1, all.Count(p => p.IsActive));
            Check.Equal(active.Id, all.Single(p => p.IsActive).Id);
            // Deliberately no plan switch: tests must not change the machine's power settings. Use() with the active plan is a no-op.
            using (PowerScheme.Use(active.Id)) Check.Equal(active.Id, PowerScheme.ActiveId());
            Check.Equal(active.Id, PowerScheme.ActiveId());
        }

        // ---------- DPI ----------

        public void Dpi_MonitorsAndAwarenessScope()
        {
            var monitors = DpiInfo.Monitors();
            Check.True(monitors.Count >= 1);
            Check.Equal(1, monitors.Count(m => m.IsPrimary));
            foreach (var m in monitors) Check.True(m.DpiX >= 72 && m.DpiX <= 960 && m.Width > 0, m.ToString());
            Check.True(DpiInfo.SystemDpi >= 96);

            DpiAwareness before = DpiInfo.CurrentAwareness;
            using (DpiInfo.AwarenessScope(DpiAwareness.PerMonitorV2)) Check.Equal(DpiAwareness.PerMonitorV2, DpiInfo.CurrentAwareness);
            using (DpiInfo.AwarenessScope(DpiAwareness.Unaware)) Check.Equal(DpiAwareness.Unaware, DpiInfo.CurrentAwareness);
            Check.Equal(before, DpiInfo.CurrentAwareness);
            Check.Equal(150, DpiInfo.Scale(100, 144));
            Check.Equal(25, DpiInfo.Scale(20, 120));
        }

        // ---------- storage ----------

        public void Storage_SystemDriveIsDescribed()
        {
            string sys = Path.GetPathRoot(Environment.SystemDirectory)!;
            StorageDevice d = StorageDeviceInfo.Describe(sys);
            Check.Equal(sys.TrimEnd('\\'), d.Volume);
            Check.True(d.BusType != StorageBusType.Unknown, d.ToString());
            Check.False(d.IsExternal, "the system drive is internal: " + d);
            Check.Equal(d.BusType, StorageDeviceInfo.Describe(Environment.SystemDirectory).BusType);   // any path on the volume
            Check.Throws<ArgumentException>(() => StorageDeviceInfo.Describe(@"\\server\share\x"));
        }

        public void Storage_UncPaths()
        {
            Check.Null(StorageDeviceInfo.GetUncPath(Environment.SystemDirectory));
            Check.False(StorageDeviceInfo.IsNetworkDrive(Environment.SystemDirectory));
            Check.Equal(@"\\server\share\dir\f.txt", StorageDeviceInfo.GetUncPath(@"\\server\share\dir\f.txt"));
            // If this machine has a mapped drive, it must resolve to a UNC path (we never create mappings in tests).
            foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Network))
            {
                string? unc = StorageDeviceInfo.GetUncPath(drive.Name);
                Check.True(unc != null && unc.StartsWith(@"\\", StringComparison.Ordinal), $"{drive.Name} -> {unc}");
                Check.True(StorageDeviceInfo.IsNetworkDrive(drive.Name));
            }
        }

        // ---------- job object limits ----------

        private static Process StartPowerShell(string command)
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"" + command + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            return Process.Start(psi)!;
        }

        public void Job_ProcessMemoryLimitStopsABigAllocation()
        {
            using var job = new JobObject();
            job.SetProcessMemoryLimit(400L << 20);
            // ErrorActionPreference=Stop: PowerShell otherwise reports the OutOfMemoryException and carries on.
            using var p = StartPowerShell("$ErrorActionPreference = 'Stop'; Start-Sleep -Milliseconds 800; $a = New-Object byte[] 1500MB; 'allocated'");
            job.AssignProcess(p);
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Check.True(p.WaitForExit(30000), "child finished");
            Check.True(output.Contains("OutOfMemoryException"), "the allocation must fail: " + output);
            Check.False(output.Contains("allocated"), "a 1.5 GB array must not fit under a 400 MB cap: " + output);
            JobAccounting acct = job.QueryAccounting();
            Check.True(acct.TotalProcesses >= 1 && acct.PeakProcessMemoryBytes > 0 && acct.PeakProcessMemoryBytes <= (400L << 20) + (64L << 20),
                $"peak {acct.PeakProcessMemoryBytes}");
        }

        public void Job_CpuRateCapThrottlesABusyLoop()
        {
            using var job = new JobObject();
            job.SetCpuRateLimit(1);                                                    // 1% of the whole machine
            using var p = StartPowerShell("$end = (Get-Date).AddSeconds(3); while ((Get-Date) -lt $end) { }");
            job.AssignProcess(p);
            Thread.Sleep(2000);
            var cpu = job.QueryAccounting().TotalCpuTime;
            p.Kill();
            p.WaitForExit(10000);
            // An uncapped busy loop would burn ~2 s of CPU in 2 s; 1% of N cores allows 0.02·N·2 s.
            double allowed = 0.01 * Environment.ProcessorCount * 2.0;
            Check.True(cpu.TotalSeconds < Math.Max(0.6, allowed * 3), $"CPU {cpu.TotalSeconds:F2}s with a 1% cap on {Environment.ProcessorCount} cores");
            Check.Throws<ArgumentOutOfRangeException>(() => job.SetCpuRateLimit(0));
            job.SetCpuRateLimit(null);                                                 // removing the cap is allowed
        }

        public void Job_ActiveProcessLimitAndKillOnCloseSurvive()
        {
            Process? a = null, b = null;
            try
            {
                using var job = new JobObject(killOnClose: true);
                job.SetActiveProcessLimit(1);
                a = Process.Start(new ProcessStartInfo("ping.exe", "127.0.0.1 -n 30") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
                b = Process.Start(new ProcessStartInfo("ping.exe", "127.0.0.1 -n 30") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
                job.AssignProcess(a);
                Check.Throws<Win32Exception>(() => job.AssignProcess(b));             // over the limit
                Check.Equal(1, job.QueryAccounting().ActiveProcesses);
                job.Dispose();
                Check.True(a.WaitForExit(10000), "kill-on-close still applies after setting other limits");
            }
            finally
            {
                foreach (var p in new[] { a, b }) { try { if (p != null && !p.HasExited) p.Kill(); } catch { } p?.Dispose(); }
            }
        }

        // ---------- clipboard: files and images ----------

        public void Dib_DecodesBottomUp24AndTopDownAlpha32()
        {
            // 2x2, 24-bit, bottom-up: stored rows are bottom then top, each padded to 8 bytes.
            var dib = new byte[40 + 16];
            BitConverter.GetBytes(40).CopyTo(dib, 0); BitConverter.GetBytes(2).CopyTo(dib, 4); BitConverter.GetBytes(2).CopyTo(dib, 8);
            BitConverter.GetBytes((short)1).CopyTo(dib, 12); BitConverter.GetBytes((short)24).CopyTo(dib, 14);
            byte[] bottom = { 1, 2, 3, 4, 5, 6, 0, 0 }, top = { 7, 8, 9, 10, 11, 12, 0, 0 };
            bottom.CopyTo(dib, 40); top.CopyTo(dib, 48);
            ScreenImage img = ClipboardUtils.DecodeDib(dib);
            Check.Equal("7,8,9,255,10,11,12,255,1,2,3,255,4,5,6,255", string.Join(",", img.Pixels));

            // 1x2, 32-bit BI_BITFIELDS with an alpha mask (V4-style 108-byte header), top-down.
            var v4 = new byte[108 + 8];
            BitConverter.GetBytes(108).CopyTo(v4, 0); BitConverter.GetBytes(1).CopyTo(v4, 4); BitConverter.GetBytes(-2).CopyTo(v4, 8);
            BitConverter.GetBytes((short)1).CopyTo(v4, 12); BitConverter.GetBytes((short)32).CopyTo(v4, 14); BitConverter.GetBytes(3).CopyTo(v4, 16);
            BitConverter.GetBytes(0x00FF0000).CopyTo(v4, 40); BitConverter.GetBytes(0x0000FF00).CopyTo(v4, 44);
            BitConverter.GetBytes(0x000000FF).CopyTo(v4, 48); BitConverter.GetBytes(unchecked((int)0xFF000000)).CopyTo(v4, 52);
            new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 }.CopyTo(v4, 108);
            Check.Equal("10,20,30,40,50,60,70,80", string.Join(",", ClipboardUtils.DecodeDib(v4).Pixels));
            Check.Throws<NotSupportedException>(() => ClipboardUtils.DecodeDib(WithBpp(dib, 8)));
        }

        private static byte[] WithBpp(byte[] dib, short bpp)
        {
            var copy = (byte[])dib.Clone();
            BitConverter.GetBytes(bpp).CopyTo(copy, 14);
            return copy;
        }

        // Runs a clipboard test only when the user's clipboard holds nothing or only text (which is restored afterwards).
        private static void WithDisposableClipboard(Action body)
        {
            string? saved;
            try
            {
                if (ClipboardUtils.ContainsImage() || ClipboardUtils.ContainsFileDropList()) return;   // don't clobber real content
                saved = ClipboardUtils.GetText();
            }
            catch (Win32Exception) { return; }
            try { body(); }
            catch (Win32Exception) { return; }                                         // clipboard contended: inconclusive
            finally
            {
                try { if (saved != null) ClipboardUtils.SetText(saved); else ClipboardUtils.Clear(); } catch (Win32Exception) { }
            }
        }

        public void Clipboard_FileListRoundTrip()
        {
            WithDisposableClipboard(() =>
            {
                string[] files = { @"C:\Windows\notepad.exe", Path.Combine(Path.GetTempPath(), "ünïcode name.txt") };
                ClipboardUtils.SetFileDropList(files);
                Check.True(ClipboardUtils.ContainsFileDropList());
                Check.Equal(string.Join("|", files), string.Join("|", ClipboardUtils.GetFileDropList()!));
            });
            Check.Throws<ArgumentException>(() => ClipboardUtils.SetFileDropList(Array.Empty<string>()));
        }

        public void Clipboard_ImageRoundTrip()
        {
            WithDisposableClipboard(() =>
            {
                var px = new byte[3 * 2 * 4];
                for (int i = 0; i < px.Length; i++) px[i] = (byte)(i * 9);
                for (int i = 3; i < px.Length; i += 4) px[i] = 255;                    // opaque (CF_DIB has no defined alpha)
                ClipboardUtils.SetImage(new ScreenImage(3, 2, px));
                Check.True(ClipboardUtils.ContainsImage());
                ScreenImage back = ClipboardUtils.GetImage()!.Value;
                Check.Equal(3, back.Width);
                Check.Equal(2, back.Height);
                for (int i = 0; i < px.Length; i++) if (i % 4 != 3) Check.Equal(px[i], back.Pixels[i], $"byte {i}");
            });
        }

        // ---------- single-instance handoff ----------

        public void SingleInstance_SecondLaunchForwardsArguments()
        {
            string id = "ToolBeltTest." + Guid.NewGuid().ToString("N");
            using var primary = SingleInstanceApp.Start(id, new[] { "first" });
            Check.True(primary.IsPrimary);
            var received = new TaskCompletionSource<SecondInstanceEventArgs>();
            primary.SecondInstanceStarted += (_, e) => received.TrySetResult(e);

            // A second "launch" on another thread (the mutex is per thread, so this behaves like another process).
            var second = Task.Run(() =>
            {
                using var app = SingleInstanceApp.Start(id, new[] { "open", "file with spaces.txt", "ünï" });
                return (app.IsPrimary, app.ForwardedToPrimary);
            }).Result;
            Check.False(second.IsPrimary);
            Check.True(second.ForwardedToPrimary);
            Check.True(received.Task.Wait(5000), "primary was notified");
            var e = received.Task.Result;
            Check.Equal("open|file with spaces.txt|ünï", string.Join("|", e.Args));
            Check.Equal(Environment.ProcessId, e.ProcessId);
            Check.Equal(Environment.CurrentDirectory, e.WorkingDirectory);
        }

        // ---------- review regressions ----------

        private static string PipeNameFor(string id)
            => "ToolBelt.SingleInstance." + id + "." + Process.GetCurrentProcess().SessionId + "."
               + (System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

        private static (bool IsPrimary, bool Forwarded) LaunchSecond(string id, string[] args, TimeSpan? timeout = null)
            => Task.Run(() =>
            {
                using var app = SingleInstanceApp.Start(id, args, timeout);
                return (app.IsPrimary, app.ForwardedToPrimary);
            }).Result;

        public void SingleInstance_SurvivesMalformedAndSilentClients()
        {
            string id = "ToolBeltTest." + Guid.NewGuid().ToString("N");
            using var primary = SingleInstanceApp.Start(id, Array.Empty<string>());
            var got = new System.Collections.Concurrent.ConcurrentQueue<string>();
            primary.SecondInstanceStarted += (_, e) => got.Enqueue(string.Join("|", e.Args));

            // A client that sends garbage (huge length prefix, bad bytes).
            using (var bad = new System.IO.Pipes.NamedPipeClientStream(".", PipeNameFor(id), System.IO.Pipes.PipeDirection.InOut))
            {
                bad.Connect(5000);
                var pid = new byte[4];
                bad.ReadExactly(pid);
                try
                {
                    bad.Write(new byte[] { 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF });
                    bad.Flush();
                }
                catch (IOException) { /* the primary may hang up on us mid-write: that's the point */ }
            }
            var r1 = LaunchSecond(id, new[] { "after-garbage" });
            Check.True(r1.Forwarded, "the listener must survive a malformed client");

            // A client that connects and never speaks: later launches still get through (per-connection timeout).
            using (var silent = new System.IO.Pipes.NamedPipeClientStream(".", PipeNameFor(id), System.IO.Pipes.PipeDirection.InOut))
            {
                silent.Connect(5000);
                var r2 = LaunchSecond(id, new[] { "after-silent" }, TimeSpan.FromSeconds(15));
                Check.True(r2.Forwarded, "a silent client must not block later launches for good");
            }
            for (int i = 0; i < 50 && got.Count < 2; i++) Thread.Sleep(100);
            Check.Equal("after-garbage,after-silent", string.Join(",", got));
        }

        public void SingleInstance_LaunchBeforeSubscribeIsDelivered()
        {
            string id = "ToolBeltTest." + Guid.NewGuid().ToString("N");
            using var primary = SingleInstanceApp.Start(id, Array.Empty<string>());
            var r = LaunchSecond(id, new[] { "early" });                               // before anyone subscribed
            Check.True(r.Forwarded);
            var received = new TaskCompletionSource<string>();
            primary.SecondInstanceStarted += (_, e) => received.TrySetResult(string.Join("|", e.Args));
            Check.True(received.Task.Wait(5000), "the queued launch was delivered on subscribe");
            Check.Equal("early", received.Task.Result);
        }

        public void Dib_HostileHeadersAreRejected()
        {
            byte[] Header(int w, int h, short bpp, int compression, int clrUsed, int extra = 64)
            {
                var b = new byte[40 + extra];
                BitConverter.GetBytes(40).CopyTo(b, 0); BitConverter.GetBytes(w).CopyTo(b, 4); BitConverter.GetBytes(h).CopyTo(b, 8);
                BitConverter.GetBytes((short)1).CopyTo(b, 12); BitConverter.GetBytes(bpp).CopyTo(b, 14);
                BitConverter.GetBytes(compression).CopyTo(b, 16); BitConverter.GetBytes(clrUsed).CopyTo(b, 32);
                return b;
            }
            Check.Throws<InvalidOperationException>(() => ClipboardUtils.DecodeDib(Header(1 << 27, 1, 32, 0, 0)));   // stride overflow
            Check.Throws<InvalidOperationException>(() => ClipboardUtils.DecodeDib(Header(1 << 16, 1 << 16, 32, 0, 0)));   // claims 16 GB
            Check.Throws<InvalidOperationException>(() => ClipboardUtils.DecodeDib(Header(2, 2, 32, 0, -5)));
            Check.Throws<InvalidOperationException>(() => ClipboardUtils.DecodeDib(Header(2, int.MinValue, 32, 0, 0)));
            var rgba = Header(1, 1, 32, 3, 0);
            BitConverter.GetBytes(0x000000FF).CopyTo(rgba, 40);                         // red in the low byte: RGBA order
            BitConverter.GetBytes(0x0000FF00).CopyTo(rgba, 44);
            BitConverter.GetBytes(0x00FF0000).CopyTo(rgba, 48);
            Check.Throws<NotSupportedException>(() => ClipboardUtils.DecodeDib(rgba));
            var bgr40 = Header(1, 1, 32, 3, 0);                                         // 40-byte header + BGR masks + 1 pixel
            BitConverter.GetBytes(0x00FF0000).CopyTo(bgr40, 40); BitConverter.GetBytes(0x0000FF00).CopyTo(bgr40, 44);
            BitConverter.GetBytes(0x000000FF).CopyTo(bgr40, 48);
            new byte[] { 9, 8, 7, 6 }.CopyTo(bgr40, 52);
            Check.Equal("9,8,7,255", string.Join(",", ClipboardUtils.DecodeDib(bgr40).Pixels));
        }

        public void Unc_DevicePathsAreLocal()
        {
            Check.Null(StorageDeviceInfo.GetUncPath(@"\\?\" + Environment.SystemDirectory));
            Check.Null(StorageDeviceInfo.GetUncPath(@"\\.\" + Environment.SystemDirectory));
            Check.Equal(@"\\server\share\x", StorageDeviceInfo.GetUncPath(@"\\?\UNC\server\share\x"));
            Check.False(StorageDeviceInfo.IsNetworkDrive(@"\\?\" + Environment.SystemDirectory));
        }

        public void SingleInstance_NoPrimaryListening_TimesOutQuickly()
        {
            string id = "ToolBeltTest." + Guid.NewGuid().ToString("N");
            using var gate = SingleInstance.TryAcquire(@"Local\ToolBelt.SingleInstance." + id);   // holds the name but serves no pipe
            var sw = Stopwatch.StartNew();
            var r = Task.Run(() =>
            {
                using var app = SingleInstanceApp.Start(id, new[] { "x" }, TimeSpan.FromMilliseconds(400));
                return (app.IsPrimary, app.ForwardedToPrimary);
            }).Result;
            Check.False(r.IsPrimary);
            Check.False(r.ForwardedToPrimary);
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"took {sw.Elapsed}");
            Check.Throws<ArgumentException>(() => SingleInstanceApp.Start(@"bad\id", Array.Empty<string>()));
        }
    }
}
