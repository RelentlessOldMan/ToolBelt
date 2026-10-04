// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (BCL + Win32 P/Invoke).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>Total / free / user-available byte counts for a volume or directory.</summary>
    public readonly struct DiskSpace
    {
        internal DiskSpace(long total, long free, long available)
        {
            TotalBytes = total;
            FreeBytes = free;
            AvailableBytes = available;
        }

        /// <summary>Total capacity in bytes.</summary>
        public long TotalBytes { get; }

        /// <summary>Total free bytes on the volume.</summary>
        public long FreeBytes { get; }

        /// <summary>Free bytes available to the calling user (respects quotas; ≤ <see cref="FreeBytes"/>).</summary>
        public long AvailableBytes { get; }

        /// <summary>Fraction of capacity used, 0–1.</summary>
        public double UsedFraction => TotalBytes <= 0 ? 0 : 1.0 - (double)FreeBytes / TotalBytes;

        public override string ToString() => $"{FreeBytes:N0} free of {TotalBytes:N0} bytes";
    }

    /// <summary>A ready fixed drive and its volume metadata.</summary>
    public readonly struct FixedDrive
    {
        internal FixedDrive(string name, string volumeLabel, string fileSystem, DiskSpace space)
        {
            Name = name;
            VolumeLabel = volumeLabel;
            FileSystem = fileSystem;
            Space = space;
        }

        /// <summary>The drive root, e.g. <c>C:\</c>.</summary>
        public string Name { get; }
        public string VolumeLabel { get; }
        public string FileSystem { get; }
        public DiskSpace Space { get; }

        public override string ToString() =>
            $"{Name} [{(VolumeLabel.Length == 0 ? "no label" : VolumeLabel)}, {FileSystem}] {Space}";
    }

    /// <summary>Enumerates fixed drives and reports free space for an arbitrary path.</summary>
    public static class DriveAndVolumeInfo
    {
        /// <summary>
        /// Returns free-space figures for the volume that contains <paramref name="path"/> (which may be any
        /// directory, not just a drive root). Throws <see cref="Win32Exception"/> if the path is invalid.
        /// </summary>
        public static DiskSpace GetSpace(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (!GetDiskFreeSpaceEx(path, out ulong available, out ulong total, out ulong free))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"GetDiskFreeSpaceEx failed for '{path}'.");
            return new DiskSpace((long)total, (long)free, (long)available);
        }

        /// <summary>Lists the ready fixed drives with their labels, file systems, and free space.</summary>
        public static IReadOnlyList<FixedDrive> FixedDrives()
        {
            var result = new List<FixedDrive>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    continue;
                var space = new DiskSpace(drive.TotalSize, drive.TotalFreeSpace, drive.AvailableFreeSpace);
                result.Add(new FixedDrive(drive.Name, drive.VolumeLabel ?? "", drive.DriveFormat ?? "", space));
            }
            return result;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailableToCaller,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);
    }
}
