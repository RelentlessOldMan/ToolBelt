// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.IO;

namespace ToolBelt.IO
{
    /// <summary>
    /// A uniquely named temporary file that is deleted when the scope is disposed. Use with
    /// <c>using</c> so the file is cleaned up even on exceptions. Deletion is best-effort — a file that
    /// cannot be removed (e.g. still open elsewhere) does not throw from <see cref="Dispose"/>.
    /// </summary>
    public sealed class TempFile : IDisposable
    {
        private bool _disposed;

        public TempFile(string? extension = null)
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "toolbelt-" + Guid.NewGuid().ToString("N") + NormalizeExtension(extension));
            // Create the (empty) file so it exists immediately, matching GetTempFileName semantics.
            using (File.Create(path)) { }
            Path = path;
        }

        /// <summary>The full path to the temporary file.</summary>
        public string Path { get; }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            TryDelete(Path);
        }

        private static string NormalizeExtension(string? extension)
        {
            if (string.IsNullOrEmpty(extension))
                return string.Empty;
            return extension!.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        }

        internal static void TryDelete(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;
                try { File.Delete(path); }
                catch (UnauthorizedAccessException)
                {
                    // Read-only: clear the attribute and try once more.
                    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                    File.Delete(path);
                }
            }
            catch (IOException) { /* best-effort */ }
            catch (UnauthorizedAccessException) { /* best-effort */ }
        }

        // Clears ReadOnly on everything under dir (not through links), which Directory.Delete refuses to remove.
        internal static void ClearReadOnlyTree(string dir)
        {
            var pending = new Stack<string>();
            pending.Push(dir);
            while (pending.Count > 0)
            {
                string d = pending.Pop();
                foreach (string f in List(d, directories: false))
                {
                    FileAttributes a = File.GetAttributes(Exact(f));
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(Exact(f), a & ~FileAttributes.ReadOnly);
                }
                foreach (string sub in List(d, directories: true))
                {
                    FileAttributes a = File.GetAttributes(Exact(sub));
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(Exact(sub), a & ~FileAttributes.ReadOnly);
                    if ((a & FileAttributes.ReparsePoint) == 0) pending.Push(sub);
                }
            }
        }

        // Deletes a tree through the exact form of each name; a linked folder loses only the link.
        internal static void DeleteTree(string dir)
        {
            foreach (string f in List(dir, directories: false)) File.Delete(Exact(f));
            foreach (string sub in List(dir, directories: true))
            {
                if ((File.GetAttributes(Exact(sub)) & FileAttributes.ReparsePoint) != 0) Directory.Delete(Exact(sub));
                else DeleteTree(sub);
            }
            Directory.Delete(Exact(dir));
        }

        // Win32 path normalisation drops a trailing dot or space from each name, so a listed file "a." was read, replaced
        // or deleted as "a" (another file) and a folder "d." could not be opened. The \\?\ form is used as written.
        private static string Exact(string path)
        {
            if (System.IO.Path.DirectorySeparatorChar != '\\' || !NeedsExact(path)) return path;
            string plain = path.Replace('/', '\\');
            return plain[0] == '\\' ? @"\\?\UNC\" + plain.Substring(2) : @"\\?\" + plain;
        }

        // A full path ("C:\..." or "\\server\share\...") with a name ending in a dot or space, and no "." or ".." to resolve.
        private static bool NeedsExact(string path)
        {
            bool drive = path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
            bool unc = path.Length >= 3 && (path[0] == '\\' || path[0] == '/') && (path[1] == '\\' || path[1] == '/') && path[2] != '?' && path[2] != '.';
            if (!drive && !unc) return false;
            int start = drive ? 3 : 2;
            bool found = false;
            for (int i = start; i <= path.Length; i++)
            {
                if (i < path.Length && path[i] != '\\' && path[i] != '/') continue;
                int len = i - start;
                if (len > 0)
                {
                    char last = path[i - 1];
                    bool dots = (len == 1 && last == '.') || (len == 2 && last == '.' && path[i - 2] == '.');
                    if (dots) return false;   // left to Path.GetFullPath, which resolves it
                    if (last == '.' || last == ' ') found = true;
                }
                start = i + 1;
            }
            return found;
        }

        // A folder's files or subfolders, listed through the exact form when its name needs it and returned in the plain one.
        private static string[] List(string dir, bool directories, string pattern = "*")
        {
            string exact = Exact(dir);
            string[] found = directories ? Directory.GetDirectories(exact) : Directory.GetFiles(exact, pattern);
            if (!ReferenceEquals(exact, dir))
                for (int i = 0; i < found.Length; i++)
                    found[i] = dir + found[i].Substring(exact.Length);
            return found;
        }
    }

    /// <summary>
    /// A uniquely named temporary directory that is recursively deleted when disposed. Deletion is
    /// best-effort and never throws from <see cref="Dispose"/>.
    /// </summary>
    public sealed class TempDirectory : IDisposable
    {
        private bool _disposed;

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "toolbelt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>The full path to the temporary directory.</summary>
        public string Path { get; }

        /// <summary>Combines a relative path onto this directory.</summary>
        public string Combine(string relative)
        {
            if (relative is null) throw new ArgumentNullException(nameof(relative));
            return System.IO.Path.Combine(Path, relative);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                if (!Directory.Exists(Path))
                    return;
                try { Directory.Delete(Path, recursive: true); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // A read-only file (common after a git clone into it), or on .NET Framework a name ending in a dot or
                    // space, which its recursive delete cannot reach: clear the attributes and delete name by name.
                    TempFile.ClearReadOnlyTree(Path);
                    TempFile.DeleteTree(Path);
                }
            }
            catch (IOException) { /* best-effort */ }
            catch (UnauthorizedAccessException) { /* best-effort */ }
        }
    }
}
