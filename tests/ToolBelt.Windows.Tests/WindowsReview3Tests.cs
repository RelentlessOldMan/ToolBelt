using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Round 41 review regressions for the Windows satellite (third pass): each test failed before its fix.
    public sealed class WindowsReview3Tests
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

        // ---------- DpiInfo ----------

        public void DpiInfo_SystemDpi_IsTheRealOneForAnUnawareThread()
        {
            int real;
            using (DpiInfo.AwarenessScope(DpiAwareness.PerMonitorV2)) real = DpiInfo.SystemDpi;
            using (DpiInfo.AwarenessScope(DpiAwareness.Unaware))
                Check.Equal(real, DpiInfo.SystemDpi);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string cls, string? title, int style, int x, int y, int w, int h,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);

        public void DpiInfo_ForWindow_IsTheMonitorsDpiForAnUnawareWindow()
        {
            // GetDpiForWindow gave 96 for a DPI-unaware window on a 200% monitor.
            int monitorDpi = DpiInfo.Monitors().First(m => m.IsPrimary).DpiX;
            int dpi = OnThread(() =>
            {
                IntPtr hwnd;
                using (DpiInfo.AwarenessScope(DpiAwareness.Unaware))
                    hwnd = CreateWindowEx(0, "STATIC", "tb", 0, 10, 10, 50, 50, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                Check.True(hwnd != IntPtr.Zero, "CreateWindowEx failed");
                try { return DpiInfo.ForWindow(hwnd); }
                finally { DestroyWindow(hwnd); }
            });
            Check.Equal(monitorDpi, dpi);
        }

        public void DpiInfo_AwarenessScope_UnknownValueNamesAwareness()
        {
            var e = Check.Throws<ArgumentOutOfRangeException>(() => DpiInfo.AwarenessScope((DpiAwareness)99));
            Check.Equal("awareness", e.ParamName);
        }

        // ---------- RegistryUtils ----------

        private static string TestKey() => @"Software\ToolBeltReviewTest\" + Guid.NewGuid().ToString("N");

        [Flags] private enum Bits32 : int { None = 0, Low = 1, High = unchecked((int)0x80000000) }
        [Flags] private enum UBits32 : uint { None = 0, Low = 1, High = 0x80000000 }
        [Flags] private enum Bits64 : long { None = 0, Low = 1, High = long.MinValue }

        public void RegistryUtils_HighBitDwordReadsIntoAnEnumOfItsWidth()
        {
            string key = TestKey();
            try
            {
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "d", unchecked((int)0x80000001), RegistryValueKind.DWord);
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "q", long.MinValue + 1, RegistryValueKind.QWord);
                Check.Equal(Bits32.High | Bits32.Low, RegistryUtils.GetValue<Bits32>(RegistryHive.CurrentUser, key, "d"));
                Check.Equal(UBits32.High | UBits32.Low, RegistryUtils.GetValue<UBits32>(RegistryHive.CurrentUser, key, "d"));
                Check.Equal(Bits64.High | Bits64.Low, RegistryUtils.GetValue<Bits64>(RegistryHive.CurrentUser, key, "q"));
            }
            finally { RegistryUtils.DeleteKey(RegistryHive.CurrentUser, key); }
        }

        public void RegistryUtils_UnsignedValuesAreStoredAsTheirBits()
        {
            string key = TestKey();
            try
            {
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "a", 0x80000000u, RegistryValueKind.DWord);
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "b", 0x80000000u);
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "c", ulong.MaxValue, RegistryValueKind.QWord);
                Check.Equal(0x80000000u, RegistryUtils.GetValue<uint>(RegistryHive.CurrentUser, key, "a"));
                Check.Equal(0x80000000u, RegistryUtils.GetValue<uint>(RegistryHive.CurrentUser, key, "b"));
                Check.Equal(ulong.MaxValue, RegistryUtils.GetValue<ulong>(RegistryHive.CurrentUser, key, "c"));
                using RegistryKey k = Registry.CurrentUser.OpenSubKey(key)!;
                Check.Equal(RegistryValueKind.DWord, k.GetValueKind("b"));
                Check.Equal(RegistryValueKind.QWord, k.GetValueKind("c"));
            }
            finally { RegistryUtils.DeleteKey(RegistryHive.CurrentUser, key); }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegSetKeyValueW(IntPtr hKey, string subKey, string valueName, int type, byte[] data, int size);

        public void RegistryUtils_DeleteValue_DeletesAKindDotNetCannotRead()
        {
            string key = TestKey();
            try
            {
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "keep", 1);
                const int REG_RESOURCE_LIST = 8;
                Check.Equal(0, RegSetKeyValueW(new IntPtr(unchecked((int)0x80000001)), key, "r", REG_RESOURCE_LIST, new byte[] { 0, 0, 0, 0 }, 4));
                Check.True(RegistryUtils.GetValueNames(RegistryHive.CurrentUser, key).Contains("r"));
                Check.True(RegistryUtils.DeleteValue(RegistryHive.CurrentUser, key, "r"), "reported missing");
                Check.False(RegistryUtils.GetValueNames(RegistryHive.CurrentUser, key).Contains("r"), "not deleted");
                Check.False(RegistryUtils.DeleteValue(RegistryHive.CurrentUser, key, "r"));
            }
            finally { RegistryUtils.DeleteKey(RegistryHive.CurrentUser, key); }
        }

        public void RegistryUtils_DeleteKey_RefusesThePathOfTheHiveItself()
        {
            // DeleteKey(hive, @"\") deleted everything in the hive. The invalid hive keeps this test harmless if the guard
            // is ever lost: the subkey must be rejected before the hive is opened.
            foreach (string subKey in new[] { "", @"\", @"\\", @"\\\" })
            {
                var e = Check.Throws<ArgumentException>(() => RegistryUtils.DeleteKey((RegistryHive)123, subKey), "'" + subKey + "'");
                Check.Equal("subKey", e.ParamName);
            }
        }

        public void RegistryUtils_BadHiveAndOverlongSegment_AreArgumentErrors()
        {
            var e = Check.Throws<ArgumentOutOfRangeException>(() => RegistryUtils.GetValue((RegistryHive)123, "Software", "x"));
            Check.Equal("hive", e.ParamName);
            string longKey = @"Software\ToolBeltReviewTest\" + new string('x', 300);
            Check.Equal(5, RegistryUtils.GetValue(RegistryHive.CurrentUser, longKey, "v", 5));
            Check.False(RegistryUtils.KeyExists(RegistryHive.CurrentUser, longKey));
            var w = Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(RegistryHive.CurrentUser, longKey, "v", 1));
            Check.Equal("subKey", w.ParamName);
        }

        // ---------- ResourceSampler ----------

        public void ResourceSampler_Measure_OverlongIntervalNamesInterval()
        {
            using var self = Process.GetCurrentProcess();
            var e = Check.Throws<ArgumentOutOfRangeException>(() => ResourceSampler.Measure(self, TimeSpan.FromDays(30)));
            Check.Equal("interval", e.ParamName);
        }

        // ---------- SingleInstanceApp ----------

        private static string NewAppId() => "TbR41." + Guid.NewGuid().ToString("N").Substring(0, 12);

        private static string PipeName(string appId)
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var self = Process.GetCurrentProcess();
            return "ToolBelt.SingleInstance." + appId + "." + self.SessionId + "." + identity.User!.Value;
        }

        private static string GateName(string appId)
        {
            using var identity = WindowsIdentity.GetCurrent();
            return @"Local\ToolBelt.SingleInstance." + appId + "." + identity.User!.Value;
        }

        public void SingleInstanceApp_ForwardsAMessageWithinThePrimarysBudget()
        {
            // The client counted the length headers against the 1 MiB budget, the primary does not: 1,045,000 bytes of
            // arguments were refused (and the launch lost) though the primary would take them.
            string id = NewAppId();
            using var primary = SingleInstanceApp.Start(id, Array.Empty<string>());
            Check.True(primary.IsPrimary);
            string[] args = Enumerable.Range(0, 1000).Select(i => new string((char)('a' + i % 26), 1045)).ToArray();
            bool forwarded = OnThread(() => { using var s = SingleInstanceApp.Start(id, args, TimeSpan.FromSeconds(20)); return s.ForwardedToPrimary; });
            Check.True(forwarded, "not forwarded");
        }

        public void SingleInstanceApp_ForwardTimeout_BoundsAPrimaryThatStopsReading()
        {
            // The primary takes the connection and sends its id, then never reads: the synchronous write and Flush hung the
            // launch for good.
            string id = NewAppId();
            using var gate = SingleInstance.TryAcquire(GateName(id));
            Check.True(gate.IsOwned);
            using var server = new NamedPipeServerStream(PipeName(id), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var fake = new Thread(() =>
            {
                try
                {
                    server.WaitForConnection();
                    server.Write(BitConverter.GetBytes(Environment.ProcessId));
                }
                catch (Exception) { }
            }) { IsBackground = true };
            fake.Start();
            bool? forwarded = null;
            var launch = new Thread(() =>
            {
                try { using var s = SingleInstanceApp.Start(id, new[] { "aaaaaaaaaa" }, TimeSpan.FromSeconds(2)); forwarded = s.ForwardedToPrimary; }
                catch (Exception) { forwarded = true; }
            }) { IsBackground = true };
            var sw = Stopwatch.StartNew();
            launch.Start();
            Check.True(launch.Join(15000), "the launch hung");
            Check.True(sw.ElapsedMilliseconds < 8000, $"a 2 s forward timeout took {sw.ElapsedMilliseconds} ms");
            Check.Equal(false, forwarded);
        }
    }
}
