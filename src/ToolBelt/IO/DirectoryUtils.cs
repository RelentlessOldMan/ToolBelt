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

        /// <summary>Give copies the source's last-write time (default true); when false a copy's last-write time is when it was made.</summary>
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
    /// wipe out. Walks are iterative, so deep trees cannot overflow the stack. A symbolic link to a <i>file</i> is
    /// treated as that file: <see cref="Copy"/> copies the target's content.
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
            // Through a link (a junction j → src makes j\c the same folder as src\c) the string check alone passed.
            if (IsSameOrInside(dst, src) || IsSameOrInside(ResolveLinks(dst), ResolveLinks(src)) || InsideByMark(dst, src))
                throw new ArgumentException("The destination may not be the source or lie inside it.", nameof(destination));

            int copied = 0, skipped = 0, created = 0, errors = 0;
            long bytes = 0;

            // The source's folders are listed before anything is written, so a destination that still lies inside the source
            // by a route not caught above (a link that cannot be resolved here) is copied once, not nested forever.
            var folders = new List<string>();
            var unreadable = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(src);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                folders.Add(dir);
                string[] dirs;
                try { dirs = List(dir, directories: true); }
                catch (Exception ex) when (IsIoError(ex)) { errors++; Report(options.OnError, dir, ex); unreadable.Add(dir); continue; }
                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    if (IsLink(dirs[i])) continue;
                    if (options.DirectoryFilter != null && !options.DirectoryFilter(Relative(src, dirs[i]))) continue;
                    pending.Push(dirs[i]);
                }
            }

            var failed = new HashSet<string>(StringComparer.Ordinal);   // source folders whose target could not be made
            foreach (string dir in folders)
            {
                string? parent = dir.Length > src.Length ? Path.GetDirectoryName(dir) : null;
                if (parent != null && failed.Contains(parent)) { failed.Add(dir); continue; }   // its subtree goes with it
                string rel = Relative(src, dir);
                string target = rel.Length == 0 ? dst : Path.Combine(dst, rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    // A junction/symlink already sitting in the destination tree would redirect the copy outside it.
                    if (rel.Length > 0 && IsLink(target))
                        throw new IOException($"Destination '{target}' is a link; refusing to copy through it.");
                    if (!Directory.Exists(Exact(target))) { Directory.CreateDirectory(Exact(target)); created++; }
                }
                catch (Exception ex) when (IsIoError(ex)) { errors++; Report(options.OnError, target, ex); failed.Add(dir); continue; }
                if (unreadable.Contains(dir)) continue;

                string[] files;
                try { files = List(dir, directories: false); }
                catch (Exception ex) when (IsIoError(ex)) { errors++; Report(options.OnError, dir, ex); continue; }

                foreach (string file in files)
                {
                    string fileRel = Relative(src, file);
                    if (options.FileFilter != null && !options.FileFilter(fileRel)) continue;
                    string to = Path.Combine(target, Path.GetFileName(file));
                    try
                    {
                        // A folder on either side swapped for a link since it was checked would read or write outside the tree.
                        if (LinkBetween(src, dir)) throw new IOException($"Source folder '{dir}' became a link; refusing to copy through it.");
                        if (LinkBetween(dst, target)) throw new IOException($"Destination folder '{target}' became a link; refusing to copy through it.");
                        if (IsLink(to)) throw new IOException($"Destination '{to}' is a link; refusing to write through it.");
                        bool exists = File.Exists(Exact(to));
                        if (exists)
                        {
                            bool replace = options.Overwrite switch
                            {
                                OverwritePolicy.Skip => false,
                                OverwritePolicy.Fail => throw new IOException($"Destination file '{to}' already exists."),
                                OverwritePolicy.IfNewer => File.GetLastWriteTimeUtc(Exact(file)) > File.GetLastWriteTimeUtc(Exact(to)),
                                _ => true,
                            };
                            if (!replace) { skipped++; continue; }
                        }
                        CopyFile(Exact(file), Exact(to), exists, options.PreserveTimestamps);
                        bytes += new FileInfo(Exact(to)).Length;
                        copied++;
                    }
                    catch (Exception ex) when (IsIoError(ex)) { errors++; Report(options.OnError, file, ex); }
                }
            }
            return new CopyResult(copied, skipped, created, bytes, errors);
        }

        // Copied beside the target and moved over it. Deleting the target first lost it when the copy then failed (source
        // locked, disk full); the move replaces the name, so a hard link at the target is still broken rather than written
        // through to its other name (which may be outside the destination).
        private static void CopyFile(string file, string to, bool replace, bool preserveTimestamps)
        {
            string temp = Path.Combine(Path.GetDirectoryName(to)!, ".copy-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(file, temp, overwrite: false);
                // File.Copy keeps the source's last-write time on Windows, so a fresh one must be set explicitly.
                File.SetLastWriteTimeUtc(temp, preserveTimestamps ? File.GetLastWriteTimeUtc(file) : DateTime.UtcNow);
                if (replace) ClearReadOnly(to);
#if NET6_0_OR_GREATER
                File.Move(temp, to, overwrite: replace);
#else
                if (replace) File.Delete(to);
                File.Move(temp, to);
#endif
            }
            finally
            {
                try { if (File.Exists(temp)) { ClearReadOnly(temp); File.Delete(temp); } }
                catch (Exception ex) when (IsIoError(ex)) { }
            }
        }

        // ---------- delete / empty ----------

        /// <summary>
        /// Deletes a directory tree (or a single file), clearing read-only attributes and retrying each item up to
        /// <paramref name="retries"/> times, <paramref name="retryDelay"/> apart (100 ms by default), when it is briefly
        /// locked. A link is removed without touching its target, including a folder swapped for a link while the delete
        /// runs. Returns false if nothing existed at the path. Blocks while waiting between retries;
        /// <paramref name="cancellationToken"/> ends the wait (and the delete, part done) with
        /// <see cref="OperationCanceledException"/>.
        /// </summary>
        public static bool Delete(string path, int retries = 3, TimeSpan? retryDelay = null, CancellationToken cancellationToken = default)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (retries < 0) throw new ArgumentOutOfRangeException(nameof(retries), retries, "Must be non-negative.");
            TimeSpan delay = RequireDelay(retryDelay);
            string full = Path.GetFullPath(path);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(full))
            {
                Retry(() => { ClearReadOnly(full); File.Delete(full); }, retries, delay, cancellationToken);
                return true;
            }
            if (!Directory.Exists(full)) return false;
            DeleteTree(full, retries, delay, deleteRoot: true, cancellationToken);
            return true;
        }

        /// <summary>
        /// Makes <paramref name="path"/> an empty directory: creates it if missing, otherwise deletes everything inside it.
        /// A link (junction or symbolic link) throws <see cref="IOException"/> rather than emptying its target. Retries
        /// and <paramref name="cancellationToken"/> work as in <see cref="Delete"/>.
        /// </summary>
        public static void EnsureEmpty(string path, int retries = 3, TimeSpan? retryDelay = null, CancellationToken cancellationToken = default)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (retries < 0) throw new ArgumentOutOfRangeException(nameof(retries), retries, "Must be non-negative.");
            TimeSpan delay = RequireDelay(retryDelay);
            string full = Path.GetFullPath(path);
            if (File.Exists(full)) throw new IOException($"'{path}' is a file, not a directory.");
            if (Directory.Exists(full) && IsLink(full)) throw new IOException($"'{path}' is a link; refusing to empty its target.");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(full);
            DeleteTree(full, retries, delay, deleteRoot: false, cancellationToken);
        }

        private static void DeleteTree(string root, int retries, TimeSpan delay, bool deleteRoot, CancellationToken token)
        {
            if (IsLink(root))
            {
                // Remove the link only; recursing would delete the target's contents.
                if (deleteRoot) Retry(() => { ClearReadOnly(root); Directory.Delete(root, recursive: false); }, retries, delay, token);
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
                string[] subs;
                try { subs = List(dir, directories: true); }
                catch (DirectoryNotFoundException) { continue; }   // removed meanwhile: the delete's goal is met
                foreach (string sub in subs)
                {
                    if (IsLink(sub)) Retry(() => { ClearReadOnly(sub); Directory.Delete(Exact(sub), recursive: false); }, retries, delay, token);
                    else pending.Push(sub);
                }
            }
            for (int i = order.Count - 1; i >= 0; i--)
            {
                token.ThrowIfCancellationRequested();
                string dir = order[i];
                // A folder swapped for a link since it was listed (while a retry waited on a locked file, say) would have its
                // target's files deleted: each file is checked just before it goes, and such a folder loses only the link.
                bool swapped = LinkBetween(root, dir);
                if (!swapped)
                {
                    string[] files;
                    try { files = List(dir, directories: false); }
                    catch (DirectoryNotFoundException) { files = Array.Empty<string>(); }   // removed meanwhile
                    foreach (string file in files)
                    {
                        Retry(() =>
                        {
                            if (LinkBetween(root, dir)) { swapped = true; return; }
                            ClearReadOnly(file);
                            File.Delete(Exact(file));
                        }, retries, delay, token);
                        if (swapped) break;
                    }
                }
                // A link above this folder is removed in its own turn; this folder, seen through it, is not touched.
                if (swapped && !IsLink(dir)) continue;
                if (i > 0 || deleteRoot)
                    Retry(() => { ClearReadOnly(dir); Directory.Delete(Exact(dir), recursive: false); }, retries, delay, token);
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
            CheckSearchPattern(searchPattern, nameof(searchPattern));
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
                    files = ListMatching(dir, pattern);
                    dirs = List(dir, directories: true);
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

        /// <summary>
        /// Total size, file count and subdirectory count of a tree; unreadable parts are counted and skipped.
        /// Linked folders are not followed, and a file symbolic link counts as a file of the link's own size, not its target's.
        /// </summary>
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
                try { fileList = List(dir, directories: false); dirList = List(dir, directories: true); }
                catch (Exception ex) when (IsIoError(ex)) { inaccessible++; onError?.Invoke(dir, ex); continue; }
                foreach (string f in fileList)
                {
                    try { bytes += new FileInfo(Exact(f)).Length; files++; }
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
            string r = FullDirectory(root, nameof(root)), p = FullExact(path);
            if (!IsSameOrInside(p, r)) throw new ArgumentException($"'{path}' is not inside '{root}'.", nameof(path));
            return Relative(r, p);
        }

        // ---------- helpers ----------

        // A pattern is a file name, not a path: Directory.GetFiles(dir, "..\\x\\*") lists another directory, so a walk of
        // the root could return files outside it.
        internal static void CheckSearchPattern(string pattern, string name)
        {
            if (pattern is null) throw new ArgumentNullException(name);
            if (pattern.IndexOf('/') >= 0 || pattern.IndexOf('\\') >= 0 || pattern.IndexOf('\0') >= 0 || Path.IsPathRooted(pattern))
                throw new ArgumentException($"Search pattern '{pattern.Replace('\0', '?')}' must be a file name, without a directory part.", name);
        }

        private static string Relative(string root, string path)
        {
            string trimmed = path.Length > root.Length
                ? path.Substring(root.Length).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : "";
            return trimmed.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static bool IsSameOrInside(string candidate, string root)
        {
            string c = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string r = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(c, r, PathComparison)) return true;
            return c.StartsWith(r + Path.DirectorySeparatorChar, PathComparison);
        }

        // The path with every existing link along it replaced by its final target (net6+; older targets cannot read link
        // targets without P/Invoke and return the path unchanged). The part that does not exist yet is kept as written.
        private static string ResolveLinks(string full)
        {
#if NET6_0_OR_GREATER
            string current = Path.GetPathRoot(full) ?? "";
            string[] parts = full.Substring(current.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string next = Path.Combine(current, parts[i]);
                if (!Directory.Exists(next))
                    return Path.Combine(current, string.Join(Path.DirectorySeparatorChar.ToString(), parts, i, parts.Length - i));
                if (IsLink(next))
                {
                    try
                    {
                        FileSystemInfo? final = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true);
                        if (final != null) next = FullDirectory(final.FullName, nameof(full));
                    }
                    catch (Exception ex) when (IsIoError(ex)) { }   // unreadable link: compare by name
                }
                current = next;
            }
            return current;
#else
            return full;
#endif
        }

        // Where links cannot be read (before net6), a link along either path hid a destination inside the source, which was
        // then copied into itself once with no error. A uniquely named mark in the destination's nearest existing folder,
        // found in the source's tree (links not followed, as Copy does), shows it is inside. A folder the mark cannot be
        // written to is not checked.
        private static bool InsideByMark(string dst, string src)
        {
#if NET6_0_OR_GREATER
            return false;
#else
            if (!LinkAlong(dst) && !LinkAlong(src)) return false;
            string existing = dst;
            while (!Directory.Exists(existing))
            {
                string? parent = Path.GetDirectoryName(existing);
                if (parent is null) return false;
                existing = parent;
            }
            string name = ".toolbelt-copy-probe-" + Guid.NewGuid().ToString("N");
            string mark = Path.Combine(existing, name);
            try { using (new FileStream(mark, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) return HasFile(src, name); }
            catch (Exception ex) when (IsIoError(ex)) { return false; }
#endif
        }

#if !NET6_0_OR_GREATER
        private static bool LinkAlong(string full)
        {
            for (string? p = full; p != null; p = Path.GetDirectoryName(p))
                if (Directory.Exists(p) && IsLink(p)) return true;
            return false;
        }

        private static bool HasFile(string root, string name)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                if (File.Exists(Exact(Path.Combine(dir, name)))) return true;
                string[] dirs;
                try { dirs = List(dir, directories: true); }
                catch (Exception ex) when (IsIoError(ex)) { continue; }
                foreach (string d in dirs)
                    if (!IsLink(d)) pending.Push(d);
            }
            return false;
        }
