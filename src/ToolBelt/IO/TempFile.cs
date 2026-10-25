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
                foreach (string f in Directory.GetFiles(d))
                {
                    FileAttributes a = File.GetAttributes(f);
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly);
                }
                foreach (string sub in Directory.GetDirectories(d))
                {
                    FileAttributes a = File.GetAttributes(sub);
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(sub, a & ~FileAttributes.ReadOnly);
                    if ((a & FileAttributes.ReparsePoint) == 0) pending.Push(sub);
                }
            }
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
                catch (UnauthorizedAccessException)
                {
                    // A read-only file (common after a git clone into it): clear the attributes and try once more.
                    TempFile.ClearReadOnlyTree(Path);
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException) { /* best-effort */ }
            catch (UnauthorizedAccessException) { /* best-effort */ }
        }
    }
}
