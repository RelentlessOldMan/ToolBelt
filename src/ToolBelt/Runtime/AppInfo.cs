// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ToolBelt.Runtime
{
    /// <summary>
    /// One place for the application's identity: assembly version, informational version, build
    /// configuration, an approximate build timestamp, entry-assembly location, and the runtime description —
    /// otherwise re-derived with slightly different reflection everywhere. Describes the entry assembly, or
    /// the executing assembly when there is no entry assembly (e.g. some test hosts).
    /// </summary>
    public static class AppInfo
    {
        private static readonly Assembly Target = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        /// <summary>The assembly (file) version, e.g. "1.2.3.0".</summary>
        public static string? Version => Target.GetName().Version?.ToString();

        /// <summary>The informational version ([AssemblyInformationalVersion]), often a semver/build string.</summary>
        public static string? InformationalVersion =>
            Target.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        /// <summary>The build configuration ([AssemblyConfiguration]), typically "Debug" or "Release"; null if absent.</summary>
        public static string? Configuration =>
            Target.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;

        /// <summary>The full path of the target assembly, or null if not available (e.g. single-file publish).</summary>
        public static string? Location
        {
            get
            {
                try { return string.IsNullOrEmpty(Target.Location) ? null : Target.Location; }
                catch { return null; }
            }
        }

        /// <summary>Best-effort build timestamp from the assembly file's last-write time (UTC); null if unavailable.</summary>
        public static DateTime? BuildTimestampUtc
        {
            get
            {
                string? location = Location;
                if (location == null || !File.Exists(location)) return null;
                try { return File.GetLastWriteTimeUtc(location); }
                catch { return null; }
            }
        }

        /// <summary>The runtime description, e.g. ".NET 8.0.0".</summary>
        public static string FrameworkDescription => RuntimeInformation.FrameworkDescription;

        /// <summary>The process architecture, e.g. X64 / Arm64.</summary>
        public static Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;
    }
}
