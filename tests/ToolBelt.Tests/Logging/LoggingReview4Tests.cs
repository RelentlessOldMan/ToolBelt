using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
    /// Regressions from review round 69 (fourth pass over Logging, Diagnostics, Cli): each test failed before its fix
    /// (a few only on .NET Framework, as noted). Anything that used to hang runs under a bounded wait.
    /// </summary>
    public sealed class LoggingReview4Tests
    {
        private static LogEvent Event(string message, Exception? exception = null, long seconds = 0) =>
            new LogEvent(DateTimeOffset.FromUnixTimeSeconds(seconds), LogLevel.Info, "c", message, exception);

        private sealed class BadToString : Exception
        {
            public BadToString() : base("bad") { }
            public override string ToString() => throw new InvalidOperationException("no");
        }

        private sealed class BrokenWriter : TextWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) => throw new IOException("pipe closed");
            public override void Write(string? value) => throw new IOException("pipe closed");
            public override void WriteLine(string? value) => throw new IOException("pipe closed");
        }

        // ---------- Formatters ----------

        public void StructuredText_DelimiterWithSpaces_NeverLeftInAValue()
        {
            foreach (string delimiter in new[] { " | ", ", ", "  ", "a a", " :: " })
            {
                var f = new StructuredTextFormatter(new[] { LogField.Message, LogField.Category }, delimiter);
                foreach (string message in new[] { "x | | y", "x,, y", "a   b", "xa a ay", "p :: :: q", "a a a a a" })
                {
                    string line = f.Format(Event(message));
                    int fields = line.Split(new[] { delimiter }, StringSplitOptions.None).Length;
                    Check.Equal(2, fields, $"delimiter '{delimiter}', message '{message}': {line}");
                }
            }
        }

        public void StructuredText_UnicodeLineBreaks_BecomeSpaces()
        {
            var f = new StructuredTextFormatter(new[] { LogField.Message });
            string line = f.Format(Event("a\u2028b\u2029c\u0085d"));
            Check.Equal(-1, line.IndexOfAny(new[] { '\u2028', '\u2029', '\u0085' }), line);
        }

        public void JsonLine_UnpairedSurrogates_AreEscaped()
        {
            string json = LogFormatters.JsonLine(Event("x\uD800y\uDC00z\uD83D\uDE00"));
            Check.True(json.Contains("x\\ud800y\\udc00z"), json);
            Check.True(json.Contains("\uD83D\uDE00"), "a pair stays as it is: " + json);
            // Round trip through UTF-8 keeps the lone surrogates (raw, they became U+FFFD).
            string back = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(json));
            Check.False(back.Contains("\uFFFD"), back);
        }

        public void Formatters_ExceptionWhoseToStringThrows_StillFormat()
        {
            LogEvent e = Event("m", new BadToString());
            Check.True(LogFormatters.Plain(e).Contains(typeof(BadToString).FullName!));
            Check.True(LogFormatters.JsonLine(e).Contains(typeof(BadToString).FullName!));
            var f = new StructuredTextFormatter(new[] { LogField.Message, LogField.Exception }, "\t");
            Check.True(f.Format(e).Contains(typeof(BadToString).FullName!));
            Check.True(SyslogSink.Format(e, SyslogFacility.Local0, "h", "a", "1").Contains(typeof(BadToString).FullName!));
        }

        // ---------- Sinks ----------

        public void Sinks_NullEvent_ThrowArgumentNullNamingLogEvent()
        {
            var inner = new RollingMemorySink(4);
            var sinks = new List<ILogSink>
            {
                inner,
                new FilterSink(inner, _ => true),
                new RouterSink().AddRoute(_ => true, inner),
                new RateLimitedSink(inner, TimeSpan.FromSeconds(1), () => DateTimeOffset.FromUnixTimeSeconds(0)),
                new TextWriterSink(new StringWriter()),
                new SyslogSink("127.0.0.1", 9, SyslogTransport.Udp),
                new AsyncLogSink(new RollingMemorySink(4)),
            };
            foreach (ILogSink sink in sinks)
            {
                var ex = Check.Throws<ArgumentNullException>(() => sink.Emit(null!), sink.GetType().Name);
                Check.Equal("logEvent", ex.ParamName, sink.GetType().Name);
            }
            Check.Equal(0, inner.Snapshot().Count);
            foreach (ILogSink sink in sinks) sink.Dispose();

            string dir = Path.Combine(Path.GetTempPath(), "tb-r69-null-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using var file = new FileSink(Path.Combine(dir, "log.txt"));
                Check.Equal("logEvent", Check.Throws<ArgumentNullException>(() => file.Emit(null!)).ParamName);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void AsyncLogSink_UndefinedOverflowPolicy_IsRefused()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => new AsyncLogSink(new RollingMemorySink(1), 4, (ToolBelt.Threading.PipelineOverflowPolicy)99));
            Check.Equal("overflowPolicy", ex.ParamName);
        }

        public void RateLimitedSink_Summary_CarriesTheLastRepeatsTimestamp()
        {
            var inner = new RollingMemorySink(10);
            long clock = 0;
            var sink = new RateLimitedSink(inner, TimeSpan.FromSeconds(10), () => DateTimeOffset.FromUnixTimeSeconds(clock));
            sink.Emit(Event("same", seconds: 0));
            clock = 1;
            sink.Emit(Event("same", seconds: 1));
            clock = 5000;
            sink.Dispose();
            IReadOnlyList<LogEvent> written = inner.Snapshot();
            Check.Equal(2, written.Count);
            Check.Equal(DateTimeOffset.FromUnixTimeSeconds(1), written[1].Timestamp);
        }

        public void RateLimitedSink_InnerSinkThatWaitsOnAnotherLogger_DoesNotDeadlock()
        {
            // The inner sink waits for another thread that logs through the same RateLimitedSink, as a blocking
            // AsyncLogSink with a full queue waits for its consumer. Under the old lock the other thread never got in.
            RateLimitedSink? sink = null;
            bool otherFinished = false;
            var inner = new DelegateSink(e =>
            {
                if (e.Message != "first") return;
                var other = new Thread(() => sink!.Emit(Event("second")));
                other.IsBackground = true;
                other.Start();
                otherFinished = other.Join(TimeSpan.FromSeconds(5));
            });
            sink = new RateLimitedSink(inner, TimeSpan.FromSeconds(1));
            var main = Task.Run(() => sink.Emit(Event("first")));
            Check.True(main.Wait(TimeSpan.FromSeconds(20)), "the first emit returned");
            Check.True(otherFinished, "the nested emit was not blocked by the lock");
        }

        public void FileSink_EmptyOrInvalidPath_NamesPath()
        {
            foreach (string path in new[] { "   ", "\t", "a\0b" })
            {
                var ex = Check.Throws<ArgumentException>(() => new FileSink(path).Dispose(), $"'{path}'");
                Check.Equal("path", ex.ParamName, $"'{path}'");
            }
        }

        public void FileSink_Rotation_DeletesBackupsNumberedPastMaxFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r69-rot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "log.txt");
                File.WriteAllText(path + ".7", "old");
                File.WriteAllText(path + ".3", "old");
                File.WriteAllText(path + ".x", "keep");
                File.WriteAllText(path + ".07", "keep");
                using (var sink = new FileSink(path, e => e.Message, maxBytes: 10, maxFiles: 2))
                    for (int i = 0; i < 5; i++) sink.Emit(Event("0123456789-" + i));
                Check.False(File.Exists(path + ".7"), ".7 pruned");
                Check.False(File.Exists(path + ".3"), ".3 pruned");
                Check.True(File.Exists(path + ".1") && File.Exists(path + ".2"));
                Check.True(File.Exists(path + ".x") && File.Exists(path + ".07"), "other names kept");
            }
            finally { Directory.Delete(dir, true); }
        }

        public void SyslogSink_Tcp_DeliversOverIPv6Loopback()
        {
            // .NET Framework only: a TcpClient with no family is IPv4-only there.
            if (!Socket.OSSupportsIPv6) return;
            var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<TcpClient> accept = listener.AcceptTcpClientAsync();
                using (var sink = new SyslogSink("::1", port, SyslogTransport.Tcp))
                {
                    sink.Emit(Event("hello"));
                    Check.Equal(0L, sink.FailedSends);
                }
                Check.True(accept.Wait(TimeSpan.FromSeconds(10)), "connected");
                using TcpClient client = accept.Result;
                var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
                var read = reader.ReadToEndAsync();
                Check.True(read.Wait(TimeSpan.FromSeconds(10)));
                Check.True(read.Result.Contains("hello"), read.Result);
            }
            finally { listener.Stop(); }
        }

        public void SyslogSink_Tcp_CollectorThatStopsReading_CountsAFailureAndBacksOff()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var accepted = new List<TcpClient>();
            var acceptLoop = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        TcpClient c = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        c.ReceiveBufferSize = 1024;
                        lock (accepted) accepted.Add(c);            // never read
                    }
                }
                catch (Exception) { }
            });
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var sink = new SyslogSink("127.0.0.1", port, SyslogTransport.Tcp);
                string big = new string('x', 60000);
                var deadline = Stopwatch.StartNew();
                bool stalled = false;
                while (deadline.Elapsed < TimeSpan.FromSeconds(40))
                {
                    var one = Stopwatch.StartNew();
                    sink.Emit(Event(big));
                    if (one.Elapsed > TimeSpan.FromSeconds(3)) { stalled = true; break; }
                }
                Check.True(stalled, "the send buffer filled");
                Check.True(sink.FailedSends >= 1, "the timed-out send was counted, FailedSends=" + sink.FailedSends);
                var next = Stopwatch.StartNew();
                sink.Emit(Event(big));
                Check.True(next.Elapsed < TimeSpan.FromSeconds(1), "backing off, not stalling again: " + next.Elapsed);
            }
            finally
            {
                listener.Stop();
                lock (accepted) foreach (TcpClient c in accepted) c.Dispose();
            }
        }

        // ---------- Diagnostics ----------

        public void ProbeResult_UndefinedStatus_IsRefused()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => new ProbeResult((HealthStatus)(-1)));
            Check.Equal("status", ex.ParamName);
            Check.Equal("status", Check.Throws<ArgumentOutOfRangeException>(() => new ProbeResult((HealthStatus)3)).ParamName);
        }

        public void HealthCheck_HungSyncProbe_DoesNotStartAnotherRun()
        {
            int started = 0;
            using var gate = new ManualResetEventSlim();
            var check = new HealthCheck().Add("hang", () => { Interlocked.Increment(ref started); gate.Wait(TimeSpan.FromSeconds(30)); return ProbeResult.Healthy(); },
                TimeSpan.FromMilliseconds(50));
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var run = check.RunAsync();
                    Check.True(run.Wait(TimeSpan.FromSeconds(10)), "run " + i);
                    Check.Equal(HealthStatus.Unhealthy, run.Result.Status);
                }
                Check.Equal(1, Volatile.Read(ref started));
            }
            finally { gate.Set(); }
        }

        public void ExceptionUtils_IsTransient_HttpClientErrorsAreNot()
        {
#if !NETFRAMEWORK
            foreach (HttpStatusCode code in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
                Check.False(ExceptionUtils.IsTransient(new System.Net.Http.HttpRequestException("x", null, code)), code.ToString());
            foreach (HttpStatusCode code in new[] { HttpStatusCode.RequestTimeout, (HttpStatusCode)429, HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway })
                Check.True(ExceptionUtils.IsTransient(new System.Net.Http.HttpRequestException("x", null, code)), code.ToString());
            Check.True(ExceptionUtils.IsTransient(new System.Net.Http.HttpRequestException("no status")));
#endif
            Check.True(ExceptionUtils.IsTransient(new WebException("no response")));
        }

        public void ExceptionUtils_Describe_DataIsInvariant()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var e = new InvalidOperationException("x");
                e.Data["v"] = 1.5;
                string report = ExceptionUtils.Describe(e, includeStackTrace: false);
                Check.True(report.Contains("data[v] = 1.5"), report);
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        public void ScopedTimer_DefaultClock_IsMonotonicAndFine()
        {
            // The wall clock (15.6 ms steps on .NET Framework) timed most 2 ms blocks as 0.
            for (int i = 0; i < 20; i++)
            {
                TimeSpan elapsed = TimeSpan.MinValue;
                using (new ScopedTimer("t", (_, d) => elapsed = d))
                {
                    var spin = Stopwatch.StartNew();
                    while (spin.Elapsed < TimeSpan.FromMilliseconds(2)) { }
                }
                Check.True(elapsed >= TimeSpan.FromMilliseconds(1.9) && elapsed < TimeSpan.FromSeconds(5), "run " + i + ": " + elapsed);
            }
            var field = typeof(ScopedTimer).GetField("_clock", BindingFlags.NonPublic | BindingFlags.Instance);
            using var timer = new ScopedTimer("t", (_, _) => { });
            Check.Null(field?.GetValue(timer), "no wall clock by default");
        }

        public void MetricsRegistry_SampleCap_KeepsCountMinMaxMeanExact()
        {
            var registry = (MetricsRegistry)Activator.CreateInstance(typeof(MetricsRegistry), 1000)!;
            const int n = 100_000;
            double sum = 0;
            for (int i = 1; i <= n; i++) { registry.Record("t", i); sum += i; }
            TimerStats stats = registry.Snapshot().Timers["t"];
            Check.Equal((long)n, stats.Count);
            Check.Equal(1.0, stats.Min);
            Check.Equal((double)n, stats.Max);
            Check.Close(sum / n, stats.Mean, 1e-9);
            Check.Close(n * 0.50, stats.P50, n * 0.06);
            Check.Close(n * 0.95, stats.P95, n * 0.03);
            Check.Close(n * 0.99, stats.P99, n * 0.02);
            Check.Equal("maxSamplesPerTimer", Check.Throws<TargetInvocationException>(() => Activator.CreateInstance(typeof(MetricsRegistry), 0))
                .InnerException is ArgumentOutOfRangeException a ? a.ParamName : null);
        }

        public void MetricsRegistry_HugeSamplesPastTheCap_MeanDoesNotOverflow()
        {
            var registry = (MetricsRegistry)Activator.CreateInstance(typeof(MetricsRegistry), 2)!;
            for (int i = 0; i < 10; i++) registry.Record("t", double.MaxValue);
            TimerStats stats = registry.Snapshot().Timers["t"];
            Check.Equal(double.MaxValue, stats.Mean);
            Check.Equal(10L, stats.Count);
        }

        public void FirstChanceMonitor_LongMessages_AreCutInTheSignature()
        {
            using var monitor = new FirstChanceMonitor(_ => { }, attach: false);
            monitor.Observe(new InvalidOperationException(new string('m', 100_000)));
            string key = monitor.Counts().Keys.Single();
            Check.True(key.Length < 600, "signature length " + key.Length);
        }

        public void AssertInvariant_PathWithCharactersIllegalOnNetFramework_KeepsTheViolation()
        {
            // .NET Framework only: Path.GetFileName threw "Illegal characters in path".
            var ex = Check.Throws<InvariantViolationException>(() => AssertInvariant.That(false, "boom", "M", @"C:\src\a<b>|c.cs", 7));
            Check.True(ex.Message.Contains("a<b>|c.cs:7"), ex.Message);
            ex = Check.Throws<InvariantViolationException>(() => AssertInvariant.That(false, "boom", "M", "/src/x/y.cs", 8));
            Check.True(ex.Message.Contains(" y.cs:8"), ex.Message);
        }

        public void EnvironmentReport_NullVariableName_NamesItsOwnParameter()
        {
            var ex = Check.Throws<ArgumentException>(() => EnvironmentReport.Capture(new string[] { "PATH", null! }));
            Check.Equal("environmentVariableNames", ex.ParamName);
            Check.Equal("environmentVariableNames", Check.Throws<ArgumentException>(() => EnvironmentReport.Capture(new[] { "" })).ParamName);
        }

        // ---------- Cli ----------

        public void AnsiStyle_UndefinedColor_IsRefused()
        {
            var style = new AnsiStyle(true);
            Check.Equal("color", Check.Throws<ArgumentOutOfRangeException>(() => style.Foreground("x", (AnsiColor)99)).ParamName);
            Check.Equal("color", Check.Throws<ArgumentOutOfRangeException>(() => style.Background("x", (AnsiColor)(-1))).ParamName);
            Check.Equal("foreground", Check.Throws<ArgumentOutOfRangeException>(() => style.Combine("x", (AnsiColor)16)).ParamName);
            Check.Equal("background", Check.Throws<ArgumentOutOfRangeException>(() => style.Combine("x", null, (AnsiColor)(-1))).ParamName);
        }

        public void AnsiStyle_CompoundResetInsideText_ReopensTheOuterStyle()
        {
            var style = new AnsiStyle(true);
            foreach (string reset in new[] { "\u001b[0;1m", "\u001b[;4m", "\u001b[00m", "\u001b[0m", "\u001b[m" })
            {
                string s = style.Foreground("a" + reset + "b", AnsiColor.Red);
                Check.True(s.Contains(reset + "\u001b[31m"), reset.Replace("\u001b", "ESC") + ": " + s.Replace("\u001b", "ESC"));
            }
            // Not resets: these leave the outer style alone.
            string bold = style.Foreground("a\u001b[1mb\u001b[10;0mc", AnsiColor.Red);
            Check.Equal("\u001b[31ma\u001b[1mb\u001b[10;0mc\u001b[0m", bold);
        }

        public void Sparkline_BadExplicitBounds_AreRefused()
        {
            double[] v = { 1, 2, 3 };
            Check.Equal("min", Check.Throws<ArgumentOutOfRangeException>(() => Sparkline.Render(v, double.NaN)).ParamName);
            Check.Equal("min", Check.Throws<ArgumentOutOfRangeException>(() => Sparkline.Render(v, double.NegativeInfinity)).ParamName);
            Check.Equal("max", Check.Throws<ArgumentOutOfRangeException>(() => Sparkline.Render(v, null, double.PositiveInfinity)).ParamName);
            Check.Equal("max", Check.Throws<ArgumentOutOfRangeException>(() => Sparkline.Render(v, 5, 1)).ParamName);
            Check.Equal(3, Sparkline.Render(v, 2, 2).Length);
        }

        public void ProgressBar_IsFullOnlyWhenDone()
        {
            Check.Equal(19, ProgressBar.Render(0.98, 20).Count(c => c == '█'));
            Check.Equal(19, ProgressBar.Render(0.9999999, 20).Count(c => c == '█'));
            Check.Equal(20, ProgressBar.Render(1.0, 20).Count(c => c == '█'));
            Check.True(ProgressBar.RenderWithPercent(0.996).EndsWith(" 99%"), ProgressBar.RenderWithPercent(0.996));
            Check.True(ProgressBar.RenderWithPercent(1.0).EndsWith(" 100%"));
            Check.True(ProgressBar.RenderWithPercent(0.29).EndsWith(" 29%"), ProgressBar.RenderWithPercent(0.29));
            Check.Equal(29, ProgressBar.Render(0.29, 100).Count(c => c == '█'));
            Check.Equal(0, ProgressBar.Render(0.04, 20).Count(c => c == '█'));
            // Floored against a naive integer reference on random fractions.
            var rng = new Random(69);
            for (int i = 0; i < 2000; i++)
            {
                int width = rng.Next(1, 200), filled = rng.Next(0, width);
                double fraction = (filled + rng.NextDouble() * 0.999) / width;
                Check.Equal(filled, ProgressBar.Render(fraction, width).Count(c => c == '█'), $"{fraction} × {width}");
            }
        }

        public void ConsoleSpinner_Fit_CountsColumnsAndKeepsSurrogatePairs()
        {
            MethodInfo? fit = typeof(ConsoleSpinner).GetMethod("Fit", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo? columns = typeof(ConsoleSpinner).GetMethod("Columns", BindingFlags.NonPublic | BindingFlags.Static);
            Check.NotNull(fit, "a column-aware cut");
            Check.NotNull(columns);
            string Fit(string s, int w) => (string)fit!.Invoke(null, new object[] { s, w })!;
            Check.Equal("ab", Fit("ab\uD83D\uDE00cd", 3));
            Check.Equal("ab\uD83D\uDE00", Fit("ab\uD83D\uDE00cd", 4));
            Check.Equal("\u6F22", Fit("\u6F22\u5B57abc", 3));
            Check.Equal("e\u0301x", Fit("e\u0301xyz", 2));
            Check.Equal("plain", Fit("plain", 10));
            Check.Equal(7, (int)columns!.Invoke(null, new object[] { "\u6F22\u5B57a\uD83D\uDE00" })!);
        }

        public void ConsoleSpinner_BrokenWriter_DoesNotThrowFromConstructorOrMessage()
        {
            using (var quiet = new ConsoleSpinner("m", new BrokenWriter(), interactive: false))
                quiet.Message = "n";
            using (var live = new ConsoleSpinner("m", new BrokenWriter(), interactive: true, interval: TimeSpan.FromSeconds(10)))
                live.Message = "n";
        }

        public void ConsoleTable_LineBreaksAndTabsInCells_StayOnOneLine()
        {
            var table = new ConsoleTable("h\r\n1", "h2").AddRow("a\nb", "c\td").AddRow("e\rf", "g");
            IReadOnlyList<string> lines = table.Render();
            Check.Equal(4, lines.Count);
            foreach (string line in lines)
            {
                Check.Equal(-1, line.IndexOfAny(new[] { '\r', '\n', '\t' }), line);
                Check.Equal(lines[0].Length, line.Length);
            }
            Check.Equal(4, table.ToString().Split('\n').Length);
        }
    }
}
