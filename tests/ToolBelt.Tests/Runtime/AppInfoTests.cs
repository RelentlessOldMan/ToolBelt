using System;
using System.Runtime.InteropServices;
using ToolBelt.Runtime;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Runtime
{
    public sealed class AppInfoTests
    {
        public void FrameworkDescriptionLooksLikeDotNet()
        {
            Check.True(AppInfo.FrameworkDescription.Contains(".NET"), AppInfo.FrameworkDescription);
        }

        public void VersionIsPresent()
        {
            Check.NotNull(AppInfo.Version);
        }

        public void ProcessArchitectureIsKnown()
        {
            var arch = AppInfo.ProcessArchitecture;
            Check.True(arch == Architecture.X64 || arch == Architecture.X86
                || arch == Architecture.Arm64 || arch == Architecture.Arm, arch.ToString());
        }

        public void LocationAndBuildTimestampAreConsistent()
        {
            // On a normal (non single-file) test run the assembly exists on disk.
            string? loc = AppInfo.Location;
            if (loc != null)
                Check.NotNull(AppInfo.BuildTimestampUtc);
        }
    }
}
