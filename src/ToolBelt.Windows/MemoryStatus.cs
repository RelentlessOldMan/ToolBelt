// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (kernel32 P/Invoke).
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>A snapshot of system-wide physical and page-file memory.</summary>
    public readonly struct SystemMemory
    {
        internal SystemMemory(int load, long totalPhys, long availPhys, long totalPage, long availPage)
        {
            MemoryLoadPercent = load;
            TotalPhysicalBytes = totalPhys;
            AvailablePhysicalBytes = availPhys;
            TotalPageFileBytes = totalPage;
            AvailablePageFileBytes = availPage;
        }

        /// <summary>Approximate percentage of physical memory in use (0–100).</summary>
        public int MemoryLoadPercent { get; }

        public long TotalPhysicalBytes { get; }
        public long AvailablePhysicalBytes { get; }
        public long TotalPageFileBytes { get; }
        public long AvailablePageFileBytes { get; }

        /// <summary>Physical memory currently in use, in bytes.</summary>
        public long UsedPhysicalBytes => TotalPhysicalBytes - AvailablePhysicalBytes;

        // Invariant, as OsVersion and MonitorDpi are: "N0" grouped with '.' in German and Arabic-Indic separators in Persian.
        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0}% used, {1:N0} of {2:N0} bytes free", MemoryLoadPercent, AvailablePhysicalBytes, TotalPhysicalBytes);
    }

    /// <summary>Reads system-wide memory figures via the Win32 <c>GlobalMemoryStatusEx</c> API.</summary>
    public static class MemoryStatus
    {
        /// <summary>Queries current system memory usage.</summary>
        public static SystemMemory Query()
        {
            var info = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalMemoryStatusEx failed.");
            return new SystemMemory(
                (int)info.dwMemoryLoad,
                (long)info.ullTotalPhys,
                (long)info.ullAvailPhys,
                (long)info.ullTotalPageFile,
                (long)info.ullAvailPageFile);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
