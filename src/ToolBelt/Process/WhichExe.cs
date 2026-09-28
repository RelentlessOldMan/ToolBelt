// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ToolBelt.Process
{
    /// <summary>
    /// Resolves an executable name against the <c>PATH</c> (and, on Windows, the <c>PATHEXT</c> extension
    /// list), returning the full path or null. Makes a "command not found" explicable instead of surfacing an
    /// opaque platform error from a failed launch.
    /// </summary>
    public static class WhichExe
    {
        public static string? Find(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (name.Length == 0) return null;

            bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            // A name that already includes a directory is resolved directly.
            if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                string full = Path.GetFullPath(name);
                if (File.Exists(full)) return full;
                if (windows)
                    foreach (string ext in Extensions(true))
                        if (File.Exists(full + ext)) return full + ext;
                return null;
            }

            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in pathVar.Split(Path.PathSeparator))
            {
                if (dir.Length == 0) continue;
                string candidate;
                try { candidate = Path.Combine(dir, name); }
                catch (ArgumentException) { continue; } // skip malformed PATH entries

                if (File.Exists(candidate)) return candidate;
                if (windows)
                    foreach (string ext in Extensions(true))
                        if (File.Exists(candidate + ext)) return candidate + ext;
            }
            return null;
        }

        private static string[] Extensions(bool windows)
        {
            if (!windows) return new[] { string.Empty };
            string pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
            return pathext.Split(';');
        }
    }
}
