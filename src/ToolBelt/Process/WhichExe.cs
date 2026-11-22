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
            bool tryAsIs;
            // A trailing '.' is an empty extension, as the launcher reads it: "cmd." names a file "cmd" and is not given
            // PATHEXT (it found cmd.EXE, which "cmd." does not start), and "cmd.exe." names cmd.exe (it found nothing).
            bool trailingDot = windows && name[name.Length - 1] == '.';
            try { tryAsIs = !windows || trailingDot || Path.HasExtension(name); }
            catch (ArgumentException) { return null; } // .NET Framework checks for invalid characters here ("a|b")

            // A name that already includes a directory is resolved directly.
            if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                string full;
                try { full = Path.GetFullPath(name); }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                { return null; } // an invalid path names nothing (.NET Framework threw for "a\b|c")
                return Resolve(full, tryAsIs, windows && !trailingDot);
            }

            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string entry in SplitPath(pathVar, windows))
            {
                // Windows allows a quoted entry ("C:\Some Dir"), as cmd and CreateProcess do; quotes can't be in a name.
                // Leading and trailing spaces are dropped, as cmd does ("C:\a; C:\b"): .NET 8 read " C:\b" as a
                // relative folder where .NET Framework trimmed it.
                string dir = windows ? entry.Replace("\"", string.Empty).Trim(' ') : entry;
                if (dir.Length == 0) continue;
                string candidate;
                // Full: a relative entry (bin) gave a relative result, though the full path is promised.
                try { candidate = Path.GetFullPath(Path.Combine(dir, name)); }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                { continue; } // skip malformed PATH entries

                string? found = Resolve(candidate, tryAsIs, windows && !trailingDot);
                if (found != null) return found;
            }
            return null;
        }

        // Windows splits at a ';' outside quotes: a quoted folder may contain one ("C:\a;b"), which a plain split cut in two.
        private static string[] SplitPath(string pathVar, bool windows)
        {
            if (!windows || pathVar.IndexOf('"') < 0) return pathVar.Split(Path.PathSeparator);
            var parts = new System.Collections.Generic.List<string>();
            bool quoted = false;
            int start = 0;
            for (int i = 0; i < pathVar.Length; i++)
            {
                if (pathVar[i] == '"') quoted = !quoted;
                else if (pathVar[i] == ';' && !quoted) { parts.Add(pathVar.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(pathVar.Substring(start));
            return parts.ToArray();
        }

        private static string? Resolve(string path, bool tryAsIs, bool pathExt)
        {
            if (tryAsIs && File.Exists(path)) return path;
            if (pathExt)
            {
                string pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD"; // cmd's own default order
                foreach (string ext in pathext.Split(';'))
                    if (ext.Length > 0 && File.Exists(path + ext)) return path + ext;
            }
            return null;
        }
    }
}
