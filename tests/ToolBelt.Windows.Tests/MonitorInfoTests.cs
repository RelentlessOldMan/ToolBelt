using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class MonitorInfoTests
    {
        // These run on headless CI too, so they tolerate zero monitors but assert internal consistency.

        public void All_AtMostOnePrimary_AndPositiveBounds()
        {
            var monitors = MonitorInfo.All();

            int primaries = 0;
            foreach (var m in monitors)
            {
                if (m.IsPrimary) primaries++;
                Check.True(m.Bounds.Width > 0 && m.Bounds.Height > 0, $"{m.DeviceName} has positive bounds");
                // Work area never exceeds the full bounds.
                Check.True(m.WorkArea.Width <= m.Bounds.Width && m.WorkArea.Height <= m.Bounds.Height,
                    $"{m.DeviceName} work area within bounds");
            }
            Check.True(primaries <= 1, $"at most one primary monitor, got {primaries}");
        }

        public void Primary_ConsistentWithAll()
        {
            var all = MonitorInfo.All();
            var primary = MonitorInfo.Primary();

            if (all.Count == 0)
            {
                Check.Null(primary, "no monitors -> no primary");
                return;
            }
            // When any monitor is reported, if one is flagged primary, Primary() returns it.
            bool anyPrimary = false;
            foreach (var m in all) if (m.IsPrimary) anyPrimary = true;
            if (anyPrimary)
            {
                Check.NotNull(primary, "a primary exists");
                Check.True(primary!.Value.IsPrimary, "Primary() is flagged primary");
            }
        }
    }
}
