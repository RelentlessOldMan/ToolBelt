// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ToolBelt.Process
{
    /// <summary>
    /// Resolves an executable name against the <c>PATH</c> (and, on Windows, the <c>PATHEXT</c> extension
    /// list), returning the full path or null. On Windows a name without an extension only matches through
    /// <c>PATHEXT</c>, as the shell resolves it: an extensionless file next to <c>npm.cmd</c> (a Unix script)
    /// can't be launched. Makes a "command not found" explicable instead of surfacing an
    /// opaque platform error from a failed launch.
    /// </summary>
    public static class WhichExe
    {
        public static string? Find(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (name.Length == 0) return null;

            bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool tryAsIs = !windows || Path.HasExtension(name);

            // A name that already includes a directory is resolved directly.
            if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                string full;
                try { full = Path.GetFullPath(name); }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                { return null; } // an invalid path names nothing (.NET Framework threw for "a\b|c")
                return Resolve(full, tryAsIs, windows);
            }

            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string entry in pathVar.Split(Path.PathSeparator))
            {
                // Windows allows a quoted entry ("C:\Some Dir"), as cmd and CreateProcess do; quotes can't be in a name.
                string dir = windows ? entry.Replace("\"", string.Empty) : entry;
                if (dir.Length == 0) continue;
                string candidate;
                try { candidate = Path.Combine(dir, name); }
                catch (ArgumentException) { continue; } // skip malformed PATH entries

                string? found = Resolve(candidate, tryAsIs, windows);
                if (found != null) return found;
            }
            return null;
        }

        private static string? Resolve(string path, bool tryAsIs, bool windows)
        {
            if (tryAsIs && File.Exists(path)) return path;
            if (windows)
            {
                string pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
                foreach (string ext in pathext.Split(';'))
                    if (ext.Length > 0 && File.Exists(path + ext)) return path + ext;
            }
            return null;
        }
    }
}
