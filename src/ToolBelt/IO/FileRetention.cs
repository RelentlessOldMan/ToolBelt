// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ToolBelt.IO
{
    /// <summary>What <see cref="FileRetention.Prune"/> keeps. Unset limits don't apply; a file is deleted if it breaks any limit.</summary>
    public sealed class RetentionPolicy
    {
        /// <summary>Keep at most this many files (newest first).</summary>
        public int? MaxCount { get; set; }

        /// <summary>Delete files last written longer ago than this.</summary>
        public TimeSpan? MaxAge { get; set; }

        /// <summary>Keep the newest files whose combined size fits in this many bytes.</summary>
        public long? MaxTotalBytes { get; set; }

        /// <summary>Never delete fewer than this many newest files, whatever the other limits say (default 0).</summary>
        public int KeepAtLeast { get; set; }
    }

    /// <summary>The outcome of a prune.</summary>
    public sealed class PruneResult
    {
        internal PruneResult(IReadOnlyList<string> kept, IReadOnlyList<string> deleted, IReadOnlyList<(string Path, Exception Error)> failed, long bytesFreed)
        {
            Kept = kept;
            Deleted = deleted;
            Failed = failed;
            BytesFreed = bytesFreed;
        }

        public IReadOnlyList<string> Kept { get; }
        public IReadOnlyList<string> Deleted { get; }

        /// <summary>Files that should have been deleted but couldn't be (locked, permissions) — left in place.</summary>
        public IReadOnlyList<(string Path, Exception Error)> Failed { get; }

        public long BytesFreed { get; }
    }

    /// <summary>
    /// Housekeeping for directories that fill up — logs, captures, exports, backups: <see cref="Prune"/> deletes the oldest
    /// files matching a pattern by count, age and total size (with a <see cref="WhatIf"/> dry run), <see cref="ShiftNumbered"/>
    /// does classic <c>app.log → app.log.1 → app.log.2</c> rotation, and <see cref="TimestampedName"/> makes sortable
    /// names (<c>capture_20260305_142233.csv</c>). Age and order come from the last-write time; the clock is injectable.
    /// </summary>
    public static class FileRetention
    {
        /// <summary>Deletes files in <paramref name="directory"/> matching <paramref name="searchPattern"/> that break the policy.</summary>
        public static PruneResult Prune(string directory, string searchPattern, RetentionPolicy policy, DateTime? nowUtc = null)
            => Run(directory, searchPattern, policy, nowUtc, delete: true);

        /// <summary>What <see cref="Prune"/> would do, without deleting anything.</summary>
        public static PruneResult WhatIf(string directory, string searchPattern, RetentionPolicy policy, DateTime? nowUtc = null)
            => Run(directory, searchPattern, policy, nowUtc, delete: false);

        private static PruneResult Run(string directory, string searchPattern, RetentionPolicy policy, DateTime? nowUtc, bool delete)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (searchPattern is null) throw new ArgumentNullException(nameof(searchPattern));
            if (policy is null) throw new ArgumentNullException(nameof(policy));
            if (policy.MaxCount < 0 || policy.MaxTotalBytes < 0 || policy.KeepAtLeast < 0 || policy.MaxAge < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(policy), "Retention limits must not be negative.");
            if (!Directory.Exists(directory)) return new PruneResult(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<(string, Exception)>(), 0);

            DateTime now = nowUtc ?? DateTime.UtcNow;
            var files = new DirectoryInfo(directory).GetFiles(searchPattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).ToList();

            var kept = new List<string>();
            var doomed = new List<FileInfo>();
            long total = 0;
            for (int i = 0; i < files.Count; i++)
            {
                FileInfo f = files[i];
                bool protectedByMinimum = i < policy.KeepAtLeast;
                bool tooMany = policy.MaxCount is int max && i >= max;
                bool tooOld = policy.MaxAge is TimeSpan age && now - f.LastWriteTimeUtc > age;
                bool tooBig = policy.MaxTotalBytes is long cap && total + f.Length > cap;
                if (!protectedByMinimum && (tooMany || tooOld || tooBig)) doomed.Add(f);
                else { kept.Add(f.FullName); total += f.Length; }
            }

            var deleted = new List<string>();
            var failed = new List<(string, Exception)>();
            long freed = 0;
            foreach (FileInfo f in doomed)
            {
                if (!delete) { deleted.Add(f.FullName); freed += f.Length; continue; }
                try
                {
                    long len = f.Length;
                    if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                    f.Delete();
                    deleted.Add(f.FullName);
                    freed += len;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    failed.Add((f.FullName, ex));
                }
            }
            return new PruneResult(kept, deleted, failed, freed);
        }

        /// <summary>
        /// Numbered rotation: <c>path.(n-1) → path.n</c> … <c>path → path.1</c>, deleting what falls off the end so at most
        /// <paramref name="keep"/> rotated copies remain. Afterwards <paramref name="path"/> does not exist. Returns false (and
        /// does nothing) if <paramref name="path"/> doesn't exist.
        /// </summary>
        public static bool ShiftNumbered(string path, int keep)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (keep < 1) throw new ArgumentOutOfRangeException(nameof(keep), keep, "Keep at least one rotated copy.");
            if (!File.Exists(path)) return false;
            string Numbered(int n) => path + "." + n.ToString(CultureInfo.InvariantCulture);
            if (File.Exists(Numbered(keep))) File.Delete(Numbered(keep));
            for (int n = keep - 1; n >= 1; n--)
                if (File.Exists(Numbered(n))) File.Move(Numbered(n), Numbered(n + 1));
            File.Move(path, Numbered(1));
            // Copies beyond `keep` left by an earlier, larger setting.
            for (int n = keep + 1; File.Exists(Numbered(n)); n++) File.Delete(Numbered(n));
            return true;
        }

        /// <summary>
        /// <c>prefix_yyyyMMdd_HHmmss[_fff].ext</c> — names that sort chronologically. If the name is taken in
        /// <paramref name="directory"/> (when given), a <c>_2</c>, <c>_3</c> … suffix is added.
        /// </summary>
        public static string TimestampedName(string prefix, string extension, DateTime time, bool milliseconds = false, string? directory = null)
        {
            if (prefix is null) throw new ArgumentNullException(nameof(prefix));
            if (extension is null) throw new ArgumentNullException(nameof(extension));
            string ext = extension.Length == 0 || extension[0] == '.' ? extension : "." + extension;
            string stem = (prefix.Length == 0 ? "" : prefix + "_") + time.ToString(milliseconds ? "yyyyMMdd_HHmmss_fff" : "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string name = stem + ext;
            if (directory != null)
                for (int n = 2; File.Exists(Path.Combine(directory, name)) || Directory.Exists(Path.Combine(directory, name)); n++)
                    name = stem + "_" + n.ToString(CultureInfo.InvariantCulture) + ext;
            return name;
        }
    }
}
