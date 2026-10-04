using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class MemoryStatusTests
    {
        public void Query_IsConsistent()
        {
            var m = MemoryStatus.Query();
            Check.True(m.TotalPhysicalBytes > 0, "total physical > 0");
            Check.True(m.AvailablePhysicalBytes >= 0 && m.AvailablePhysicalBytes <= m.TotalPhysicalBytes,
                "0 <= available <= total");
            Check.True(m.UsedPhysicalBytes >= 0 && m.UsedPhysicalBytes <= m.TotalPhysicalBytes, "used in range");
            Check.True(m.MemoryLoadPercent >= 0 && m.MemoryLoadPercent <= 100, $"load 0-100, got {m.MemoryLoadPercent}");
            Check.True(m.TotalPageFileBytes >= m.TotalPhysicalBytes - m.TotalPhysicalBytes, "page file non-negative"); // sanity
            Check.True(m.ToString().Length > 0, "ToString non-empty");
        }
    }
}
