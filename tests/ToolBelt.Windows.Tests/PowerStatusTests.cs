using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class PowerStatusTests
    {
        public void Query_ReturnsSaneSnapshot()
        {
            var s = PowerStatus.Query();

            // If a battery is present, its charge (when known) must be a valid percentage.
            if (s.BatteryPresent && s.BatteryChargePercent is int pct)
                Check.True(pct >= 0 && pct <= 100, $"battery % in range: {pct}");

            // Remaining time, when reported, is non-negative.
            if (s.BatteryLifeRemaining is System.TimeSpan t)
                Check.True(t.TotalSeconds >= 0, $"remaining >= 0: {t}");

            Check.True(s.ToString().Length > 0, "ToString non-empty");
        }

        public void Query_IsRepeatable()
        {
            // Two back-to-back calls must both succeed (no handle/marshal leak or throw).
            PowerStatus.Query();
            PowerStatus.Query();
        }
    }
}
