using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 79 (Windows satellite, pass 5) regressions.
    public sealed class WindowsReview79Tests
    {
        private static string NewAppId() => "TbR79." + Guid.NewGuid().ToString("N").Substring(0, 12);

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "TbR79." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ---------- SingleInstanceApp ----------

        public void SingleInstanceApp_SlowHandler_QueueIsBounded()
        {
            string id = NewAppId();
            using var primary = SingleInstanceApp.Start(id, Array.Empty<string>());
            Check.True(primary.IsPrimary);
            using var release = new ManualResetEventSlim();
            primary.SecondInstanceStarted += (_, e) => release.Wait(TimeSpan.FromSeconds(30));
            int forwarded = 0, refused = 0;
            try
            {
                for (int i = 0; i < 130; i++)
                {
                    using var s = SingleInstanceApp.Start(id, new[] { "launch " + i }, TimeSpan.FromSeconds(2));
                    Check.False(s.IsPrimary);
                    if (s.ForwardedToPrimary) forwarded++; else refused++;
                }
            }
            finally { release.Set(); }
            // One launch in the handler and at most 100 waiting; every launch past that was acknowledged and held (515 MB
            // for 300 launches of 2 MB).
            Check.True(forwarded <= 101 && forwarded >= 100, $"forwarded {forwarded}, refused {refused}");
            Check.Equal(130 - forwarded, refused);
        }

        public void SingleInstanceApp_AppIdsDifferingInCase_AreOneInstance()
        {
            string id = "TbR79.Case" + Guid.NewGuid().ToString("N").Substring(0, 12);
            using var primary = SingleInstanceApp.Start(id, Array.Empty<string>());
            Check.True(primary.IsPrimary);
            using var got = new ManualResetEventSlim();
            string received = "";
            primary.SecondInstanceStarted += (_, e) => { received = e.Args[0]; got.Set(); };
            // The pipe name ignores case and the gate did not: both were primary, and the second's launches went to the first.
            using var other = SingleInstanceApp.Start(id.ToLowerInvariant(), new[] { "lower" }, TimeSpan.FromSeconds(3));
            Check.False(other.IsPrimary, "an id differing only in case became a second primary");
            Check.True(other.ForwardedToPrimary);
            Check.True(got.Wait(TimeSpan.FromSeconds(5)));
            Check.Equal("lower", received);
        }

        public void SingleInstanceApp_Forward_HasNoSyncOverAsync()
        {
            // Start is synchronous, and its forward blocked on async pipe calls (GetAwaiter().GetResult()).
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "ToolBelt.sln"))) dir = Path.GetDirectoryName(dir)!;
            Check.NotNull(dir, "repo root not found");
            string source = File.ReadAllText(Path.Combine(dir!, "src", "ToolBelt.Windows", "SingleInstanceApp.cs"));
            int start = source.IndexOf("private static bool Forward(", StringComparison.Ordinal);
            Check.True(start >= 0);
            int end = source.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            string forward = source.Substring(start, end - start);
            Check.False(forward.Contains("GetResult()"), "Forward blocks on a task");
            Check.False(forward.Contains("Async("), "Forward calls an async method");
        }

        public void SingleInstanceApp_StalledPrimary_StillTimesOut()
        {
            // Guard for the synchronous forward: a primary that takes the connection and never writes must not hang it.
            string id = NewAppId();
            using var self = Process.GetCurrentProcess();
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            using var gate = SingleInstance.TryAcquire(@"Local\ToolBelt.SingleInstance." + id.ToUpperInvariant() + "." + sid);
            string pipe = "ToolBelt.SingleInstance.2." + id + "." + self.SessionId + "." + sid;
            using var server = new System.IO.Pipes.NamedPipeServerStream(pipe, System.IO.Pipes.PipeDirection.InOut, 1,
                System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.CurrentUserOnly | System.IO.Pipes.PipeOptions.Asynchronous);
            var accepted = server.WaitForConnectionAsync();
            var watch = Stopwatch.StartNew();
            using var app = SingleInstanceApp.Start(id, new[] { "x" }, TimeSpan.FromMilliseconds(800));
            Check.False(app.ForwardedToPrimary);
            Check.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
            Check.True(accepted.IsCompleted);
        }

        // ---------- SingleInstance ----------

        public void SingleInstance_BarePrefixOrMissingSession_ThrowsArgumentException()
        {
            foreach (string name in new[] { @"Global\", @"Local\", @"Session\1\" })
                Check.Equal("name", Check.Throws<ArgumentException>(() => SingleInstance.TryAcquire(name), name).ParamName);
            // A session that does not exist threw DirectoryNotFoundException.
            Check.Equal("name", Check.Throws<ArgumentException>(() => SingleInstance.TryAcquire(@"Session\99999999999999\x")).ParamName);
        }

        // ---------- ScreenCapture, DpiInfo ----------

        public void ScreenImage_BadHeight_NamesHeight()
        {
            Check.Equal("height", Check.Throws<ArgumentOutOfRangeException>(() => new ScreenImage(5, 0, new byte[0])).ParamName);
            Check.Equal("height", Check.Throws<ArgumentOutOfRangeException>(() => new ScreenImage(5, -1, new byte[0])).ParamName);
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => new ScreenImage(0, 5, new byte[0])).ParamName);
        }

        public void DpiInfo_ScaleWithNonPositiveDpi_Throws()
        {
            Check.Equal("dpi", Check.Throws<ArgumentOutOfRangeException>(() => DpiInfo.Scale(10, 0)).ParamName);
            Check.Equal("dpi", Check.Throws<ArgumentOutOfRangeException>(() => DpiInfo.Scale(10, -96)).ParamName);
            Check.Equal(15, DpiInfo.Scale(10, 144));
        }

        public void MonitorDpi_ToString_IsInvariant()
        {
            var monitors = DpiInfo.Monitors();
            if (monitors.Count == 0) return;                                          // no display (service session)
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                string text = monitors[0].ToString();
                // tr-TR puts the percent sign first ("%200").
                Check.False(text.Contains("(%"), text);
                Check.True(text.Contains(" %)") || text.Contains("%)"), text);
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        // ---------- ResourceSampler ----------

        public void ResourceSampler_SampleAfterDispose_ThrowsObjectDisposed()
        {
            using var process = Process.GetCurrentProcess();
            var sampler = new ResourceSampler(process);
            sampler.Sample();
            sampler.Dispose();
            sampler.Dispose();                                                        // twice is fine
            // A caller's process kept being sampled after Dispose; an owned one threw the "process exited" error.
            Check.Throws<ObjectDisposedException>(() => sampler.Sample());
            var owned = ResourceSampler.ForCurrentProcess();
            owned.Dispose();
            Check.Throws<ObjectDisposedException>(() => owned.Sample());
        }

        // ---------- DriveAndVolumeInfo ----------

        public void GetSpace_NulOrBlank_ThrowsArgumentException()
        {
            string sys = Path.GetPathRoot(Environment.SystemDirectory)!;
            // The API ended the path at the NUL and reported the drive's figures.
            Check.Equal("path", Check.Throws<ArgumentException>(() => DriveAndVolumeInfo.GetSpace(sys + "\0no such dir")).ParamName);
            Check.Equal("path", Check.Throws<ArgumentException>(() => DriveAndVolumeInfo.GetSpace("")).ParamName);
            Check.Equal("path", Check.Throws<ArgumentException>(() => DriveAndVolumeInfo.GetSpace("   ")).ParamName);
            Check.True(DriveAndVolumeInfo.GetSpace(sys).TotalBytes > 0);
        }

        public void GetSpace_LongDevicePath_Works()
        {
            string root = TempDir();
            try
            {
                string deep = root;
                while (deep.Length < 300) deep = Path.Combine(deep, "segment-of-a-long-path");
                Directory.CreateDirectory(deep);
                long expected = DriveAndVolumeInfo.GetSpace(Path.GetPathRoot(root)!).TotalBytes;
                // \\.\C:\... past 248 characters was rewritten to \\?\UNC\.\C:\... and failed.
                Check.Equal(expected, DriveAndVolumeInfo.GetSpace(@"\\.\" + deep).TotalBytes);
                Check.Equal(expected, DriveAndVolumeInfo.GetSpace(@"\\.\" + root).TotalBytes);
            }
            finally { Directory.Delete(@"\\?\" + root, recursive: true); }
        }

        // ---------- EventLogWriter ----------

        public void EventLogWriter_NulOrNullNames_AreRejected()
        {
            // "Application" + NUL + "junk" opened the Application source, and IsSourceRegistered said it was registered.
            Check.Equal("source", Check.Throws<ArgumentException>(() => new EventLogWriter("Application\0junk")).ParamName);
            Check.Equal("source", Check.Throws<ArgumentException>(() => EventLogWriter.IsSourceRegistered("Application\0junk")).ParamName);
            Check.Equal("log", Check.Throws<ArgumentException>(() => EventLogWriter.IsSourceRegistered("x", "Application\0junk")).ParamName);
            Check.Equal("source", Check.Throws<ArgumentException>(() => EventLogWriter.UnregisterSource("Application\0junk")).ParamName);
            // A null was ArgumentException, unlike the rest of the satellite.
            Check.Equal("source", Check.Throws<ArgumentNullException>(() => new EventLogWriter(null!)).ParamName);
            Check.Equal("source", Check.Throws<ArgumentNullException>(() => EventLogWriter.IsSourceRegistered(null!)).ParamName);
            Check.Equal("log", Check.Throws<ArgumentNullException>(() => EventLogWriter.IsSourceRegistered("x", null!)).ParamName);
        }

        public void EventLogWriter_NulInMessage_IsKept()
        {
            MethodInfo m = typeof(EventLogWriter).GetMethod("EventText", BindingFlags.NonPublic | BindingFlags.Static)!;
            Check.NotNull(m, "EventLogWriter.EventText is missing");
            // The API ends the text at a NUL: everything after one was lost.
            string text = (string)m.Invoke(null, new object[] { "before\0after" })!;
            Check.Equal("before" + (char)0x2400 + "after", text);
            Check.Equal("plain", (string)m.Invoke(null, new object[] { "plain" })!);
        }

        // ---------- ShortcutUtils ----------

        public void Shortcut_NulsAndEmptyValues_AreRejected()
        {
            string dir = TempDir();
            try
            {
                string target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
                string lnk = Path.Combine(dir, "n.lnk");
                // A NUL cut the shortcut path short and wrote n.lnk; one in a value dropped the rest of it.
                Check.Equal("shortcutPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk + "\0junk", target)).ParamName);
                Check.False(File.Exists(lnk), "a file was written for the cut-short path");
                Check.Equal("targetPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target + "\0x")).ParamName);
                Check.Equal("arguments", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target, arguments: "a\0b")).ParamName);
                Check.Equal("workingDirectory", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target, workingDirectory: "w\0x")).ParamName);
                Check.Equal("description", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target, description: "d\0e")).ParamName);
                Check.Equal("iconPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target, iconPath: "i\0x")).ParamName);
                // An empty target wrote a shortcut to nothing; an empty path threw DirectoryNotFoundException.
                Check.Equal("targetPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, "")).ParamName);
                Check.Equal("shortcutPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create("", target)).ParamName);
                Check.Equal("shortcutPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Read("")).ParamName);
                Check.Equal("shortcutPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Read(lnk + "\0x")).ParamName);
                Check.False(File.Exists(lnk));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        public void Shortcut_CorruptFile_ThrowsInvalidData()
        {
            string dir = TempDir();
            try
            {
                string lnk = Path.Combine(dir, "bad.lnk");
                File.WriteAllBytes(lnk, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
                // A bare COMException (E_FAIL) before.
                Check.Throws<InvalidDataException>(() => ShortcutUtils.Read(lnk));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        // ---------- ClipboardUtils ----------

        public void ClipboardText_LengthStopsAtNulAndIsBoundedByTheStringLimit()
        {
            Type t = typeof(ClipboardUtils);
            MethodInfo? m = t.GetMethod("TextLength", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo? max = t.GetField("MaxStringLength", BindingFlags.NonPublic | BindingFlags.Static);
            // The bound was int.MaxValue, so a 2.1-4.3 GB block threw OutOfMemoryException instead of the documented error.
            Check.NotNull(m, "ClipboardUtils.TextLength is missing");
            Check.NotNull(max, "ClipboardUtils.MaxStringLength is missing");
            int limit = (int)max!.GetValue(null)!;
            Check.Throws<OutOfMemoryException>(() => new string('a', limit + 1));     // fails before allocating
            const int chars = 200_000;
            IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(chars * 2);
            try
            {
                var text = new char[chars];
                Array.Fill(text, 'x');
                System.Runtime.InteropServices.Marshal.Copy(text, 0, block, chars);
                Check.Equal((long)chars, (long)m!.Invoke(null, new object[] { block, (long)chars })!);
                foreach (int at in new[] { 0, 1, 65_535, 65_536, 65_539, 150_000, chars - 1 })
                {
                    System.Runtime.InteropServices.Marshal.WriteInt16(block, at * 2, 0);
                    Check.Equal((long)at, (long)m.Invoke(null, new object[] { block, (long)chars })!, "NUL at " + at);
                    System.Runtime.InteropServices.Marshal.WriteInt16(block, at * 2, 'x');
                }
                Check.Equal(0L, (long)m.Invoke(null, new object[] { block, 0L })!);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(block); }
        }

        // ---------- PrivilegeScope, FileAssociation ----------

        public void PrivilegeAndAssociation_NulInName_IsRejected()
        {
            // The API ended the name at the NUL: the real privilege was reported held (and Enable would turn it on).
            Check.Equal("privilegeName", Check.Throws<ArgumentException>(() => PrivilegeScope.IsHeld("SeShutdownPrivilege\0x")).ParamName);
            Check.Equal("privilegeName", Check.Throws<ArgumentException>(() => PrivilegeScope.IsEnabled("SeChangeNotifyPrivilege\0x")).ParamName);
            Check.Equal("privilegeName", Check.Throws<ArgumentException>(() => PrivilegeScope.Enable("SeChangeNotifyPrivilege\0x")).ParamName);
            // .txt + NUL answered for .txt.
            Check.Equal("extension", Check.Throws<ArgumentException>(() => FileAssociation.GetProgId(".txt\0x")).ParamName);
            Check.Equal("extension", Check.Throws<ArgumentException>(() => FileAssociation.GetAssociatedExecutable("txt\0x")).ParamName);
        }
    }
}
