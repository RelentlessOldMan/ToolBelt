using System;
using System.IO;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class ShortcutUtilsTests
    {
        public void CreateThenRead_RoundTrips()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ToolBelt.Windows.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string lnk = Path.Combine(dir, "test.lnk");
            string target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            try
            {
                ShortcutUtils.Create(lnk, target,
                    arguments: "/A readme.txt",
                    workingDirectory: dir,
                    description: "ToolBelt test shortcut");

                Check.True(File.Exists(lnk), "shortcut file written");

                var info = ShortcutUtils.Read(lnk);
                // The shell canonicalizes path casing on save; Windows paths are case-insensitive.
                Check.True(string.Equals(target, info.TargetPath, StringComparison.OrdinalIgnoreCase),
                    $"target round-trips: expected '{target}', got '{info.TargetPath}'");
                Check.Equal("/A readme.txt", info.Arguments);
                Check.True(string.Equals(info.WorkingDirectory.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase),
                    $"working dir round-trips: '{info.WorkingDirectory}'");
                Check.Equal("ToolBelt test shortcut", info.Description);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        public void Create_NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => ShortcutUtils.Create(null!, "x"));
            Check.Throws<ArgumentNullException>(() => ShortcutUtils.Create("x.lnk", null!));
            Check.Throws<ArgumentNullException>(() => ShortcutUtils.Read(null!));
        }
    }
}
