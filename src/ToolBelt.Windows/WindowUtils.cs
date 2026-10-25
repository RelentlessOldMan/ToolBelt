// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Win32 user32).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ToolBelt.Windows
{
    /// <summary>A top-level window: its handle, title, class name, owning process id, and visibility.</summary>
    public readonly struct WindowInfo
    {
        internal WindowInfo(IntPtr handle, string title, string className, int processId, bool isVisible)
        {
            Handle = handle;
            Title = title;
            ClassName = className;
            ProcessId = processId;
            IsVisible = isVisible;
        }

        public IntPtr Handle { get; }
        public string Title { get; }
        public string ClassName { get; }
        public int ProcessId { get; }
        public bool IsVisible { get; }

        public override string ToString() => $"'{Title}' [{ClassName}] pid={ProcessId}";
    }

    /// <summary>
    /// Read-only inspection of top-level desktop windows via the Win32 window APIs: enumerate them, read the
    /// foreground window, and look one up by title. Does not move, resize, or close windows.
    /// </summary>
    public static class WindowUtils
    {
        /// <summary>
        /// Enumerates top-level windows. When <paramref name="visibleTitledOnly"/> is true (default), only
        /// visible windows with a non-empty title are returned — the windows a user would recognise.
        /// </summary>
        public static IReadOnlyList<WindowInfo> TopLevelWindows(bool visibleTitledOnly = true)
        {
            var windows = new List<WindowInfo>();
            EnumWindowsProc callback = (hwnd, _) =>
            {
                bool visible = IsWindowVisible(hwnd);
                string title = ReadText(hwnd);
                if (visibleTitledOnly && (!visible || title.Length == 0))
                    return true;
                GetWindowThreadProcessId(hwnd, out int pid);
                windows.Add(new WindowInfo(hwnd, title, ReadClass(hwnd), pid, visible));
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return windows;
        }

        /// <summary>The handle of the foreground window, or <see cref="IntPtr.Zero"/> if there is none.</summary>
        public static IntPtr ForegroundWindow() => GetForegroundWindow();

        /// <summary>Details of the foreground window, or null if there is none.</summary>
        public static WindowInfo? ForegroundWindowInfo()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out int pid);
            return new WindowInfo(hwnd, ReadText(hwnd), ReadClass(hwnd), pid, IsWindowVisible(hwnd));
        }

        /// <summary>The title text of a window handle (empty if it has none).</summary>
        public static string GetWindowTitle(IntPtr hwnd) => ReadText(hwnd);

        /// <summary>The first top-level window whose title contains <paramref name="substring"/> (ordinal, case-insensitive), or null.</summary>
        public static WindowInfo? FindByTitle(string substring)
        {
            if (substring is null) throw new ArgumentNullException(nameof(substring));
            foreach (var w in TopLevelWindows())
                if (w.Title.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0)
                    return w;
            return null;
        }

        // InternalGetWindowText reads the stored title without sending WM_GETTEXT. GetWindowText sends that message to
        // windows of this process, which blocks for as long as the owning thread isn't pumping (a deadlock when it is
        // waiting on the caller).
        private static string ReadText(IntPtr hwnd)
        {
            for (int capacity = 256; ; capacity *= 2)
            {
                var buffer = new char[capacity];
                int len = InternalGetWindowText(hwnd, buffer, capacity);
                if (len <= 0) return string.Empty;
                if (len < capacity - 1 || capacity >= 1 << 16) return new string(buffer, 0, Math.Min(len, capacity));
            }
        }

        private static string ReadClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int InternalGetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
    }
}
