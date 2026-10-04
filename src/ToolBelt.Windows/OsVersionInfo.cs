// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (ntdll P/Invoke).
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>The true Windows version (major/minor/build).</summary>
    public readonly struct OsVersion
    {
        internal OsVersion(int major, int minor, int build)
        {
            Major = major;
            Minor = minor;
            Build = build;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Build { get; }

        /// <summary>True on Windows 10 (build ≥ 10240) or later.</summary>
        public bool IsWindows10OrGreater => Major > 10 || (Major == 10 && Build >= 10240);

        /// <summary>True on Windows 11 (build ≥ 22000) or later.</summary>
        public bool IsWindows11OrGreater => Major > 10 || (Major == 10 && Build >= 22000);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, Build);
    }

    /// <summary>
    /// Reports the real OS version via <c>RtlGetVersion</c>. Unlike <see cref="Environment.OSVersion"/>,
    /// this is not subject to application-manifest "compatibility" version lies — it returns the actual
    /// running Windows version, which is why Windows 11 detection (build ≥ 22000) works.
    /// </summary>
    public static class OsVersionInfo
    {
        /// <summary>Queries the current OS version.</summary>
        public static OsVersion Get()
        {
            var info = new RTL_OSVERSIONINFOW { dwOSVersionInfoSize = (uint)Marshal.SizeOf<RTL_OSVERSIONINFOW>() };
            int status = RtlGetVersion(ref info);
            if (status != 0) // STATUS_SUCCESS == 0
                throw new Win32Exception(status, "RtlGetVersion failed.");
            return new OsVersion((int)info.dwMajorVersion, (int)info.dwMinorVersion, (int)info.dwBuildNumber);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RTL_OSVERSIONINFOW
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
        }

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOW lpVersionInformation);
    }
}
