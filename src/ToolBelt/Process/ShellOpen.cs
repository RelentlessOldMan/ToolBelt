// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
#if NETSTANDARD2_0
using System.Text;
#endif
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
            else
            {
                var psi = new ProcessStartInfo(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open") { UseShellExecute = false };
                // Passed as one argument (a path with spaces stays whole); a relative path starting with '-' is made
                // absolute so the handler doesn't read it as an option.
                string argument = target[0] == '-' ? Path.GetFullPath(target) : target;
#if NETSTANDARD2_0
                psi.Arguments = Quote(argument);
#else
                psi.ArgumentList.Add(argument);
#endif
                using var _ = DiagnosticsProcess.Start(psi);
            }
        }

#if NETSTANDARD2_0
        // Quotes one argument for the runtime's command-line splitting (the same rules on every platform):
        // backslashes are literal except before a quote, where they and the quote are escaped.
        private static string Quote(string argument)
        {
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { backslashes++; continue; }
                sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                backslashes = 0;
                sb.Append(c);
            }
            return sb.Append('\\', backslashes * 2).Append('"').ToString();
        }
#endif
    }
}
