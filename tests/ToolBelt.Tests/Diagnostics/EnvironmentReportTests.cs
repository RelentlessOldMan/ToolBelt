using System;
using ToolBelt.Diagnostics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Diagnostics
{
    public sealed class EnvironmentReportTests
    {
        public void CapturesPlausibleValues()
        {
            var r = EnvironmentReport.Capture();
            Check.True(r.FrameworkDescription.Contains(".NET"), r.FrameworkDescription);
            Check.True(r.ProcessorCount > 0);
            Check.True(!string.IsNullOrEmpty(r.MachineName));
            Check.True(!string.IsNullOrEmpty(r.RuntimeVersion));
            Check.True(r.Uptime >= TimeSpan.Zero);
            Check.True(r.WorkingSetBytes > 0);
        }

        public void IncludesRequestedEnvironmentVariable()
        {
            Environment.SetEnvironmentVariable("TB_REPORT_TEST", "value-123");
            try
            {
                var r = EnvironmentReport.Capture(new[] { "TB_REPORT_TEST", "TB_DOES_NOT_EXIST_XYZ" });
                Check.True(r.EnvironmentVariables.ContainsKey("TB_REPORT_TEST"));
                Check.Equal("value-123", r.EnvironmentVariables["TB_REPORT_TEST"]);
                Check.False(r.EnvironmentVariables.ContainsKey("TB_DOES_NOT_EXIST_XYZ")); // unset ones are skipped
            }
            finally { Environment.SetEnvironmentVariable("TB_REPORT_TEST", null); }
        }

        public void DoesNotDumpAllEnvironmentVariables()
        {
            // Default capture uses a small curated set, not every variable (avoids leaking secrets).
            var r = EnvironmentReport.Capture();
            Check.True(r.EnvironmentVariables.Count <= 8, $"default set unexpectedly large: {r.EnvironmentVariables.Count}");
        }

        public void ToTextIsReadable()
        {
            string text = EnvironmentReport.Capture().ToText();
            Check.True(text.Contains("Framework:"), text);
            Check.True(text.Contains("Processors:"), text);
            Check.True(text.Contains("Machine:"), text);
        }
    }
}
