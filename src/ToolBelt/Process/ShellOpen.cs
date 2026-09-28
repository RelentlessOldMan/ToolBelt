// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DiagnosticsProcess = System.Diagnostics.Process;

namespace ToolBelt.Process
{
    /// <summary>
    /// Best-effort "open this file, folder or URL with the default handler", with the per-platform
    /// difference isolated in one place (Windows shell-execute, macOS <c>open</c>, Linux <c>xdg-open</c>) so
    /// callers stop writing their own conditional. This launches an external handler — a real side effect.
    /// </summary>
    public static class ShellOpen
    {
        public static void Open(string target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (target.Length == 0) throw new ArgumentException("Target must not be empty.", nameof(target));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var _ = DiagnosticsProcess.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                using var _ = DiagnosticsProcess.Start("open", target);
            }
            else
            {
                using var _ = DiagnosticsProcess.Start("xdg-open", target);
            }
        }
    }
}
