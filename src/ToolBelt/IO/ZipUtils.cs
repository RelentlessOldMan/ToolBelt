// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>
    /// Zip archive helpers over the framework's <see cref="ZipArchive"/>: create from a directory (with an
    /// optional filter and progress), extract with <b>path-traversal (Zip Slip) protection</b>, list entries,
    /// add or replace a single entry, and read one entry without extracting. The escape check lives here, in
    /// one audited place, because <c>../</c> and absolute entry names are the classic way a malicious archive
    /// writes outside its target directory — and a junction or symbolic link already inside the target is the other.
    /// </summary>
    public static class ZipUtils
    {
        private static readonly StringComparison PathComparison =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// Zips every file under <paramref name="sourceDirectory"/> (recursively). Links (junctions, symbolic links) are
        /// not followed or stored, the archive itself is skipped when it lies inside the source, and a failed run leaves
        /// no partial zip behind.
        /// </summary>
        public static void CreateFromDirectory(
            string sourceDirectory, string zipPath,
            Func<string, bool>? filter = null, Action<string>? onEntry = null, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (sourceDirectory is null) throw new ArgumentNullException(nameof(sourceDirectory));
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);

            string root = Path.GetFullPath(sourceDirectory);
            string zipFull = Path.GetFullPath(zipPath);
            bool completed = false;
            try
            {
                using (var fs = new FileStream(zipFull, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (string file in EnumerateFilesNoLinks(root))
                    {
                        string full = Path.GetFullPath(file);
                        if (string.Equals(full, zipFull, PathComparison)) continue;   // the archive being written
                        string entryName = full.Substring(root.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Replace(Path.DirectorySeparatorChar, '/');   // only the separator: a backslash is a name character on Unix
                        if (filter != null && !filter(entryName)) continue;

                        ZipArchiveEntry entry = archive.CreateEntry(entryName, level);
                        using (Stream entryStream = entry.Open())
                        using (FileStream input = File.OpenRead(file))
                            input.CopyTo(entryStream);
                        onEntry?.Invoke(entryName);
                    }
                }
                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    try { File.Delete(zipFull); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        /// <summary>
        /// Extracts the archive into <paramref name="destinationDirectory"/>, rejecting (with <see cref="IOException"/>)
        /// entries that escape it — by <c>../</c>, an absolute name, or a junction or symbolic link already inside the
        /// destination, which is never written through.
        /// </summary>
        public static void ExtractToDirectory(string zipPath, string destinationDirectory, bool overwrite = false, Action<string>? onEntry = null)
        {
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (destinationDirectory is null) throw new ArgumentNullException(nameof(destinationDirectory));

            string root = Path.GetFullPath(destinationDirectory);
            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root : root + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(root);

            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string target = SafeTarget(root, rootWithSeparator, entry.FullName);
                EnsureNoLinks(rootWithSeparator, target, entry.FullName);

                // Directory entry (name ends with a separator, no file name).
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                if (target.Equals(root, PathComparison) || target.Equals(rootWithSeparator, PathComparison))
                    throw new IOException($"Zip entry '{entry.FullName}' resolves to the target directory itself.");

                string? dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(target) && !overwrite)
                    throw new IOException($"Destination file already exists: {target}");

                // Written beside the target and moved into place: a corrupt entry then leaves the existing file as it was, and
                // the move replaces the name, so an existing hard link at the target is broken rather than written through.
                string temp = Path.Combine(Path.GetDirectoryName(target)!, ".zip-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (Stream entryStream = entry.Open())
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        entryStream.CopyTo(output);
#if NET6_0_OR_GREATER
                    File.Move(temp, target, overwrite);
#else
                    if (overwrite && File.Exists(target)) File.Delete(target);
                    File.Move(temp, target);
#endif
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                onEntry?.Invoke(entry.FullName);
            }
        }

        /// <summary>The names of every entry in the archive.</summary>
        public static IReadOnlyList<string> ListEntries(string zipPath)
        {
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            var names = new List<string>();
            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in archive.Entries) names.Add(entry.FullName);
            return names;
        }

        /// <summary>Adds an entry, replacing every existing one with the same name.</summary>
        public static void AddOrReplaceEntry(string zipPath, string entryName, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (entryName is null) throw new ArgumentNullException(nameof(entryName));
            if (content is null) throw new ArgumentNullException(nameof(content));

            using var fs = new FileStream(zipPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Update);
            // GetEntry finds only one of several same-named entries; remove them all.
            var stale = new List<ZipArchiveEntry>();
            foreach (ZipArchiveEntry e in archive.Entries)
                if (string.Equals(e.FullName, entryName, StringComparison.Ordinal)) stale.Add(e);
            foreach (ZipArchiveEntry e in stale) e.Delete();
            ZipArchiveEntry entry = archive.CreateEntry(entryName, level);
            using Stream entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }

        /// <summary>
        /// Reads a single entry's bytes without extracting, or null if it is absent. An entry that inflates past
        /// <paramref name="maxBytes"/> throws <see cref="InvalidDataException"/> instead of exhausting memory
        /// (a guard against zip bombs and lying size headers).
        /// </summary>
        public static byte[]? ReadEntryBytes(string zipPath, string entryName, long maxBytes = int.MaxValue)
        {
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (entryName is null) throw new ArgumentNullException(nameof(entryName));
            if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Must be non-negative.");
            long cap = Math.Min(maxBytes, 0x7FFFFFC7L);   // the largest byte array the runtime allows

            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            ZipArchiveEntry? entry = archive.GetEntry(entryName);
            if (entry is null) return null;
            if (entry.Length > cap)
                throw new InvalidDataException($"Zip entry '{entryName}' is {entry.Length} bytes, over the {cap}-byte limit.");
            using Stream entryStream = entry.Open();
            // Not pre-sized from entry.Length: the header is the archive's claim, and a 10-byte entry claiming 256 MB
            // allocated 256 MB. The stream grows with what actually inflates.
            using var ms = new MemoryStream((int)Math.Min(entry.Length, 81920));
            var buffer = new byte[81920];
            int read;
            while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (ms.Length + read > cap)
                    throw new InvalidDataException($"Zip entry '{entryName}' inflates past the {cap}-byte limit.");
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }

        /// <summary>Reads a single entry as text (UTF-8 by default), or throws if it is absent.</summary>
        public static string ReadEntryText(string zipPath, string entryName, Encoding? encoding = null)
        {
            byte[]? bytes = ReadEntryBytes(zipPath, entryName)
                ?? throw new FileNotFoundException($"Entry not found in archive: {entryName}");
            return (encoding ?? Encoding.UTF8).GetString(bytes);
        }

        // Resolves an entry name against the root and verifies it stays inside — the Zip Slip guard.
        private static string SafeTarget(string root, string rootWithSeparator, string entryName)
        {
            // NUL threw ArgumentException from the path APIs; on Windows "x.txt:hidden" wrote an alternate data stream of
            // x.txt, data no directory listing shows.
            if (entryName.IndexOf('\0') >= 0 || (Path.DirectorySeparatorChar == '\\' && entryName.IndexOf(':') >= 0))
                throw new IOException($"Zip entry '{entryName.Replace('\0', '?')}' has a character not allowed in a file name.");
            string normalized = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, normalized));
            if (!full.Equals(root, PathComparison) && !full.StartsWith(rootWithSeparator, PathComparison))
                throw new IOException($"Zip entry '{entryName}' would extract outside the target directory.");
            return full;
        }

        // A junction or symbolic link between the root and the target (or the target itself) would redirect the write
        // outside the destination, which the string check in SafeTarget cannot see. Every existing component is checked.
        private static void EnsureNoLinks(string rootWithSeparator, string target, string entryName)
        {
            if (target.Length <= rootWithSeparator.Length) return;
            string path = rootWithSeparator;
            foreach (string part in target.Substring(rootWithSeparator.Length).Split(
                new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                path = Path.Combine(path, part);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (FileNotFoundException) { return; }
                catch (DirectoryNotFoundException) { return; }
                if (IsLink(path, attributes))
                    throw new IOException($"Zip entry '{entryName}' would be written through the link '{path}'.");
            }
        }

        private static IEnumerable<string> EnumerateFilesNoLinks(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                foreach (string f in Directory.EnumerateFiles(dir))
                    if (!IsLink(f, File.GetAttributes(f))) yield return f;
                foreach (string d in Directory.EnumerateDirectories(dir))
                    if (!IsLink(d, File.GetAttributes(d))) pending.Push(d);
            }
        }

        // A reparse point that is a link (junction or symbolic link). Other reparse points, such as cloud-file
        // placeholders, are ordinary files and folders.
        private static bool IsLink(string path, FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReparsePoint) == 0) return false;
#if NET6_0_OR_GREATER
            try
            {
                FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
                return info.LinkTarget != null;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
#else
            return true;
#endif
        }
    }
}
