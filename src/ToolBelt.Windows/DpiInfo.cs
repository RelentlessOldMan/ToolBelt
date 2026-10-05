// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (user32/shcore DPI APIs, Windows 10 1703+).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>How a thread sees DPI (the DPI_AWARENESS_CONTEXT values).</summary>
    public enum DpiAwareness
    {
        /// <summary>Unaware: Windows bitmap-stretches the window and reports scaled (96-dpi) coordinates.</summary>
        Unaware,
        /// <summary>System aware: one DPI for all monitors, fixed at process start.</summary>
        System,
        /// <summary>Per-monitor (v1).</summary>
        PerMonitor,
        /// <summary>Per-monitor v2: physical pixels everywhere; what screen capture and pixel-exact work need.</summary>
        PerMonitorV2,
        /// <summary>GDI-scaled unaware.</summary>
        UnawareGdiScaled,
    }

    /// <summary>Scale information for one monitor.</summary>
    public readonly struct MonitorDpi
    {
        public MonitorDpi(string deviceName, int dpiX, int dpiY, bool isPrimary, int left, int top, int width, int height)
        {
            DeviceName = deviceName;
            DpiX = dpiX;
            DpiY = dpiY;
            IsPrimary = isPrimary;
            Left = left; Top = top; Width = width; Height = height;
        }

        public string DeviceName { get; }
        public int DpiX { get; }
        public int DpiY { get; }

        /// <summary>The Settings ▸ Display scale as a fraction (1.25 = 125%).</summary>
        public double Scale => DpiX / 96.0;

        public bool IsPrimary { get; }

        /// <summary>Monitor bounds in physical pixels (as seen by a per-monitor-v2 thread).</summary>
        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }

        public override string ToString() => $"{DeviceName}: {DpiX} dpi ({Scale:P0}){(IsPrimary ? " primary" : "")}";
    }

    /// <summary>
    /// DPI and display scaling: each monitor's effective DPI and scale factor, the system DPI, the calling thread's DPI
    /// awareness — and <see cref="AwarenessScope"/>, which switches the <em>current thread</em> to per-monitor-v2 (or any
    /// awareness) and restores it on dispose. Run screen capture or window-geometry code inside such a scope so
    /// coordinates are physical pixels rather than DPI-virtualized ones; that's the difference between a sharp 3840×2160
    /// capture and a blurry upscaled 2560×1440 one on a 150% display.
    /// </summary>
    public static class DpiInfo
    {
        /// <summary>The system DPI (96 = 100%).</summary>
        public static int SystemDpi => (int)GetDpiForSystem();

        /// <summary>The DPI awareness of the calling thread.</summary>
        public static DpiAwareness CurrentAwareness => FromContext(GetThreadDpiAwarenessContext());

        /// <summary>Every monitor's effective DPI and physical bounds (queried per-monitor-v2 so the values are real).</summary>
        public static IReadOnlyList<MonitorDpi> Monitors()
        {
            using (AwarenessScope(DpiAwareness.PerMonitorV2))
            {
                var result = new List<MonitorDpi>();
                MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
                {
                    var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                    GetMonitorInfo(hMonitor, ref info);
                    int hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dx, out uint dy);
                    if (hr != 0) { dx = dy = GetDpiForSystem(); }
                    result.Add(new MonitorDpi(info.szDevice, (int)dx, (int)dy, (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top));
                    return true;
                };
                if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
                GC.KeepAlive(callback);
                return result;
            }
        }

        /// <summary>The DPI of the monitor showing the window (or nearest to it).</summary>
        public static int ForWindow(IntPtr hwnd)
        {
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi == 0) throw new ArgumentException("Not a valid window handle.", nameof(hwnd));
            return (int)dpi;
        }

        /// <summary>Pixels at <paramref name="dpi"/> for a length given in 96-dpi device-independent units.</summary>
        public static int Scale(int logicalPixels, int dpi) => (int)Math.Round(logicalPixels * dpi / 96.0, MidpointRounding.AwayFromZero);

        /// <summary>
        /// Sets the calling thread's DPI awareness until disposed (then restores it). Windows created on the thread while
        /// the scope is open keep the awareness they were created with.
        /// </summary>
        public static IDisposable AwarenessScope(DpiAwareness awareness)
        {
            IntPtr previous = SetThreadDpiAwarenessContext(ToContext(awareness));
            if (previous == IntPtr.Zero) throw new InvalidOperationException("SetThreadDpiAwarenessContext failed (needs Windows 10 1607+; mixed-mode DPI may be disabled for the process).");
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private IntPtr _previous;
            public Restore(IntPtr previous) => _previous = previous;

            public void Dispose()
            {
                if (_previous == IntPtr.Zero) return;
                SetThreadDpiAwarenessContext(_previous);
                _previous = IntPtr.Zero;
            }
        }

        private static IntPtr ToContext(DpiAwareness a) => a switch
        {
            DpiAwareness.Unaware => new IntPtr(-1),
            DpiAwareness.System => new IntPtr(-2),
            DpiAwareness.PerMonitor => new IntPtr(-3),
            DpiAwareness.PerMonitorV2 => new IntPtr(-4),
            DpiAwareness.UnawareGdiScaled => new IntPtr(-5),
            _ => throw new ArgumentOutOfRangeException(nameof(a)),
        };

        private static DpiAwareness FromContext(IntPtr context)
        {
            foreach (DpiAwareness a in Enum.GetValues(typeof(DpiAwareness)))
                if (AreDpiAwarenessContextsEqual(context, ToContext(a))) return a;
            return GetAwarenessFromDpiAwarenessContext(context) switch
            {
                0 => DpiAwareness.Unaware,
                1 => DpiAwareness.System,
                _ => DpiAwareness.PerMonitor,
            };
        }

        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint MONITORINFOF_PRIMARY = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
        [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int type, out uint dpiX, out uint dpiY);
    }
}
