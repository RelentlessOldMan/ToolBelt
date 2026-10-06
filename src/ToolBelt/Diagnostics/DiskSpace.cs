// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.IO;

namespace ToolBelt.Diagnostics
{
    /// <summary>Space on the volume holding a path.</summary>
    public readonly struct DiskSpaceInfo
    {
        public DiskSpaceInfo(string rootPath, long availableBytes, long totalFreeBytes, long totalBytes)
        {
            RootPath = rootPath;
            AvailableBytes = availableBytes;
            TotalFreeBytes = totalFreeBytes;
            TotalBytes = totalBytes;
        }

        /// <summary>The root (mount point or drive) of the volume the path lives on.</summary>
        public string RootPath { get; }

        /// <summary>Free bytes available to the <b>current user</b> (honours quotas) — the number to plan with.</summary>
        public long AvailableBytes { get; }

        /// <summary>Free bytes on the volume regardless of quotas.</summary>
        public long TotalFreeBytes { get; }

        /// <summary>Total size of the volume.</summary>
        public long TotalBytes { get; }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture, "{0}: {1:N0} of {2:N0} bytes available", RootPath, AvailableBytes, TotalBytes);
    }

    /// <summary>The outcome of a <see cref="DiskSpace.Preflight"/> check.</summary>
    public readonly struct PreflightResult
    {
        public PreflightResult(bool isWritable, long availableBytes, long requiredBytes, long reserveBytes)
        {
            IsWritable = isWritable;
            AvailableBytes = availableBytes;
            RequiredBytes = requiredBytes;
            ReserveBytes = reserveBytes;
        }

        /// <summary>Whether a file could be created in the target directory.</summary>
        public bool IsWritable { get; }

        /// <summary>Bytes available to the current user on the target volume.</summary>
        public long AvailableBytes { get; }

        /// <summary>The bytes the run needs.</summary>
        public long RequiredBytes { get; }

        /// <summary>Headroom that must remain free afterwards.</summary>
        public long ReserveBytes { get; }

        /// <summary>True when the run's bytes fit while leaving <see cref="ReserveBytes"/> free.</summary>
        public bool HasRoom => AvailableBytes - ReserveBytes >= RequiredBytes;

        /// <summary>Bytes that would have to be freed for the run to fit (0 when it fits).</summary>
        public long ShortfallBytes => HasRoom ? 0 : RequiredBytes - (AvailableBytes - ReserveBytes);

        /// <summary>True when the directory is writable and there is room — i.e. the run may start.</summary>
        public bool IsOk => IsWritable && HasRoom;

        /// <summary>A one-line explanation suitable for a log or an error message.</summary>
        public string Message
        {
            get
            {
                if (!IsWritable) return "Target directory is not writable.";
                if (!HasRoom)
                    return string.Format(CultureInfo.InvariantCulture,
                        "Insufficient disk space: need {0:N0} bytes (+{1:N0} reserve), {2:N0} available; short by {3:N0}.",
                        RequiredBytes, ReserveBytes, AvailableBytes, ShortfallBytes);
                return string.Format(CultureInfo.InvariantCulture,
                    "OK: need {0:N0} bytes (+{1:N0} reserve), {2:N0} available.", RequiredBytes, ReserveBytes, AvailableBytes);
            }
        }

        public override string ToString() => Message;
    }

    /// <summary>
    /// Portable disk-space queries and a pre-run check: how much room is on the volume holding a path, can this
    /// process write there, and will a run of a given size fit. The volume is resolved by the longest matching
    /// mount point, so a path under a Unix mount like <c>/mnt/data</c> reports that volume rather than the root one. On
    /// Windows only drive letters are enumerated, so a volume mounted into a folder (<c>C:\Mounts\Data</c>) reports the
    /// drive it sits on — give such paths by their own drive letter or volume GUID path. Answers are a snapshot — other processes can consume space after you check —
    /// so leave a reserve for anything long-running.
    /// </summary>
    public static class DiskSpace
    {
        /// <summary>Space on the volume containing <paramref name="path"/> (a file or directory; it need not exist yet).</summary>
        public static DiskSpaceInfo Get(string path)
        {
            DriveInfo drive = ResolveDrive(path);
            return new DiskSpaceInfo(drive.RootDirectory.FullName, drive.AvailableFreeSpace, drive.TotalFreeSpace, drive.TotalSize);
        }

        /// <summary>Bytes available to the current user on the volume containing <paramref name="path"/>.</summary>
        public static long AvailableBytes(string path) => ResolveDrive(path).AvailableFreeSpace;

        /// <summary>
        /// True if this process can create a file in <paramref name="directory"/>. Probes by creating and deleting a
        /// uniquely named temporary file, which is the only reliable test (permissions, read-only media and
        /// policy all show up). Returns false if the directory does not exist.
        /// </summary>
        public static bool IsWritable(string directory)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (directory.Length == 0) throw new ArgumentException("Directory must not be empty.", nameof(directory));
            if (!Directory.Exists(directory))
                return false;
            string probe = Path.Combine(directory, ".toolbelt-write-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                }
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
            finally
            {
                // DeleteOnClose normally removes it; this covers platforms or failures where it lingered.
                try { if (File.Exists(probe)) File.Delete(probe); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Checks that <paramref name="directory"/> is writable and that <paramref name="requiredBytes"/> fit on its
        /// volume while leaving <paramref name="reserveBytes"/> free. Call before a long capture or export so it
        /// fails up front rather than hours in.
        /// </summary>
        public static PreflightResult Preflight(string directory, long requiredBytes, long reserveBytes = 0)
        {
            if (requiredBytes < 0) throw new ArgumentOutOfRangeException(nameof(requiredBytes), requiredBytes, "Must be non-negative.");
            if (reserveBytes < 0) throw new ArgumentOutOfRangeException(nameof(reserveBytes), reserveBytes, "Must be non-negative.");
            bool writable = IsWritable(directory);
            return new PreflightResult(writable, AvailableBytes(directory), requiredBytes, reserveBytes);
        }

        /// <summary>
        /// Estimates the bytes a run will write: <paramref name="bytesPerSecond"/> × <paramref name="duration"/> ×
        /// <paramref name="safetyFactor"/>, rounded up. Feed the result to <see cref="Preflight"/>.
        /// </summary>
        public static long EstimateBytes(double bytesPerSecond, TimeSpan duration, double safetyFactor = 1.2)
        {
            if (double.IsNaN(bytesPerSecond) || bytesPerSecond < 0)
                throw new ArgumentOutOfRangeException(nameof(bytesPerSecond), bytesPerSecond, "Must be non-negative.");
            if (duration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(duration), duration, "Must be non-negative.");
            if (double.IsNaN(safetyFactor) || safetyFactor < 1)
                throw new ArgumentOutOfRangeException(nameof(safetyFactor), safetyFactor, "Must be at least 1.");
            double bytes = Math.Ceiling(bytesPerSecond * duration.TotalSeconds * safetyFactor);
            if (bytes >= long.MaxValue)
                throw new OverflowException("Estimated size exceeds the range of a 64-bit byte count.");
            return (long)bytes;
        }

        private static DriveInfo ResolveDrive(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (path.Length == 0) throw new ArgumentException("Path must not be empty.", nameof(path));
            string full = Path.GetFullPath(path);

            // Longest mount-point prefix wins, so nested mounts resolve to the right volume.
            StringComparison cmp = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            DriveInfo? best = null;
            int bestLength = -1;
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                string root;
                try
                {
                    if (!d.IsReady) continue;
                    root = d.RootDirectory.FullName;
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                if (IsUnder(full, root, cmp) && root.Length > bestLength)
                {
                    best = d;
                    bestLength = root.Length;
                }
            }
            if (best is not null)
                return best;

            string? pathRoot = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(pathRoot))
                throw new ArgumentException("Could not determine the volume for the path.", nameof(path));
            return new DriveInfo(pathRoot);
        }

        private static bool IsUnder(string full, string root, StringComparison cmp)
        {
            if (!full.StartsWith(root, cmp)) return false;
            if (full.Length == root.Length) return true;
            // Require a separator boundary so "/mnt/data2" is not treated as under "/mnt/data".
            char last = root[root.Length - 1];
            if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar) return true;
            char next = full[root.Length];
            return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
        }
    }
}
