// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>
    /// Writes a file atomically: the data is written to a sibling temporary file, flushed, then swapped
    /// into place, so a crash mid-write cannot leave a truncated file. Optionally keeps the previous
    /// contents as a <c>.bak</c> backup. Prevents the classic corrupted-configuration-on-crash failure.
    /// </summary>
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string contents, Encoding? encoding = null, bool backup = false)
        {
            if (contents is null) throw new ArgumentNullException(nameof(contents));
            WriteAllBytes(path, (encoding ?? new UTF8Encoding(false)).GetBytes(contents), backup);
        }

        public static void WriteAllBytes(string path, byte[] bytes, bool backup = false)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (bytes is null) throw new ArgumentNullException(nameof(bytes));

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath) ?? ".";
            string temp = Path.Combine(directory, Path.GetFileName(fullPath) + ".tmp-" + Guid.NewGuid().ToString("N"));

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                if (File.Exists(fullPath))
                {
                    string? backupPath = backup ? fullPath + ".bak" : null;
                    File.Replace(temp, fullPath, backupPath);
                }
                else
                {
                    File.Move(temp, fullPath);
                }
            }
            catch
            {
                try { File.Delete(temp); } catch { /* best-effort cleanup */ }
                throw;
            }
        }
    }
}
