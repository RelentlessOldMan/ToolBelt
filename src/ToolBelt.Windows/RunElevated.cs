// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (ShellExecute "runas").
using System;
using System.ComponentModel;
using System.Diagnostics;
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
        /// <see cref="OperationCanceledException"/> if the user declines the UAC prompt.
        /// </summary>
        public static SystemProcess Start(string fileName, string? arguments = null, string? workingDirectory = null)
        {
            if (fileName is null) throw new ArgumentNullException(nameof(fileName));
            if (fileName.Length == 0) throw new ArgumentException("File name must not be empty.", nameof(fileName));

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
        /// current (non-elevated) instance.
        /// </summary>
        public static SystemProcess RestartCurrent(string? arguments = null)
        {
            string? path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Could not determine the current executable path.");
            return Start(path!, arguments);
        }
    }
}
