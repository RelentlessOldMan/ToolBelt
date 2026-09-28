// ToolBelt drop-in — fully self-contained (BCL only).
using System;
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
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException) { /* best-effort */ }
            catch (UnauthorizedAccessException) { /* best-effort */ }
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
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException) { /* best-effort */ }
            catch (UnauthorizedAccessException) { /* best-effort */ }
        }
    }
}
