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
    /// <see cref="WriteAllText"/> writes the encoding's preamble (byte order mark) like <see cref="File.WriteAllText(string, string, Encoding)"/>;
    /// the default UTF-8 encoding has none.
    /// </summary>
    public static class AtomicFile
    {
        /// <exception cref="PathTooLongException">As for <see cref="WriteAllBytes"/>.</exception>
        public static void WriteAllText(string path, string contents, Encoding? encoding = null, bool backup = false)
        {
            if (contents is null) throw new ArgumentNullException(nameof(contents));
            encoding ??= new UTF8Encoding(false);
            byte[] preamble = encoding.GetPreamble();
            byte[] body = encoding.GetBytes(contents);
            if (preamble.Length == 0)
            {
                WriteAllBytes(path, body, backup);
                return;
            }
            var bytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
            WriteAllBytes(path, bytes, backup);
        }

        /// <exception cref="PathTooLongException"><paramref name="backup"/> is set and the name with <c>.bak</c> added passes
        /// the 255-character name limit. Nothing is written.</exception>
        public static void WriteAllBytes(string path, byte[] bytes, bool backup = false)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (bytes is null) throw new ArgumentNullException(nameof(bytes));

            string fullPath = Path.GetFullPath(path);
            // The replace failed with "Unable to remove the file to be replaced", which names neither the backup nor the length.
            if (backup)
            {
                string name = Path.GetFileName(fullPath) + ".bak";
                int length = Path.DirectorySeparatorChar == '\\' ? name.Length : Encoding.UTF8.GetByteCount(name);
                if (length > 255)
                    throw new PathTooLongException($"The backup name '{name}' is longer than the 255-character limit for a file name.");
            }
            string directory = Path.GetDirectoryName(fullPath) ?? ".";
            // A short temp name: appending to a long file name would push it past the 255-character limit.
            string temp = Path.Combine(directory, ".atomic-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool replacing = false;

            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(fullPath))
                {
                    string? backupPath = backup ? fullPath + ".bak" : null;
                    replacing = true;
                    File.Replace(temp, fullPath, backupPath);
                }
                else
                {
                    File.Move(temp, fullPath);
                }
            }
            catch
            {
                // File.Replace can fail after removing the original (ERROR_UNABLE_TO_MOVE_REPLACEMENT without a backup): the
                // complete new data then exists only in the temp file, so it is moved into place, or kept, never deleted.
                if (replacing && !File.Exists(fullPath))
                {
                    try { File.Move(temp, fullPath); } catch { /* keep the temp file: it is the only copy */ }
                }
                else
                {
                    try { File.Delete(temp); } catch { /* best-effort cleanup */ }
                }
                throw;
            }
        }
    }
}
