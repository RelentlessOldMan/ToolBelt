using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Logging
{
    // Regressions from review round 213 (logging, diagnostics, CLI, thirteenth pass): each failed on the code before its fix.
    public sealed class LoggingReview213Tests
    {
        // A positive estimate whose double product underflowed came out 0, though the doc says "rounded up".
        public void EstimateBytes_UnderflowingProduct_IsOneByte()
        {
            Check.Equal(1L, DiskSpace.EstimateBytes(1e-320, TimeSpan.FromTicks(1), 1.2));
            Check.Equal(1L, DiskSpace.EstimateBytes(double.Epsilon, TimeSpan.FromSeconds(0.5), 1.0));
            Check.Equal(0L, DiskSpace.EstimateBytes(0, TimeSpan.FromSeconds(10)));
            Check.Equal(0L, DiskSpace.EstimateBytes(1e-320, TimeSpan.Zero));
            Check.Equal(12000L, DiskSpace.EstimateBytes(1000, TimeSpan.FromSeconds(10)));
        }

        // A clock stepped back mid-block reported a negative elapsed time.
        public void ScopedTimer_ClockSteppedBack_ReportsZero()
        {
            var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var now = t;
            TimeSpan seen = TimeSpan.MaxValue;
            using (new ScopedTimer("x", (_, e) => seen = e, () => now)) now = t.AddMinutes(-5);
            Check.Equal(TimeSpan.Zero, seen);
            using (new ScopedTimer("x", (_, e) => seen = e, () => now)) now = now.AddSeconds(3);
            Check.Equal(TimeSpan.FromSeconds(3), seen);
        }

        // A probe message of several lines put its later lines at column 0, where they read as lines of the report's
        // own; the report's lines ended with '\n' where the other reports use Environment.NewLine.
        public async Task HealthReport_MultiLineMessage_StaysUnderItsProbe()
        {
            HealthReport report = await new HealthCheck()
                .Add("db", () => ProbeResult.Unhealthy("Overall: Healthy\r\nsecond\nthird\rfourth"))
                .Add("disk", () => ProbeResult.Healthy())
                .RunAsync();
            string[] lines = report.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Check.Equal(6, lines.Length, report.ToString());
            Check.True(lines[0].StartsWith("Overall: Unhealthy", StringComparison.Ordinal), lines[0]);
            int probes = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                Check.True(lines[i].StartsWith("  ", StringComparison.Ordinal), lines[i]);
                Check.False(lines[i].Contains("\r") || lines[i].Contains("\n"), lines[i]);
                if (!lines[i].StartsWith("    ", StringComparison.Ordinal)) probes++;
            }
            Check.Equal(2, probes, report.ToString());
            Check.Equal("    second", lines[2]);
            Check.Equal("    fourth", lines[4]);
        }

        // One astral character in a header token became two placeholders, one per UTF-16 unit.
        public void Syslog_AstralCharacterInToken_IsOnePlaceholder()
        {
            var e = new LogEvent(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Info, "c\U0001F600t", "m", null);
            string line = SyslogSink.Format(e, SyslogFacility.User, "h\U0001F600st", "a\U0001F600p", "1");
            Check.True(line.Contains(" h_st a_p 1 "), line);
            Check.True(line.Contains(" c_t "), line);
            // A lone surrogate is still one placeholder of its own.
            line = SyslogSink.Format(e, SyslogFacility.User, "h\uD800st", "a\uDC00p", "1");
            Check.True(line.Contains(" h_st a_p 1 "), line);
        }

        // Pool work started by the inner sink carries the consumer's flow, so a full blocking queue refused its event with
        // a raw InvalidOperationException and lost it. It is written through to the inner sink.
        public void AsyncLogSink_PoolWorkFromInnerSink_FullQueue_IsWrittenThrough()
        {
            var inner = new GatedSink();
            using (var sink = new AsyncLogSink(inner, capacity: 1, overflowPolicy: PipelineOverflowPolicy.Block))
            {
                inner.Outer = sink;
                sink.Emit(Event("first"));                  // the consumer takes it and waits in the inner sink
                Check.True(inner.Entered.Wait(10000));
                sink.Emit(Event("fill"));                   // the queue's one slot
                inner.Filled.Set();
                Check.True(inner.ChildDone.Wait(10000));
                Check.True(inner.ChildError is null, inner.ChildError?.ToString() ?? "");
            }
            Check.True(inner.Seen.Contains("child"), string.Join(",", inner.Seen));
            Check.True(inner.Seen.Contains("fill"), string.Join(",", inner.Seen));
        }

        private static LogEvent Event(string message) =>
            new LogEvent(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", message, null);

        private sealed class GatedSink : ILogSink
        {
            public AsyncLogSink? Outer;
            public readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            public readonly ManualResetEventSlim Filled = new ManualResetEventSlim();
            public readonly ManualResetEventSlim ChildDone = new ManualResetEventSlim();
            public readonly ConcurrentQueue<string> Seen = new ConcurrentQueue<string>();
            public Exception? ChildError;

            public void Emit(LogEvent logEvent)
            {
                Seen.Enqueue(logEvent.Message);
                if (logEvent.Message != "first") return;
                Entered.Set();
                Filled.Wait(10000);
                Task child = Task.Run(() =>
                {
                    try { Outer!.Emit(Event("child")); }
                    catch (Exception ex) { ChildError = ex; }
                    finally { ChildDone.Set(); }
                });
                child.Wait(10000);
            }

            public void Dispose() { }
        }
    }
}