#endif

        private static string FullDirectory(string path, string name)
        {
            if (path is null) throw new ArgumentNullException(name);
            if (path.Length == 0) throw new ArgumentException("Path must not be empty.", name);
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full) ?? "";
            // Drop a trailing separator, except on a bare root ("C:", "/") where it is the path.
            return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        }

        // A junction or symbolic link. Other reparse points (cloud-file placeholders, deduplicated files) are ordinary
        // files and folders; telling them apart needs LinkTarget (net6+), so older targets treat every reparse point as a link.
        private static bool IsLink(string path)
        {
            try
            {
                path = Exact(path);
                FileAttributes a = File.GetAttributes(path);
                if ((a & FileAttributes.ReparsePoint) == 0) return false;
#if NET6_0_OR_GREATER
                FileSystemInfo info = (a & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
                try { return info.LinkTarget != null; }
                catch (Exception ex) when (IsIoError(ex)) { return true; }
#else
                return true;
#endif
            }
            catch (Exception ex) when (IsIoError(ex)) { return false; }
        }

        private static void ClearReadOnly(string path)
        {
            FileAttributes a;
            path = Exact(path);
            try { a = File.GetAttributes(path); }
            catch (FileNotFoundException) { return; }        // already gone (someone else deleted it): nothing to clear
            catch (DirectoryNotFoundException) { return; }
            if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, a & ~FileAttributes.ReadOnly);
        }

        // Checked up front: Thread.Sleep threw its own ArgumentOutOfRangeException ("timeout") for a delay past
        // int.MaxValue ms, and only once a delete had already failed, half done; a negative delay was silently skipped.
        private static TimeSpan RequireDelay(TimeSpan? retryDelay)
        {
            TimeSpan delay = retryDelay ?? TimeSpan.FromMilliseconds(100);
            if (delay < TimeSpan.Zero || delay.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(retryDelay), delay, "Retry delay must be 0 to int.MaxValue milliseconds.");
            return delay;
        }

        // The wait is on the token's handle, not Thread.Sleep, so cancelling ends it at once.
        private static void Retry(Action action, int retries, TimeSpan delay, CancellationToken token)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { action(); return; }
                catch (DirectoryNotFoundException) { return; }   // the item vanished meanwhile: the delete's goal is met
                catch (FileNotFoundException) { return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < retries)
                {
                    // transient lock: wait and try again
                    if (delay > TimeSpan.Zero) token.WaitHandle.WaitOne(delay);
                    token.ThrowIfCancellationRequested();
                }
            }
        }

        // True if a folder from path up to root (not included) is a link: one swapped in after the tree was listed.
        private static bool LinkBetween(string root, string path)
        {
            for (string? p = path; p != null && p.Length > root.Length; p = Path.GetDirectoryName(p))
                if (IsLink(p)) return true;
            return false;
        }

        // Win32 path normalisation drops a trailing dot or space from each name, so a listed file "a." was read, replaced
        // or deleted as "a" (another file) and a folder "d." could not be opened. The \\?\ form is used as written.
        internal static string Exact(string path)
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

        // Path.GetFullPath, except that a name ending in a dot or space (as listed by Walk) keeps it.
        private static string FullExact(string path)
        {
            string exact = Exact(path);
            if (ReferenceEquals(exact, path)) return Path.GetFullPath(path);
            string full = Path.GetFullPath(exact);
            return full.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + full.Substring(8)
                : full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full.Substring(4) : full;
        }

        // A folder's files matching pattern. .NET Framework's listing also matched each file's 8.3 short name ("*.txt" listed
        // a.txtx, whose short name is A~1.TXT) and refused a pattern ending in ".." that .NET Core takes, so there the
        // listing is of every file and the pattern is applied here, by the rules .NET Core uses.
        private static string[] ListMatching(string dir, string pattern)
        {
#if NET6_0_OR_GREATER
            return List(dir, directories: false, pattern);
#else
            if (Path.DirectorySeparatorChar != '\\' || pattern == "*") return List(dir, directories: false, pattern);
            string expression = DosExpression(pattern);
            var kept = new List<string>();
            foreach (string f in List(dir, directories: false))
                if (DosMatch(expression, Path.GetFileName(f))) kept.Add(f);
            return kept.ToArray();
#endif
        }

