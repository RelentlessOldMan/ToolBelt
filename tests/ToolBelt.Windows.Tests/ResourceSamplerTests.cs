using System;
using System.Diagnostics;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class ResourceSamplerTests
    {
        public void Sample_ReportsPositiveMemoryAndSaneCpu()
        {
            var sampler = ResourceSampler.ForCurrentProcess();

            // Do measurable work so the interval is non-zero and some CPU is consumed.
            double acc = 0;
            for (int i = 0; i < 2_000_000; i++) acc += Math.Sqrt(i);
            GC.KeepAlive(acc);

            ResourceSample s = sampler.Sample();
            Check.True(s.WorkingSetBytes > 0, "working set > 0");
            Check.True(s.PrivateBytes > 0, "private bytes > 0");
            Check.True(s.Interval > TimeSpan.Zero, "interval elapsed");
            Check.True(s.CpuPercent >= 0 && !double.IsNaN(s.CpuPercent) && !double.IsInfinity(s.CpuPercent),
                $"cpu finite & non-negative: {s.CpuPercent}");
            // Normalised across all cores, so it cannot exceed ~100% by much.
            Check.True(s.CpuPercent <= 100.0 * Environment.ProcessorCount + 1, $"cpu within bounds: {s.CpuPercent}");
        }

        public void Measure_BlocksForTheInterval()
        {
            var interval = TimeSpan.FromMilliseconds(40);
            var s = ResourceSampler.MeasureCurrent(interval);
            // Allow timer slack but it must be in the right ballpark.
            Check.True(s.Interval.TotalMilliseconds >= 25, $"interval ~>= requested: {s.Interval.TotalMilliseconds} ms");
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new ResourceSampler(null!));
            Check.Throws<ArgumentNullException>(() => ResourceSampler.Measure(null!, TimeSpan.FromMilliseconds(10)));
            Check.Throws<ArgumentOutOfRangeException>(() => ResourceSampler.MeasureCurrent(TimeSpan.Zero));
            Check.Throws<ArgumentOutOfRangeException>(() => ResourceSampler.MeasureCurrent(TimeSpan.FromSeconds(-1)));
        }
    }
}
