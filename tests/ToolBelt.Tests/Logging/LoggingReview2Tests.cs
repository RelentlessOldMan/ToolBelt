using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    // Regressions from review round 29 (logging, diagnostics, CLI, second pass).
    public sealed class LoggingReview2Tests
    {
        private static LogEvent Event(string message, string category = "c") =>
            new LogEvent(DateTimeOffset.UnixEpoch, LogLevel.Info, category, message, null);

        private sealed class DelegateSink : ILogSink
        {
            private readonly Action<LogEvent> _emit;
            public DelegateSink(Action<LogEvent> emit) => _emit = emit;
            public void Emit(LogEvent logEvent) => _emit(logEvent);
            public void Dispose() { }
        }

        private sealed class ThrowingValue
        {
            public override string ToString() => throw new InvalidOperationException("no text");
        }

        private sealed class BrokenWriter : StringWriter
        {
            public override void WriteLine(string? value) => throw new IOException("The pipe is being closed.");
        }

        private static Exception Thrown(Exception inner)
        {
            try { throw new InvalidOperationException("outer", inner); }
            catch (Exception ex) { return ex; }
        }

        private static Exception ThrownInner()
        {
            try { throw new ArgumentException("inner"); }
            catch (Exception ex) { return ex; }
        }

        // ---------- ExceptionUtils ----------

        public void Describe_StackFramesAreIndentedOnce()
        {
            string text = ExceptionUtils.Describe(Thrown(ThrownInner()));
            Check.False(text.Contains("at at"), text);                                // the first frame got a second "at"
            string[] lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            Check.True(lines.Any(l => l.StartsWith("  at ", StringComparison.Ordinal)), text);
            Check.True(lines.Any(l => l.StartsWith("    at ", StringComparison.Ordinal)), text);   // the inner's frames
            foreach (string l in lines.Where(l => l.TrimStart().StartsWith("at ", StringComparison.Ordinal)))
                Check.True(l.StartsWith("  ", StringComparison.Ordinal), "frame not indented: " + l);
        }

        public void Describe_ThrowingDataValueIsAPlaceholder()
        {
            var ex = new InvalidOperationException("boom");
            ex.Data["bad"] = new ThrowingValue();
            ex.Data["good"] = 42;
            string text = ExceptionUtils.Describe(ex, includeStackTrace: false);    // threw InvalidOperationException
            Check.True(text.Contains("data[good] = 42"), text);
            Check.True(text.Contains("data[bad] = <"), text);
        }

        public void Describe_DeepChainIsNotQuadratic()
        {
            Exception e = new Exception("leaf");
            for (int i = 0; i < 5000; i++) e = new Exception("x", e);
            string text = ExceptionUtils.Describe(e, includeStackTrace: false);
            Check.True(text.Length < 1_000_000, $"{text.Length} chars");             // ~25M with an indent per level
        }

        // ---------- MetricsRegistry ----------

        public void Metrics_MeanStaysWithinTheSamples()
        {
            var m = new MetricsRegistry();
            for (int i = 0; i < 3; i++) m.Record("t", 0.1);
            var s = m.Snapshot().Timers["t"];
            Check.True(s.Mean <= s.Max, s.Mean.ToString("R"));                       // was 0.10000000000000002
        }

        public void Metrics_PercentileStaysWithinTheSamples()
        {
            var m = new MetricsRegistry();
            for (int i = 0; i < 3; i++) m.Record("t", 25.935401432800763);
            var s = m.Snapshot().Timers["t"];
            Check.Equal(s.Min, s.P95);                                               // was 25.93540143280076
            Check.Equal(s.Min, s.P99);
        }

        public void Metrics_HugeSamplesKeepAFiniteMean()
        {
            var m = new MetricsRegistry();
            m.Record("t", double.MaxValue);
            m.Record("t", double.MaxValue);
            Check.Equal(double.MaxValue, m.Snapshot().Timers["t"].Mean);            // the sum overflowed to infinity
            Check.Throws<ArgumentOutOfRangeException>(() => m.Record("t", double.PositiveInfinity));
            Check.Throws<ArgumentOutOfRangeException>(() => m.Record("t", double.NegativeInfinity));
        }

        // ---------- FirstChanceMonitor ----------

        public void FirstChance_GuardIsPerMonitor()
        {
            var seenByB = new List<string>();
            using var b = new FirstChanceMonitor(r => seenByB.Add(r.Exception.Message), attach: false);
            using var a = new FirstChanceMonitor(_ => b.Observe(new InvalidOperationException("from A's reporter")), attach: false);
            a.Observe(new InvalidOperationException("first"));
            Check.Equal(1, seenByB.Count);                                           // B was blind inside A's callback
            Check.Equal("from A's reporter", seenByB[0]);
        }

        // ---------- AnsiStyle ----------

        public void Ansi_ShortResetAlsoReappliesTheStyle()
        {
            string s = new AnsiStyle(enabled: true).Bold("a\u001b[mb");
            Check.Equal("\u001b[1ma\u001b[m\u001b[1mb\u001b[0m", s);
        }

        // ---------- SyslogSink ----------

        public void Syslog_Utf8MessageStartsWithABom()
        {
            string line = SyslogSink.Format(Event("température"), SyslogFacility.User, "h", "a", "1");
            Check.True(line.EndsWith(" - ﻿température", StringComparison.Ordinal), line);
            string ascii = SyslogSink.Format(Event("plain"), SyslogFacility.User, "h", "a", "1");
            Check.True(ascii.EndsWith(" - plain", StringComparison.Ordinal), ascii);
        }

        public void Syslog_FacilityOutOfRangeIsRejected()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => SyslogSink.Format(Event("m"), (SyslogFacility)24, "h", "a", "1"));
            Check.Throws<ArgumentOutOfRangeException>(() => SyslogSink.Format(Event("m"), (SyslogFacility)(-1), "h", "a", "1"));
            Check.Throws<ArgumentOutOfRangeException>(() => new SyslogSink("127.0.0.1", 514, SyslogTransport.Udp, (SyslogFacility)40));
            Check.True(SyslogSink.Format(Event("m"), (SyslogFacility)23, "h", "a", "1").StartsWith("<190>1 ", StringComparison.Ordinal));
        }

        // ---------- AsyncLogSink ----------

        public void AsyncSinks_ForwardingBothWaysDoesNotDeadlock()
        {
            AsyncLogSink? a = null, b = null;
            int replies = 0;
            a = new AsyncLogSink(new DelegateSink(e =>
            {
                if (e.Category == "req") b!.Emit(e);
                else Interlocked.Increment(ref replies);
            }), capacity: 1);
            b = new AsyncLogSink(new DelegateSink(e => a.Emit(Event("reply", "rep"))), capacity: 1);
            var run = Task.Run(() =>
            {
                for (int i = 0; i < 500; i++) a.Emit(Event("m" + i, "req"));
                a.Dispose();
                b.Dispose();
            });
            Check.True(run.Wait(TimeSpan.FromSeconds(20)), "the two consumers deadlocked");
            Check.True(replies > 0);
        }

        // ---------- FileSink ----------

        public void FileSink_HugeMaxFilesRotatesQuickly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r29-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "app.log");
            try
            {
                var run = Task.Run(() =>
                {
                    using var sink = new FileSink(path, maxBytes: 1, maxFiles: int.MaxValue);
                    for (int i = 0; i < 3; i++) sink.Emit(Event("line " + i));
                });
                Check.True(run.Wait(TimeSpan.FromSeconds(20)), "rotation probed every index up to maxFiles");
                Check.True(File.Exists(path + ".1"));
                Check.True(File.Exists(path + ".3"));
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public void FileSink_RotationStillPrunesAtMaxFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r29-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "app.log");
            try
            {
                using (var sink = new FileSink(path, maxBytes: 1, maxFiles: 2))
                    for (int i = 0; i < 5; i++) sink.Emit(Event("line " + i));
                Check.True(File.Exists(path + ".1"));
                Check.True(File.Exists(path + ".2"));
                Check.False(File.Exists(path + ".3"));
                Check.True(File.ReadAllText(path + ".1").Contains("line 4"));
                Check.True(File.ReadAllText(path + ".2").Contains("line 3"));
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        // ---------- decorators ----------

        public void RateLimited_FailingSummaryKeepsTheNewEvent()
        {
            var written = new List<string>();
            var inner = new DelegateSink(e =>
            {
                if (e.Message.Contains("repeated")) throw new IOException("summary failed");
                written.Add(e.Message);
            });
            var sink = new RateLimitedSink(inner, TimeSpan.FromHours(1), () => DateTimeOffset.UnixEpoch);
            for (int i = 0; i < 3; i++) sink.Emit(Event("same"));
            Check.Throws<IOException>(() => sink.Emit(Event("different")));
            Check.True(written.Contains("different"), string.Join(",", written));   // was lost
            sink.Emit(Event("third"));                                               // the summary isn't retried
            Check.Equal("same,different,third", string.Join(",", written));
        }

        public void Router_AddRouteDuringEmit()
        {
            var router = new RouterSink();
            int late = 0;
            bool added = false;
            router.AddRoute(_ => true, new DelegateSink(e =>
            {
                if (!added) { added = true; router.AddRoute(_ => true, new DelegateSink(_ => late++)); }
            }));
            router.Emit(Event("one"));                                               // threw "Collection was modified"
            router.Emit(Event("two"));
            Check.Equal(1, late);
        }

        // ---------- CLI ----------

        public async Task ConsoleApp_BrokenStderrKeepsTheExitCode()
        {
            int code = await ConsoleApp.RunAsync(_ => throw new InvalidOperationException("boom"), new BrokenWriter(),
                verbose: false, hookConsole: false, CancellationToken.None);
            Check.Equal(ExitCodes.Software, code);                                  // threw IOException
            code = await ConsoleApp.RunAsync(_ => throw new ArgumentException("bad"), new BrokenWriter(),
                verbose: false, hookConsole: false, CancellationToken.None);
            Check.Equal(ExitCodes.Usage, code);
        }

        public void Spinner_IntervalPastTaskDelayIsRejected()
        {
            Check.Throws<ArgumentOutOfRangeException>(() =>
                new ConsoleSpinner("x", new StringWriter(), interactive: false, interval: TimeSpan.FromDays(30)));
            using var ok = new ConsoleSpinner("x", new StringWriter(), interactive: false, interval: TimeSpan.FromMilliseconds(int.MaxValue));
        }
    }
}
