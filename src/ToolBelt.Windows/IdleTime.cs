// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (user32 P/Invoke).
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>
    /// Reports how long the user has been idle (no keyboard or mouse input) via the Win32
    /// <c>GetLastInputInfo</c> API — useful for screensaver/auto-lock style behaviour. Requires an
    /// interactive session; it throws in a non-interactive (service / session 0) context.
    /// </summary>
    public static class IdleTime
    {
        /// <summary>The time since the last user input across the session.</summary>
        public static TimeSpan Get()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetLastInputInfo failed (no interactive session?).");

            // Both GetLastInputInfo's tick and Environment.TickCount share GetTickCount's 32-bit ms domain,
            // so an unsigned subtraction handles the ~49.7-day wrap correctly.
            uint now = unchecked((uint)Environment.TickCount);
            uint idleMs = unchecked(now - info.dwTime);
            return TimeSpan.FromMilliseconds(idleMs);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    }
}
