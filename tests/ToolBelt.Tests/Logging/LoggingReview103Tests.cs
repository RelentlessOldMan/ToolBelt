using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    /// <summary>
    /// Regressions from review round 103 (sixth pass over Logging, Diagnostics, Cli): each test failed before its fix.
    /// Anything that used to hang or overflow the stack runs under a bounded wait.
    /// </summary>
    public sealed class LoggingReview103Tests
    {
        private static LogEvent Ev(string message) =>
            new LogEvent(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", message, null);

        // Records what it is given; the first event with the blocking message waits for the gate.
        private sealed class Recording : ILogSink
        {
            private readonly string? _blockOn;
            private int _blocked;
            public readonly List<string> Lines = new List<string>();
            public readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            public readonly ManualResetEventSlim Gate = new ManualResetEventSlim();
            public volatile bool Disposed;

            public Recording(string? blockOn = null) => _blockOn = blockOn;

            public void Emit(LogEvent logEvent)
            {
                if (logEvent.Message == _blockOn && Interlocked.Exchange(ref _blocked, 1) == 0)
                {
                    Entered.Set();
                    Check.True(Gate.Wait(TimeSpan.FromSeconds(10)), "gate");
                }
                lock (Lines) Lines.Add(logEvent.Message);
            }

            public void Dispose() => Disposed = true;

            public string[] Snapshot() { lock (Lines) return Lines.ToArray(); }
        }

        public void Async_ReentryThroughAnotherSinksConsumerIsBounded()
        {
            // Each sink's inner reports into the same logger, as a failure monitor would. On A's consumer the events reached
            // B's inner straight through with no depth count, and recursed until the stack overflowed.
            var logger = new Logger(minimumLevel: LogLevel.Trace);
            int written = 0;
            ILogSink Reporting(string name) => new DelegateSink(e =>
            {
                Interlocked.Increment(ref written);
                logger.Warning(name + " saw " + e.Message.Length);
            });
            var a = new AsyncLogSink(Reporting("a"));
            var b = new AsyncLogSink(Reporting("b"));
            logger.AddSink(a).AddSink(b);
            var run = Task.Run(() =>
            {
                logger.Info("go");
                a.Dispose();
                b.Dispose();
            });
            Check.True(run.Wait(TimeSpan.FromSeconds(30)), "finished");
            run.GetAwaiter().GetResult();
            Check.True(Volatile.Read(ref written) > 2 && Volatile.Read(ref written) < 10000, "written " + written);
            Check.True(a.DroppedCount + b.DroppedCount > 0, "re-entry dropped");
        }

        public void Async_AStraightThroughWriteAfterDisposeIsRefused()
        {
            var mem = new RollingMemorySink(8);
            var errors = new List<Exception>();
            var b = new AsyncLogSink(mem, ownsInner: false);
            var a = new AsyncLogSink(b, ownsInner: false, onError: ex => { lock (errors) errors.Add(ex); });
            b.Dispose();
            a.Emit(Ev("via a"));
            a.Dispose();
            Check.Equal(0, mem.Snapshot().Count);
            lock (errors)
            {
                Check.Equal(1, errors.Count);
                Check.True(errors[0] is ObjectDisposedException, errors[0].GetType().Name);
            }
        }

        public void Health_TwoChecksAtOnceBothRunTheProbe()
        {
            foreach (bool sync in new[] { false, true })
            {
                using var entered = new SemaphoreSlim(0);
                using var gate = new ManualResetEventSlim();
                var check = new HealthCheck();
                if (sync)
                    check.Add("p", () => { entered.Release(); gate.Wait(TimeSpan.FromSeconds(10)); return ProbeResult.Healthy(); }, TimeSpan.FromSeconds(20));
                else
                    check.Add("p", _ => { entered.Release(); gate.Wait(TimeSpan.FromSeconds(10)); return Task.FromResult(ProbeResult.Healthy()); }, TimeSpan.FromSeconds(20));
                try
                {
                    var first = check.RunAsync();
                    Check.True(entered.Wait(TimeSpan.FromSeconds(10)), "first started");
                    var second = check.RunAsync();
                    // The second run's probe is not hung: it is waiting its turn like the first.
                    Check.True(entered.Wait(TimeSpan.FromSeconds(10)), "second started (sync " + sync + ")");
                    gate.Set();
                    Check.True(Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(10)), "both done");
                    Check.Equal(HealthStatus.Healthy, first.Result.Status);
                    Check.Equal(HealthStatus.Healthy, second.Result.Status);
                }
                finally { gate.Set(); }
            }

            // A start that timed out and is still blocked still holds the next check off (no new thread each time).
            int started = 0;
            using (var hang = new ManualResetEventSlim())
            {
                var stuck = new HealthCheck().Add("hang", _ =>
                {
                    Interlocked.Increment(ref started);
                    hang.Wait(TimeSpan.FromSeconds(30));
                    return Task.FromResult(ProbeResult.Healthy());
                }, TimeSpan.FromMilliseconds(50));
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var run = stuck.RunAsync();
                        Check.True(run.Wait(TimeSpan.FromSeconds(10)), "run " + i);
                        Check.Equal(HealthStatus.Unhealthy, run.Result.Status);
                    }
                    Check.Equal(1, Volatile.Read(ref started));
                }
                finally { hang.Set(); }
            }
        }

        public void Rate_TheSummaryFollowsTheEventItSummarizes()
        {
            var now = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
            var inner = new Recording(blockOn: "N");
            var sink = new RateLimitedSink(inner, TimeSpan.FromMinutes(1), () => now);
            sink.Emit(Ev("M"));
            var writer = Task.Run(() => sink.Emit(Ev("N")));
            try
            {
                Check.True(inner.Entered.Wait(TimeSpan.FromSeconds(10)), "first N in flight");
                sink.Emit(Ev("N"));   // suppressed
                sink.Emit(Ev("P"));   // ends N's run while its first write is still going
            }
            finally { inner.Gate.Set(); }
            Check.True(writer.Wait(TimeSpan.FromSeconds(10)), "writer");
            string[] lines = inner.Snapshot();
            int first = Array.IndexOf(lines, "N"), summary = Array.IndexOf(lines, "N (repeated 1 times)");
            Check.True(first >= 0 && summary > first, string.Join(" | ", lines));
            Check.Equal(4, lines.Length);

            // Dispose while the first write is in flight: the summary still follows it, and the owned sink is disposed
            // only once it has been written.
            var owned = new Recording(blockOn: "N");
            var disposing = new RateLimitedSink(owned, TimeSpan.FromMinutes(1), () => now, ownsInner: true);
            var w2 = Task.Run(() => disposing.Emit(Ev("N")));
            try
            {
                Check.True(owned.Entered.Wait(TimeSpan.FromSeconds(10)), "N in flight");
                disposing.Emit(Ev("N"));
                disposing.Dispose();
                Check.False(owned.Disposed, "disposed under a write");
            }
            finally { owned.Gate.Set(); }
            Check.True(w2.Wait(TimeSpan.FromSeconds(10)), "w2");
            Check.Equal("N|N (repeated 1 times)", string.Join("|", owned.Snapshot()));
            Check.True(owned.Disposed, "disposed after the write");
        }

        public void Ansi_AResetInALaterParameterReopensTheStyle()
        {
            var style = new AnsiStyle(true);
            const string E = "\u001b[";
            var cases = new[]
            {
                (E + "1;0m", E + "1;0m" + E + "31m"),
                (E + "10;0m", E + "10;0m" + E + "31m"),
                (E + "1;0;4m", E + "1;0m" + E + "31m" + E + "4m"),
                (E + "38;5;0;0m", E + "38;5;0;0m" + E + "31m"),
                (E + "1;;4m", E + "1;m" + E + "31m" + E + "4m"),
                // Colour arguments are not resets.
                (E + "38;5;0m", E + "38;5;0m"),
                (E + "48;2;0;0;0m", E + "48;2;0;0;0m"),
            };
            foreach (var (seq, expected) in cases)
                Check.Equal(E + "31ma" + expected + "b" + E + "0m", style.Foreground("a" + seq + "b", AnsiColor.Red), seq.Replace("\u001b", "ESC"));
        }

        public void Scope_ADisposalOnAnotherFlowEndsTheScope()
        {
            Task.Run(() =>
            {
                var a = ScopedContext.Push("A");
                Task.Run(() => a.Dispose()).Wait();
                Check.Equal(null, ScopedContext.Current);

                var outer = ScopedContext.Push("X");
                var inner = ScopedContext.Push("Y");
                Task.Run(() => inner.Dispose()).Wait();
                Check.Equal("X", ScopedContext.Current);
                outer.Dispose();
                Check.Equal(null, ScopedContext.Current);
            }).Wait();
        }

        public void Scope_ReadingEveryLevelCostsLinearMemory()
        {
            Task.Run(() =>
            {
                const int depth = 10000;
                var tokens = new IDisposable[depth];
                long before = GC.GetTotalMemory(true);
                for (int i = 0; i < depth; i++)
                {
                    tokens[i] = ScopedContext.Push("s");
                    Check.True(ScopedContext.Current!.Length == 2 * i + 1, "length at " + i);
                }
                long held = GC.GetTotalMemory(true) - before;
                Check.True(held < 50L * 1024 * 1024, "held " + held);
                for (int i = depth - 1; i >= 0; i--) tokens[i].Dispose();
                Check.Equal(null, ScopedContext.Current);
            }).Wait();
        }

        public void Sinks_RefuseEventsAfterDispose()
        {
            var text = new StringWriter();
            var tw = new TextWriterSink(text);
            tw.Dispose();
            Check.Throws<ObjectDisposedException>(() => tw.Emit(Ev("after")));
            Check.Equal("", text.ToString());
            tw.Dispose();   // twice is fine

            int calls = 0;
            var del = new DelegateSink(_ => calls++);
            del.Dispose();
            Check.Throws<ObjectDisposedException>(() => del.Emit(Ev("after")));
            Check.Equal(0, calls);

            var mem = new RollingMemorySink(4);
            mem.Emit(Ev("before"));
            mem.Dispose();
            Check.Throws<ObjectDisposedException>(() => mem.Emit(Ev("after")));
            Check.Equal("before", mem.Snapshot().Single().Message);   // still readable for a crash report
        }

        public void FileSink_AnUnencodableEventDoesNotPoisonTheSink()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-r103-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "app.log");
                var sink = new FileSink(path, e => e.Message, encoding: new UTF8Encoding(false, true));
                Check.Throws<EncoderFallbackException>(() => sink.Emit(Ev("bad \uD800 x")));
                sink.Emit(Ev("good"));
                sink.Emit(Ev("good"));
                sink.Dispose();
                Check.Equal("good" + Environment.NewLine + "good" + Environment.NewLine, File.ReadAllText(path));

                // A new file still gets the encoding's preamble, once.
                string bom = Path.Combine(dir, "bom.log");
                using (var s = new FileSink(bom, e => e.Message, encoding: new UTF8Encoding(true))) s.Emit(Ev("x"));
                using (var s = new FileSink(bom, e => e.Message, encoding: new UTF8Encoding(true))) s.Emit(Ev("y"));
                byte[] bytes = File.ReadAllBytes(bom);
                Check.Equal(3 + 2 * (1 + Environment.NewLine.Length), bytes.Length);
                Check.True(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "preamble");
            }
            finally { Directory.Delete(dir, true); }
        }

        public void Spinner_ALineBreakInTheMessageStaysOnOneLine()
        {
            var w = new StringWriter();
            var spinner = new ConsoleSpinner("line1\nline2\tx\r\u0085y", w, interactive: true, frames: ConsoleSpinner.Ascii, interval: TimeSpan.FromHours(1));
            for (int i = 0; i < 3; i++) spinner.Tick();
            spinner.Message = "a\u2028b\u2029c";
            spinner.Tick();
            string drawn = w.ToString();
            spinner.Stop();
            // Each frame starts with '\r' to redraw in place; nothing else may move the cursor.
            foreach (char c in drawn.Replace("\r", ""))
                Check.False(char.IsControl(c) || c == '\u2028' || c == '\u2029', "drew U+" + ((int)c).ToString("X4"));
            Check.True(drawn.Contains("line1 line2 x  y") && drawn.Contains("a b c"), drawn);
        }

        public void Router_AddRouteAfterDisposeIsRefused()
        {
            var router = new RouterSink(ownsSinks: true);
            router.Dispose();
            var sink = new Recording();
            Check.Throws<ObjectDisposedException>(() => router.AddRoute(_ => true, sink));
            Check.False(sink.Disposed, "the caller still owns it");
        }

        private sealed class Hanging : ILogSink
        {
            public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
            public volatile bool Disposed;
            public int Written;
            public void Emit(LogEvent logEvent) { Entered.Set(); Release.Wait(TimeSpan.FromSeconds(30)); Interlocked.Increment(ref Written); }
            public void Dispose() => Disposed = true;
        }

        // R69-1 (open since round 29): Dispose waited for the drain without limit, so an inner sink that hung hung it.
        public void Async_DisposeIsBoundedByTheDrainTimeout()
        {
            var inner = new Hanging();
            var sink = new AsyncLogSink(inner, drainTimeout: TimeSpan.FromMilliseconds(200));
            sink.Emit(Ev("first"));
            sink.Emit(Ev("second"));
            Check.True(inner.Entered.Wait(TimeSpan.FromSeconds(10)), "the consumer started");
            Task dispose = Task.Run(() => sink.Dispose());
            Check.True(dispose.Wait(TimeSpan.FromSeconds(10)), "Dispose returned past its drain timeout");
            Check.False(inner.Disposed, "the inner sink is still in use");
            inner.Release.Set();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!inner.Disposed && DateTime.UtcNow < deadline) Thread.Yield();
            Check.True(inner.Disposed, "closed once the drain ended");
            Check.Equal(2, Volatile.Read(ref inner.Written));
        }

        public void Async_ABadDrainTimeoutIsRefused()
        {
            Check.Equal("drainTimeout", Check.Throws<ArgumentOutOfRangeException>(() => new AsyncLogSink(new Recording(), drainTimeout: TimeSpan.FromMilliseconds(-2))).ParamName);
            Check.Equal("drainTimeout", Check.Throws<ArgumentOutOfRangeException>(() => new AsyncLogSink(new Recording(), drainTimeout: TimeSpan.FromDays(30))).ParamName);
            new AsyncLogSink(new Recording(), drainTimeout: Timeout.InfiniteTimeSpan).Dispose();
            new AsyncLogSink(new Recording(), drainTimeout: TimeSpan.Zero).Dispose();
        }

        public void Syslog_AnUndefinedTransportIsRefused()
        {
            Check.Equal("transport", Check.Throws<ArgumentOutOfRangeException>(() => new SyslogSink("127.0.0.1", 9, (SyslogTransport)7)).ParamName);
            Check.Equal("transport", Check.Throws<ArgumentOutOfRangeException>(() => new SyslogSink("127.0.0.1", 9, (SyslogTransport)(-1))).ParamName);
        }

        public void Formatter_AnUndefinedFieldIsRefused()
        {
            Check.Equal("fields", Check.Throws<ArgumentOutOfRangeException>(() => new StructuredTextFormatter(new[] { LogField.Message, (LogField)42 })).ParamName);
            Check.Equal("fields", Check.Throws<ArgumentOutOfRangeException>(() => new StructuredTextFormatter(new[] { (LogField)(-1) })).ParamName);
        }
    }
}
