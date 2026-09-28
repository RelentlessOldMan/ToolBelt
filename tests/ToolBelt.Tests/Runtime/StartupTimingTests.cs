using System;
using System.Collections.Generic;
using ToolBelt.Runtime;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Runtime
{
    public sealed class StartupTimingTests
    {
        private static Func<DateTimeOffset> ClockOf(params DateTimeOffset[] times)
        {
            var q = new Queue<DateTimeOffset>(times);
            return () => q.Dequeue();
        }

        public void MarksRecordCumulativeElapsed()
        {
            var t0 = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
            // ctor consumes t0; then two Marks consume +100ms and +250ms.
            var timing = new StartupTiming(ClockOf(t0, t0.AddMilliseconds(100), t0.AddMilliseconds(250)));
            timing.Mark("config loaded");
            timing.Mark("ready");

            Check.Equal(2, timing.Milestones.Count);
            Check.Close(100, timing.Milestones[0].Elapsed.TotalMilliseconds, 1e-6);
            Check.Close(250, timing.Milestones[1].Elapsed.TotalMilliseconds, 1e-6);
            Check.Close(250, timing.Total.TotalMilliseconds, 1e-6);
        }

        public void ReportIsNonEmpty()
        {
            var t0 = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
            var timing = new StartupTiming(ClockOf(t0, t0.AddMilliseconds(5)));
            timing.Mark("step");
            Check.True(timing.Report().Contains("step"));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new StartupTiming(() => DateTimeOffset.UtcNow).Mark(null!));
        }
    }
}
