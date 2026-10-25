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

            bool completed = false;
            try
            {
                using var fs = new FileStream(archiveFull, FileMode.Create, FileAccess.Write, FileShare.None);
                using Stream output = UseGzip(archivePath, gzip) ? new GZipStream(fs, CompressionLevel.Optimal) : (Stream)fs;
                using var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: false);
                foreach ((string path, bool isEmptyDirectory) in EnumerateNoLinks(root))
                {
                    if (string.Equals(Path.GetFullPath(path), archiveFull, PathComparison)) continue;   // the archive being written
                    string name = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');   // a backslash is a name character on Unix
                    if (isEmptyDirectory) name += "/";
                    if (filter != null && !filter(name)) continue;
                    if (isEmptyDirectory) writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name));
                    else writer.WriteEntry(path, name);
                    onEntry?.Invoke(name);
                }
                completed = true;
            }
            finally
            {
                // Don't leave a partial archive behind.
                if (!completed) { try { File.Delete(archiveFull); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }

        /// <summary>
        /// Extracts into <paramref name="destinationDirectory"/>, rejecting escaping entries. Links are never created: a link
        /// entry throws <see cref="InvalidDataException"/>, or is skipped when <paramref name="skipLinks"/>.
        /// </summary>
        public static void ExtractToDirectory(string archivePath, string destinationDirectory, bool overwrite = false, bool? gzip = null,
            bool skipLinks = false, Action<string>? onEntry = null)
        {
            if (archivePath is null) throw new ArgumentNullException(nameof(archivePath));
            if (destinationDirectory is null) throw new ArgumentNullException(nameof(destinationDirectory));
            string root = Path.GetFullPath(destinationDirectory);
            string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(root);

            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using Stream input = UseGzip(archivePath, gzip) ? new GZipStream(fs, CompressionMode.Decompress) : (Stream)fs;
            using var reader = new TarReader(input);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) != null)
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
                        break;
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                    case TarEntryType.ContiguousFile:
                        // "x/.." is the destination itself, which cannot be opened as a file (UnauthorizedAccessException).
                        // "..." too: Windows drops trailing dots, so it resolves to the destination with its separator.
                        if (target.Length <= rootWithSep.Length)
                            throw new InvalidDataException($"Archive entry '{entry.Name}' names the destination directory itself.");
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        if (File.Exists(target) && !overwrite) throw new IOException($"Destination file already exists: {target}");
                        WriteFile(target, entry.DataStream, entry.Length, overwrite);
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
        }

        /// <summary>The names of every entry (files, directories and links).</summary>
        public static IReadOnlyList<string> ListEntries(string archivePath, bool? gzip = null)
        {
            if (archivePath is null) throw new ArgumentNullException(nameof(archivePath));
            var names = new List<string>();
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using Stream input = UseGzip(archivePath, gzip) ? new GZipStream(fs, CompressionMode.Decompress) : (Stream)fs;
            using var reader = new TarReader(input);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) != null)
                if (IsExtractable(entry.EntryType)) names.Add(entry.Name);   // not PAX/GNU metadata headers
            return names;
        }

        // Written beside the target and moved into place: a truncated archive then leaves the existing file as it was, and
        // the move replaces the name, so an existing hard link at the target is broken rather than written through.
        private static void WriteFile(string target, Stream? data, long length, bool overwrite)
        {
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
                File.Move(temp, target, overwrite);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
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
            string relative = entryName.Replace('\\', '/');
            if (relative.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidDataException($"Archive entry '{entryName}' has an absolute path.");
            string target = Path.GetFullPath(Path.Combine(rootWithSep, relative));
            if (!(target + Path.DirectorySeparatorChar).StartsWith(rootWithSep, PathComparison))
                throw new InvalidDataException($"Archive entry '{entryName}' would extract outside the destination.");
            return target;
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
                foreach (string f in Directory.EnumerateFiles(dir))
                    if (!IsLink(f, File.GetAttributes(f))) { any = true; yield return (f, false); }
                foreach (string d in Directory.EnumerateDirectories(dir))
                    if (!IsLink(d, File.GetAttributes(d))) { any = true; pending.Push(d); }
                if (!any && !string.Equals(dir, root, PathComparison)) yield return (dir, true);
            }
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
    }
}
#endif
