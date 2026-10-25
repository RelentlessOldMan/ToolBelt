using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Round 15 review regressions for the Windows satellite.
    public sealed class WindowsReviewTests
    {
        // ---------- FileAssociation ----------

        public void FileAssociation_GetProgId_ReturnsAProgIdNotAPropertyList()
        {
            // ASSOCSTR 11 is the info-tip property list ("prop:System.ItemTypeText;..."); the ProgID is ASSOCSTR 20.
            foreach (string ext in new[] { ".txt", ".html", ".png" })
            {
                string? id = FileAssociation.GetProgId(ext);
                Check.True(id is null || (!id.StartsWith("prop:", StringComparison.OrdinalIgnoreCase) && id.IndexOf(';') < 0),
                    $"{ext} ProgID looks like a ProgID, got '{id}'");
            }
        }

        // ---------- DPI: capture and monitor bounds are physical pixels ----------

        private static MonitorDpi PrimaryPhysical()
        {
            foreach (var m in DpiInfo.Monitors()) if (m.IsPrimary) return m;
            throw new InvalidOperationException("No primary monitor.");
        }

        public void ScreenCapture_PrimaryScreen_IsTheWholePhysicalMonitor()
        {
            var before = DpiInfo.CurrentAwareness;
            var primary = PrimaryPhysical();
            var image = ScreenCapture.CapturePrimaryScreen();
            Check.Equal(primary.Width, image.Width, "capture width is the monitor's physical width");
            Check.Equal(primary.Height, image.Height, "capture height is the monitor's physical height");
            Check.Equal(before, DpiInfo.CurrentAwareness, "the thread's DPI awareness is restored");
        }

        public void MonitorInfo_Bounds_ArePhysicalPixels()
        {
            var primary = PrimaryPhysical();
            var m = MonitorInfo.Primary();
            Check.True(m.HasValue, "a primary monitor");
            Check.Equal(primary.Width, m!.Value.Bounds.Width, "width agrees with DpiInfo (physical)");
            Check.Equal(primary.Height, m.Value.Bounds.Height, "height agrees with DpiInfo (physical)");
        }

        // ---------- ResourceSampler ----------

        [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);

        public void ResourceSampler_NormalisesByTheMachinesCores_NotTheCallersAffinity()
        {
            // One busy thread is 1/cores of the machine. Environment.ProcessorCount is the caller's affinity, so a sampler
            // running pinned to one core (as under low.cmd, or in a job) reported ~100% for it.
            int cores = (int)GetActiveProcessorCount(0xFFFF);
            var sampler = ResourceSampler.ForCurrentProcess();
            var sw = Stopwatch.StartNew();
            double sink = 0;
            while (sw.ElapsedMilliseconds < 600) sink += Math.Sqrt(sw.ElapsedTicks);
            var sample = sampler.Sample();
            Check.True(sink > 0);
            double ceiling = 100.0 * 2 / cores + 10;
            Check.True(sample.CpuPercent <= ceiling, $"one busy thread on {cores} cores reads {sample.CpuPercent:0.0}% (ceiling {ceiling:0.0}%)");
        }

        // ---------- JobObject ----------

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Process StartChildInUnreferencedJob()
        {
            var job = new JobObject();
            var child = Process.Start(new ProcessStartInfo("ping.exe", "127.0.0.1 -n 30") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
            job.AssignProcess(child);
            return child;
        }

        public void JobObject_GarbageCollection_DoesNotKillTheChildren()
        {
            Process? child = null;
            try
            {
                child = StartChildInUnreferencedJob();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(300);
                Check.False(child.HasExited, "a collected (undisposed) job must not kill its children before the process exits");
            }
            finally
            {
                try { if (child is not null && !child.HasExited) child.Kill(); } catch { }
                child?.Dispose();
            }
        }

        // ---------- RegistryUtils ----------

        private static string TestKey() => @"Software\ToolBeltReviewTest\" + Guid.NewGuid().ToString("N");

        public void RegistryUtils_GetValue_UnsignedReadsHighBitValues()
        {
            string key = TestKey();
            try
            {
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "d", -1, RegistryValueKind.DWord);
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "q", long.MinValue, RegistryValueKind.QWord);
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "small", 5, RegistryValueKind.DWord);
                Check.Equal(uint.MaxValue, RegistryUtils.GetValue<uint>(RegistryHive.CurrentUser, key, "d", 7u));
                Check.Equal(0x8000000000000000UL, RegistryUtils.GetValue<ulong>(RegistryHive.CurrentUser, key, "q", 7ul));
                Check.Equal(5u, RegistryUtils.GetValue<uint>(RegistryHive.CurrentUser, key, "small", 7u));
            }
            finally { RegistryUtils.DeleteKey(RegistryHive.CurrentUser, key); }
        }

        public void RegistryUtils_GetValue_ConvertsStringsInvariantly()
        {
            string key = TestKey();
            var culture = CultureInfo.CurrentCulture;
            try
            {
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "s", "1.5", RegistryValueKind.String);
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Check.Equal(1.5, RegistryUtils.GetValue<double>(RegistryHive.CurrentUser, key, "s", -1.0));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
                RegistryUtils.DeleteKey(RegistryHive.CurrentUser, key);
            }
        }

        // ---------- WindowUtils ----------

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(int exStyle, string className, string title, uint style, int x, int y, int w, int h,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);

        public void WindowUtils_SameProcessWindowOnABlockedThread_DoesNotHang()
        {
            // GetWindowText sends WM_GETTEXT to a window of this process; its thread is blocked and not pumping.
            const string title = "ToolBelt review: blocked thread window";
            IntPtr hwnd = IntPtr.Zero;
            using var created = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var owner = new Thread(() =>
            {
                hwnd = CreateWindowExW(0, "STATIC", title, 0x80000000 /* WS_POPUP */, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                created.Set();
                release.Wait();
                if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
            });
            owner.SetApartmentState(ApartmentState.MTA);
            owner.IsBackground = true;
            owner.Start();
            created.Wait();
            try
            {
                Check.True(hwnd != IntPtr.Zero, "window created");
                var titleTask = Task.Run(() => WindowUtils.GetWindowTitle(hwnd));
                var listTask = Task.Run(() => WindowUtils.TopLevelWindows(visibleTitledOnly: false));
                bool titleDone = titleTask.Wait(3000), listDone = listTask.Wait(3000);
                release.Set();
                Check.True(titleDone, "GetWindowTitle returns while the owning thread is blocked");
                Check.True(listDone, "TopLevelWindows returns while a window's thread is blocked");
                Check.Equal(title, titleTask.Result);
                bool listed = false;
                foreach (var w in listTask.Result) if (w.Handle == hwnd) listed = w.Title == title;
                Check.True(listed, "the window is listed with its title");
            }
            finally
            {
                release.Set();
                owner.Join(10000);
            }
        }

        // ---------- SingleInstance / SingleInstanceApp ----------

        private static string NewAppId() => "TBReview." + Guid.NewGuid().ToString("N");

        private static T OnOtherThread<T>(Func<T> f)
        {
            T result = default!;
            Exception? error = null;
            var t = new Thread(() => { try { result = f(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            t.Start();
            t.Join();
            if (error is not null) throw new AggregateException(error);
            return result;
        }

        public void SingleInstanceApp_DisposeTwice_DoesNotThrow()
        {
            var app = SingleInstanceApp.Start(NewAppId(), Array.Empty<string>());
            app.Dispose();
            app.Dispose();
        }

        public void SingleInstanceApp_OverlongAppId_IsRejectedUpFront()
        {
            // The id goes into the pipe name with the session and SID; past the 256-character pipe-name limit the listener
            // could never be created and every second launch silently lost its arguments after a 5 s wait.
            var ex = Check.Throws<ArgumentException>(() => SingleInstanceApp.Start("TBReview." + new string('x', 200), Array.Empty<string>()));
            Check.Equal("appId", ex.ParamName);
            using var ok = SingleInstanceApp.Start(("TBReview." + Guid.NewGuid().ToString("N") + new string('y', 100)).Substring(0, 100), Array.Empty<string>());
            Check.True(ok.IsPrimary);
        }

        public void SingleInstanceApp_GateIsPerUser_ASessionWideNameDoesNotBlockIt()
        {
            // The pipe was per user but the gate mutex was session-wide: whoever held "Local\ToolBelt.SingleInstance.<id>"
            // (another account in this session) made every launch a secondary with no primary to forward to.
            string id = NewAppId();
            using var hold = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var squatter = new Thread(() =>
            {
                using var m = new Mutex(true, @"Local\ToolBelt.SingleInstance." + id);
                hold.Set();
                release.Wait();
                m.ReleaseMutex();
            }) { IsBackground = true };
            squatter.Start();
            hold.Wait();
            try
            {
                using var app = SingleInstanceApp.Start(id, Array.Empty<string>(), TimeSpan.FromMilliseconds(300));
                Check.True(app.IsPrimary, "a session-wide name held by someone else does not block this user's instance");
            }
            finally
            {
                release.Set();
                squatter.Join();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, int revision, out IntPtr sd, IntPtr size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateMutexW(ref SECURITY_ATTRIBUTES sa, bool initialOwner, string name);

        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr h);

        public void SingleInstance_NameHeldWithADenyingAcl_IsNotOwned_RatherThanThrowing()
        {
            // An elevated or other-user process's mutex can't be opened (access denied); TryAcquire promises an instance.
            string name = @"Local\ToolBelt.Windows.Tests.Denied." + Guid.NewGuid().ToString("N");
            Check.True(ConvertStringSecurityDescriptorToSecurityDescriptorW("D:", 1, out IntPtr sd, IntPtr.Zero), "SDDL");
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
            IntPtr h = CreateMutexW(ref sa, false, name);
            try
            {
                Check.True(h != IntPtr.Zero, "deny-all mutex created");
                using var gate = SingleInstance.TryAcquire(name);
                Check.False(gate.IsOwned, "someone else's inaccessible gate is not ours");
            }
            finally
            {
                if (h != IntPtr.Zero) CloseHandle(h);
                LocalFree(sd);
            }
        }

        public void SingleInstanceApp_Launches_AreDeliveredOneAtATimeInOrder()
        {
            string id = NewAppId();
            using var app = SingleInstanceApp.Start(id, Array.Empty<string>());
            Check.True(app.IsPrimary);
            var order = new List<string>();
            int running = 0, maxRunning = 0;
            using var all = new CountdownEvent(4);
            app.SecondInstanceStarted += (s, e) =>
            {
                int now = Interlocked.Increment(ref running);
                lock (order) { maxRunning = Math.Max(maxRunning, now); }
                Thread.Sleep(200);
                lock (order) order.Add(e.Args[0]);
                Interlocked.Decrement(ref running);
                all.Signal();
            };
            for (int i = 0; i < 4; i++)
            {
                string arg = "launch" + i;
                bool forwarded = OnOtherThread(() => { using var s = SingleInstanceApp.Start(id, new[] { arg }); return s.ForwardedToPrimary; });
                Check.True(forwarded, arg + " forwarded");
            }
            Check.True(all.Wait(10000), "all four delivered");
            Check.Equal(1, maxRunning, "handlers never overlap");
            Check.Equal("launch0,launch1,launch2,launch3", string.Join(",", order), "delivered in launch order");
        }

        public void SingleInstanceApp_MessageThePrimaryWouldReject_FailsFast()
        {
            string id = NewAppId();
            using var app = SingleInstanceApp.Start(id, Array.Empty<string>());
            var tooMany = new string[1001];
            for (int i = 0; i < tooMany.Length; i++) tooMany[i] = "a";
            foreach (var args in new[] { tooMany, new[] { new string('z', 40000) } })
            {
                var sw = Stopwatch.StartNew();
                bool forwarded = OnOtherThread(() => { using var s = SingleInstanceApp.Start(id, args); return s.ForwardedToPrimary; });
                sw.Stop();
                Check.False(forwarded, "the primary cannot accept it");
                Check.True(sw.ElapsedMilliseconds < 2000, $"fails fast instead of retrying until the timeout ({sw.ElapsedMilliseconds} ms)");
            }
        }

        // ---------- ShortcutUtils ----------

        public void ShortcutUtils_FieldsTheShellCannotRoundTrip_AreRejected_AndTheLongestThatFitRoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-review-lnk-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string lnk = Path.Combine(dir, "a.lnk");
                string target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
                string Long(char c, int n) => @"C:\" + new string(c, n - 3);

                // The longest values the shell stores and reads back intact.
                string args = new string('a', 259) + "Z", wd = Long('w', 260), desc = new string('d', 259) + "Z", icon = Long('i', 260);
                ShortcutUtils.Create(lnk, target, args, wd, desc, icon, 3);
                var info = ShortcutUtils.Read(lnk);
                Check.Equal(args, info.Arguments, "260-char arguments");
                Check.Equal(wd, info.WorkingDirectory, "260-char working directory");
                Check.Equal(desc, info.Description, "260-char description");
                Check.Equal(icon, info.IconPath, "260-char icon path");

                // One more character was silently truncated or garbled on read (or, for the target, an opaque E_FAIL).
                Check.Equal("arguments", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target, arguments: new string('a', 261))).ParamName);
                Check.Equal("workingDirectory", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target, workingDirectory: Long('w', 261))).ParamName);
                Check.Equal("description", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target, description: new string('d', 261))).ParamName);
                Check.Equal("iconPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target, iconPath: Long('i', 261))).ParamName);
                Check.Equal("targetPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, Long('t', 300))).ParamName);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---------- EventLogWriter ----------

        public void EventLogWriter_LogAndSourceNames_CannotEscapeTheirRegistryKey()
        {
            // "...\EventLog\{log}\{source}" with an empty log collapsed to "...\EventLog\{source}": UnregisterSource("Application",
            // log: "") would delete the whole Application log key.
            Check.Equal("log", Check.Throws<ArgumentException>(() => EventLogWriter.IsSourceRegistered("Application", log: "")).ParamName);
            Check.Equal("log", Check.Throws<ArgumentException>(() => EventLogWriter.UnregisterSource("Application", log: "")).ParamName);
            Check.Equal("log", Check.Throws<ArgumentException>(() => EventLogWriter.RegisterSource("x", log: @"Application\..")).ParamName);
            Check.Equal("source", Check.Throws<ArgumentException>(() => EventLogWriter.IsSourceRegistered(@"a\b")).ParamName);
            Check.Equal("source", Check.Throws<ArgumentException>(() => EventLogWriter.UnregisterSource(@"x\..\..")).ParamName);
        }

        // ---------- RunElevated (no UAC: the command line is built by helpers) ----------

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string cmdLine, out int argc);

        private static string[] ParseCommandLine(string cmdLine)
        {
            IntPtr argv = CommandLineToArgvW(cmdLine, out int argc);
            try
            {
                var result = new string[argc];
                for (int i = 0; i < argc; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
                return result;
            }
            finally { LocalFree(argv); }
        }

        public void RunElevated_JoinArguments_SurvivesTheCommandLineParser()
        {
            var join = typeof(RunElevated).GetMethod("JoinArguments", BindingFlags.Public | BindingFlags.Static);
            Check.NotNull(join, "RunElevated.JoinArguments exists (a raw string had no safe way to carry paths with spaces)");
            var args = new[] { "plain", "with space", "quote\"inside", @"trail\", @"C:\Program Files\x\", "", @"a\\""b", "tab\there", @"\\server\share" };
            string joined = (string)join!.Invoke(null, new object[] { args })!;
            var parsed = ParseCommandLine("app.exe " + joined);
            Check.Equal(args.Length + 1, parsed.Length, "argument count: " + joined);
            for (int i = 0; i < args.Length; i++) Check.Equal(args[i], parsed[i + 1], $"argument {i}: {joined}");
        }

        public void RunElevated_RestartUnderTheDotnetHost_RelaunchesTheApp_NotTheBareHost()
        {
            var m = typeof(RunElevated).GetMethod("RestartCommand", BindingFlags.NonPublic | BindingFlags.Static);
            Check.NotNull(m, "the restart command is computed by RestartCommand");
            var host = ((string, string))m!.Invoke(null, new object?[] { @"C:\Program Files\dotnet\dotnet.exe", @"C:\apps\My App.dll", "--x" })!;
            Check.Equal(@"C:\Program Files\dotnet\dotnet.exe", host.Item1);
            Check.Equal("\"C:\\apps\\My App.dll\" --x", host.Item2);
            var apphost = ((string, string))m.Invoke(null, new object?[] { @"C:\apps\app.exe", @"C:\apps\app.dll", "--x" })!;
            Check.Equal(@"C:\apps\app.exe", apphost.Item1);
            Check.Equal("--x", apphost.Item2);
        }

        // ---------- StorageDeviceInfo ----------

        public void StorageDeviceInfo_DevicePathPrefixes_AreLocalPaths()
        {
            string drive = Path.GetPathRoot(Environment.SystemDirectory)!.Substring(0, 2);   // "C:"
            var plain = StorageDeviceInfo.Describe(drive + @"\Windows");
            var verbatim = StorageDeviceInfo.Describe(@"\\?\" + drive + @"\Windows");
            var device = StorageDeviceInfo.Describe(@"\\.\" + drive);
            Check.Equal(plain.BusType, verbatim.BusType, @"\\?\ path");
            Check.Equal(plain.BusType, device.BusType, @"\\.\ path");
        }
    }
}
