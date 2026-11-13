using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 65 (Windows satellite, pass 4) regressions.
    public sealed class WindowsReview4Tests
    {
        // ---------- ClipboardUtils: file lists ----------

        // DecodeDropFiles is new; reached by reflection so this file also builds against the code before it.
        private static string[] Decode(byte[] block)
        {
            MethodInfo m = typeof(ClipboardUtils).GetMethod("DecodeDropFiles", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("ClipboardUtils.DecodeDropFiles is missing.");
            try { return (string[])m.Invoke(null, new object[] { block })!; }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        }

        private static byte[] DropBlock(uint pFiles, bool wide, byte[] names)
        {
            var block = new byte[20 + names.Length];
            BitConverter.GetBytes(pFiles).CopyTo(block, 0);
            BitConverter.GetBytes(wide ? 1 : 0).CopyTo(block, 16);
            names.CopyTo(block, 20);
            return block;
        }

        public void DecodeDropFiles_ReadsWideNames()
        {
            byte[] names = Encoding.Unicode.GetBytes("C:\\a.txt\0D:\\b \u00e9\uD83D\uDE00.bin\0\0");
            string[] files = Decode(DropBlock(20, true, names));
            Check.Equal(2, files.Length);
            Check.Equal("C:\\a.txt", files[0]);
            Check.Equal("D:\\b \u00e9\uD83D\uDE00.bin", files[1]);
        }

        public void DecodeDropFiles_ReadsAnsiNames()
        {
            string[] files = Decode(DropBlock(20, false, Encoding.ASCII.GetBytes("C:\\x\0C:\\y\0\0")));
            Check.Equal(2, files.Length);
            Check.Equal("C:\\y", files[1]);
        }

        public void DecodeDropFiles_AnOffsetOutsideTheBlock_Throws()
        {
            byte[] names = Encoding.Unicode.GetBytes("C:\\a\0\0");
            Check.Throws<InvalidOperationException>(() => Decode(DropBlock(0x7FFF0000, true, names)));
            Check.Throws<InvalidOperationException>(() => Decode(DropBlock(4, true, names)));   // inside the header
            Check.Throws<InvalidOperationException>(() => Decode(new byte[12]));                  // truncated header
            Check.Throws<ArgumentNullException>(() => Decode(null!));
        }

        public void DecodeDropFiles_AMissingTerminator_StaysInTheBlock()
        {
            // No closing NULs at all, and an odd trailing byte: the last name ends at the block's end.
            byte[] names = Encoding.Unicode.GetBytes("C:\\a\0C:\\bc");
            Array.Resize(ref names, names.Length + 1);
            names[names.Length - 1] = (byte)'z';
            string[] files = Decode(DropBlock(20, true, names));
            Check.Equal(2, files.Length);
            Check.Equal("C:\\bc", files[1]);
        }

        public void GetFileDropList_AHostileOffset_ThrowsInsteadOfReadingOutOfBounds()
        {
            // Any process can put this on the clipboard. DragQueryFile followed pFiles ~2 GB past the block (an access
            // violation in this process, or reading whatever memory lay there).
            RunWithClipboard(() =>
            {
                byte[] names = Encoding.Unicode.GetBytes("C:\\a\0\0");
                PutRaw(15 /* CF_HDROP */, DropBlock(0x7FFF0000, true, names));
                Check.Throws<InvalidOperationException>(() => ClipboardUtils.GetFileDropList());
            });
        }

        // ---------- ClipboardUtils: images ----------

        public void SetImage_ASideOverTheReadLimit_Throws()
        {
            // 65537 x 1 went on the clipboard, then GetImage refused to read it back. Validation only: no clipboard needed.
            var wide = new ScreenImage(65537, 1, new byte[65537 * 4]);
            Check.Equal("image", Check.Throws<ArgumentException>(() => ClipboardUtils.SetImage(wide)).ParamName);
            var tall = new ScreenImage(1, 65537, new byte[65537 * 4]);
            Check.Equal("image", Check.Throws<ArgumentException>(() => ClipboardUtils.SetImage(tall)).ParamName);
        }

        // ---------- SingleInstance / SingleInstanceApp ----------

        public void SingleInstance_LocalPrefix_Works_ButAStrayBackslashThrows()
        {
            string id = Guid.NewGuid().ToString("N");
            using (var gate = SingleInstance.TryAcquire(@"Local\TbR65." + id)) Check.True(gate.IsOwned);
            int session;
            using (var self = Process.GetCurrentProcess()) session = self.SessionId;
            using (var gate = SingleInstance.TryAcquire(@"Session\" + session + @"\TbR65." + id)) Check.True(gate.IsOwned);
            // The mutex threw DirectoryNotFoundException (an IOException, not an argument error) for a name like this.
            Check.Equal("name", Check.Throws<ArgumentException>(() => SingleInstance.TryAcquire(@"TbR65\" + id)).ParamName);
            Check.Equal("name", Check.Throws<ArgumentException>(() => SingleInstance.TryAcquire(@"Local\a\" + id)).ParamName);
            // The prefixes are case-sensitive in the kernel: "local\x" named a missing directory too.
            Check.Equal("name", Check.Throws<ArgumentException>(() => SingleInstance.TryAcquire(@"local\" + id)).ParamName);
        }

        public void SingleInstanceApp_AControlCharacterInTheAppId_Throws()
        {
            Check.Equal("appId", Check.Throws<ArgumentException>(() => SingleInstanceApp.Start("TbR65\0x", Array.Empty<string>())).ParamName);
            Check.Equal("appId", Check.Throws<ArgumentException>(() => SingleInstanceApp.Start("TbR65\nx", Array.Empty<string>())).ParamName);
        }

        public void SingleInstanceApp_ALoneSurrogate_IsForwardedIntact()
        {
            // A Windows file name may hold an unpaired surrogate; UTF-8 on the wire turned it into U+FFFD, naming another file.
            string id = "TbR65." + Guid.NewGuid().ToString("N").Substring(0, 12);
            using var primary = SingleInstanceApp.Start(id, Array.Empty<string>());
            Check.True(primary.IsPrimary);
            string arg = "C:\\odd\uD800name.txt";
            string? received = null;
            using var got = new ManualResetEventSlim();
            primary.SecondInstanceStarted += (_, e) => { received = e.Args.Count > 0 ? e.Args[0] : ""; got.Set(); };
            var t = new Thread(() => { using var s = SingleInstanceApp.Start(id, new[] { arg }, TimeSpan.FromSeconds(10)); });
            t.Start();
            Check.True(t.Join(20000), "the second launch hung");
            Check.True(got.Wait(10000), "nothing arrived");
            Check.Equal(arg, received);
        }

        // ---------- EventLogWriter ----------

        public void EventLogWriter_ANegativeCategoryOrUnknownKind_Throws()
        {
            // An unregistered source opens unelevated (events go to the Application log); validation throws before any write.
            using var log = new EventLogWriter("ToolBeltReviewProbe-" + Guid.NewGuid().ToString("N"));
            Check.Equal("category", Check.Throws<ArgumentOutOfRangeException>(() => log.Write("x", category: -1)).ParamName);
            Check.Equal("kind", Check.Throws<ArgumentOutOfRangeException>(() => log.Write("x", (EventLogEntryKind)99)).ParamName);
        }

        public void EventLogWriter_IsSourceRegistered_AnOverlongName_IsFalse()
        {
            // OpenSubKey threw ArgumentException for a key name over 255 characters; no such source can be registered.
            Check.False(EventLogWriter.IsSourceRegistered(new string('s', 300)));
            Check.False(EventLogWriter.IsSourceRegistered("ToolBeltReviewProbe", new string('l', 300)));
        }

        // ---------- JobObject ----------

        public void JobObject_Start_AnEmptyWorkingDirectory_MeansTheCurrentOne()
        {
            // CreateProcess was handed "" as the directory and failed (ERROR_INVALID_NAME); Process.Start treats it as unset.
            using var job = new JobObject();
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            using Process p = job.Start(cmd, "/c exit 7", "");
            Check.True(p.WaitForExit(20000), "child did not exit");
            Check.Equal(7, p.ExitCode);
            Check.Equal("workingDirectory", Check.Throws<ArgumentException>(() => job.Start(cmd, null, "C:\\\0x")).ParamName);
        }

        // ---------- PrivilegeScope ----------

        public void PrivilegeScope_AnUnknownName_NamesTheParameter()
        {
            var ex = Check.Throws<ArgumentException>(() => PrivilegeScope.Enable("SeNoSuchPrivilegeTbR65"));
            Check.Equal("privilegeName", ex.ParamName);
            Check.Equal("privilegeName", Check.Throws<ArgumentException>(() => PrivilegeScope.IsHeld("SeNoSuchPrivilegeTbR65")).ParamName);
        }

        // ---------- ToString on default structs ----------

        public void ToString_OnDefaultStructs_DoesNotThrow()
        {
            Check.Equal("0x0 (0 bytes BGRA)", default(ScreenImage).ToString());
            Check.Equal("", default(ShortcutInfo).ToString());
            Check.True(default(FixedDrive).ToString().Contains("no label"), "default drive");
        }

        // ---------- ScreenCapture ----------

        public void CaptureRegion_JustUnderInt32MaxOver4_IsRefusedUpFront()
        {
            // int.MaxValue / 4 let through regions whose buffer (w*h*4) is over Array.MaxLength, so the pixel array failed
            // late; the cap is now Array.MaxLength / 4.
            int w = Array.MaxLength / 4 + 4;
            Check.True((long)w * 4 <= int.MaxValue, "test setup");
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => ScreenCapture.CaptureRegion(0, 0, w, 1)).ParamName);
        }

        // ---------- ResourceSampler ----------

        public void ResourceSampler_IsDisposable_AndForCurrentProcessOwnsItsProcess()
        {
            // ForCurrentProcess opened a Process the sampler never released.
            Check.True(typeof(IDisposable).IsAssignableFrom(typeof(ResourceSampler)), "not IDisposable");
            var sampler = ResourceSampler.ForCurrentProcess();
            ((IDisposable)(object)sampler).Dispose();
            // A caller's own process is left alone.
            using var self = Process.GetCurrentProcess();
            var borrowed = new ResourceSampler(self);
            ((IDisposable)(object)borrowed).Dispose();
            Check.True(self.Id > 0 && self.ProcessName.Length > 0, "the caller's process was disposed");
        }

        public void ResourceSampler_Measure_EndsAtOnceWhenCancelled()
        {
            // Measure slept the whole interval in Thread.Sleep; it now waits on the token. Found by reflection: the token
            // parameter is new.
            MethodInfo m = typeof(ResourceSampler).GetMethod("Measure", new[] { typeof(Process), typeof(TimeSpan), typeof(CancellationToken) })
                ?? throw new InvalidOperationException("Measure takes no CancellationToken.");
            using var self = Process.GetCurrentProcess();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var sw = Stopwatch.StartNew();
            var ex = Check.Throws<TargetInvocationException>(() => m.Invoke(null, new object[] { self, TimeSpan.FromMinutes(10), cts.Token }));
            Check.True(ex.InnerException is OperationCanceledException, "not cancelled: " + ex.InnerException?.GetType().Name);
            Check.True(sw.ElapsedMilliseconds < 10000, $"took {sw.ElapsedMilliseconds} ms");
            var pre = Check.Throws<TargetInvocationException>(() => m.Invoke(null, new object[] { self, TimeSpan.FromSeconds(1), new CancellationToken(true) }));
            Check.True(pre.InnerException is OperationCanceledException, "an already-cancelled token was ignored");
        }

        // ---------- DriveAndVolumeInfo ----------

        public void GetSpace_AFilePath_ReportsItsVolume()
        {
            // GetDiskFreeSpaceEx wants a directory: a file path failed with ERROR_DIRECTORY.
            string file = Path.Combine(Path.GetTempPath(), "TbR65-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(file, "x");
            try
            {
                DiskSpace space = DriveAndVolumeInfo.GetSpace(file);
                Check.True(space.TotalBytes > 0, "no total");
            }
            finally { File.Delete(file); }
        }

        public void GetSpace_ALongDirectory_Works()
        {
            // A path over MAX_PATH failed (ERROR_FILENAME_EXCED_RANGE) though .NET itself handles it.
            string root = Path.Combine(Path.GetTempPath(), "TbR65-" + Guid.NewGuid().ToString("N"));
            string deep = root;
            while (deep.Length < 300) deep = Path.Combine(deep, new string('d', 40));
            Directory.CreateDirectory(deep);
            try
            {
                Check.True(DriveAndVolumeInfo.GetSpace(deep).TotalBytes > 0, "no total");
                Check.True(DriveAndVolumeInfo.GetSpace(deep + "\\").TotalBytes > 0, "no total (trailing slash)");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        // ---------- RegistryUtils ----------

        private static string ProbeKey() => @"Software\ToolBeltReviewProbe-" + Guid.NewGuid().ToString("N");

        public void Registry_GetBool_ReadsOneAndZeroStrings()
        {
            string key = ProbeKey();
            try
            {
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "on", "1");
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "off", " 0 ");
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "word", "True");
                Check.Equal(true, RegistryUtils.GetValue<bool>(RegistryHive.CurrentUser, key, "on"));
                Check.Equal(false, RegistryUtils.GetValue<bool>(RegistryHive.CurrentUser, key, "off", true));
                Check.Equal(true, RegistryUtils.GetValue<bool?>(RegistryHive.CurrentUser, key, "on"));
                Check.Equal(true, RegistryUtils.GetValue<bool>(RegistryHive.CurrentUser, key, "word"));
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); }
        }

        public void Registry_ALeadingBackslash_IsIgnoredEverywhere()
        {
            string key = ProbeKey();
            string slashed = "\\" + key;
            try
            {
                RegistryUtils.SetValue(RegistryHive.CurrentUser, slashed, "v", 5);
                Check.True(RegistryUtils.KeyExists(RegistryHive.CurrentUser, slashed), "KeyExists");
                Check.True(RegistryUtils.KeyExists(RegistryHive.CurrentUser, key), "written to the wrong key");
                Check.Equal(5, RegistryUtils.GetValue<int>(RegistryHive.CurrentUser, slashed, "v"));
                Check.Equal(1, RegistryUtils.GetValueNames(RegistryHive.CurrentUser, slashed).Count);
                Check.True(RegistryUtils.DeleteValue(RegistryHive.CurrentUser, slashed, "v"), "DeleteValue");
                Check.True(RegistryUtils.DeleteKey(RegistryHive.CurrentUser, slashed), "DeleteKey");
                Check.False(RegistryUtils.KeyExists(RegistryHive.CurrentUser, key), "key survived");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); }
        }

        // ---------- clipboard plumbing ----------

        // Saves/restores the clipboard text and self-skips when the clipboard can't be opened, as ClipboardUtilsTests does.
        private static void RunWithClipboard(Action body)
        {
            string? saved;
            try { saved = ClipboardUtils.GetText(); }
            catch (Win32Exception) { return; }
            try { body(); }
            catch (Win32Exception) { return; }
            finally
            {
                try
                {
                    if (saved is not null) ClipboardUtils.SetText(saved);
                    else ClipboardUtils.Clear();
                }
                catch (Win32Exception) { }
            }
        }

        // Puts raw bytes on the clipboard under a format, as a hostile program could.
        private static void PutRaw(uint format, byte[] bytes)
        {
            // On an STA thread, as ClipboardUtils does: OpenClipboard kept failing from the test's own thread.
            Exception? error = null;
            var t = new Thread(() => { try { PutRawCore(format, bytes); } catch (Exception ex) { error = ex; } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        private static void PutRawCore(uint format, byte[] bytes)
        {
            IntPtr h = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)bytes.Length);
            if (h == IntPtr.Zero) throw new InvalidOperationException("GlobalAlloc failed: " + Marshal.GetLastWin32Error());
            IntPtr p = GlobalLock(h);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            GlobalUnlock(h);
            bool open = false;
            for (int i = 0; i < 20 && !(open = OpenClipboard(IntPtr.Zero)); i++) Thread.Sleep(50);
            if (!open) { GlobalFree(h); throw new Win32Exception(); }   // clipboard busy: the test self-skips
            try
            {
                if (!EmptyClipboard()) throw new InvalidOperationException("EmptyClipboard failed: " + Marshal.GetLastWin32Error());
                if (SetClipboardData(format, h) == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    GlobalFree(h);
                    throw new InvalidOperationException("SetClipboardData failed: " + err);
                }
            }
            finally { CloseClipboard(); }
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr h);
    }
}
