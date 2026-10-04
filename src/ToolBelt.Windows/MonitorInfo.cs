// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Win32 P/Invoke).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>An integer screen rectangle (pixels), left/top inclusive, right/bottom exclusive.</summary>
    public readonly struct ScreenRect
    {
        internal ScreenRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
    }

    /// <summary>A display monitor: its full bounds, its work area (excluding the taskbar), and whether it is primary.</summary>
    public readonly struct MonitorDetails
    {
        internal MonitorDetails(ScreenRect bounds, ScreenRect workArea, bool isPrimary, string deviceName)
        {
            Bounds = bounds;
            WorkArea = workArea;
            IsPrimary = isPrimary;
            DeviceName = deviceName;
        }

        public ScreenRect Bounds { get; }
        public ScreenRect WorkArea { get; }
        public bool IsPrimary { get; }
        public string DeviceName { get; }

        public override string ToString() =>
            $"{DeviceName} {Bounds}{(IsPrimary ? " [primary]" : "")}";
    }

    /// <summary>Enumerates attached display monitors via the Win32 <c>EnumDisplayMonitors</c> API.</summary>
    public static class MonitorInfo
    {
        /// <summary>Returns every attached monitor. May be empty on a headless session with no display.</summary>
        public static IReadOnlyList<MonitorDetails> All()
        {
            var monitors = new List<MonitorDetails>();
            MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    monitors.Add(new MonitorDetails(
                        ToRect(info.rcMonitor),
                        ToRect(info.rcWork),
                        (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        info.szDevice ?? ""));
                }
                return true; // continue enumeration
            };

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                return Array.Empty<MonitorDetails>();
            GC.KeepAlive(callback);
            return monitors;
        }

        /// <summary>Returns the primary monitor, or null if none is reported.</summary>
        public static MonitorDetails? Primary()
        {
            foreach (var m in All())
                if (m.IsPrimary)
                    return m;
            return null;
        }

        private static ScreenRect ToRect(RECT r) => new ScreenRect(r.left, r.top, r.right, r.bottom);

        private const int MONITORINFOF_PRIMARY = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    }
}
