using System;
using System.IO;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class DriveAndVolumeInfoTests
    {
        public void GetSpace_SystemDrive_IsConsistent()
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory)!; // e.g. C:\
            var space = DriveAndVolumeInfo.GetSpace(root);

            Check.True(space.TotalBytes > 0, "total > 0");
            Check.True(space.FreeBytes >= 0 && space.FreeBytes <= space.TotalBytes, "0 <= free <= total");
            Check.True(space.AvailableBytes >= 0 && space.AvailableBytes <= space.FreeBytes, "0 <= available <= free");
            Check.True(space.UsedFraction >= 0 && space.UsedFraction <= 1, "used fraction in [0,1]");
        }

        public void GetSpace_AcceptsAnyDirectory()
        {
            // Works on a plain directory, not just a drive root.
            var space = DriveAndVolumeInfo.GetSpace(Path.GetTempPath());
            Check.True(space.TotalBytes > 0, "temp dir volume has capacity");
        }

        public void FixedDrives_IncludeSystemDrive()
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory)!;
            var drives = DriveAndVolumeInfo.FixedDrives();
            Check.True(drives.Count >= 1, "at least one fixed drive");

            bool found = false;
            foreach (var d in drives)
            {
                Check.True(d.Space.TotalBytes > 0, $"{d.Name} total > 0");
                if (string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase))
                    found = true;
            }
            Check.True(found, $"system drive {root} present among fixed drives");
        }

        public void GetSpace_NullPath_Throws()
        {
            Check.Throws<ArgumentNullException>(() => DriveAndVolumeInfo.GetSpace(null!));
        }
    }
}
