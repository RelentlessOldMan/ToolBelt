using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 164 (Windows satellite, pass 10) regressions.
    public sealed class WindowsReview164Tests
    {
        private static readonly string[] Cultures = { "de-DE", "fa-IR", "sv-SE", "ar-SA", "fr-CH", "hi-IN", "ps-AF" };

        private static T Make<T>(params object[] args)
            => (T)Activator.CreateInstance(typeof(T), BindingFlags.Instance | BindingFlags.NonPublic, null, args, null)!;

        // The text under every culture is the text under the invariant culture.
        private static void SameEverywhere(object value)
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                string expected = value.ToString()!;
                foreach (string c in Cultures)
                {
                    CultureInfo.CurrentCulture = new CultureInfo(c);
                    Check.Equal(expected, value.ToString(), value.GetType().Name + " in " + c);
                }
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        // ---------- ToString of the info structs followed the current culture ----------

        // SystemMemory, DiskSpace and ResourceSample printed "N0" and "0.0" in the current culture (17.554.702.336 in German,
        // CPU 0,0%), where OsVersion and MonitorDpi are invariant.
        public void InfoText_IsInvariant()
        {
            var rng = new Random(164);
            for (int i = 0; i < 200; i++)
            {
                long a = (long)(rng.NextDouble() * long.MaxValue), b = (long)(rng.NextDouble() * long.MaxValue);
                SameEverywhere(Make<SystemMemory>(rng.Next(0, 101), a, b, b, a));
                SameEverywhere(Make<DiskSpace>(a, b, b));
                SameEverywhere(Make<ResourceSample>(rng.NextDouble() * 800, a, b, TimeSpan.FromTicks(b / 1000)));
            }
            SameEverywhere(MemoryStatus.Query());
            SameEverywhere(DriveAndVolumeInfo.GetSpace(Environment.SystemDirectory));
            foreach (FixedDrive d in DriveAndVolumeInfo.FixedDrives()) SameEverywhere(d);
        }

        // ScreenRect printed a monitor left of or above the primary, at negative coordinates, with the culture's minus
        // (U+2212 in Swedish).
        public void ScreenRectText_IsInvariant()
        {
            var rng = new Random(1641);
            for (int i = 0; i < 200; i++)
            {
                int l = rng.Next(-40000, 40000), t = rng.Next(-40000, 40000);
                var r = Make<ScreenRect>(l, t, l + rng.Next(1, 8000), t + rng.Next(1, 8000));
                SameEverywhere(r);
                SameEverywhere(Make<MonitorDetails>(r, r, i % 2 == 0, @"\\.\DISPLAY" + i));
            }
            Check.Equal("(-1920,-8)-(0,1072) 1920x1080", Make<ScreenRect>(-1920, -8, 0, 1072).ToString());
            foreach (MonitorDetails m in MonitorInfo.All()) SameEverywhere(m);
        }

        // ---------- ScreenCapture: alpha as documented ----------

        // The docs said a capture leaves alpha zero; a capture through DWM came back 255 on every pixel. Alpha is now 255
        // on every pixel of every capture, whatever GDI left there.
        public void Capture_IsOpaqueOnEveryPixel()
        {
            ScreenImage[] images;
            try
            {
                images = new[]
                {
                    ScreenCapture.CaptureRegion(0, 0, 64, 32), ScreenCapture.CapturePrimaryScreen(),
                    ScreenCapture.CaptureVirtualScreen(),
                    // Off every monitor: GDI fills the part outside the desktop with alpha 0.
                    ScreenCapture.CaptureRegion(1000000, 1000000, 16, 16), ScreenCapture.CaptureRegion(-1000000, -5, 32, 8),
                };
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException) { return; }
            foreach (ScreenImage img in images)
                for (int i = 3; i < img.Pixels.Length; i += 4)
                    if (img.Pixels[i] != 255) { Check.Equal((byte)255, img.Pixels[i], "alpha at byte " + i); break; }
        }

        // ---------- ScreenCapture: regions GDI cannot hold in one bitmap ----------

        private static ScreenImage? TryCapture(int x, int y, int w, int h)
        {
            try { return ScreenCapture.CaptureRegion(x, y, w, h); }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException) { return null; }
        }

        // A region wider than GDI's bitmap limit (about 134,000,000 pixels) passed validation and threw "GetDIBits
        // failed" with a meaningless code. It is captured in tiles now; the part off every monitor is opaque black.
        public void Capture_WiderThanGdiHolds()
        {
            if (TryCapture(0, 0, 8, 8) == null) return;           // no desktop to capture
            ScreenImage img = ScreenCapture.CaptureRegion(-200_000_000, -3_000_000, 135_000_000, 1);
            Check.Equal(135_000_000, img.Width);
            Check.Equal(1, img.Height);
            Check.Equal(135_000_000L * 4, img.Pixels.LongLength);
            for (int i = 0; i < img.Pixels.Length; i += 4_000_001 * 4)
                Check.Equal(255, (int)img.Pixels[i + 3], "alpha at pixel " + i / 4);
        }

        // Tile seams land on the screen: pixels either side of a seam match a capture of that area alone.
        public void Capture_TileSeamsMatchADirectCapture()
        {
            if (TryCapture(0, 0, 8, 8) == null) return;
            MonitorDetails m = MonitorInfo.All().First(d => d.IsPrimary);
            int left = m.Bounds.Left, top = m.Bounds.Top;
            const int Tile = 1 << 24;
            // Horizontal: the second tile starts 100 pixels into the screen. Vertical: 4096 wide, so 4096 rows a tile.
            var cases = new[]
            {
                (x: left - Tile + 100, y: top + 10, w: Tile + 100, h: 2, sx: Tile - 100, sy: 0, dw: 200, dh: 2),
                (x: left, y: top - 4096 + 50, w: 4096, h: 4096 + 100, sx: 0, sy: 4096 - 50, dw: 300, dh: 100),
            };
            foreach (var c in cases)
            {
                bool same = false;
                string why = "";
                for (int attempt = 0; attempt < 4 && !same; attempt++)   // the screen may change between captures
                {
                    ScreenImage big = ScreenCapture.CaptureRegion(c.x, c.y, c.w, c.h);
                    ScreenImage direct = ScreenCapture.CaptureRegion(c.x + c.sx, c.y + c.sy, c.dw, c.dh);
                    same = true;
                    for (int r = 0; r < c.dh && same; r++)
                        for (int k = 0; k < c.dw * 4; k++)
                        {
                            byte a = big.Pixels[((long)(c.sy + r) * c.w + c.sx) * 4 + k], b = direct.Pixels[(r * c.dw) * 4 + k];
                            if (a != b) { same = false; why = $"row {r} byte {k}: {a} vs {b}"; break; }
                        }
                }
                Check.True(same, "tiled capture differs from a direct one at " + why);
            }
        }

        // ---------- RegistryUtils.SetValue: a path Windows refuses part way ----------

        private const RegistryHive Hive = RegistryHive.CurrentUser;

        private static string[] SubKeysOf(string sub)
        {
            using RegistryKey? k = Registry.CurrentUser.OpenSubKey(sub);
            return k?.GetSubKeyNames() ?? Array.Empty<string>();
        }

        // 600 levels threw IOException after creating 30 of them, left behind; a 35,000-character path threw IOException.
        // Both are refused as ArgumentException(subKey), and what was made is removed while existing keys are kept.
        public void Registry_RefusedCreateLeavesNothing()
        {
            string root = @"Software\ToolBelt.Windows.Tests\R164." + Guid.NewGuid().ToString("N");
            try
            {
                RegistryUtils.SetValue(Hive, root + @"\keep\inner", "v", 7);
                string deep = root + @"\keep" + string.Concat(Enumerable.Repeat(@"\a", 600));
                var ex = Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, deep, "v", 1));
                Check.Equal("subKey", ex.ParamName);
                Check.Equal("inner", string.Join(",", SubKeysOf(root + @"\keep")));
                Check.Equal(7, RegistryUtils.GetValue<int>(Hive, root + @"\keep\inner", "v"));

                string longPath = root + @"\" + string.Join(@"\", Enumerable.Repeat(new string('b', 250), 140));
                ex = Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, longPath, "v", 1));
                Check.Equal("subKey", ex.ParamName);
                Check.Equal("keep", string.Join(",", SubKeysOf(root)));

                // A path that fits still works.
                RegistryUtils.SetValue(Hive, root + @"\keep\x\y", "v", 3);
                Check.Equal(3, RegistryUtils.GetValue<int>(Hive, root + @"\keep\x\y", "v"));
            }
            finally { try { Registry.CurrentUser.DeleteSubKeyTree(root, false); } catch (Exception) { } }
        }

        // ---------- ShortcutUtils.Create: a relative target ----------

        // The shell rooted sub\x.exe at the drive root (C:\sub\x.exe); it is resolved against the current directory.
        public void Shortcut_RelativeTargetIsResolvedAgainstTheCurrentDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb164-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "work", "sub"));
            string saved = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = Path.Combine(dir, "work");
                File.WriteAllText(Path.Combine(dir, "work", "here.txt"), "");
                string lnk = Path.Combine(dir, "s.lnk");
                foreach (string t in new[] { @"sub\x.exe", @"..\x.exe", "sub/y.exe", @".\here.txt", "here.txt" })
                {
                    ShortcutUtils.Create(lnk, t);
                    Check.Equal(Path.GetFullPath(t), ShortcutUtils.Read(lnk).TargetPath, t);
                }
                // Left for the shell: a name found on the PATH and an environment variable.
                ShortcutUtils.Create(lnk, "notepad.exe");
                Check.Equal(Path.Combine(Environment.SystemDirectory, "notepad.exe").ToUpperInvariant(),
                    ShortcutUtils.Read(lnk).TargetPath.ToUpperInvariant(), "notepad.exe");
            }
            finally
            {
                Environment.CurrentDirectory = saved;
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }
    }
}
