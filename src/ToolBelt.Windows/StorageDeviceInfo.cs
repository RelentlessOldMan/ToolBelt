// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (DeviceIoControl, mpr).
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ToolBelt.Windows
{
    /// <summary>The bus a storage device is attached through (STORAGE_BUS_TYPE).</summary>
    public enum StorageBusType
    {
        Unknown = 0, Scsi = 1, Atapi = 2, Ata = 3, Ieee1394 = 4, Ssa = 5, FibreChannel = 6, Usb = 7, Raid = 8, iScsi = 9,
        Sas = 10, Sata = 11, Sd = 12, Mmc = 13, Virtual = 14, FileBackedVirtual = 15, Spaces = 16, Nvme = 17, Scm = 18, Ufs = 19,
    }

    /// <summary>What <see cref="StorageDeviceInfo.Describe"/> found out about a volume's device.</summary>
    public sealed class StorageDevice
    {
        internal StorageDevice(string volume, StorageBusType busType, bool removableMedia, string? vendor, string? product, string? serial, bool? seekPenalty)
        {
            Volume = volume;
            BusType = busType;
            RemovableMedia = removableMedia;
            Vendor = vendor;
            Product = product;
            SerialNumber = serial;
            IncursSeekPenalty = seekPenalty;
        }

        /// <summary>The volume queried, e.g. "C:".</summary>
        public string Volume { get; }

        public StorageBusType BusType { get; }

        /// <summary>The media can be removed (SD card reader, optical drive). USB sticks report true; USB hard disks often false.</summary>
        public bool RemovableMedia { get; }

        /// <summary>True for USB/SD/MMC/1394 buses or removable media — "might disappear mid-write".</summary>
        public bool IsExternal => RemovableMedia || BusType == StorageBusType.Usb || BusType == StorageBusType.Sd
                                  || BusType == StorageBusType.Mmc || BusType == StorageBusType.Ieee1394;

        public string? Vendor { get; }
        public string? Product { get; }
        public string? SerialNumber { get; }

        /// <summary>True for spinning disks, false for SSDs, null when the device doesn't say.</summary>
        public bool? IncursSeekPenalty { get; }

        public override string ToString() => $"{Volume} {BusType} {Vendor} {Product}".Trim() + (IsExternal ? " (external)" : "");
    }

    /// <summary>
    /// Storage questions a data-acquisition tool asks before writing a long capture: is this drive USB (it may be
    /// unplugged), an SSD or a spinning disk, and is a drive letter actually a network share (<see cref="GetUncPath"/>
    /// turns <c>Z:\data</c> into <c>\\server\share\data</c> for logging where data really went). Device queries use
    /// <c>IOCTL_STORAGE_QUERY_PROPERTY</c>, which needs no elevation.
    /// </summary>
    public static class StorageDeviceInfo
    {
        /// <summary>Describes the device behind the volume containing <paramref name="path"/> (a drive letter or any path on it).</summary>
        public static StorageDevice Describe(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new ArgumentException("Path has no root.", nameof(path));
            if (root.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("Network paths have no local storage device.", nameof(path));
            string volume = root.TrimEnd('\\');
            using SafeFileHandle h = CreateFile(@"\\.\" + volume, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open volume {volume}.");

            byte[] desc = Query(h, StorageDeviceProperty, 1024);
            // STORAGE_DEVICE_DESCRIPTOR: Version, Size, DeviceType(1), DeviceTypeModifier(1), RemovableMedia(1), CommandQueueing(1),
            // VendorIdOffset, ProductIdOffset, ProductRevisionOffset, SerialNumberOffset, BusType ...
            bool removable = desc[10] != 0;
            string? vendor = AsciiAt(desc, BitConverter.ToInt32(desc, 12));
            string? product = AsciiAt(desc, BitConverter.ToInt32(desc, 16));
            string? serial = AsciiAt(desc, BitConverter.ToInt32(desc, 24));
            var bus = (StorageBusType)BitConverter.ToInt32(desc, 28);
            if (!Enum.IsDefined(typeof(StorageBusType), bus)) bus = StorageBusType.Unknown;

            bool? seek = null;
            try
            {
                byte[] pen = Query(h, StorageDeviceSeekPenaltyProperty, 12);
                seek = pen[8] != 0;                                              // DEVICE_SEEK_PENALTY_DESCRIPTOR.IncursSeekPenalty
            }
            catch (Win32Exception) { /* not reported by this device */ }
            return new StorageDevice(volume, bus, removable, vendor, product, serial, seek);
        }

        /// <summary>
        /// The UNC path behind a mapped network drive (<c>Z:\a\b</c> → <c>\\server\share\a\b</c>); a UNC path is returned
        /// unchanged; a local path returns null.
        /// </summary>
        public static string? GetUncPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            string full = Path.GetFullPath(path);
            // Device paths are local unless they name a UNC share: \\?\UNC\server\share → \\server\share; \\?\C:\x → C:\x.
            if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + full.Substring(8);
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                full = full.Substring(4);
                if (full.Length < 2 || full[1] != ':') return null;                    // a volume GUID or device: no network share
            }
            else if (full.StartsWith(@"\\", StringComparison.Ordinal)) return full;
            string drive = full.Substring(0, 2);
            int length = 512;
            var sb = new StringBuilder(length);
            int err = WNetGetConnection(drive, sb, ref length);
            if (err == ERROR_MORE_DATA) { sb = new StringBuilder(length); err = WNetGetConnection(drive, sb, ref length); }
            // 0 = connected; ERROR_CONNECTION_UNAVAIL = a remembered mapping that is currently disconnected (the name is still returned).
            bool mapped = (err == 0 || err == ERROR_CONNECTION_UNAVAIL) && sb.Length > 0;
            if (!mapped)
            {
                if (err == 0 || err == ERROR_CONNECTION_UNAVAIL || err == ERROR_NOT_CONNECTED || err == ERROR_BAD_DEVICE || err == ERROR_NO_NETWORK) return null;
                throw new Win32Exception(err, $"WNetGetConnection failed for {drive}.");
            }
            string rest = full.Length > 3 ? full.Substring(2) : "";
            return sb.ToString().TrimEnd('\\') + (rest.Length > 0 ? rest : @"\");
        }

        /// <summary>True if the path is on a mapped network drive (connected or remembered-but-disconnected).</summary>
        public static bool IsNetworkDrive(string path)
            => !Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path))).StartsWith(@"\\", StringComparison.Ordinal) && GetUncPath(path) != null;

        private static byte[] Query(SafeFileHandle h, int propertyId, int size)
        {
            var query = new byte[12];                                             // STORAGE_PROPERTY_QUERY: PropertyId, QueryType, AdditionalParameters
            BitConverter.GetBytes(propertyId).CopyTo(query, 0);
            var output = new byte[size];
            if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, query, query.Length, output, output.Length, out int returned, IntPtr.Zero) || returned < 8)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "IOCTL_STORAGE_QUERY_PROPERTY failed.");
            return output;
        }

        private static string? AsciiAt(byte[] buffer, int offset)
        {
            if (offset <= 0 || offset >= buffer.Length) return null;
            int end = offset;
            while (end < buffer.Length && buffer[end] != 0) end++;
            string s = Encoding.ASCII.GetString(buffer, offset, end - offset).Trim();
            return s.Length == 0 ? null : s;
        }

        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        private const int StorageDeviceProperty = 0, StorageDeviceSeekPenaltyProperty = 7;
        private const int ERROR_MORE_DATA = 234, ERROR_NOT_CONNECTED = 2250, ERROR_BAD_DEVICE = 1200, ERROR_NO_NETWORK = 1222, ERROR_CONNECTION_UNAVAIL = 1201;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuffer, int inSize, byte[] outBuffer, int outSize, out int returned, IntPtr overlapped);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
    }
}
