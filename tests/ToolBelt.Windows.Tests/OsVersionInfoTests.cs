using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class OsVersionInfoTests
    {
        public void Get_ReturnsSaneVersion()
        {
            var v = OsVersionInfo.Get();
            // Any supported Windows is at least version 6 (Vista) with a real build number.
            Check.True(v.Major >= 6, $"major >= 6, got {v.Major}");
            Check.True(v.Build > 0, $"build > 0, got {v.Build}");
            Check.True(v.ToString().Length > 0, "ToString non-empty");

            // The Win10/Win11 predicates must be internally consistent.
            if (v.IsWindows11OrGreater)
                Check.True(v.IsWindows10OrGreater, "Win11 implies Win10");
        }
    }
}
