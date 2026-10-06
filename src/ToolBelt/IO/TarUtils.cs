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
    /// extraction rejects entries that would escape the destination (<c>../</c>, absolute paths) and never creates symbolic
    /// or hard links (which can point outside it): a link entry is an error, or is skipped with <c>skipLinks</c>. Gzip is chosen by extension (<c>.tar.gz</c>,
    /// <c>.tgz</c>) or by the <c>gzip</c> argument. Entries are written in PAX format with forward-slash names.
    /// </summary>
    public static class TarUtils
    {
        private static readonly StringComparison PathComparison =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>Archives every file under <paramref name="sourceDirectory"/> (recursively, links not followed).</summary>
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
                foreach (string file in EnumerateFilesNoLinks(root))
                {
                    if (string.Equals(Path.GetFullPath(file), archiveFull, PathComparison)) continue;   // the archive being written
                    string name = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (filter != null && !filter(name)) continue;
                    writer.WriteEntry(file, name);
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
                string target = SafeTarget(rootWithSep, entry.Name);
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(target);
                        break;
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                    case TarEntryType.ContiguousFile:
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        if (File.Exists(target) && !overwrite) throw new IOException($"Destination file already exists: {target}");
                        using (var outFile = new FileStream(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            entry.DataStream?.CopyTo(outFile);
                        break;
                    case TarEntryType.SymbolicLink:
                    case TarEntryType.HardLink:
                        // Creating links safely (no escape through link chains, hardlinks to files outside) is a minefield, and
                        // TarEntry.ExtractToFile doesn't support links anyway. Refuse them, or skip on request.
                        if (skipLinks) continue;
                        throw new InvalidDataException($"Archive entry '{entry.Name}' is a link; links are not extracted (pass skipLinks to ignore them).");
                    default:
                        continue;                                           // PAX/GNU metadata, devices, FIFOs: skipped
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
            while ((entry = reader.GetNextEntry()) != null) names.Add(entry.Name);
            return names;
        }

        private static bool UseGzip(string path, bool? gzip)
            => gzip ?? (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase));

        private static string SafeTarget(string rootWithSep, string entryName)
        {
            if (string.IsNullOrEmpty(entryName)) throw new InvalidDataException("Archive entry has no name.");
            string relative = entryName.Replace('\\', '/');
            if (relative.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidDataException($"Archive entry '{entryName}' has an absolute path.");
            string target = Path.GetFullPath(Path.Combine(rootWithSep, relative));
            if (!(target + Path.DirectorySeparatorChar).StartsWith(rootWithSep, PathComparison))
                throw new InvalidDataException($"Archive entry '{entryName}' would extract outside the destination.");
            return target;
        }

        private static IEnumerable<string> EnumerateFilesNoLinks(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                foreach (string f in Directory.EnumerateFiles(dir))
                    if ((File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0) yield return f;
                foreach (string d in Directory.EnumerateDirectories(dir))
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) pending.Push(d);
            }
        }
    }
}
#endif
