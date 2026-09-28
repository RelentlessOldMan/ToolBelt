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
    /// writes outside its target directory.
    /// </summary>
    public static class ZipUtils
    {
        private static readonly StringComparison PathComparison =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>Zips every file under <paramref name="sourceDirectory"/> (recursively).</summary>
        public static void CreateFromDirectory(
            string sourceDirectory, string zipPath,
            Func<string, bool>? filter = null, Action<string>? onEntry = null, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (sourceDirectory is null) throw new ArgumentNullException(nameof(sourceDirectory));
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);

            string root = Path.GetFullPath(sourceDirectory);
            using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string entryName = Path.GetFullPath(file).Substring(root.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                if (filter != null && !filter(entryName)) continue;

                ZipArchiveEntry entry = archive.CreateEntry(entryName, level);
                using (Stream entryStream = entry.Open())
                using (FileStream input = File.OpenRead(file))
                    input.CopyTo(entryStream);
                onEntry?.Invoke(entryName);
            }
        }

        /// <summary>Extracts the archive into <paramref name="destinationDirectory"/>, rejecting entries that escape it.</summary>
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

                // Directory entry (name ends with a separator, no file name).
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                string? dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(target) && !overwrite)
                    throw new IOException($"Destination file already exists: {target}");

                using (Stream entryStream = entry.Open())
                using (var output = new FileStream(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    entryStream.CopyTo(output);
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

        /// <summary>Adds an entry, replacing any existing one with the same name.</summary>
        public static void AddOrReplaceEntry(string zipPath, string entryName, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (entryName is null) throw new ArgumentNullException(nameof(entryName));
            if (content is null) throw new ArgumentNullException(nameof(content));

            using var fs = new FileStream(zipPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Update);
            archive.GetEntry(entryName)?.Delete();
            ZipArchiveEntry entry = archive.CreateEntry(entryName, level);
            using Stream entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }

        /// <summary>Reads a single entry's bytes without extracting, or null if it is absent.</summary>
        public static byte[]? ReadEntryBytes(string zipPath, string entryName)
        {
            if (zipPath is null) throw new ArgumentNullException(nameof(zipPath));
            if (entryName is null) throw new ArgumentNullException(nameof(entryName));

            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            ZipArchiveEntry? entry = archive.GetEntry(entryName);
            if (entry is null) return null;
            using Stream entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
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
            string normalized = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, normalized));
            if (!full.Equals(root, PathComparison) && !full.StartsWith(rootWithSeparator, PathComparison))
                throw new IOException($"Zip entry '{entryName}' would extract outside the target directory.");
            return full;
        }
    }
}
