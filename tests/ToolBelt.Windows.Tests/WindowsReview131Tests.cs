using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 131 (Windows satellite, pass 8) regressions.
    public sealed class WindowsReview131Tests
    {
        private const RegistryHive Hive = RegistryHive.CurrentUser;

        private static string NewKey() => @"Software\ToolBelt.Windows.Tests\R131." + Guid.NewGuid().ToString("N");

        private static void Cleanup(string sub)
        {
            try { RegistryUtils.DeleteKey(Hive, sub); } catch (Exception) { }
        }

        private enum Wide : uint { High = 0x80000001u }

        // ---------- StorageDeviceInfo.GetUncPath: the device spelling needs a share too ----------

        // \\?\UNC\server returned \\server and \\?\UNC\ returned \\, though the plain spellings are refused.
        public void Storage_GetUncPath_DeviceSpellingNeedsAServerAndAShare()
        {
            foreach (string p in new[] { @"\\?\UNC\server", @"\\?\UNC\", @"\\.\UNC\server", @"\\?\UNC\server\" })
                Check.Equal("path", Check.Throws<ArgumentException>(() => StorageDeviceInfo.GetUncPath(p)).ParamName);
            Check.Equal(@"\\server\share\a", StorageDeviceInfo.GetUncPath(@"\\?\UNC\server\share\a"));
            Check.Equal(@"\\server\share", StorageDeviceInfo.GetUncPath(@"\\.\UNC\server\share"));
        }

        // ---------- DriveAndVolumeInfo.GetSpace: a volume GUID without its trailing backslash ----------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder name, int length);

        public void DriveSpace_VolumeGuidWithoutTrailingBackslash_Works()
        {
            string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
            var sb = new StringBuilder(64);
            Check.True(GetVolumeNameForVolumeMountPoint(root, sb, sb.Capacity), "volume name for " + root);
            string guid = sb.ToString();                     // \\?\Volume{...}\
            long total = DriveAndVolumeInfo.GetSpace(root).TotalBytes;
            Check.Equal(total, DriveAndVolumeInfo.GetSpace(guid).TotalBytes);
            Check.Equal(total, DriveAndVolumeInfo.GetSpace(guid.TrimEnd('\\')).TotalBytes);
        }

        // ---------- RegistryUtils.SetValue: what GetValue reads writes back ----------

        // DWORD 0xFFFFFFFF reads into long as 4294967295, which threw (ParamName null) when written back as a DWORD.
        public void Registry_UnsignedRangeIntegersWriteAsWords()
        {
            string sub = NewKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub, "d", 0xFFFFFFFFu);
                long asLong = RegistryUtils.GetValue<long>(Hive, sub, "d");
                Check.Equal(4294967295L, asLong);
                RegistryUtils.SetValue(Hive, sub, "d2", asLong, RegistryValueKind.DWord);
                Check.Equal(0xFFFFFFFFu, RegistryUtils.GetValue<uint>(Hive, sub, "d2"));
                RegistryUtils.SetValue(Hive, sub, "d3", -1L, RegistryValueKind.DWord);
                Check.Equal(-1, RegistryUtils.GetValue<int>(Hive, sub, "d3"));
                RegistryUtils.SetValue(Hive, sub, "q", ulong.MaxValue, RegistryValueKind.QWord);
                Check.Equal(ulong.MaxValue, RegistryUtils.GetValue<ulong>(Hive, sub, "q"));
                RegistryUtils.SetValue(Hive, sub, "u", 7UL, RegistryValueKind.DWord);
                Check.Equal(7, RegistryUtils.GetValue<int>(Hive, sub, "u"));
                RegistryUtils.SetValue(Hive, sub, "e", Wide.High, RegistryValueKind.DWord);
                Check.Equal(Wide.High, RegistryUtils.GetValue<Wide>(Hive, sub, "e"));
                RegistryUtils.SetValue(Hive, sub, "w", 3.0, RegistryValueKind.DWord);
                Check.Equal(3, RegistryUtils.GetValue<int>(Hive, sub, "w"));

                // Out of range, or a fraction (2.5 was stored as 2): refused, naming the value.
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "x", 1L << 32, RegistryValueKind.DWord)).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "x", (long)int.MinValue - 1, RegistryValueKind.DWord)).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "x", 2.5, RegistryValueKind.DWord)).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "x", double.NaN, RegistryValueKind.QWord)).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "x", "text", RegistryValueKind.DWord)).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "x", 5, RegistryValueKind.MultiString)).ParamName);
                Check.Equal(null, RegistryUtils.GetValue(Hive, sub, "x"));
            }
            finally { Cleanup(sub); }
        }

        // A List<string> was stored as "System.Collections.Generic.List`1[System.String]".
        public void Registry_ACollectionIsNotStoredAsItsTypeName()
        {
            string sub = NewKey();
            try
            {
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "l", new List<string> { "a", "b" })).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "i", new[] { 1, 2 })).ParamName);
                Check.Equal(null, RegistryUtils.GetValue(Hive, sub, "l"));
                RegistryUtils.SetValue(Hive, sub, "s", new[] { "a", "b" });
                Check.Equal(2, RegistryUtils.GetValue<string[]>(Hive, sub, "s")!.Length);
                RegistryUtils.SetValue(Hive, sub, "b", new byte[] { 1, 2, 3 });
                Check.Equal(3, RegistryUtils.GetValue<byte[]>(Hive, sub, "b")!.Length);
            }
            finally { Cleanup(sub); }
        }

        // Version and IPAddress were written as text but read back as null: only Parse(string, IFormatProvider) was tried.
        // A Uri had no Parse at all, and was written unescaped.
        public void Registry_VersionIPAddressAndUri_RoundTrip()
        {
            string sub = NewKey();
            try
            {
                var v = new Version(1, 2, 3);
                RegistryUtils.SetValue(Hive, sub, "v", v);
                Check.Equal(v, RegistryUtils.GetValue<Version>(Hive, sub, "v"));
                var ip = IPAddress.Parse("127.0.0.1");
                RegistryUtils.SetValue(Hive, sub, "ip", ip);
                Check.Equal(ip, RegistryUtils.GetValue<IPAddress>(Hive, sub, "ip"));
                var uri = new Uri("https://example.com/a%20b?q=1#f");
                RegistryUtils.SetValue(Hive, sub, "u", uri);
                Check.Equal(uri.OriginalString, RegistryUtils.GetValue<Uri>(Hive, sub, "u")!.OriginalString);
                RegistryUtils.SetValue(Hive, sub, "r", new Uri("rel/path", UriKind.Relative));
                Check.Equal("rel/path", RegistryUtils.GetValue<Uri>(Hive, sub, "r")!.OriginalString);
                RegistryUtils.SetValue(Hive, sub, "bad", "not a version");
                Check.Equal(null, RegistryUtils.GetValue<Version>(Hive, sub, "bad"));
            }
            finally { Cleanup(sub); }
        }

        // ---------- ScreenCapture.CaptureRegion: an edge past int range ----------

        public void ScreenCapture_AnEdgePastIntRangeIsRefused()
        {
            Check.Equal("x", Check.Throws<ArgumentOutOfRangeException>(() => ScreenCapture.CaptureRegion(int.MaxValue, 0, 10, 10)).ParamName);
            Check.Equal("y", Check.Throws<ArgumentOutOfRangeException>(() => ScreenCapture.CaptureRegion(0, int.MaxValue - 5, 10, 10)).ParamName);
        }

        // ---------- ShortcutUtils: the shortcut file's own path ----------

        // A 395-character shortcut path returned normally after overwriting the file its first 259 characters named.
        public void Shortcut_ALongShortcutPathIsRefusedAndOverwritesNothing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ToolBelt.Windows.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            try
            {
                string lnk = Path.Combine(dir, new string('a', 395 - dir.Length - 1));
                string victim = lnk.Substring(0, 259);
                File.WriteAllText(victim, "precious data");
                var ex = Check.Throws<ArgumentException>(() => ShortcutUtils.Create(lnk, target));
                Check.Equal("shortcutPath", ex.ParamName);
                Check.Equal("precious data", File.ReadAllText(victim));
                Check.Equal("shortcutPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Read(lnk)).ParamName);

                // A relative path counts at its full length.
                string cwd = Environment.CurrentDirectory;
                try
                {
                    Environment.CurrentDirectory = dir;
                    string rel = new string('b', 259 - dir.Length - 1) + ".lnk";
                    Check.Equal("shortcutPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(rel, target)).ParamName);
                }
                finally { Environment.CurrentDirectory = cwd; }

                string ok = Path.Combine(dir, new string('c', 258 - dir.Length - 1));
                Check.Equal(258, ok.Length);
                ShortcutUtils.Create(ok, target);
                Check.True(string.Equals(target, ShortcutUtils.Read(ok).TargetPath, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        // A blank path threw COM's bare ArgumentException from Create and named "path" from Read.
        public void Shortcut_ABlankPathNamesItsParameter()
        {
            string target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            Check.Equal("shortcutPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create("   ", target)).ParamName);
            Check.Equal("shortcutPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Read("   ")).ParamName);
            Check.Equal("targetPath", Check.Throws<ArgumentException>(() => ShortcutUtils.Create(Path.Combine(Path.GetTempPath(), "x.lnk"), " ")).ParamName);
        }

        // ---------- ShortcutUtils.Create: an icon index without a path ----------

        public void Shortcut_IconIndexWithoutAPath_IsKept()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ToolBelt.Windows.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string lnk = Path.Combine(dir, "icon.lnk");
            string target = Path.Combine(Environment.SystemDirectory, "shell32.dll");
            try
            {
                ShortcutUtils.Create(lnk, target, iconIndex: 4);
                var info = ShortcutUtils.Read(lnk);
                Check.Equal(4, info.IconIndex);
                Check.True(string.Equals(target, Environment.ExpandEnvironmentVariables(info.IconPath), StringComparison.OrdinalIgnoreCase), info.IconPath);
                ShortcutUtils.Create(lnk, target);
                Check.Equal(0, ShortcutUtils.Read(lnk).IconIndex);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
