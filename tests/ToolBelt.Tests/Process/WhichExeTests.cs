using System;
using System.Runtime.InteropServices;
using ToolBelt.Process;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Process
{
    public sealed class WhichExeTests
    {
        public void FindsAKnownExecutable()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string? cmd = WhichExe.Find("cmd");
                Check.NotNull(cmd);
                Check.True(cmd!.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase), cmd);
            }
            else
            {
                Check.NotNull(WhichExe.Find("sh")); // present on macOS/Linux
            }
        }

        public void MissingExecutableReturnsNull()
        {
            Check.Equal(null, WhichExe.Find("surely-not-a-real-program-xyz123"));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => WhichExe.Find(null!));
        }
    }
}