#if !NET6_0_OR_GREATER
        // .NET Core's Win32 pattern translation: '?' is DOS_QM ('>'), a '.' before '?' or '*' is DOS_DOT ('"'), and a
        // final "*." is DOS_STAR ('<'); "*.*" is "*".
        private static string DosExpression(string pattern)
        {
            if (pattern == "*.*") return "*";
            var sb = new System.Text.StringBuilder(pattern.Length);
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '.')
                {
                    if (i >= 1 && i == pattern.Length - 1 && pattern[i - 1] == '*') sb[sb.Length - 1] = '<';   // DOS_STAR: a final "*." is a name with no extension
                    else if (i < pattern.Length - 1 && (pattern[i + 1] == '?' || pattern[i + 1] == '*')) sb.Append('"');
                    else sb.Append('.');
                }
                else sb.Append(c == '?' ? '>' : c);
            }
            return sb.ToString();
        }

        // FsRtlIsNameInExpression's rules, case-insensitive: '*' any run; '<' any run that does not take the name's last
        // '.'; '>' one character other than '.', or nothing at a '.' or the end; '"' a '.', or nothing at the end.
        private static bool DosMatch(string expression, string name)
        {
            int lastDot = name.LastIndexOf('.');
            var memo = new Dictionary<long, bool>();
            bool At(int p, int n)
            {
                if (p == expression.Length) return n == name.Length;
                long key = ((long)p << 32) | (uint)n;
                if (memo.TryGetValue(key, out bool known)) return known;
                bool result;
                char c = expression[p];
                switch (c)
                {
                    case '*':
                        result = false;
                        for (int k = n; k <= name.Length && !result; k++) result = At(p + 1, k);
                        break;
                    case '<':
                        result = false;
                        for (int k = n; k <= name.Length && !result; k++)
                        {
                            if (k > n && k - 1 == lastDot) break;   // it may not take the last dot
                            result = At(p + 1, k);
                        }
                        break;
                    case '>':
                        result = n < name.Length && name[n] != '.' ? At(p + 1, n + 1) : At(p + 1, n);
                        break;
                    case '"':
                        result = n < name.Length ? name[n] == '.' && At(p + 1, n + 1) : At(p + 1, n);
                        break;
                    default:
                        result = n < name.Length && char.ToUpperInvariant(c) == char.ToUpperInvariant(name[n]) && At(p + 1, n + 1);
                        break;
                }
                memo[key] = result;
                return result;
            }
            return At(0, 0);
        }
#endif

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

        private static void Report(Action<string, Exception>? onError, string path, Exception ex)
        {
            if (onError is null) throw ex is IOException io ? io : new IOException($"Failed on '{path}': {ex.Message}", ex);
            onError(path, ex);
        }

        private static bool IsIoError(Exception ex) => ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException;
    }
}
