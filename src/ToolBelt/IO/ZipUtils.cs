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
        /// Zips every file under <paramref name="sourceDirectory"/> (recursively), with its last-write time. Empty directories
        /// are kept as directory entries (named with a trailing <c>/</c>, which is also what <paramref name="filter"/> sees).
        /// Links (junctions, symbolic links) are not followed or stored, the archive itself is skipped when it lies inside
        /// the source, and a failed run leaves no partial zip behind (and an existing one as it was).
        /// </summary>
        public static void CreateFromDirectory(
            string sourceDirectory, string zipPath,
            Func<string, bool>? filter = null, Action<string>? onEntry = null, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (sourceDirectory is null) throw new ArgumentNullException(nameof(sourceDirectory));
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
            CheckLevel(level);

            string root = Path.GetFullPath(sourceDirectory);
            string zipFull = Path.GetFullPath(zipPath);
            // Written beside the zip and moved over it once complete: opening the zip with FileMode.Create destroyed an
            // existing good one when the run then failed.
            string temp = Path.Combine(Path.GetDirectoryName(zipFull)!, ".zip-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach ((string file, bool isEmptyDirectory) in EnumerateNoLinks(root))
                    {
                        // The listed path as it is: Path.GetFullPath would drop a trailing dot or space from a name.
                        if (string.Equals(file, zipFull, PathComparison) || string.Equals(file, temp, PathComparison)) continue;   // the archive being written
                        string entryName = file.Substring(root.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Replace(Path.DirectorySeparatorChar, '/');   // only the separator: a backslash is a name character on Unix
                        if (isEmptyDirectory) entryName += "/";
                        if (filter != null && !filter(entryName)) continue;

                        ZipArchiveEntry entry = archive.CreateEntry(entryName, level);
                        // CreateEntry stamps the current time.
                        try { entry.LastWriteTime = isEmptyDirectory ? Directory.GetLastWriteTime(Exact(file)) : File.GetLastWriteTime(Exact(file)); }
                        catch (ArgumentOutOfRangeException) { }   // outside 1980-2107, which zip cannot store
                        if (!isEmptyDirectory)
                        {
                            using (Stream entryStream = entry.Open())
                            using (FileStream input = File.OpenRead(Exact(file)))
                                input.CopyTo(entryStream);
                        }
                        onEntry?.Invoke(entryName);
                    }
                }
#if NET6_0_OR_GREATER
                File.Move(temp, zipFull, overwrite: true);
#else
                if (File.Exists(zipFull)) File.Delete(zipFull);
                File.Move(temp, zipFull);
#endif
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Extracts the archive into <paramref name="destinationDirectory"/>, rejecting (with <see cref="IOException"/>)
        /// entries that escape it — by <c>../</c>, an absolute name, or a junction or symbolic link already inside the
        /// destination, which is never written through. Files and directories get their archived last-write times. On
        /// Windows a reserved device name (<c>CON</c>, <c>aux.txt</c>, <c>COM1</c>) is rejected too. Extraction stops with
        /// <see cref="InvalidDataException"/> once the files written pass <paramref name="maxTotalBytes"/> (a guard against
        /// zip bombs, which the archive's own size fields cannot be trusted for); what was extracted before then is kept.
        /// </summary>
        public static void ExtractToDirectory(string zipPath, string destinationDirectory, bool overwrite = false, Action<string>? onEntry = null,
            long maxTotalBytes = long.MaxValue)
        {
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (destinationDirectory is null) throw new ArgumentNullException(nameof(destinationDirectory));
            if (maxTotalBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes), maxTotalBytes, "Must be non-negative.");
            long budget = maxTotalBytes;

            string root = Path.GetFullPath(destinationDirectory);
            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root : root + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(root);

            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = OpenArchive(fs, ZipArchiveMode.Read);
            var directoryTimes = new List<(string Path, DateTime Time)>();
            foreach (ZipArchiveEntry entry in EntriesOf(archive))
            {
                string target = SafeTarget(root, rootWithSeparator, entry.FullName);
                EnsureNoLinks(rootWithSeparator, target, entry.FullName);

                // Directory entry (name ends with a separator, no file name).
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(target);
                    directoryTimes.Add((target, entry.LastWriteTime.DateTime));
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
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (read > budget)
                                throw new InvalidDataException($"Extracting '{entry.FullName}' passed the {maxTotalBytes}-byte limit.");
                            budget -= read;
                            output.Write(buffer, 0, read);
                        }
                    }
                    SetTime(temp, entry.LastWriteTime.DateTime, directory: false);
                    // A read-only file is not replaced by a move (UnauthorizedAccessException); overwrite means replace it.
                    if (overwrite) ClearReadOnly(target);
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
            // Last, and deepest first: writing into a directory sets its last-write time.
            for (int i = directoryTimes.Count - 1; i >= 0; i--)
                SetTime(directoryTimes[i].Path, directoryTimes[i].Time, directory: true);
        }

        // Zip times are local; one the file system cannot store is left as it is.
        private static void SetTime(string path, DateTime local, bool directory)
        {
            try
            {
                if (directory) Directory.SetLastWriteTime(path, local);
                else File.SetLastWriteTime(path, local);
            }
            catch (ArgumentOutOfRangeException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>The names of every entry in the archive.</summary>
        public static IReadOnlyList<string> ListEntries(string zipPath)
        {
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            var names = new List<string>();
            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = OpenArchive(fs, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in EntriesOf(archive)) names.Add(entry.FullName);
            return names;
        }

        /// <summary>
        /// Adds an entry, replacing every existing one with the same name. The zip is rewritten beside itself and moved
        /// over the original once complete, so a failure leaves it as it was.
        /// </summary>
        public static void AddOrReplaceEntry(string zipPath, string entryName, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (entryName is null) throw new ArgumentNullException(nameof(entryName));
            if (content is null) throw new ArgumentNullException(nameof(content));
            CheckLevel(level);

            // Updating in place truncated the zip on any failure part way (an unknown level, a full disk): every entry lost.
            string zipFull = Path.GetFullPath(zipPath);
            string temp = Path.Combine(Path.GetDirectoryName(zipFull)!, ".zip-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    if (File.Exists(zipFull))
                        using (var original = new FileStream(zipFull, FileMode.Open, FileAccess.Read, FileShare.Read))
                            original.CopyTo(fs);
                    fs.Position = 0;
                    using var archive = OpenArchive(fs, ZipArchiveMode.Update);
                    // GetEntry finds only one of several same-named entries; remove them all.
                    var stale = new List<ZipArchiveEntry>();
                    foreach (ZipArchiveEntry e in EntriesOf(archive))
                        if (string.Equals(e.FullName, entryName, StringComparison.Ordinal)) stale.Add(e);
                    foreach (ZipArchiveEntry e in stale) e.Delete();
                    ZipArchiveEntry entry = archive.CreateEntry(entryName, level);
                    using Stream entryStream = entry.Open();
                    entryStream.Write(content, 0, content.Length);
                }
#if NET6_0_OR_GREATER
                File.Move(temp, zipFull, overwrite: true);
#else
                if (File.Exists(zipFull)) File.Delete(zipFull);
                File.Move(temp, zipFull);
#endif
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static void ClearReadOnly(string path)
        {
            if (!File.Exists(path)) return;
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        // ZipArchive.CreateEntry takes any value and fails later, part way through writing.
        private static void CheckLevel(CompressionLevel level)
        {
            if (!Enum.IsDefined(typeof(CompressionLevel), level))
                throw new ArgumentOutOfRangeException(nameof(level), level, "Not a defined CompressionLevel.");
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
            using var archive = OpenArchive(fs, ZipArchiveMode.Read);
            EntriesOf(archive);
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

        /// <summary>
        /// Reads a single entry as text (UTF-8 by default), or throws if it is absent. A byte-order mark is honoured and
        /// dropped, as <see cref="File.ReadAllText(string, Encoding)"/> does.
        /// </summary>
        public static string ReadEntryText(string zipPath, string entryName, Encoding? encoding = null)
        {
            byte[]? bytes = ReadEntryBytes(zipPath, entryName)
                ?? throw new FileNotFoundException($"Entry not found in archive: {entryName}");
            using var reader = new StreamReader(new MemoryStream(bytes), encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        // .NET Framework's ZipArchive passes each name it reads through its path checks, so an archive with an entry named
        // "x.txt:hidden" or holding a NUL threw ArgumentException (blaming no argument) from every read, where .NET Core reads
        // it and extraction refuses the name with IOException. Update mode reads the names in the constructor.
        private static ZipArchive OpenArchive(Stream stream, ZipArchiveMode mode)
        {
            try { return new ZipArchive(stream, mode); }
            catch (ArgumentException ex) { throw UnreadableName(ex); }
        }

        private static System.Collections.ObjectModel.ReadOnlyCollection<ZipArchiveEntry> EntriesOf(ZipArchive archive)
        {
            try { return archive.Entries; }
            catch (ArgumentException ex) { throw UnreadableName(ex); }
        }

        private static IOException UnreadableName(ArgumentException ex)
            => new IOException("The archive has an entry name with a character not allowed in a file name, which .NET Framework cannot read.", ex);

        // Resolves an entry name against the root and verifies it stays inside — the Zip Slip guard.
        private static string SafeTarget(string root, string rootWithSeparator, string entryName)
        {
            // NUL threw ArgumentException from the path APIs; on Windows "x.txt:hidden" wrote an alternate data stream of
            // x.txt, data no directory listing shows.
            if (entryName.IndexOf('\0') >= 0 || (Path.DirectorySeparatorChar == '\\' && entryName.IndexOf(':') >= 0))
                throw new IOException($"Zip entry '{entryName.Replace('\0', '?')}' has a character not allowed in a file name.");
            string normalized = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            // On Windows "aux.txt" or "x\CON" opens a device: the data went to it (or the write failed with a misleading error).
            if (Path.DirectorySeparatorChar == '\\')
                foreach (string part in normalized.Split(Path.DirectorySeparatorChar))
                {
                    if (IsDeviceName(part))
                        throw new IOException($"Zip entry '{entryName}' uses the reserved device name '{part}'.");
                    if (IsBadWindowsName(part))
                        throw new IOException($"Zip entry '{entryName}' has a name Windows does not allow ('{part}').");
                }
            string full;
            // .NET Framework's path APIs throw ArgumentException for '<', '|', '"' and the like.
            try { full = Path.GetFullPath(Path.Combine(root, normalized)); }
            catch (ArgumentException ex) { throw new IOException($"Zip entry '{entryName}' has a character not allowed in a file name.", ex); }
            catch (NotSupportedException ex) { throw new IOException($"Zip entry '{entryName}' has a character not allowed in a file name.", ex); }
            if (!full.Equals(root, PathComparison) && !full.StartsWith(rootWithSeparator, PathComparison))
                throw new IOException($"Zip entry '{entryName}' would extract outside the target directory.");
            return full;
        }

        // Windows refuses '<', '>', '"', '|', '?', '*' and control characters in a name (the write failed part way with a
        // bare IOException), and drops a trailing dot or space, so "a." was written as "a", another entry's file.
        private static bool IsBadWindowsName(string part)
        {
            if (part.Length > 0 && part != "." && part != ".." && (part[part.Length - 1] == '.' || part[part.Length - 1] == ' ')) return true;
            foreach (char c in part)
                if (c < ' ' || c == '<' || c == '>' || c == '"' || c == '|' || c == '?' || c == '*') return true;
            return false;
        }

        private static bool IsDeviceName(string part)
        {
            int dot = part.IndexOf('.');
            string stem = (dot >= 0 ? part.Substring(0, dot) : part).TrimEnd(' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CONIN$" || stem == "CONOUT$") return true;
            return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "0123456789\u00B9\u00B2\u00B3".IndexOf(stem[3]) >= 0;
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

        // Files, plus directories that hold nothing archivable (so they survive the round trip). Links are not followed.
        private static IEnumerable<(string Path, bool IsEmptyDirectory)> EnumerateNoLinks(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                bool any = false;
                foreach (string f in List(dir, directories: false))
                    if (!IsLink(Exact(f), File.GetAttributes(Exact(f)))) { any = true; yield return (f, false); }
                foreach (string d in List(dir, directories: true))
                    if (!IsLink(Exact(d), File.GetAttributes(Exact(d)))) { any = true; pending.Push(d); }
                if (!any && !string.Equals(dir, root, PathComparison)) yield return (dir, true);
            }
        }

        // Win32 path normalisation drops a trailing dot or space from each name, so a listed file "a." was read, replaced
        // or deleted as "a" (another file) and a folder "d." could not be opened. The \\?\ form is used as written.
        private static string Exact(string path)
        {
            if (Path.DirectorySeparatorChar != '\\' || !NeedsExact(path)) return path;
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
