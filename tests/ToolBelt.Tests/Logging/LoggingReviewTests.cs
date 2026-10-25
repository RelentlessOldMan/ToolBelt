using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    /// <summary>Regression tests for review round 12: Logging, Diagnostics and Cli.</summary>
    public sealed class LoggingReviewTests
    {
        private static LogEvent Ev(string message, string category = "c", LogLevel level = LogLevel.Info)
            => new LogEvent(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero), level, category, message, null);

        private static void WithCulture(string name, Action action)
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                action();
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        // ---------- Logging ----------

        public void FileSink_FailedRotation_KeepsLogging()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-r12-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "app.log");
                using (var sink = new FileSink(path, LogFormatters.Compact, maxBytes: 50, maxFiles: 2))
                {
                    // A viewer holding the file without FileShare.Delete makes the rotation's move fail on Windows.
                    using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        sink.Emit(Ev(new string('x', 60)));
                        sink.Emit(Ev("second"));
                    }
                    sink.Emit(Ev("third"));
                }
                string all = string.Concat(Directory.GetFiles(dir).Select(File.ReadAllText));
                Check.True(all.Contains("second") && all.Contains("third"), all);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void AsyncLogSink_ReentrantEmitFromConsumer_DoesNotDeadlock()
        {
            var got = new ConcurrentQueue<string>();
            AsyncLogSink sink = null!;
            sink = new AsyncLogSink(new DelegateSink(e =>
            {
                got.Enqueue(e.Message);
                // e.g. a FirstChanceMonitor reporting a sink failure into the same logger, on the consumer thread
                if (e.Message.StartsWith("outer", StringComparison.Ordinal))
                    for (int i = 0; i < 10; i++) sink.Emit(Ev("inner"));
            }), capacity: 2);
            var run = Task.Run(() =>
            {
                for (int i = 0; i < 20; i++) sink.Emit(Ev("outer" + i));
                sink.Dispose();
            });
            Check.True(run.Wait(TimeSpan.FromSeconds(10)), "deadlocked");
            Check.Equal(20, got.Count(m => m.StartsWith("outer", StringComparison.Ordinal)));
            Check.Equal(200, got.Count(m => m == "inner"));
        }

        public void AsyncLogSink_DoubleDispose_DisposesInnerOnce()
        {
            int disposed = 0;
            var inner = new CountingSink(() => disposed++);
            var sink = new AsyncLogSink(inner);
            sink.Dispose();
            sink.Dispose();
            Check.Equal(1, disposed);
        }

        private sealed class CountingSink : ILogSink
        {
            private readonly Action _onDispose;
            public CountingSink(Action onDispose) => _onDispose = onDispose;
            public void Emit(LogEvent logEvent) { }
            public void Dispose() => _onDispose();
        }

        public void SyslogSink_UnreachableTcp_DoesNotBlockEveryEvent()
        {
            using var sink = new SyslogSink("10.255.255.1", 601, SyslogTransport.Tcp); // non-routable: connect hangs
            var sw = Stopwatch.StartNew();
            sink.Emit(Ev("one"));
            long first = sw.ElapsedMilliseconds;
            sw.Restart();
            sink.Emit(Ev("two"));
            sink.Emit(Ev("three"));
            long rest = sw.ElapsedMilliseconds;
            Check.True(first < 8000, "first connect took " + first + " ms");
            Check.True(rest < 1000, "later events took " + rest + " ms");
            Check.Equal(3L, sink.FailedSends);
        }

        public void RouterSink_ThrowingRoute_DoesNotStarveOthers()
        {
            var all = new List<LogEvent>();
            var router = new RouterSink()
                .AddRoute(_ => true, new DelegateSink(_ => throw new IOException("disk full")))
                .AddRoute(_ => true, new DelegateSink(all.Add));
            Check.Throws<IOException>(() => router.Emit(Ev("m")));
            Check.Equal(1, all.Count);
        }

        public void RateLimitedSink_PipeInFields_DoesNotCollide()
        {
            var got = new List<LogEvent>();
            using var sink = new RateLimitedSink(new DelegateSink(got.Add), TimeSpan.FromMinutes(1),
                () => new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
            sink.Emit(Ev("z", "x|y"));
            sink.Emit(Ev("y|z", "x"));
            Check.Equal(2, got.Count);
            Check.Equal("y|z", got[1].Message);
        }

        public void StructuredTextFormatter_InvalidTimestampFormat_RejectedUpFront()
        {
            Check.Throws<ArgumentException>(() => new StructuredTextFormatter(new[] { LogField.Timestamp }, " ", "%"));
        }

        public void ScopedContext_EmptyNestedScope_AddsNoSeparator()
        {
            using (ScopedContext.Push("a"))
            using (ScopedContext.Push(""))
                Check.Equal("a", ScopedContext.Current);
        }

        // ---------- Diagnostics ----------

        public void ExceptionUtils_Describe_IndentsByTreeDepth()
        {
            var ex = new AggregateException(
                new InvalidOperationException("A", new FormatException("X")),
                new ArgumentException("B"));
            string[] lines = ExceptionUtils.Describe(ex, includeStackTrace: false).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            Check.True(lines[1].StartsWith("  System.InvalidOperationException: A", StringComparison.Ordinal), lines[1]);
            Check.True(lines[2].StartsWith("    System.FormatException: X", StringComparison.Ordinal), lines[2]);
            Check.True(lines[3].StartsWith("  System.ArgumentException: B", StringComparison.Ordinal), lines[3]);
        }

        public void ExceptionUtils_Describe_ManySiblings_IsLinear()
        {
            var inner = Enumerable.Range(0, 2000).Select(i => new InvalidOperationException("e" + i)).ToArray();
            string text = ExceptionUtils.Describe(new AggregateException(inner), includeStackTrace: false);
            Check.True(text.Length < 200_000, "length " + text.Length);
        }

        public void ExceptionUtils_RootCause_TerminatesOnCycle()
        {
            var a = new InvalidOperationException("a");
            var b = new InvalidOperationException("b", a);
            FieldInfo? field = typeof(Exception).GetField("_innerException", BindingFlags.Instance | BindingFlags.NonPublic);
            Check.NotNull(field);
            field!.SetValue(a, b); // a -> b -> a
            var run = Task.Run(() => ExceptionUtils.RootCause(b));
            Check.True(run.Wait(TimeSpan.FromSeconds(5)), "RootCause looped");
        }

        public void ExceptionUtils_IsTransient_PermanentIoFailuresAreNot()
        {
            Check.False(ExceptionUtils.IsTransient(new FileNotFoundException()));
            Check.False(ExceptionUtils.IsTransient(new DirectoryNotFoundException()));
            Check.False(ExceptionUtils.IsTransient(new PathTooLongException()));
            Check.False(ExceptionUtils.IsTransient(new EndOfStreamException()));
            Check.True(ExceptionUtils.IsTransient(new IOException("sharing violation")));
        }

        public void DiskSpace_EstimateBytes_RejectsInfiniteRate()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => DiskSpace.EstimateBytes(double.PositiveInfinity, TimeSpan.Zero));
        }

        public void DiskSpace_Shortfall_DoesNotOverflow()
        {
            var r = new PreflightResult(true, 100, long.MaxValue, 1000);
            Check.False(r.HasRoom);
            Check.Equal(long.MaxValue, r.ShortfallBytes);
        }

        public void DiskSpace_Preflight_MissingDrive_ReportsNotWritable()
        {
            if (Path.DirectorySeparatorChar != '\\') return;
            var used = new HashSet<char>(DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])));
            char letter = "QRSTUVWXYZ".FirstOrDefault(c => !used.Contains(c));
            if (letter == default(char)) return;
            PreflightResult r = DiskSpace.Preflight(letter + @":\nope", 10);
            Check.False(r.IsWritable);
            Check.False(r.IsOk);
        }

        public void DiskSpace_UncPath_ExplainsLimitation()
        {
            if (Path.DirectorySeparatorChar != '\\') return;
            var ex = Check.Throws<ArgumentException>(() => DiskSpace.Get(@"\\localhost\C$\Windows"));
            Check.True(ex.Message.Contains("UNC"), ex.Message);
        }

        public void HealthCheck_TimeoutBeyondTimerRange_RejectedUpFront()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new HealthCheck(TimeSpan.FromDays(30)));
        }

        public void MetricsRegistry_Record_RejectsNaN()
        {
            var m = new MetricsRegistry();
            Check.Throws<ArgumentOutOfRangeException>(() => m.Record("t", double.NaN));
        }

        public void Benchmark_ToString_IsCultureInvariant()
        {
            WithCulture("de-DE", () =>
            {
                BenchmarkResult r = Benchmark.Run("x", () => { }, iterations: 10, warmupIterations: 1);
                string expected = string.Format(CultureInfo.InvariantCulture, "x: {0:F1} ns/op, {1:N0} ops/s, {2} B/op",
                    r.MeanNanoseconds, r.OperationsPerSecond, r.AllocatedBytesPerOperation);
                Check.Equal(expected, r.ToString());
            });
        }

        public void FirstChanceRecord_ToString_IsCultureInvariant()
        {
            WithCulture("fi-FI", () =>
            {
                FirstChanceRecord? seen = null;
                var m = new FirstChanceMonitor(r => seen = r, attach: false,
                    clock: () => new DateTimeOffset(2026, 10, 25, 12, 34, 56, 789, TimeSpan.Zero));
                m.Observe(new InvalidOperationException("boom"));
                Check.NotNull(seen);
                Check.True(seen!.ToString().StartsWith("12:34:56.789", StringComparison.Ordinal), seen.ToString());
            });
        }

        // ---------- Cli ----------

        public void Sparkline_OneExplicitBound_IsHonoured()
        {
            Check.Equal("▁▁█", Sparkline.Render(new double[] { -5, 0, 10 }, min: 0));
            Check.Equal("▁██", Sparkline.Render(new double[] { 0, 10, 20 }, max: 10));
        }

        public void Sparkline_InfiniteAndHugeValues_Render()
        {
            Check.Equal("▁█", Sparkline.Render(new[] { 0, double.PositiveInfinity }));
            Check.Equal("▁▁█", Sparkline.Render(new[] { double.NegativeInfinity, 0, 1 }));
            Check.Equal("▁▅█", Sparkline.Render(new[] { -1e308, 0, 1e308 }));
        }

        public void ConsoleTable_NullHeader_RendersEmpty()
        {
            var t = new ConsoleTable("a", null!).AddRow("1", "2");
            var lines = t.Render();
            Check.Equal(lines[0].Length, lines[2].Length);
        }

        private static (Prompt Prompt, StringWriter Output) Make(string input)
        {
            var o = new StringWriter();
            return (new Prompt(new StringReader(input), o, interactive: true), o);
        }

        public void Prompt_Text_WhitespaceOnlyIsNotAnAnswer()
        {
            var (p, o) = Make("   \nname\n");
            Check.Equal("name", p.Text("Name"));
            Check.True(o.ToString().Contains("A value is required."), o.ToString());
        }

        public void Prompt_Integer_DefaultOutsideRange_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Make("\n").Prompt.Integer("n", 1, 10, defaultValue: 99));
        }

        public void Prompt_Labels_AreCultureInvariant()
        {
            WithCulture("sv-SE", () =>
            {
                var (p, o) = Make("x\n\n");
                Check.Equal(-3, p.Integer("n", -10, 10, defaultValue: -3));
                string text = o.ToString();
                Check.True(text.Contains("n [-3]: "), text);
                Check.True(text.Contains("from -10 to 10"), text);
            });
        }

        public async Task ConsoleApp_WrappedCancellation_IsInterrupted()
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(50);
            var err = new StringWriter();
            int code = await ConsoleApp.RunAsync(ct =>
            {
                Task.Delay(-1, ct).Wait(); // sync-over-async: throws AggregateException(TaskCanceledException)
                return Task.FromResult(0);
            }, err, verbose: false, hookConsole: false, externalToken: cts.Token);
            Check.Equal(ExitCodes.Interrupted, code);
            Check.True(err.ToString().Contains("Cancelled."), err.ToString());
        }

        public void ConsoleSpinner_BrokenWriter_DisposeDoesNotThrow()
        {
            var w = new BreakableWriter();
            var s = new ConsoleSpinner("work", w, interactive: true, frames: ConsoleSpinner.Ascii, interval: TimeSpan.FromHours(1));
            w.Broken = true;
            s.Dispose(); // a broken stderr pipe must not throw out of a using block
        }

        private sealed class BreakableWriter : StringWriter
        {
            public bool Broken;
            public override void Write(string? value)
            {
                if (Broken) throw new IOException("The pipe is being closed.");
                base.Write(value);
            }
            public override void Flush()
            {
                if (Broken) throw new IOException("The pipe is being closed.");
            }
        }
    }
}
