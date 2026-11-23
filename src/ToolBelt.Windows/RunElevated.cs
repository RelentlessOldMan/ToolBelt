// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (ShellExecute "runas").
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SystemProcess = System.Diagnostics.Process;

namespace ToolBelt.Windows
{
    /// <summary>
    /// Launches a process <b>elevated</b> via the shell's <c>runas</c> verb, which triggers the Windows UAC
    /// consent prompt. If the user declines the prompt the launch throws <see cref="OperationCanceledException"/>;
    /// other shell failures surface as <see cref="Win32Exception"/>. Use <see cref="Elevation"/> first to
    /// decide whether elevation is actually needed. (This starts a process and shows UAC — an outward side
    /// effect — so callers should invoke it deliberately, not speculatively.)
    /// </summary>
    public static class RunElevated
    {
        private const int ERROR_CANCELLED = 1223;

        /// <summary>
        /// Starts <paramref name="fileName"/> elevated and returns the started process. Throws
        /// <see cref="OperationCanceledException"/> if the user declines the UAC prompt. <paramref name="arguments"/> is
        /// the raw command-line tail; build it with <see cref="JoinArguments"/> when it holds paths or other values that
        /// may contain spaces or quotes.
        /// </summary>
        public static SystemProcess Start(string fileName, string? arguments = null, string? workingDirectory = null)
        {
            if (fileName is null) throw new ArgumentNullException(nameof(fileName));
            if (fileName.Length == 0) throw new ArgumentException("File name must not be empty.", nameof(fileName));
            // The shell reads C strings: text after a NUL was dropped, so a different file or a shorter command line ran.
            if (fileName.IndexOf('\0') >= 0) throw new ArgumentException("File name must not contain a NUL character.", nameof(fileName));
            if (arguments != null && arguments.IndexOf('\0') >= 0)
                throw new ArgumentException("Arguments must not contain a NUL character.", nameof(arguments));
            if (workingDirectory != null && workingDirectory.IndexOf('\0') >= 0)
                throw new ArgumentException("Working directory must not contain a NUL character.", nameof(workingDirectory));

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workingDirectory ?? string.Empty,
                UseShellExecute = true, // required for the "runas" verb
                Verb = "runas",
            };

            try
            {
                SystemProcess? process = SystemProcess.Start(psi);
                if (process is null)
                    throw new InvalidOperationException($"The shell did not start a process for '{fileName}'.");
                return process;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
            {
                throw new OperationCanceledException("The user declined the elevation (UAC) prompt.", ex);
            }
        }

        /// <summary>
        /// Relaunches the current executable elevated (optionally with <paramref name="arguments"/>) and
        /// returns the new elevated process. Throws <see cref="OperationCanceledException"/> if the user
        /// declines. Typical use: call when <see cref="Elevation.IsElevated"/> is false, then exit the
        /// current (non-elevated) instance. The new process starts in the current directory, and under the
        /// <c>dotnet</c> host it keeps the host's options (such as <c>exec --runtimeconfig</c> or <c>--roll-forward</c>).
        /// </summary>
        public static SystemProcess RestartCurrent(string? arguments = null)
        {
            var (file, args, directory) = RestartStart(arguments);
            return Start(file, args, directory);
        }

        // An elevated process started without a directory began in System32, so relative paths in its arguments broke.
        private static (string File, string Arguments, string WorkingDirectory) RestartStart(string? arguments)
        {
            string? path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Could not determine the current executable path.");
            var (file, args) = RestartCommandFrom(path!, Assembly.GetEntryAssembly()?.Location, CurrentCommandLine(), arguments);
            return (file, args, Environment.CurrentDirectory);
        }

        /// <summary>
        /// Quotes each argument so the target's command-line parser (the MSVCRT / <c>CommandLineToArgvW</c> rules every .NET
        /// and C program uses) reads back exactly <paramref name="arguments"/>, and joins them with spaces.
        /// </summary>
        public static string JoinArguments(IEnumerable<string> arguments)
        {
            if (arguments is null) throw new ArgumentNullException(nameof(arguments));
            var sb = new StringBuilder();
            bool first = true;
            foreach (string arg in arguments)
            {
                if (arg is null) throw new ArgumentException("Arguments must not be null.", nameof(arguments));
                // The command line is a C string: "a\0b c" read back as just "a", losing the rest.
                if (arg.IndexOf('\0') >= 0) throw new ArgumentException("Arguments must not contain a NUL character.", nameof(arguments));
                if (!first) sb.Append(' ');
                first = false;
                AppendQuoted(sb, arg);
            }
            return sb.ToString();
        }

        private static void AppendQuoted(StringBuilder sb, string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                sb.Append(arg);                                                    // backslashes are literal outside quotes
                return;
            }
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                // Backslashes are literal unless they precede a quote: then each one, and the quote, must be escaped.
                sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                sb.Append(c);
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);                                     // before the closing quote
            sb.Append('"');
        }

        // Under "dotnet app.dll" the process is the shared host; relaunching it bare would start the dotnet CLI, not the app.
        private static (string File, string Arguments) RestartCommand(string processPath, string? entryAssemblyPath, string? arguments) =>
            RestartCommandFrom(processPath, entryAssemblyPath, null, arguments);

        // commandLine is the process's own argv (host first). The host's options before the app's path are kept: relaunching
        // "dotnet exec --runtimeconfig x.json app.dll" as "dotnet app.dll" could bind the app to a different runtime.
        private static (string File, string Arguments) RestartCommandFrom(string processPath, string? entryAssemblyPath,
            string[]? commandLine, string? arguments)
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                return (processPath, arguments ?? string.Empty);
            if (string.IsNullOrEmpty(entryAssemblyPath))
                throw new InvalidOperationException("Running under the dotnet host, but the app's entry assembly path is unknown.");
            var parts = new List<string>();
            if (commandLine != null)
            {
                for (int i = 1; i < commandLine.Length; i++)
                {
                    if (SamePath(commandLine[i], entryAssemblyPath!))
                    {
                        for (int j = 1; j < i; j++) parts.Add(commandLine[j]);
                        break;
                    }
                }
            }
            parts.Add(entryAssemblyPath!);
            string app = JoinArguments(parts);
            return (processPath, string.IsNullOrEmpty(arguments) ? app : app + " " + arguments);
        }

        private static bool SamePath(string candidate, string path)
        {
            try
            {
                return candidate.Length > 0
                    && string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }

        // The command line as the process got it: Environment.GetCommandLineArgs drops the dotnet host and its options.
        private static string[]? CurrentCommandLine()
        {
            IntPtr argv = CommandLineToArgvW(GetCommandLineW(), out int count);
            if (argv == IntPtr.Zero) return null;
            try
            {
                var result = new string[count];
                for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? string.Empty;
                return result;
            }
            finally { LocalFree(argv); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetCommandLineW();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(IntPtr lpCmdLine, out int pNumArgs);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
