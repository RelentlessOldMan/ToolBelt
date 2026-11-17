using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 110 (Windows satellite, pass 7) regressions.
    public sealed class WindowsReview110Tests
    {
        private const RegistryHive Hive = RegistryHive.CurrentUser;

        private static string NewKey() => @"Software\ToolBelt.Windows.Tests\R110." + Guid.NewGuid().ToString("N");

        private static void Cleanup(string sub)
        {
            try { RegistryUtils.DeleteKey(Hive, sub); } catch (Exception) { }
        }

        // ---------- ClipboardUtils: lone surrogates ----------

        private static byte[] DropBlock(string names)
        {
            byte[] raw = new byte[names.Length * 2];
            for (int i = 0; i < names.Length; i++) BitConverter.GetBytes(names[i]).CopyTo(raw, i * 2);
            var block = new byte[20 + raw.Length];
            BitConverter.GetBytes(20u).CopyTo(block, 0);
            BitConverter.GetBytes(1).CopyTo(block, 16);
            raw.CopyTo(block, 20);
            return block;
        }

        // A name holding a lone surrogate (legal in NTFS) decoded with U+FFFD in its place: a file that does not exist.
        public void Clipboard_DecodeDropFiles_KeepsALoneSurrogate()
        {
            string a = "C:\\odd\\\uD800name.txt", b = "D:\\x\uDC00\\y\uDBFF";
            string[] files = ClipboardUtils.DecodeDropFiles(DropBlock(a + "\0" + b + "\0\0"));
            Check.Equal(2, files.Length);
            Check.Equal(a, files[0]);
            Check.Equal(b, files[1]);
        }

        // The writers encoded with Encoding.Unicode, which turns a lone surrogate into U+FFFD; the payload is checked
        // through the private encoder so no clipboard is needed.
        public void Clipboard_Writers_KeepALoneSurrogate()
        {
            string text = "a\uD800b\uDFFF";
            MethodInfo? raw = typeof(ClipboardUtils).GetMethod("RawUtf16", BindingFlags.NonPublic | BindingFlags.Static);
            if (raw is null)
            {
                // The code before the fix: show what it wrote.
                byte[] old = Encoding.Unicode.GetBytes(text);
                Check.Equal(text, new string(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, char>(old)), "old encoding");
                return;
            }
            byte[] bytes = (byte[])raw.Invoke(null, new object[] { text })!;
            Check.Equal(text, new string(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, char>(bytes)));
        }

        // ---------- RegistryUtils: values SetValue writes read back ----------

        private static T RoundTrip<T>(T value)
        {
            string sub = NewKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub, "v", value!);
                return RegistryUtils.GetValue<T>(Hive, sub, "v")!;
            }
            finally { Cleanup(sub); }
        }

        // A DateTime was written "G": the milliseconds and the kind were lost.
        public void Registry_DateTime_RoundTripsExactly()
        {
            var utc = new DateTime(2026, 11, 16, 13, 45, 7, 345, DateTimeKind.Utc).AddTicks(6789);
            DateTime back = RoundTrip(utc);
            Check.Equal(utc.Ticks, back.Ticks);
            Check.Equal(DateTimeKind.Utc, back.Kind);
            var local = new DateTime(2001, 2, 3, 4, 5, 6, 7, DateTimeKind.Local);
            back = RoundTrip(local);
            Check.Equal(local.Ticks, back.Ticks);
            Check.Equal(DateTimeKind.Local, back.Kind);
            var unspecified = new DateTime(1999, 12, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);
            back = RoundTrip(unspecified);
            Check.Equal(unspecified.Ticks, back.Ticks);
            Check.Equal(DateTimeKind.Unspecified, back.Kind);
            Check.Equal(DateTime.MaxValue.Ticks, RoundTrip(DateTime.MaxValue).Ticks);
            Check.Equal(DateTime.MinValue.Ticks, RoundTrip(DateTime.MinValue).Ticks);
        }

        // These read back as default, with no error.
        public void Registry_NonConvertibleTypes_RoundTrip()
        {
            var dto = new DateTimeOffset(2026, 11, 16, 13, 45, 7, 345, TimeSpan.FromMinutes(-330)).AddTicks(12);
            DateTimeOffset dtoBack = RoundTrip(dto);
            Check.Equal(dto.Ticks, dtoBack.Ticks);
            Check.Equal(dto.Offset, dtoBack.Offset);
            Check.Equal(TimeSpan.FromTicks(-123456789012345), RoundTrip(TimeSpan.FromTicks(-123456789012345)));
            Check.Equal(TimeSpan.MaxValue, RoundTrip(TimeSpan.MaxValue));
            Guid g = Guid.NewGuid();
            Check.Equal(g, RoundTrip(g));
            Check.Equal((Half)1.5, RoundTrip((Half)1.5));
            Check.Equal(Half.Epsilon, RoundTrip(Half.Epsilon));
            Check.Equal(Int128.MinValue, RoundTrip(Int128.MinValue));
            Check.Equal(UInt128.MaxValue, RoundTrip(UInt128.MaxValue));
            BigInteger big = BigInteger.Pow(-7, 91);
            Check.Equal(big, RoundTrip(big));
            Check.Equal(new DateOnly(1, 1, 1), RoundTrip(new DateOnly(1, 1, 1)));
            Check.Equal(new DateOnly(9999, 12, 31), RoundTrip(new DateOnly(9999, 12, 31)));
            Check.Equal(new TimeOnly(23, 59, 59, 999).Add(TimeSpan.FromTicks(9999)), RoundTrip(new TimeOnly(23, 59, 59, 999).Add(TimeSpan.FromTicks(9999))));
            Check.Equal((Guid?)g, RoundTrip((Guid?)g));
        }

        // The round trip holds whatever the current culture is.
        public void Registry_RoundTrip_UnderAnotherCulture()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
                var utc = new DateTime(2026, 3, 4, 5, 6, 7, 89, DateTimeKind.Utc);
                Check.Equal(utc, RoundTrip(utc));
                Check.Equal(TimeSpan.FromDays(-1.25), RoundTrip(TimeSpan.FromDays(-1.25)));
                Check.Equal((Half)(-0.333), RoundTrip((Half)(-0.333)));
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        // Text that is not a value of the type still reads as the default (the parse's error is not leaked).
        public void Registry_BadText_ReadsAsTheDefault()
        {
            string sub = NewKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub, "v", "not a value");
                Check.Equal(Guid.Empty, RegistryUtils.GetValue<Guid>(Hive, sub, "v"));
                Check.Equal(TimeSpan.FromSeconds(3), RegistryUtils.GetValue(Hive, sub, "v", TimeSpan.FromSeconds(3)));
                Check.Equal(default(DateTimeOffset), RegistryUtils.GetValue<DateTimeOffset>(Hive, sub, "v"));
                Check.Equal(default(DateTime), RegistryUtils.GetValue<DateTime>(Hive, sub, "v"));
                Check.Equal(default(Int128), RegistryUtils.GetValue<Int128>(Hive, sub, "v"));
                RegistryUtils.SetValue(Hive, sub, "big", "99999999999999999999999999999999999999999999");
                Check.Equal(default(Int128), RegistryUtils.GetValue<Int128>(Hive, sub, "big"));
                Check.Equal(default(Half), RegistryUtils.GetValue<Half>(Hive, sub, "v"));
                // Text written by hand in the old "G" form still reads.
                RegistryUtils.SetValue(Hive, sub, "g", "11/16/2026 13:45:07");
                Check.Equal(new DateTime(2026, 11, 16, 13, 45, 7), RegistryUtils.GetValue<DateTime>(Hive, sub, "g"));
            }
            finally { Cleanup(sub); }
        }

        // ---------- EventLogWriter ----------

        // A source under the Security log threw SecurityException without admin rights, though a missing one returned false.
        public void EventLog_IsSourceRegistered_SecurityLog_DoesNotThrow()
        {
            Check.True(EventLogWriter.IsSourceRegistered("Microsoft-Windows-Security-Auditing", "Security"));
            Check.False(EventLogWriter.IsSourceRegistered("ToolBelt-NoSuchSource-" + Guid.NewGuid().ToString("N"), "Security"));
        }

        // ---------- SingleInstanceApp ----------

        // More than 1000 arguments (1001 short file names fit a command line many times over) were dropped: the launch
        // was neither primary nor forwarded.
        public void SingleInstanceApp_ManyArguments_AreForwarded()
        {
            string id = "TbR110." + Guid.NewGuid().ToString("N").Substring(0, 12);
            using var primary = SingleInstanceApp.Start(id, Array.Empty<string>());
            Check.True(primary.IsPrimary);
            string[]? received = null;
            using var got = new System.Threading.ManualResetEventSlim();
            primary.SecondInstanceStarted += (_, e) => { received = e.Args.ToArray(); got.Set(); };
            var args = new string[5000];
            for (int i = 0; i < args.Length; i++) args[i] = "f" + i.ToString(CultureInfo.InvariantCulture) + ".txt";
            using var other = SingleInstanceApp.Start(id, args, TimeSpan.FromSeconds(10));
            Check.False(other.IsPrimary);
            Check.True(other.ForwardedToPrimary, "not forwarded");
            Check.True(got.Wait(TimeSpan.FromSeconds(10)), "not delivered");
            Check.Equal(args.Length, received!.Length);
            for (int i = 0; i < args.Length; i++) Check.Equal(args[i], received[i]);
        }

        // ---------- Argument checks ----------

        // A zero handle failed as a Win32Exception, as if the job had refused a process.
        public void JobObject_AssignZeroHandle_IsAnArgumentError()
        {
            using var job = new JobObject();
            Check.Equal("processHandle", Check.Throws<ArgumentException>(() => job.AssignProcess(IntPtr.Zero)).ParamName);
        }

        // A null in a string array threw RegistryKey's ArgumentException with no parameter name.
        public void Registry_NullInAStringArray_NamesValue()
        {
            string sub = NewKey();
            try
            {
                var ex = Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "m", new string?[] { "a", null }));
                Check.Equal("value", ex.ParamName);
                RegistryUtils.SetValue(Hive, sub, "m", new[] { "a", "" });
                Check.Equal(2, RegistryUtils.GetValue<string[]>(Hive, sub, "m")!.Length);
            }
            finally { Cleanup(sub); }
        }

        // \\.\PhysicalDrive0 and the like were refused as "network paths".
        public void Storage_DevicePaths_AreNotCalledNetworkPaths()
        {
            foreach (string p in new[] { @"\\.\PhysicalDrive0", @"\\?\GLOBALROOT\Device\HarddiskVolume1\", @"\\.\pipe\x" })
            {
                var ex = Check.Throws<ArgumentException>(() => StorageDeviceInfo.Describe(p));
                Check.Equal("path", ex.ParamName);
                Check.False(ex.Message.Contains("Network", StringComparison.Ordinal), p + ": " + ex.Message);
            }
            var unc = Check.Throws<ArgumentException>(() => StorageDeviceInfo.Describe(@"\\server\share\x"));
            Check.True(unc.Message.Contains("Network", StringComparison.Ordinal));
        }

        // "\\" and "\\server" were returned as UNC paths.
        public void Storage_GetUncPath_NeedsAServerAndAShare()
        {
            Check.Equal("path", Check.Throws<ArgumentException>(() => StorageDeviceInfo.GetUncPath(@"\\")).ParamName);
            Check.Equal("path", Check.Throws<ArgumentException>(() => StorageDeviceInfo.GetUncPath(@"\\server")).ParamName);
            Check.Equal(@"\\server\share\a", StorageDeviceInfo.GetUncPath(@"\\server\share\a"));
            Check.Equal(@"\\server\share", StorageDeviceInfo.GetUncPath(@"\\server\share"));
        }

        // \\?\Z:\x spells drive Z:, but any path starting \\ read as not a mapped drive. A local drive's device spelling
        // must agree with its plain spelling (a mapped drive is not available to test).
        public void Storage_IsNetworkDrive_DeviceSpellingAgrees()
        {
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Check.Equal(StorageDeviceInfo.IsNetworkDrive(sys), StorageDeviceInfo.IsNetworkDrive(@"\\?\" + sys));
            Check.False(StorageDeviceInfo.IsNetworkDrive(@"\\server\share\x"));
            MethodInfo? m = typeof(StorageDeviceInfo).GetMethod("IsUncOrDevice", BindingFlags.NonPublic | BindingFlags.Static);
            Check.True(m != null, "IsUncOrDevice is missing (the device spelling of a drive was treated as UNC)");
            Check.False((bool)m!.Invoke(null, new object[] { @"\\?\Z:\x" })!);
            Check.False((bool)m.Invoke(null, new object[] { @"\\.\Z:" })!);
            Check.True((bool)m.Invoke(null, new object[] { @"\\server\share" })!);
            Check.True((bool)m.Invoke(null, new object[] { @"\\?\UNC\server\share" })!);
        }

        // \\?\C: failed with a Win32Exception while Describe took it as the drive.
        public void DriveSpace_DeviceSpellingOfADrive_Works()
        {
            string drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!.TrimEnd('\\');
            DiskSpace plain = DriveAndVolumeInfo.GetSpace(drive + @"\");
            Check.Equal(plain.TotalBytes, DriveAndVolumeInfo.GetSpace(@"\\?\" + drive).TotalBytes);
            Check.Equal(plain.TotalBytes, DriveAndVolumeInfo.GetSpace(@"\\.\" + drive).TotalBytes);
        }
    }
}
