using System;
using System.IO;
using System.Reflection;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 211 (Windows satellite, pass 13) regressions.
    public sealed class WindowsReview211Tests
    {
        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "TbR211." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void SameText(string expected, string actual) =>
            Check.True(string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase), $"expected '{expected}', got '{actual}'");

        // ---------- DriveAndVolumeInfo ----------

        public void GetSpace_ShortRelativePathWithALongFullPath_Works()
        {
            string root = TempDir();
            string saved = Environment.CurrentDirectory;
            try
            {
                // A current directory just short of the limit, and a relative child that takes the full path past 260.
                string cwd = root;
                while (cwd.Length < 230) cwd = Path.Combine(cwd, new string('d', Math.Min(40, 232 - cwd.Length)));
                string child = new string('c', 60);
                Directory.CreateDirectory(Path.Combine(cwd, child));
                Environment.CurrentDirectory = cwd;
                Check.True(Path.GetFullPath(child).Length >= 260, "the full path is long");
                DiskSpace byFull = DriveAndVolumeInfo.GetSpace(Path.GetFullPath(child));
                // The path as given was short, so it went to the API relative, and the API failed past MAX_PATH.
                DiskSpace byRelative = DriveAndVolumeInfo.GetSpace(child);
                Check.Equal(byFull.TotalBytes, byRelative.TotalBytes);
            }
            finally
            {
                Environment.CurrentDirectory = saved;
                Directory.Delete(@"\\?\" + root, true);
            }
        }

        // ---------- PowerStatus ----------

        private static PowerStatusInfo FromRaw(byte ac, byte flag, byte percent)
        {
            Type? raw = typeof(PowerStatus).GetNestedType("SYSTEM_POWER_STATUS", BindingFlags.NonPublic);
            MethodInfo? m = typeof(PowerStatus).GetMethod("FromRaw", BindingFlags.NonPublic | BindingFlags.Static);
            Check.NotNull(raw, "SYSTEM_POWER_STATUS");
            Check.NotNull(m, "PowerStatus decodes the raw status in FromRaw");
            object s = Activator.CreateInstance(raw!)!;
            raw!.GetField("ACLineStatus")!.SetValue(s, ac);
            raw.GetField("BatteryFlag")!.SetValue(s, flag);
            raw.GetField("BatteryLifePercent")!.SetValue(s, percent);
            raw.GetField("BatteryLifeTime")!.SetValue(s, 0xFFFFFFFFu);
            return (PowerStatusInfo)m!.Invoke(null, new[] { s })!;
        }

        // Read by name so this file also builds against the code before the property existed.
        private static bool Known(PowerStatusInfo info)
        {
            PropertyInfo? p = typeof(PowerStatusInfo).GetProperty("BatteryStateKnown");
            Check.NotNull(p, "PowerStatusInfo.BatteryStateKnown");
            return (bool)p!.GetValue(info)!;
        }

        public void PowerStatus_UnknownBatteryState_IsNotReportedAsNoBattery()
        {
            PowerStatusInfo unknown = FromRaw(255, 255, 255);
            Check.False(Known(unknown), "flag 255 is unknown");
            Check.False(unknown.BatteryPresent);
            Check.True(unknown.ToString().Contains("battery unknown"), unknown.ToString());
            Check.False(unknown.ToString().Contains("no battery"), unknown.ToString());

            PowerStatusInfo none = FromRaw(1, 128, 255);
            Check.True(Known(none));
            Check.False(none.BatteryPresent);
            Check.Equal("on AC, no battery", none.ToString());

            PowerStatusInfo charging = FromRaw(1, 8, 57);
            Check.True(Known(charging));
            Check.True(charging.BatteryPresent);
            Check.Equal(57, charging.BatteryChargePercent);
        }

        // ---------- ClipboardUtils ----------

        public void DecodeDib_LastRowWithoutPadding_IsDecoded()
        {
            // 24-bit, 1 x 2, bottom-up: rows are 4 bytes with padding, but the last one ends at its third byte.
            var dib = new byte[40 + 4 + 3];
            BitConverter.GetBytes(40).CopyTo(dib, 0);
            BitConverter.GetBytes(1).CopyTo(dib, 4);
            BitConverter.GetBytes(2).CopyTo(dib, 8);
            BitConverter.GetBytes((short)1).CopyTo(dib, 12);
            BitConverter.GetBytes((short)24).CopyTo(dib, 14);
            dib[40] = 1; dib[41] = 2; dib[42] = 3;           // bottom row (y = 1)
            dib[44] = 4; dib[45] = 5; dib[46] = 6;           // top row (y = 0), unpadded
            ScreenImage image = ClipboardUtils.DecodeDib(dib);
            Check.Equal(1, image.Width);
            Check.Equal(2, image.Height);
            byte[] p = image.Pixels;
            Check.Equal(4, p[0]); Check.Equal(5, p[1]); Check.Equal(6, p[2]); Check.Equal(255, p[3]);
            Check.Equal(1, p[4]); Check.Equal(2, p[5]); Check.Equal(3, p[6]); Check.Equal(255, p[7]);

            // One byte fewer is truncated.
            Check.Throws<InvalidOperationException>(() => ClipboardUtils.DecodeDib(dib.AsSpan(0, dib.Length - 1).ToArray()));
        }

        // ---------- StorageDeviceInfo ----------

        public void StorageBusType_NvmeOverFabrics_IsKnown()
        {
            // STORAGE_BUS_TYPE 20 (BusTypeNvmeof) read as Unknown.
            Check.True(Enum.IsDefined(typeof(StorageBusType), 20), "bus type 20");
            Check.Equal("Nvmeof", ((StorageBusType)20).ToString());
        }

        // ---------- FileAssociation ----------

        public void FileAssociation_PathAsExtension_IsRejected()
        {
            // C:\x\y.txt was queried as ".C:\x\y.txt", and the shell answered for .txt.
            Check.Equal("extension", Check.Throws<ArgumentException>(() => FileAssociation.GetProgId(@"C:\x\y.txt")).ParamName);
            Check.Equal("extension", Check.Throws<ArgumentException>(() => FileAssociation.GetAssociatedExecutable("sub/y.txt")).ParamName);
            Check.Equal("extension", Check.Throws<ArgumentException>(() => FileAssociation.GetOpenCommand(".t*t")).ParamName);
            FileAssociation.GetProgId(".txt");
            FileAssociation.GetProgId("tar.gz");
        }

        // ---------- RunElevated ----------

        public void RunElevated_RestartUnderTheDotnetHost_KeepsHostOptions()
        {
            var m = typeof(RunElevated).GetMethod("RestartCommandFrom", BindingFlags.NonPublic | BindingFlags.Static);
            Check.NotNull(m, "the restart command is computed by RestartCommandFrom");
            string dotnet = @"C:\Program Files\dotnet\dotnet.exe";
            var argv = new[] { "dotnet", "exec", "--runtimeconfig", @"C:\apps\x.runtimeconfig.json", @"C:\apps\My App.dll", "old" };
            var cmd = ((string, string))m!.Invoke(null, new object?[] { dotnet, @"C:\apps\My App.dll", argv, "--x" })!;
            Check.Equal(dotnet, cmd.Item1);
            Check.Equal("exec --runtimeconfig C:\\apps\\x.runtimeconfig.json \"C:\\apps\\My App.dll\" --x", cmd.Item2);

            // Plain "dotnet app.dll", a relative app path, and no command line at all.
            argv = new[] { "dotnet", @"C:\apps\app.dll" };
            Check.Equal("C:\\apps\\app.dll", (((string, string))m.Invoke(null, new object?[] { dotnet, @"C:\apps\app.dll", argv, null })!).Item2);
            string full = Path.GetFullPath("app.dll");
            argv = new[] { "dotnet", "--roll-forward", "Major", "app.dll", "a" };
            Check.Equal("--roll-forward Major " + full, (((string, string))m.Invoke(null, new object?[] { dotnet, full, argv, null })!).Item2);
            Check.Equal(@"C:\apps\app.dll", (((string, string))m.Invoke(null, new object?[] { dotnet, @"C:\apps\app.dll", null, null })!).Item2);

            // An apphost restarts as itself.
            var apphost = ((string, string))m.Invoke(null, new object?[] { @"C:\apps\app.exe", @"C:\apps\app.dll", new[] { "app.exe", "y" }, "--x" })!;
            Check.Equal(@"C:\apps\app.exe", apphost.Item1);
            Check.Equal("--x", apphost.Item2);
        }

        public void RunElevated_Restart_StartsInTheCurrentDirectory()
        {
            // An elevated process started without a directory began in System32.
            var m = typeof(RunElevated).GetMethod("RestartStart", BindingFlags.NonPublic | BindingFlags.Static);
            Check.NotNull(m, "the restart is computed by RestartStart");
            var start = ((string, string, string))m!.Invoke(null, new object?[] { "--x" })!;
            Check.Equal(Environment.CurrentDirectory, start.Item3);
            Check.Equal(Environment.ProcessPath, start.Item1);
            Check.True(start.Item2.EndsWith("--x", StringComparison.Ordinal), start.Item2);
        }

        // ---------- EventLogWriter ----------

        public void EventLogWriter_TruncatedText_UsesTheWholeLimit()
        {
            MethodInfo m = typeof(EventLogWriter).GetMethod("EventText", BindingFlags.NonPublic | BindingFlags.Static)!;
            Check.NotNull(m, "EventLogWriter.EventText is missing");
            string text = (string)m.Invoke(null, new object[] { new string('x', EventLogWriter.MaxMessageLength + 100) })!;
            Check.Equal(EventLogWriter.MaxMessageLength, text.Length);
            Check.True(text.EndsWith("[truncated]", StringComparison.Ordinal), "marker");
            Check.Equal(EventLogWriter.MaxMessageLength, ((string)m.Invoke(null, new object[] { new string('x', EventLogWriter.MaxMessageLength) })!).Length);
        }

        // ---------- ShortcutUtils ----------

        public void Shortcut_DamagedFile_IsInvalidData()
        {
            string dir = TempDir();
            try
            {
                string good = Path.Combine(dir, "good.lnk");
                ShortcutUtils.Create(good, Path.Combine(Environment.SystemDirectory, "notepad.exe"));
                byte[] header = File.ReadAllBytes(good).AsSpan(0, 0x4C).ToArray();
                // A valid header over zeros failed with ERROR_NOT_FOUND, which came out as a bare COMException.
                string bad = Path.Combine(dir, "bad.lnk");
                var bytes = new byte[200];
                header.CopyTo(bytes, 0);
                File.WriteAllBytes(bad, bytes);
                Check.Throws<InvalidDataException>(() => ShortcutUtils.Read(bad));

                // Every truncation of a real shortcut is read or refused as invalid data, never a bare COM error.
                byte[] all = File.ReadAllBytes(good);
                for (int n = 0x4C; n < all.Length; n++)
                {
                    File.WriteAllBytes(bad, all.AsSpan(0, n).ToArray());
                    try { ShortcutUtils.Read(bad); }
                    catch (InvalidDataException) { }
                }
                Check.Throws<FileNotFoundException>(() => ShortcutUtils.Read(Path.Combine(dir, "missing.lnk")));
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Shortcut_ExtendedLengthAndStreamTargets_AreResolved()
        {
            string dir = TempDir();
            string saved = Environment.CurrentDirectory;
            try
            {
                string lnk = Path.Combine(dir, "x.lnk");
                string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
                // \\?\C:\...\notepad.exe was refused by the shell, as an ArgumentException naming no parameter.
                ShortcutUtils.Create(lnk, @"\\?\" + notepad);
                SameText(notepad, ShortcutUtils.Read(lnk).TargetPath);

                // sub\a:b was taken for a scheme and never resolved. The shell refuses a stream even as a full path, and that
                // came out as an ArgumentException naming no parameter.
                Environment.CurrentDirectory = dir;
                Directory.CreateDirectory(Path.Combine(dir, "sub"));
                File.WriteAllText(Path.Combine(dir, "sub", "a"), "x");
                Check.Equal("targetPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, @"sub\a:b")).ParamName);
                ShortcutUtils.Create(lnk, @"sub\a");
                SameText(Path.Combine(dir, "sub", "a"), ShortcutUtils.Read(lnk).TargetPath);

                // What the shell still refuses names the parameter.
                Check.Equal("targetPath", Check.Throws<ArgumentException>(() =>
                    ShortcutUtils.Create(lnk, @"\\?\Volume{00000000-0000-0000-0000-000000000000}\x.exe")).ParamName);
            }
            finally
            {
                Environment.CurrentDirectory = saved;
                Directory.Delete(dir, true);
            }
        }

        public void Shortcut_UrlAndShellTargets_RoundTrip()
        {
            string dir = TempDir();
            try
            {
                string lnk = Path.Combine(dir, "u.lnk");
                // The shell stores these as item ID lists with no file path, and Read reported an empty target.
                foreach (string url in new[] { "https://example.invalid/", "https://example.invalid/a%20b?x=1", "mailto:a@example.invalid" })
                {
                    ShortcutUtils.Create(lnk, url);
                    Check.Equal(url, ShortcutUtils.Read(lnk).TargetPath);
                }

                ShortcutUtils.Create(lnk, "shell:RecycleBinFolder");
                string bin = ShortcutUtils.Read(lnk).TargetPath;
                SameText("::{645FF040-5081-101B-9F08-00AA002F954E}", bin);
                // The parsing name Read reports goes back into Create (it was resolved as a relative file name).
                string lnk2 = Path.Combine(dir, "u2.lnk");
                ShortcutUtils.Create(lnk2, bin);
                SameText(bin, ShortcutUtils.Read(lnk2).TargetPath);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
