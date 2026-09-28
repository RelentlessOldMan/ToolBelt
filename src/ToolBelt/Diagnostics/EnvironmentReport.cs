// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DiagnosticsProcess = System.Diagnostics.Process; // avoid clash with the ToolBelt.Process namespace

namespace ToolBelt.Diagnostics
{
    /// <summary>
    /// A one-call snapshot of everything worth putting in a bug report — application and runtime versions,
    /// OS, processor count and architecture, working set, culture, current directory, uptime, command line
    /// and a curated set of environment variables. Every support request wants this and nobody has it ready.
    /// Environment variables are NOT dumped wholesale (that leaks secrets): pass the names you want, or accept
    /// the small non-sensitive default set.
    /// </summary>
    public sealed class EnvironmentReport
    {
        private static readonly string[] DefaultVariableNames =
            { "OS", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS", "LANG", "DOTNET_ENVIRONMENT" };

        public DateTimeOffset CapturedAt { get; private set; }
        public string? ApplicationVersion { get; private set; }
        public string RuntimeVersion { get; private set; } = "";
        public string FrameworkDescription { get; private set; } = "";
        public string OSDescription { get; private set; } = "";
        public Architecture OSArchitecture { get; private set; }
        public Architecture ProcessArchitecture { get; private set; }
        public int ProcessorCount { get; private set; }
        public bool Is64BitProcess { get; private set; }
        public bool Is64BitOperatingSystem { get; private set; }
        public long WorkingSetBytes { get; private set; }
        public TimeSpan Uptime { get; private set; }
        public string MachineName { get; private set; } = "";
        public string UserName { get; private set; } = "";
        public string CurrentDirectory { get; private set; } = "";
        public string CommandLine { get; private set; } = "";
        public string Culture { get; private set; } = "";
        public string UICulture { get; private set; } = "";
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; private set; } = new Dictionary<string, string>();

        /// <summary>Captures the current environment. Supply variable names to include, or null for the default safe set.</summary>
        public static EnvironmentReport Capture(IReadOnlyCollection<string>? environmentVariableNames = null)
        {
            long workingSet = 0;
            TimeSpan uptime = TimeSpan.Zero;
            try
            {
                using DiagnosticsProcess process = DiagnosticsProcess.GetCurrentProcess();
                workingSet = process.WorkingSet64;
                uptime = DateTime.Now - process.StartTime;
                if (uptime < TimeSpan.Zero) uptime = TimeSpan.Zero;
            }
            catch { /* some hosts restrict process self-inspection */ }

            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in environmentVariableNames ?? DefaultVariableNames)
            {
                string? value = Environment.GetEnvironmentVariable(name);
                if (value != null) vars[name] = value;
            }

            return new EnvironmentReport
            {
                CapturedAt = DateTimeOffset.Now,
                ApplicationVersion = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version?.ToString(),
                RuntimeVersion = Environment.Version.ToString(),
                FrameworkDescription = RuntimeInformation.FrameworkDescription,
                OSDescription = RuntimeInformation.OSDescription,
                OSArchitecture = RuntimeInformation.OSArchitecture,
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture,
                ProcessorCount = Environment.ProcessorCount,
                Is64BitProcess = Environment.Is64BitProcess,
                Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                WorkingSetBytes = workingSet,
                Uptime = uptime,
                MachineName = SafeGet(() => Environment.MachineName),
                UserName = SafeGet(() => Environment.UserName),
                CurrentDirectory = SafeGet(() => Environment.CurrentDirectory),
                CommandLine = SafeGet(() => Environment.CommandLine),
                Culture = CultureInfo.CurrentCulture.Name,
                UICulture = CultureInfo.CurrentUICulture.Name,
                EnvironmentVariables = vars,
            };
        }

        /// <summary>A readable multi-line report suitable for pasting into a bug report.</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            void Line(string label, object? value) => sb.Append(label).Append(": ").AppendLine(Convert.ToString(value, CultureInfo.InvariantCulture));

            Line("Captured", CapturedAt.ToString("o", CultureInfo.InvariantCulture));
            Line("Application version", ApplicationVersion);
            Line("Runtime version", RuntimeVersion);
            Line("Framework", FrameworkDescription);
            Line("OS", OSDescription);
            Line("OS architecture", OSArchitecture);
            Line("Process architecture", ProcessArchitecture);
            Line("64-bit process / OS", $"{Is64BitProcess} / {Is64BitOperatingSystem}");
            Line("Processors", ProcessorCount);
            Line("Working set", WorkingSetBytes + " bytes");
            Line("Uptime", Uptime);
            Line("Machine", MachineName);
            Line("User", UserName);
            Line("Current directory", CurrentDirectory);
            Line("Command line", CommandLine);
            Line("Culture / UI culture", $"{Culture} / {UICulture}");
            if (EnvironmentVariables.Count > 0)
            {
                sb.AppendLine("Environment:");
                foreach (var kv in EnvironmentVariables)
                    sb.Append("  ").Append(kv.Key).Append(" = ").AppendLine(kv.Value);
            }
            return sb.ToString().TrimEnd();
        }

        private static string SafeGet(Func<string> read)
        {
            try { return read() ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
