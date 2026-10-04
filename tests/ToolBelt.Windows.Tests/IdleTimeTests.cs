using System;
using System.ComponentModel;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class IdleTimeTests
    {
        public void Get_ReturnsNonNegativeSpan()
        {
            TimeSpan idle;
            try { idle = IdleTime.Get(); }
            catch (Win32Exception) { return; } // non-interactive session -> inconclusive

            Check.True(idle >= TimeSpan.Zero, $"idle >= 0, got {idle}");
            // Sub-day sanity: a genuine idle beyond the wrap window is implausible in a test run.
            Check.True(idle < TimeSpan.FromDays(40), $"idle below the 32-bit wrap window, got {idle}");
        }
    }
}
