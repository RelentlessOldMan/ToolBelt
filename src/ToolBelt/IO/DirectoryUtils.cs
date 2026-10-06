// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ToolBelt.IO
{
    /// <summary>What <see cref="DirectoryUtils.Copy"/> does when a destination file already exists.</summary>
    public enum OverwritePolicy
    {
        /// <summary>Replace it (even if read-only).</summary>
        Overwrite,
        /// <summary>Leave it and count the file as skipped.</summary>
        Skip,
        /// <summary>Throw <see cref="IOException"/> (or report to <see cref="CopyOptions.OnError"/>).</summary>
        Fail,
        /// <summary>Replace it only if the source was modified more recently.</summary>
        IfNewer,
    }

    /// <summary>Options for <see cref="DirectoryUtils.Copy"/>.</summary>
    public sealed class CopyOptions
    {
        public OverwritePolicy Overwrite { get; set; } = OverwritePolicy.Overwrite;

        /// <summary>Copy a file only if this returns true for its path relative to the source root ('/'-separated).</summary>
        public Func<string, bool>? FileFilter { get; set; }

        /// <summary>Descend into a subdirectory only if this returns true for its relative path ('/'-separated).</summary>
        public Func<string, bool>? DirectoryFilter { get; set; }

        /// <summary>Give copies the source's last-write time (default true).</summary>
        public bool PreserveTimestamps { get; set; } = true;

        /// <summary>
        /// Receives (path, exception) for each failure and lets the copy continue. When null, the first failure throws.
        /// </summary>
        public Action<string, Exception>? OnError { get; set; }
    }

    /// <summary>What a <see cref="DirectoryUtils.Copy"/> did.</summary>
    public readonly struct CopyResult
    {
        public CopyResult(int filesCopied, int filesSkipped, int directoriesCreated, long bytesCopied, int errors)
        {
            FilesCopied = filesCopied;
            FilesSkipped = filesSkipped;
            DirectoriesCreated = directoriesCreated;
            BytesCopied = bytesCopied;
            Errors = errors;
        }

        public int FilesCopied { get; }
        public int FilesSkipped { get; }
        public int DirectoriesCreated { get; }
        public long BytesCopied { get; }
        public int Errors { get; }
    }

    /// <summary>Size and counts for a directory tree.</summary>
    public readonly struct DirectorySummary
    {
        public DirectorySummary(long totalBytes, int fileCount, int directoryCount, int inaccessibleCount)
        {
            TotalBytes = totalBytes;
            FileCount = fileCount;
            DirectoryCount = directoryCount;
            InaccessibleCount = inaccessibleCount;
        }

        public long TotalBytes { get; }
        public int FileCount { get; }

        /// <summary>Subdirectories below the root (the root itself is not counted).</summary>
        public int DirectoryCount { get; }

        /// <summary>Directories or files that could not be read and were skipped.</summary>
        public int InaccessibleCount { get; }
    }

    /// <summary>
    /// Directory-tree operations that the framework leaves to every caller: recursive copy with overwrite and filter
    /// policies, recursive delete that clears read-only attributes and retries transient locks (antivirus and indexers
    /// briefly hold files), make-empty, size/count summaries, and a tolerant file walk that reports unreadable subtrees
    /// to a callback and carries on instead of throwing halfway.
    /// <para>
    /// Links are never followed: a junction or symbolic link to a directory is not descended into by any operation, and
    /// <see cref="Delete"/> removes the link itself — never the target's contents, which a naive recursive delete would
    /// wipe out. Walks are iterative, so deep trees cannot overflow the stack.
    /// </para>
    /// </summary>
    public static class DirectoryUtils
    {
        private static readonly StringComparison PathComparison =
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // ---------- copy ----------

        /// <summary>
        /// Copies the tree under <paramref name="source"/> into <paramref name="destination"/> (created as needed).
        /// The destination may not lie inside the source.
        /// </summary>
        public static CopyResult Copy(string source, string destination, CopyOptions? options = null)
        {
            options ??= new CopyOptions();
            string src = FullDirectory(source, nameof(source));
            string dst = FullDirectory(destination, nameof(destination));
            if (!Directory.Exists(src)) throw new DirectoryNotFoundException($"Source directory '{source}' does not exist.");
            if (IsSameOrInside(dst, src))
                throw new ArgumentException("The destination may not be the source or lie inside it.", nameof(destination));

            int copied = 0, skipped = 0, created = 0, errors = 0;
            long bytes = 0;
            var pending = new Stack<string>();
            pending.Push(src);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                string rel = Relative(src, dir);
                string target = rel.Length == 0 ? dst : Path.Combine(dst, rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    // A junction/symlink already sitting in the destination tree would redirect the copy outside it.
                    if (rel.Length > 0 && IsLink(target))
                        throw new IOException($"Destination '{target}' is a link; refusing to copy through it.");
                    if (!Directory.Exists(target)) { Directory.CreateDirectory(target); created++; }
                }
                catch (Exception ex) when (IsIoError(ex)) { errors++; Report(options.OnError, target, ex); continue; }

                string[] files, dirs;
                try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
                catch (Exception ex) when (IsIoError(ex)) { errors++; Report(options.OnError, dir, ex); continue; }

                foreach (string file in files)
                {
                    string fileRel = Relative(src, file);
                    if (options.FileFilter != null && !options.FileFilter(fileRel)) continue;
                    string to = Path.Combine(target, Path.GetFileName(file));
                    try
                    {
                        if (IsLink(to)) throw new IOException($"Destination '{to}' is a link; refusing to write through it.");
                        if (File.Exists(to))
                        {
                            bool replace = options.Overwrite switch
                            {
                                OverwritePolicy.Skip => false,
                                OverwritePolicy.Fail => throw new IOException($"Destination file '{to}' already exists."),
                                OverwritePolicy.IfNewer => File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(to),
                                _ => true,
                            };
                            if (!replace) { skipped++; continue; }
                            ClearReadOnly(to);
                        }
                        File.Copy(file, to, overwrite: true);
                        if (options.PreserveTimestamps) File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(file));
                        bytes += new FileInfo(to).Length;
                        copied++;
                    }
                    catch (Exception ex) when (IsIoError(ex)) { errors++; Report(options.OnError, file, ex); }
                }

                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    if (IsLink(dirs[i])) continue;
                    if (options.DirectoryFilter != null && !options.DirectoryFilter(Relative(src, dirs[i]))) continue;
                    pending.Push(dirs[i]);
                }
            }
            return new CopyResult(copied, skipped, created, bytes, errors);
        }

        // ---------- delete / empty ----------

        /// <summary>
        /// Deletes a directory tree (or a single file), clearing read-only attributes and retrying each item up to
        /// <paramref name="retries"/> times, <paramref name="retryDelay"/> apart (100 ms by default), when it is briefly
        /// locked. A link is removed without touching its target. Returns false if nothing existed at the path. Blocks
        /// while waiting between retries.
        /// </summary>
        public static bool Delete(string path, int retries = 3, TimeSpan? retryDelay = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (retries < 0) throw new ArgumentOutOfRangeException(nameof(retries), retries, "Must be non-negative.");
            TimeSpan delay = retryDelay ?? TimeSpan.FromMilliseconds(100);
            string full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                Retry(() => { ClearReadOnly(full); File.Delete(full); }, retries, delay);
                return true;
            }
            if (!Directory.Exists(full)) return false;
            DeleteTree(full, retries, delay, deleteRoot: true);
            return true;
        }

        /// <summary>Makes <paramref name="path"/> an empty directory: creates it if missing, otherwise deletes everything inside it.</summary>
        public static void EnsureEmpty(string path, int retries = 3, TimeSpan? retryDelay = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            string full = Path.GetFullPath(path);
            if (File.Exists(full)) throw new IOException($"'{path}' is a file, not a directory.");
            Directory.CreateDirectory(full);
            DeleteTree(full, retries, retryDelay ?? TimeSpan.FromMilliseconds(100), deleteRoot: false);
        }

        private static void DeleteTree(string root, int retries, TimeSpan delay, bool deleteRoot)
        {
            if (IsLink(root))
            {
                // Remove the link only; recursing would delete the target's contents.
                if (deleteRoot) Retry(() => { ClearReadOnly(root); Directory.Delete(root, recursive: false); }, retries, delay);
                return;
            }

            // Collect directories top-down, then delete files and directories bottom-up.
            var order = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                order.Add(dir);
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    if (IsLink(sub)) Retry(() => { ClearReadOnly(sub); Directory.Delete(sub, recursive: false); }, retries, delay);
                    else pending.Push(sub);
                }
            }
            for (int i = order.Count - 1; i >= 0; i--)
            {
                string dir = order[i];
                foreach (string file in Directory.GetFiles(dir))
                    Retry(() => { ClearReadOnly(file); File.Delete(file); }, retries, delay);
                if (i > 0 || deleteRoot)
                    Retry(() => { ClearReadOnly(dir); Directory.Delete(dir, recursive: false); }, retries, delay);
            }
        }

        // ---------- walk / summary ----------

        /// <summary>
        /// Lazily enumerates files under <paramref name="root"/> matching <paramref name="searchPattern"/>, depth-first.
        /// A directory that cannot be read is passed to <paramref name="onError"/> (if given) and skipped along with its
        /// subtree; the walk continues. <paramref name="directoryFilter"/> (relative, '/'-separated path) prunes subtrees.
        /// Links are not followed.
        /// </summary>
        public static IEnumerable<string> EnumerateFiles(string root, string searchPattern = "*",
            Action<string, Exception>? onError = null, Func<string, bool>? directoryFilter = null)
        {
            string full = FullDirectory(root, nameof(root));
            if (searchPattern is null) throw new ArgumentNullException(nameof(searchPattern));
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Directory '{root}' does not exist.");
            return Walk(full, searchPattern, onError, directoryFilter);
        }

        private static IEnumerable<string> Walk(string root, string pattern, Action<string, Exception>? onError, Func<string, bool>? directoryFilter)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                string[] files, dirs;
                try
                {
                    files = Directory.GetFiles(dir, pattern);
                    dirs = Directory.GetDirectories(dir);
                }
                catch (Exception ex) when (IsIoError(ex))
                {
                    onError?.Invoke(dir, ex);
                    continue;
                }
                foreach (string f in files) yield return f;
                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    if (IsLink(dirs[i])) continue;
                    if (directoryFilter != null && !directoryFilter(Relative(root, dirs[i]))) continue;
                    pending.Push(dirs[i]);
                }
            }
        }

        /// <summary>Total size, file count and subdirectory count of a tree; unreadable parts are counted and skipped.</summary>
        public static DirectorySummary Summarize(string path, Action<string, Exception>? onError = null)
        {
            string full = FullDirectory(path, nameof(path));
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Directory '{path}' does not exist.");
            long bytes = 0;
            int files = 0, dirs = 0, inaccessible = 0;
            var pending = new Stack<string>();
            pending.Push(full);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                string[] fileList, dirList;
                try { fileList = Directory.GetFiles(dir); dirList = Directory.GetDirectories(dir); }
                catch (Exception ex) when (IsIoError(ex)) { inaccessible++; onError?.Invoke(dir, ex); continue; }
                foreach (string f in fileList)
                {
                    try { bytes += new FileInfo(f).Length; files++; }
                    catch (Exception ex) when (IsIoError(ex)) { inaccessible++; onError?.Invoke(f, ex); }
                }
                foreach (string d in dirList)
                {
                    dirs++;
                    if (!IsLink(d)) pending.Push(d);
                }
            }
            return new DirectorySummary(bytes, files, dirs, inaccessible);
        }

        /// <summary>
        /// <paramref name="path"/> relative to <paramref name="root"/>, with '/' separators (empty for the root itself).
        /// Throws if the path is not inside the root.
        /// </summary>
        public static string GetRelativePath(string root, string path)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            if (path is null) throw new ArgumentNullException(nameof(path));
            string r = FullDirectory(root, nameof(root)), p = Path.GetFullPath(path);
            if (!IsSameOrInside(p, r)) throw new ArgumentException($"'{path}' is not inside '{root}'.", nameof(path));
            return Relative(r, p);
        }

        // ---------- helpers ----------

        private static string Relative(string root, string path)
        {
            string trimmed = path.Length > root.Length ? path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : "";
            return trimmed.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static bool IsSameOrInside(string candidate, string root)
        {
            string c = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string r = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(c, r, PathComparison)) return true;
            return c.StartsWith(r + Path.DirectorySeparatorChar, PathComparison);
        }

        private static string FullDirectory(string path, string name)
        {
            if (path is null) throw new ArgumentNullException(name);
            if (path.Length == 0) throw new ArgumentException("Path must not be empty.", name);
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full) ?? "";
            // Drop a trailing separator, except on a bare root ("C:", "/") where it is the path.
            return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        }

        private static bool IsLink(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch (Exception ex) when (IsIoError(ex)) { return false; }
        }

        private static void ClearReadOnly(string path)
        {
            FileAttributes a = File.GetAttributes(path);
            if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, a & ~FileAttributes.ReadOnly);
        }

        private static void Retry(Action action, int retries, TimeSpan delay)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { action(); return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < retries)
                {
                    if (delay > TimeSpan.Zero) Thread.Sleep(delay); // transient lock: wait and try again
                }
            }
        }

        private static void Report(Action<string, Exception>? onError, string path, Exception ex)
        {
            if (onError is null) throw ex is IOException io ? io : new IOException($"Failed on '{path}': {ex.Message}", ex);
            onError(path, ex);
        }

        private static bool IsIoError(Exception ex) => ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException;
    }
}
