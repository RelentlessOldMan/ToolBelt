using System;
using System.IO;
using ToolBelt.Diagnostics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Diagnostics
{
    public sealed class DiskSpaceTests
    {
        private static readonly string Temp = Path.GetTempPath();

        public void Get_ReportsConsistentNumbersForTempVolume()
        {
            DiskSpaceInfo info = DiskSpace.Get(Temp);
            Check.True(info.TotalBytes > 0, "total");
            Check.True(info.AvailableBytes >= 0, "available");
            Check.True(info.TotalFreeBytes <= info.TotalBytes, "free <= total");
            Check.True(info.AvailableBytes <= info.TotalFreeBytes, "user-available <= free (quotas only reduce it)");
            Check.True(Path.GetFullPath(Temp).StartsWith(info.RootPath, StringComparison.OrdinalIgnoreCase),
                $"'{info.RootPath}' should be the root of '{Temp}'");
        }

        public void Get_AcceptsPathThatDoesNotExistYet()
        {
            string future = Path.Combine(Temp, "not-created-" + Guid.NewGuid().ToString("N"), "out.bin");
            Check.True(DiskSpace.Get(future).TotalBytes > 0);
            Check.True(DiskSpace.AvailableBytes(future) >= 0);
        }

        public void IsWritable_TrueForTemp_AndLeavesNoProbeBehind()
        {
            string dir = Path.Combine(Temp, "toolbelt-diskspace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Check.True(DiskSpace.IsWritable(dir));
                Check.Equal(0, Directory.GetFiles(dir).Length);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        public void IsWritable_FalseForMissingDirectory()
        {
            Check.False(DiskSpace.IsWritable(Path.Combine(Temp, "missing-" + Guid.NewGuid().ToString("N"))));
        }

        public void Preflight_SmallRunFits()
        {
            PreflightResult r = DiskSpace.Preflight(Temp, requiredBytes: 1);
            Check.True(r.IsWritable);
            Check.True(r.HasRoom);
            Check.True(r.IsOk);
            Check.Equal(0L, r.ShortfallBytes);
            Check.True(r.Message.StartsWith("OK", StringComparison.Ordinal), r.Message);
        }

        public void Preflight_ImpossibleRunReportsShortfall()
        {
            PreflightResult r = DiskSpace.Preflight(Temp, requiredBytes: long.MaxValue / 2);
            Check.False(r.HasRoom);
            Check.False(r.IsOk);
            Check.True(r.ShortfallBytes > 0);
            Check.True(r.Message.StartsWith("Insufficient disk space", StringComparison.Ordinal), r.Message);
        }

        public void PreflightResult_Arithmetic()
        {
            var fits = new PreflightResult(true, availableBytes: 1000, requiredBytes: 900, reserveBytes: 100);
            Check.True(fits.HasRoom);            // 1000 - 100 >= 900 exactly
            Check.Equal(0L, fits.ShortfallBytes);

            var shortBy = new PreflightResult(true, availableBytes: 1000, requiredBytes: 960, reserveBytes: 50);
            Check.False(shortBy.HasRoom);
            Check.Equal(10L, shortBy.ShortfallBytes);

            var readOnly = new PreflightResult(false, availableBytes: 1000, requiredBytes: 1, reserveBytes: 0);
            Check.True(readOnly.HasRoom);
            Check.False(readOnly.IsOk);
            Check.Equal("Target directory is not writable.", readOnly.Message);
        }

        public void EstimateBytes_ClosedForm()
        {
            Check.Equal(12_000L, DiskSpace.EstimateBytes(1000, TimeSpan.FromSeconds(10)));          // ×1.2 default
            Check.Equal(10_000L, DiskSpace.EstimateBytes(1000, TimeSpan.FromSeconds(10), 1.0));
            Check.Equal(2L, DiskSpace.EstimateBytes(1, TimeSpan.FromSeconds(1.5), 1.0));             // rounds up
            Check.Equal(0L, DiskSpace.EstimateBytes(0, TimeSpan.FromHours(1)));
        }

        public void Validation()
        {
            Check.Throws<ArgumentNullException>(() => DiskSpace.Get(null!));
            Check.Throws<ArgumentException>(() => DiskSpace.Get(""));
            Check.Throws<ArgumentNullException>(() => DiskSpace.IsWritable(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => DiskSpace.Preflight(Temp, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => DiskSpace.Preflight(Temp, 1, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => DiskSpace.EstimateBytes(-1, TimeSpan.FromSeconds(1)));
            Check.Throws<ArgumentOutOfRangeException>(() => DiskSpace.EstimateBytes(1, TimeSpan.FromSeconds(-1)));
            Check.Throws<ArgumentOutOfRangeException>(() => DiskSpace.EstimateBytes(1, TimeSpan.FromSeconds(1), 0.5));
            Check.Throws<OverflowException>(() => DiskSpace.EstimateBytes(double.MaxValue, TimeSpan.FromDays(1)));
        }
    }
}
