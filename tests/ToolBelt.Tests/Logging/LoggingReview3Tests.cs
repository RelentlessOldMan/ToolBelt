using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    /// <summary>
    /// Regressions from review round 43 (third pass over Logging, Diagnostics, Cli): each test failed before its fix.
    /// Anything that used to hang runs under a bounded wait.
    /// </summary>
    public sealed class LoggingReview3Tests
    {
        private static LogEvent Event(string message, string category = "c") =>
            new LogEvent(DateTimeOffset.UnixEpoch, LogLevel.Info, category, message, null);

        private sealed class DelegateSink : ILogSink
        {
            private readonly Action<LogEvent> _emit;
            private readonly Action? _dispose;
            public DelegateSink(Action<LogEvent> emit, Action? dispose = null) { _emit = emit; _dispose = dispose; }
            public void Emit(LogEvent logEvent) => _emit(logEvent);
            public void Dispose() => _dispose?.Invoke();
        }

        // ---------- AsyncLogSink ----------

        public void AsyncLogSink_DisposeFromTheInnerSink_DoesNotHang()
        {
            var written = new List<string>();
            var innerDisposed = new ManualResetEventSlim();
            var gate = new ManualResetEventSlim();
            AsyncLogSink? sink = null;
            var inner = new DelegateSink(e =>
            {
                if (e.Message == "a") gate.Wait(10000);
                lock (written) written.Add(e.Message);
                if (e.Message == "stop") sink!.Dispose(); // a fatal event shuts logging down
            }, () => innerDisposed.Set());
            sink = new AsyncLogSink(inner);
            sink.Emit(Event("a"));
            sink.Emit(Event("stop"));
            sink.Emit(Event("b"));
            gate.Set();
            Check.True(innerDisposed.Wait(10000), "the consumer waited for itself");
            lock (written) Check.Equal("a,stop,b", string.Join(",", written));
        }

        public void AsyncLogSink_DisposeFromOnError_DoesNotHang()
        {
            var innerDisposed = new ManualResetEventSlim();
            AsyncLogSink? sink = null;
            var inner = new DelegateSink(_ => throw new IOException("disk gone"), () => innerDisposed.Set());
            sink = new AsyncLogSink(inner, onError: _ => sink!.Dispose());
            sink.Emit(Event("a"));
            Check.True(innerDisposed.Wait(10000), "Dispose from onError waited for itself");
        }

        // ---------- RateLimitedSink ----------

        public void RateLimitedSink_ClockSteppingBack_DoesNotMute()
        {
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            int forwarded = 0;
            var sink = new RateLimitedSink(new DelegateSink(_ => forwarded++), TimeSpan.FromSeconds(10), () => now);
            sink.Emit(Event("boom"));
            now -= TimeSpan.FromHours(1);
            sink.Emit(Event("boom"));
            now += TimeSpan.FromSeconds(30);
            sink.Emit(Event("boom"));
            Check.Equal(3, forwarded);
        }

        public void RateLimitedSink_DisposeWithAThrowingSummary_StillDisposesTheInner()
        {
            int disposed = 0;
            var inner = new DelegateSink(e => { if (e.Message.Contains("repeated")) throw new IOException("down"); }, () => disposed++);
            var sink = new RateLimitedSink(inner, TimeSpan.FromMinutes(1), ownsInner: true);
            for (int i = 0; i < 3; i++) sink.Emit(Event("same"));
            Check.Throws<IOException>(() => sink.Dispose());
            Check.Equal(1, disposed);
        }

        // ---------- RollingMemorySink ----------

        public void RollingMemorySink_HugeCapacity_IsNotAllocatedUpFront()
        {
            var sink = new RollingMemorySink(int.MaxValue); // threw OutOfMemoryException
            sink.Emit(Event("a"));
            Check.Equal(1, sink.Snapshot().Count);
        }

        // ---------- SyslogSink ----------

        public void SyslogSink_OverlongHost_IsAFailedSendNotAThrow()
        {
            string host = new string('a', 300) + ".example";
            foreach (var transport in new[] { SyslogTransport.Udp, SyslogTransport.Tcp })
            {
                using var sink = new SyslogSink(host, 514, transport);
                sink.Emit(Event("hello"));
                Check.Equal(1L, sink.FailedSends);
            }
        }

        // ---------- FileSink ----------

        public void FileSink_RelativePath_StaysWhereItWasCreated()
        {
            string root = Path.Combine(Path.GetTempPath(), "tb-r43-" + Guid.NewGuid().ToString("N"));
            string a = Path.Combine(root, "a"), b = Path.Combine(root, "b");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            string saved = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = a;
                using (var sink = new FileSink("app.log", maxBytes: 30))
                {
                    sink.Emit(Event("first line of the log"));
                    Environment.CurrentDirectory = b;
                    for (int i = 0; i < 4; i++) sink.Emit(Event("another line " + i));
                }
                Environment.CurrentDirectory = saved;
                Check.Equal(0, Directory.GetFiles(b).Length);
                Check.True(File.Exists(Path.Combine(a, "app.log.1")), "never rotated");
            }
            finally
            {
                Environment.CurrentDirectory = saved;
                Directory.Delete(root, recursive: true);
            }
        }

        // ---------- StructuredTextFormatter ----------

        public void StructuredText_ValueRunningIntoTheDelimiter_StaysSeparable()
        {
            foreach (var (delimiter, message) in new[] { ("--", "x-"), (" | ", "a |"), ("  ", "z ") })
            {
                var f = new StructuredTextFormatter(new[] { LogField.Message, LogField.Category }, delimiter);
                string line = f.Format(Event(message, "cat"));
                string[] parts = line.Split(new[] { delimiter }, StringSplitOptions.None);
                Check.Equal(2, parts.Length, line);
                Check.Equal("cat", parts[1], line);
            }
        }

        public void StructuredText_FieldsAreCopied()
        {
            var fields = new List<LogField> { LogField.Message };
            var f = new StructuredTextFormatter(fields);
            fields.Clear();
            Check.Equal("hi", f.Format(Event("hi")));
        }

        // ---------- ConsoleApp ----------

        private sealed class ThrowingMessage : Exception
        {
            public override string Message => throw new InvalidOperationException("no message");
            public override string ToString() => throw new InvalidOperationException("no text");
        }

        private sealed class UnsupportedWriter : StringWriter
        {
            public override void WriteLine(string? value) => throw new NotSupportedException();
        }

        public async Task ConsoleApp_WrappedExitAndArgumentExceptions_KeepTheirCodes()
        {
            var err = new StringWriter();
            int code = await ConsoleApp.RunAsync(_ => Task.FromResult(Task.Run(new Func<int>(() => throw new ExitException(3, "bad input file"))).Result),
                err, verbose: false, hookConsole: false, CancellationToken.None);
            Check.Equal(3, code);
            Check.Equal("bad input file", err.ToString().Trim());
            code = await ConsoleApp.RunAsync(_ => Task.FromResult(Task.Run(new Func<int>(() => throw new ArgumentException("bad --count"))).Result),
                new StringWriter(), verbose: false, hookConsole: false, CancellationToken.None);
            Check.Equal(ExitCodes.Usage, code);
        }

        public async Task ConsoleApp_UnprintableExceptionOrWriter_KeepsTheExitCode()
        {
            foreach (bool verbose in new[] { false, true })
            {
                int code = await ConsoleApp.RunAsync(_ => throw new ThrowingMessage(), new StringWriter(), verbose, hookConsole: false, CancellationToken.None);
                Check.Equal(70, code);
            }
            int c = await ConsoleApp.RunAsync(_ => throw new ExitException(3, "x"), new UnsupportedWriter(), false, hookConsole: false, CancellationToken.None);
            Check.Equal(3, c);
        }

        // ---------- Prompt ----------

        public void Prompt_NullQuestion_Throws()
        {
            var p = new Prompt(new StringReader("y\n1\n"), new StringWriter(), interactive: true);
            Check.Equal("question", Check.Throws<ArgumentNullException>(() => p.Confirm(null!)).ParamName);
            Check.Equal("question", Check.Throws<ArgumentNullException>(() => p.Choose(null!, new[] { "a", "b" })).ParamName);
        }

        public void Prompt_MaxAttemptsBelowOne_ReportsTheAttemptMade()
        {
            var p = new Prompt(new StringReader("zz\n"), new StringWriter(), interactive: true) { MaxAttempts = 0 };
            var e = Check.Throws<InvalidOperationException>(() => p.Integer("n"));
            Check.True(e.Message.Contains("after 1 attempts"), e.Message);
        }

        // ---------- Sparkline ----------

        public void Sparkline_ExplicitMaxBelowTheData_DrawsAtTheTop()
        {
            Check.Equal("██", Sparkline.Render(new[] { 5.0, 6.0 }, max: 1));
            Check.Equal("█", Sparkline.Render(new[] { 2.0 }, max: 1));
            Check.Equal("▁▁", Sparkline.Render(new[] { 5.0, 6.0 }, min: 10));
        }

        // ---------- Diagnostics ----------

        public void HealthCheck_UndefinedFailureStatus_IsRejected()
        {
            foreach (var status in new[] { (HealthStatus)(-1), (HealthStatus)7 })
            {
                var e = Check.Throws<ArgumentOutOfRangeException>(() =>
                    new HealthCheck().Add("db", _ => Task.FromException<ProbeResult>(new Exception("down")), failureStatus: status));
                Check.Equal("failureStatus", e.ParamName);
            }
        }

        public void Metrics_CounterSaturatesInsteadOfWrapping()
        {
            var m = new MetricsRegistry();
            m.Increment("bytes", long.MaxValue);
            m.Increment("bytes", 1);
            Check.Equal(long.MaxValue, m.Snapshot().Counters["bytes"]);
            m.Increment("down", long.MinValue);
            m.Increment("down", -1);
            Check.Equal(long.MinValue, m.Snapshot().Counters["down"]);
        }

        public void FirstChanceMonitor_CountSaturatesInsteadOfWrapping()
        {
            var monitor = new FirstChanceMonitor(_ => { }, attach: false);
            var ex = new InvalidOperationException("parse");
            monitor.Observe(ex);
            var seen = (System.Collections.IDictionary)typeof(FirstChanceMonitor).GetField("_seen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(monitor)!;
            foreach (object sig in seen.Values)
                sig.GetType().GetField("Count")!.SetValue(sig, int.MaxValue);
            monitor.Observe(ex);
            foreach (var kv in monitor.Counts()) Check.Equal(int.MaxValue, kv.Value);
        }

        public void ExceptionUtils_IsTransient_DoesNotDependOnOrder()
        {
            bool a = ExceptionUtils.IsTransient(new AggregateException(new FileNotFoundException(), new TimeoutException()));
            bool b = ExceptionUtils.IsTransient(new AggregateException(new TimeoutException(), new FileNotFoundException()));
            Check.Equal(a, b);
            Check.False(b);
        }

        public void DiskSpace_Preflight_NamesDirectory()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            string device = @"\\?\" + Path.GetTempPath();
            var e = Check.Throws<ArgumentException>(() => DiskSpace.Preflight(device, 10));
            Check.Equal("directory", e.ParamName);
        }
    }
}
