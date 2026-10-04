using System;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class RunElevatedTests
    {
        // Validation only: actually calling Start() would pop a UAC consent dialog (an outward side effect),
        // so the elevation launch itself is intentionally not exercised by automated tests.

        public void Start_NullOrEmptyFileName_Throws()
        {
            Check.Throws<ArgumentNullException>(() => RunElevated.Start(null!));
            Check.Throws<ArgumentException>(() => RunElevated.Start(""));
        }
    }
}
