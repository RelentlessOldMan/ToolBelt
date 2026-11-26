using System;
using System.Linq;
using System.Numerics;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 221 (Windows satellite, pass 14) regressions.
    public sealed class WindowsReview221Tests
    {
        private const RegistryHive Hive = RegistryHive.CurrentUser;

        private static string FreshSubKey() => @"Software\ToolBelt.Windows.Tests\" + Guid.NewGuid().ToString("N");

        private static void DeleteTree(string sub)
        {
            using RegistryKey cu = Registry.CurrentUser;
            try { cu.DeleteSubKeyTree(sub, throwOnMissingSubKey: false); } catch (Exception) { }
        }

        // ---------- RegistryUtils.SetValue ----------

        // A string[] or byte[] given String or ExpandString was stored as "System.String[]" or "System.Byte[]".
        public void SetValue_AnArrayGivenATextKind_IsRefused()
        {
            string sub = FreshSubKey();
            try
            {
                var cases = new (object Value, RegistryValueKind Kind)[]
                {
                    (new[] { "x", "y" }, RegistryValueKind.String),
                    (new[] { "x", "y" }, RegistryValueKind.ExpandString),
                    (new byte[] { 1, 2 }, RegistryValueKind.String),
                    (new byte[] { 1, 2 }, RegistryValueKind.ExpandString),
                };
                foreach (var (value, kind) in cases)
                {
                    var ex = Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "v", value, kind), $"{value.GetType().Name} as {kind}");
                    Check.Equal("value", ex.ParamName);
                    Check.Null(RegistryUtils.GetValue(Hive, sub, "v"), "nothing was stored");
                }
                // The kinds that fit still work.
                RegistryUtils.SetValue(Hive, sub, "m", new[] { "x", "y" }, RegistryValueKind.MultiString);
                Check.True(new[] { "x", "y" }.SequenceEqual((string[])RegistryUtils.GetValue(Hive, sub, "m")!));
                RegistryUtils.SetValue(Hive, sub, "b", new byte[] { 1, 2 }, RegistryValueKind.Binary);
                Check.True(new byte[] { 1, 2 }.SequenceEqual((byte[])RegistryUtils.GetValue(Hive, sub, "b")!));
            }
            finally { DeleteTree(sub); }
        }

        // REG_MULTI_SZ ends an element at a NUL: ["a\0b"] read back as ["a", "b"].
        public void SetValue_AMultiStringElementWithANul_IsRefused()
        {
            string sub = FreshSubKey();
            try
            {
                foreach (RegistryValueKind kind in new[] { RegistryValueKind.MultiString, RegistryValueKind.Unknown })
                {
                    var ex = Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "v", new[] { "ok", "a\0b" }, kind), kind.ToString());
                    Check.Equal("value", ex.ParamName);
                    Check.Null(RegistryUtils.GetValue(Hive, sub, "v"), "nothing was stored");
                }
                RegistryUtils.SetValue(Hive, sub, "e", new[] { "", "a", "" }, RegistryValueKind.MultiString);   // empty elements are fine
                Check.Equal(3, ((string[])RegistryUtils.GetValue(Hive, sub, "e")!).Length);
            }
            finally { DeleteTree(sub); }
        }

        // The doc says any integer given DWord or QWord is stored by its bits; nint, nuint, Int128, UInt128, BigInteger
        // and a whole Half were refused as types RegistryKey cannot store. Each is graded against the same number as a long.
        public void SetValue_EveryIntegerTypeGivenAWord_IsStoredByItsBits()
        {
            string sub = FreshSubKey();
            try
            {
                var rng = new Random(221);
                for (int trial = 0; trial < 200; trial++)
                {
                    bool qword = (trial & 1) == 1;
                    RegistryValueKind kind = qword ? RegistryValueKind.QWord : RegistryValueKind.DWord;
                    long n = trial < 8 ? new long[] { 0, 1, -1, int.MinValue, uint.MaxValue, int.MaxValue, long.MinValue, long.MaxValue }[trial]
                        : (long)(rng.NextDouble() * 2e9) * (rng.Next(2) == 0 ? -1 : 1);
                    if (!qword && (n < int.MinValue || n > uint.MaxValue)) n = (int)n;
                    RegistryUtils.SetValue(Hive, sub, "ref", n, kind);
                    object expected = RegistryUtils.GetValue(Hive, sub, "ref")!;
                    var values = new System.Collections.Generic.List<object> { (Int128)n, (BigInteger)n, (nint)n };
                    if (n >= 0) { values.Add((UInt128)(ulong)n); values.Add((nuint)(ulong)n); }
                    if (Math.Abs(n) <= 2048) values.Add((Half)n);
                    foreach (object v in values)
                    {
                        RegistryUtils.SetValue(Hive, sub, "v", v, kind);
                        Check.Equal(expected, RegistryUtils.GetValue(Hive, sub, "v")!, $"{v.GetType().Name} {n} as {kind}");
                    }
                }
                Check.Throws<ArgumentOutOfRangeException>(() => RegistryUtils.SetValue(Hive, sub, "v", (Int128)uint.MaxValue + 1, RegistryValueKind.DWord));
                Check.Throws<ArgumentOutOfRangeException>(() => RegistryUtils.SetValue(Hive, sub, "v", BigInteger.One << 64, RegistryValueKind.QWord));
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "v", (Half)1.5, RegistryValueKind.DWord)).ParamName);
            }
            finally { DeleteTree(sub); }
        }

        // ---------- RegistryUtils: the PerformanceData hive ----------

        // OpenSubKey("") returns null for this hive alone, so every value in it read as absent.
        public void PerformanceData_TheHiveItself_ReadsItsValues()
        {
            string[] raw;
            using (RegistryKey perf = RegistryKey.OpenBaseKey(RegistryHive.PerformanceData, RegistryView.Default))
                raw = perf.GetValueNames();
            if (raw.Length == 0) return;   // no performance data on this machine: nothing to compare
            Check.True(RegistryUtils.GetValueNames(RegistryHive.PerformanceData, "").Count > 0, "value names");
            Check.True(RegistryUtils.GetValue(RegistryHive.PerformanceData, "", "Global") is byte[] { Length: > 0 }, "the Global counters");
            Check.True(RegistryUtils.KeyExists(RegistryHive.PerformanceData, ""), "the hive exists");
            // Other hives read their root as before.
            Check.True(RegistryUtils.GetSubKeyNames(Hive, "").Contains("Software", StringComparer.OrdinalIgnoreCase));
        }

        // ---------- ScreenCapture ----------

        // A 1-pixel-wide region 2^24 tall made a 1 x 2^24 tile, one past GDI's limit: Win32Exception.
        public void CaptureRegion_OnePixelWideAndTwoToTheTwentyFourTall_Works()
        {
            foreach (int h in new[] { 1 << 24, (1 << 24) + 5 })
            {
                ScreenImage img = ScreenCapture.CaptureRegion(0, 0, 1, h);
                Check.Equal(1, img.Width);
                Check.Equal(h, img.Height);
                // The first pixel is on the primary screen; it must match a small capture of the same spot.
                ScreenImage small = ScreenCapture.CaptureRegion(0, 0, 1, 1);
                Check.Equal(small.Pixels[3], img.Pixels[3], "alpha of the first pixel");
            }
        }
    }
}
