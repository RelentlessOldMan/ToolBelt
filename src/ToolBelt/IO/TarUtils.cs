// ToolBelt drop-in — fully self-contained (BCL only; net8+ for System.Formats.Tar).
#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ToolBelt.IO
{
    /// <summary>
    /// Tar and .tar.gz archives over <see cref="System.Formats.Tar"/>, with the same safety as <see cref="ZipUtils"/>:
    /// extraction rejects entries that would escape the destination (<c>../</c>, absolute paths, or a junction or symbolic
    /// link already inside it) and never creates symbolic or hard links (which can point outside it): a link entry is an
    /// error, or is skipped with <c>skipLinks</c>. Sparse files are rejected rather than silently dropped. Gzip is chosen by extension (<c>.tar.gz</c>,
    /// <c>.tgz</c>) or by the <c>gzip</c> argument. Entries are written in PAX format with forward-slash names.
    /// </summary>
    public static class TarUtils
    {
        private static readonly StringComparison PathComparison =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// Archives every file under <paramref name="sourceDirectory"/> (recursively, links not followed). Empty directories
        /// are kept as directory entries (named with a trailing <c>/</c>, which is also what <paramref name="filter"/> sees).
        /// </summary>
        public static void CreateFromDirectory(string sourceDirectory, string archivePath, bool? gzip = null,
            Func<string, bool>? filter = null, Action<string>? onEntry = null)
        {
            if (sourceDirectory is null) throw new ArgumentNullException(nameof(sourceDirectory));
            if (archivePath is null) throw new ArgumentNullException(nameof(archivePath));
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
            string root = Path.GetFullPath(sourceDirectory);
            string archiveFull = Path.GetFullPath(archivePath);

            // Written beside the archive and moved over it once complete (after the writers' final flush): opening the archive
            // with FileMode.Create destroyed an existing good one when the run then failed.
            string temp = Path.Combine(Path.GetDirectoryName(archiveFull)!, ".tar-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (Stream output = UseGzip(archivePath, gzip) ? new GZipStream(fs, CompressionLevel.Optimal) : (Stream)fs)
                using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: false))
                foreach ((string path, bool isEmptyDirectory) in EnumerateNoLinks(root))
                {
                    // The listed path as it is: Path.GetFullPath (and GetRelativePath) would drop a trailing dot or space from a name.
                    if (string.Equals(path, archiveFull, PathComparison) || string.Equals(path, temp, PathComparison)) continue;   // the archive being written
                    string name = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace(Path.DirectorySeparatorChar, '/');   // a backslash is a name character on Unix
                    if (isEmptyDirectory) name += "/";
                    if (filter != null && !filter(name)) continue;
                    if (isEmptyDirectory) writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name) { ModificationTime = Directory.GetLastWriteTimeUtc(Exact(path)) });
                    else writer.WriteEntry(Exact(path), name);
                    onEntry?.Invoke(name);
                }
                File.Move(temp, archiveFull, overwrite: true);
            }
            finally
            {
                // Don't leave a partial archive behind.
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Extracts into <paramref name="destinationDirectory"/>, rejecting escaping entries. Links are never created: a link
        /// entry throws <see cref="InvalidDataException"/>, or is skipped when <paramref name="skipLinks"/>. Files and
        /// directories get their archived modification times; on Unix files keep their permission bits, less group and
        /// other write. On Windows a reserved device name (<c>CON</c>, <c>aux.txt</c>, <c>COM1</c>) is rejected. An entry
        /// that would take the files written past <paramref name="maxTotalBytes"/> throws <see cref="InvalidDataException"/>
        /// before it is written (a guard against gzip bombs); what was extracted before it is kept.
        /// </summary>
        public static void ExtractToDirectory(string archivePath, string destinationDirectory, bool overwrite = false, bool? gzip = null,
            bool skipLinks = false, Action<string>? onEntry = null, long maxTotalBytes = long.MaxValue)
        {
            if (archivePath is null) throw new ArgumentNullException(nameof(archivePath));
            if (destinationDirectory is null) throw new ArgumentNullException(nameof(destinationDirectory));
            if (maxTotalBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes), maxTotalBytes, "Must be non-negative.");
            long budget = maxTotalBytes;
            string root = Path.GetFullPath(destinationDirectory);
            string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(root);

            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using Stream input = UseGzip(archivePath, gzip) ? new GZipStream(fs, CompressionMode.Decompress) : (Stream)fs;
            using var reader = new TarReader(new MetadataGuardStream(input));
            var directoryTimes = new List<(string Path, DateTimeOffset Time)>();
            TarEntry? entry;
            while ((entry = NextEntry(reader)) != null)
            {
                // PAX/GNU metadata, devices and FIFOs are skipped before their names are checked: a PAX global header is
                // named with an absolute path ("$TMPDIR/GlobalHead.%p.%n"), which failed the whole extraction.
                if (!IsExtractable(entry.EntryType)) continue;
                string target = SafeTarget(rootWithSep, entry.Name);
                EnsureNoLinks(rootWithSep, target, entry.Name);
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(target);
                        directoryTimes.Add((target, entry.ModificationTime));
                        break;
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                    case TarEntryType.ContiguousFile:
                        // "x/.." is the destination itself, which cannot be opened as a file (UnauthorizedAccessException).
                        // "..." too: Windows drops trailing dots, so it resolves to the destination with its separator.
                        if (target.Length <= rootWithSep.Length)
                            throw new InvalidDataException($"Archive entry '{entry.Name}' names the destination directory itself.");
                        // "dir/" as a file: the directory was made, then writing the file over it failed with a bare IOException.
                        if (entry.Name.EndsWith("/", StringComparison.Ordinal) || entry.Name.EndsWith("\\", StringComparison.Ordinal))
                            throw new InvalidDataException($"Archive entry '{entry.Name}' is a file named like a directory.");
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        if (File.Exists(target) && !overwrite) throw new IOException($"Destination file already exists: {target}");
                        if (entry.Length > budget)
                            throw new InvalidDataException($"Extracting '{entry.Name}' would pass the {maxTotalBytes}-byte limit.");
                        budget -= entry.Length;
                        WriteFile(target, entry, overwrite);
                        break;
                    case TarEntryType.SymbolicLink:
                    case TarEntryType.HardLink:
                        // Creating links safely (no escape through link chains, hardlinks to files outside) is a minefield, and
                        // TarEntry.ExtractToFile doesn't support links anyway. Refuse them, or skip on request.
                        if (skipLinks) continue;
                        throw new InvalidDataException($"Archive entry '{entry.Name}' is a link; links are not extracted (pass skipLinks to ignore them).");
                    case TarEntryType.SparseFile:
                        throw new InvalidDataException($"Archive entry '{entry.Name}' is a sparse file, which is not supported.");
                    default:
                        continue;
                }
                onEntry?.Invoke(entry.Name);
            }
            // Last, and deepest first: writing into a directory sets its modification time.
            for (int i = directoryTimes.Count - 1; i >= 0; i--)
                SetTime(directoryTimes[i].Path, directoryTimes[i].Time, directory: true);
        }

        /// <summary>The names of every entry (files, directories and links).</summary>
        public static IReadOnlyList<string> ListEntries(string archivePath, bool? gzip = null)
        {
            if (archivePath is null) throw new ArgumentNullException(nameof(archivePath));
            var names = new List<string>();
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using Stream input = UseGzip(archivePath, gzip) ? new GZipStream(fs, CompressionMode.Decompress) : (Stream)fs;
            using var reader = new TarReader(new MetadataGuardStream(input));
            TarEntry? entry;
            while ((entry = NextEntry(reader)) != null)
                if (IsExtractable(entry.EntryType)) names.Add(entry.Name);   // not PAX/GNU metadata headers
            return names;
        }

        // Written beside the target and moved into place: a truncated archive then leaves the existing file as it was, and
        // the move replaces the name, so an existing hard link at the target is broken rather than written through.
        private static void WriteFile(string target, TarEntry entry, bool overwrite)
        {
            Stream? data = entry.DataStream;
            long length = entry.Length;
            string temp = Path.Combine(Path.GetDirectoryName(target)!, ".tar-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var outFile = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    data?.CopyTo(outFile);
                    // A truncated archive ends the entry's data early without an error (that comes at the next header).
                    if (outFile.Length != length)
                        throw new InvalidDataException($"Archive entry '{Path.GetFileName(target)}' is truncated: {outFile.Length} of {length} bytes.");
                }
                SetTime(temp, entry.ModificationTime, directory: false);
                // The permission bits, less group and other write (as tar does under the usual umask); never setuid or sticky.
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, entry.Mode & (UnixFileMode)0b111_101_101);
                // A read-only file is not replaced by a move (UnauthorizedAccessException); overwrite means replace it.
                if (overwrite && File.Exists(target))
                {
                    FileAttributes attributes = File.GetAttributes(target);
                    if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
                }
                File.Move(temp, target, overwrite);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // A time outside what the file system can store (before 1601 on Windows) is left as it is.
        private static void SetTime(string path, DateTimeOffset time, bool directory)
        {
            try
            {
                if (directory) Directory.SetLastWriteTimeUtc(path, time.UtcDateTime);
                else File.SetLastWriteTimeUtc(path, time.UtcDateTime);
            }
            catch (ArgumentOutOfRangeException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // An archive that ends inside a header threw EndOfStreamException, unlike every other kind of damage.
        private static TarEntry? NextEntry(TarReader reader)
        {
            try { return reader.GetNextEntry(); }
            catch (EndOfStreamException ex) { throw new InvalidDataException("The archive is truncated.", ex); }
        }

        // Files, directories and links: the entries ExtractToDirectory acts on (or refuses). Everything else is skipped.
        private static bool IsExtractable(TarEntryType type)
            => type == TarEntryType.Directory || type == TarEntryType.RegularFile || type == TarEntryType.V7RegularFile
            || type == TarEntryType.ContiguousFile || type == TarEntryType.SymbolicLink || type == TarEntryType.HardLink
            || type == TarEntryType.SparseFile;

        private static bool UseGzip(string path, bool? gzip)
            => gzip ?? (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase));

        private static string SafeTarget(string rootWithSep, string entryName)
        {
            if (string.IsNullOrEmpty(entryName)) throw new InvalidDataException("Archive entry has no name.");
            // On Windows "x.txt:hidden" is an alternate data stream of x.txt, data no directory listing shows.
            if (entryName.IndexOf('\0') >= 0 || (Path.DirectorySeparatorChar == '\\' && entryName.IndexOf(':') >= 0))
                throw new InvalidDataException($"Archive entry '{entryName.Replace('\0', '?')}' has a character not allowed in a file name.");
            // A backslash is a separator only where it is one: on Unix it is a name character (Create keeps it), and
            // "a\b" came back as file b in directory a.
            string relative = Path.DirectorySeparatorChar == '\\' ? entryName.Replace('\\', '/') : entryName;
            if (relative.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidDataException($"Archive entry '{entryName}' has an absolute path.");
            // On Windows "aux.txt" or "x/CON" opens a device: the data went to it (or the write failed with a misleading error).
            if (Path.DirectorySeparatorChar == '\\')
                foreach (string part in relative.Split('/'))
                {
                    if (IsDeviceName(part))
                        throw new InvalidDataException($"Archive entry '{entryName}' uses the reserved device name '{part}'.");
                    if (IsBadWindowsName(part))
                        throw new InvalidDataException($"Archive entry '{entryName}' has a name Windows does not allow ('{part}').");
                }
            string target = Path.GetFullPath(Path.Combine(rootWithSep, relative));
            if (!(target + Path.DirectorySeparatorChar).StartsWith(rootWithSep, PathComparison))
                throw new InvalidDataException($"Archive entry '{entryName}' would extract outside the destination.");
            return target;
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
        private static void EnsureNoLinks(string rootWithSep, string target, string entryName)
        {
            if (target.Length <= rootWithSep.Length) return;
            string path = rootWithSep;
            foreach (string part in target.Substring(rootWithSep.Length).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                path = Path.Combine(path, part);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (FileNotFoundException) { return; }
                catch (DirectoryNotFoundException) { return; }
                if (IsLink(path, attributes))
                    throw new InvalidDataException($"Archive entry '{entryName}' would be written through the link '{path}'.");
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

        // A reparse point that is a link (junction or symbolic link); other reparse points, such as cloud-file
        // placeholders, are ordinary files and folders.
        private static bool IsLink(string path, FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReparsePoint) == 0) return false;
            try
            {
                FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
                return info.LinkTarget != null;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        // TarReader reads a PAX or GNU metadata entry (long name, extended attributes) into one buffer of the size its header
        // declares, up to 2 GB, before anything else is checked: a 1 KB archive could demand a 2 GB allocation. This
        // forward-only stream follows the headers as TarReader reads through it and rejects an oversized metadata entry first.
        private sealed class MetadataGuardStream : Stream
        {
            private const int Block = 512;
            private const long MaxMetadataBytes = 1 << 20;
            private readonly Stream _inner;
            private readonly byte[] _header = new byte[Block];
            private int _headerPos, _headerLength;   // the header being passed on
            private long _dataLeft;                  // data and padding to pass before the next header
            private MemoryStream? _pax;              // a PAX header's records, read for a size that overrides the next entry's
            private long _paxLeft;
            private long? _nextSize;
            private bool _ended;

            public MetadataGuardStream(Stream inner) => _inner = inner;

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count == 0) return 0;
                if (_headerPos == _headerLength && _dataLeft == 0 && (_ended || !NextHeader())) return 0;
                if (_headerPos < _headerLength)
                {
                    int n = Math.Min(count, _headerLength - _headerPos);
                    Buffer.BlockCopy(_header, _headerPos, buffer, offset, n);
                    _headerPos += n;
                    return n;
                }
                int read = _inner.Read(buffer, offset, (int)Math.Min(count, _dataLeft));
                if (read == 0) { _ended = true; _dataLeft = 0; return 0; }
                _dataLeft -= read;
                if (_pax != null)
                {
                    int take = (int)Math.Min(read, _paxLeft);
                    _pax.Write(buffer, offset, take);
                    if ((_paxLeft -= take) == 0) { _nextSize = PaxSize(_pax.ToArray()); _pax = null; }
                }
                return read;
            }

            private bool NextHeader()
            {
                int got = 0, r;
                while (got < Block && (r = _inner.Read(_header, got, Block - got)) > 0) got += r;
                _headerPos = 0;
                _headerLength = got;
                if (got < Block) { _ended = true; return got > 0; }   // TarReader reports the truncation
                bool zero = true;
                foreach (byte b in _header) if (b != 0) { zero = false; break; }
                if (zero) return true;

                char type = (char)_header[156];
                long size = ParseSize();
                bool metadata = type == 'x' || type == 'g' || type == 'L' || type == 'K';
                if (metadata && size > MaxMetadataBytes)
                    throw new InvalidDataException($"Archive has a {size}-byte metadata entry; the limit is {MaxMetadataBytes} bytes.");
                if (!metadata)
                {
                    if (_nextSize is long s) size = s;
                    _nextSize = null;
                }
                if (size > long.MaxValue - Block) throw Unreadable("size field");
                _dataLeft = (size + Block - 1) / Block * Block;
                if (type == 'x' && size > 0) { _pax = new MemoryStream((int)size); _paxLeft = size; }
                return true;
            }

            // The guard is only as good as its agreement with TarReader on where each header starts, so anything the two
            // could read differently is refused rather than guessed at: a size field that is not plain octal (a leading NUL
            // read as 0 here let TarReader find gigabytes), or PAX records that are not exactly "<length> <key>=<value>\n".

            // The 12-byte size field: octal digits between leading and trailing NULs or spaces, or base-256 (0x80 then
            // eleven bytes, big-endian).
            private long ParseSize()
            {
                const int at = 124, length = 12;
                long value = 0;
                if (_header[at] == 0x80)
                {
                    for (int i = 1; i < length; i++)
                    {
                        if (value > (long.MaxValue >> 8)) throw Unreadable("size field");
                        value = (value << 8) | _header[at + i];
                    }
                    return value;
                }
                int start = 0, end = length;
                while (start < end && (_header[at + start] == 0 || _header[at + start] == ' ')) start++;
                while (end > start && (_header[at + end - 1] == 0 || _header[at + end - 1] == ' ')) end--;
                for (int k = start; k < end; k++)
                {
                    int digit = _header[at + k] - '0';
                    if (digit < 0 || digit > 7 || value > (long.MaxValue >> 3)) throw Unreadable("size field");
                    value = value * 8 + digit;
                }
                return value;
            }

            // PAX records are "<length> <key>=<value>\n". TarReader ignores the length and splits at each newline, so a
            // length that does not end exactly on a record's newline, a value that is not plain digits, or a repeated
            // "size" is refused. Only "size" matters here.
            private static long? PaxSize(byte[] data)
            {
                long? size = null;
                int pos = 0;
                while (pos < data.Length)
                {
                    if (data[pos] == 0)
                    {
                        for (int i = pos; i < data.Length; i++) if (data[i] != 0) throw Unreadable("PAX header");
                        break;   // NUL padding: TarReader finds no further record either
                    }
                    int length = 0, k = pos;
                    for (; k < data.Length && data[k] >= '0' && data[k] <= '9'; k++)
                    {
                        if (length > (int.MaxValue - 9) / 10) throw Unreadable("PAX header");
                        length = length * 10 + (data[k] - '0');
                    }
                    int recordEnd = pos + length;   // exclusive; the newline is at recordEnd - 1
                    if (k == pos || k >= data.Length || data[k] != ' ' || length <= k - pos + 1 || length > data.Length - pos
                        || data[recordEnd - 1] != '\n' || Array.IndexOf(data, (byte)'\n', k, recordEnd - 1 - k) >= 0)
                        throw Unreadable("PAX header");
                    int keyStart = k + 1;
                    if (recordEnd - 1 - keyStart >= 5 && data[keyStart] == 's' && data[keyStart + 1] == 'i' && data[keyStart + 2] == 'z'
                        && data[keyStart + 3] == 'e' && data[keyStart + 4] == '=')
                    {
                        if (size != null) throw Unreadable("PAX header");
                        long value = 0;
                        int digits = recordEnd - 1 - (keyStart + 5);
                        if (digits < 1 || digits > 18) throw Unreadable("PAX size");
                        for (int i = keyStart + 5; i < recordEnd - 1; i++)
                        {
                            if (data[i] < '0' || data[i] > '9') throw Unreadable("PAX size");
                            value = value * 10 + (data[i] - '0');
                        }
                        size = value;
                    }
                    pos = recordEnd;
                }
                return size;
            }

            private static InvalidDataException Unreadable(string what)
                => new InvalidDataException($"Archive has a malformed {what}, which TarReader could read differently from this guard; refusing it.");

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
#endif
