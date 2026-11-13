// ToolBelt test infrastructure — self-contained (BCL only). Not part of the shipping library.
using System.Runtime.InteropServices;

namespace ToolBelt.Tests.Framework
{
    /// <summary>Platform checks that compile on .NET Framework too (OperatingSystem.IsWindows is .NET 5+).</summary>
    public static class TestPlatform
    {
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}
