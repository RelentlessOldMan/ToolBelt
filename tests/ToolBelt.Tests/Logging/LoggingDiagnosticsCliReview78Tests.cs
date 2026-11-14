using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    /// <summary>Regression tests for review round 78: Logging, Diagnostics and Cli.</summary>
    public sealed class LoggingDiagnosticsCliReview78Tests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

        private static LogEvent Ev(string message, Exception? ex = null)
            => new LogEvent(T0, LogLevel.Info, "c", message, ex);

        private static MethodInfo Internal(Type type, string name)
        {
            MethodInfo? m = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            Check.NotNull(m, type.Name + "." + name);
            return m!;
        }

        private sealed class BadMessageException : Exception
        {
            public override string Message => throw new InvalidOperationException("msg boom");
        }

        private sealed class DelegateSink : ILogSink
        {
            private readonly Action<LogEvent> _emit;
            private readonly Action? _dispose;
            public DelegateSink(Action<LogEvent> emit, Action? dispose = null) { _emit = emit; _dispose = dispose; }
            public void Emit(LogEvent logEvent) => _emit(logEvent);
            public void Dispose() => _dispose?.Invoke();
        }

        // ---------- ScopedContext ----------

        private sealed class ModelNode
        {
            public readonly string Name;
            public readonly ModelNode? Parent;
            public bool Disposed;
            public ModelNode(string name, ModelNode? parent) { Name = name; Parent = parent; }
        }

        private static string? ModelText(ModelNode? n)
        {
            if (n == null) return null;
            var parts = new List<string>();
            for (; n != null; n = n.Parent) parts.Add(n.Name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        public void ScopedContext_OutOfOrderDispose_NeverRestoresADisposedScope()
        {
            Task.Run(() =>
            {
                Check.Null(ScopedContext.Current);
                IDisposable a = ScopedContext.Push("A");
                IDisposable b = ScopedContext.Push("B");
                a.Dispose();
                Check.Equal("A/B", ScopedContext.Current);
                b.Dispose();
                Check.Null(ScopedContext.Current, "A was disposed first and came back");
            }).GetAwaiter().GetResult();
        }

        public void ScopedContext_RandomPushAndDispose_MatchesModel()
        {
            Task.Run(() =>
            {
                var rng = new Random(78);
                for (int trial = 0; trial < 200; trial++)
                {
                    Check.Null(ScopedContext.Current);
                    var tokens = new List<(IDisposable Token, ModelNode? Node)>();
                    ModelNode? ambient = null;
                    for (int step = 0; step < 40; step++)
                    {
                        if (tokens.Count == 0 || rng.Next(3) > 0)
                        {
                            bool empty = rng.Next(8) == 0;
                            string name = empty ? "" : "s" + step;
                            IDisposable token = ScopedContext.Push(name);
                            ModelNode? node = null;
                            if (!empty) { node = new ModelNode(name, ambient); ambient = node; }
                            tokens.Add((token, node));
                        }
                        else
                        {
                            var (token, node) = tokens[rng.Next(tokens.Count)];
                            token.Dispose();
                            if (node != null && !node.Disposed)
                            {
                                node.Disposed = true;
                                if (ambient == node)
                                {
                                    ModelNode? up = node.Parent;
                                    while (up != null && up.Disposed) up = up.Parent;
                                    ambient = up;
                                }
                            }
                        }
                        Check.Equal(ModelText(ambient), ScopedContext.Current, $"trial {trial} step {step}");
                    }
                    foreach (var (token, _) in Enumerable.Reverse(tokens)) token.Dispose();
                    // Outer tokens disposed before inner ones may leave the chain to unwind, so clear it fully.
                    Check.Null(ScopedContext.Current, $"trial {trial} end");
                }
            }).GetAwaiter().GetResult();
        }

        public void ScopedContext_DeepNesting_CostsLinearMemory()
        {
            Task.Run(() =>
            {
                const int depth = 10000;
                var tokens = new IDisposable[depth];
                long before = GC.GetTotalMemory(true);
                for (int i = 0; i < depth; i++) tokens[i] = ScopedContext.Push("s");
                long after = GC.GetTotalMemory(true);
                // Each scope held the whole path: about 200 MB at this depth.
                Check.True(after - before < 50L * 1024 * 1024, $"{(after - before) / (1024 * 1024)} MB for {depth} scopes");
                Check.Equal(string.Join("/", Enumerable.Repeat("s", depth)), ScopedContext.Current);
                for (int i = depth - 1; i >= 0; i--) tokens[i].Dispose();
                Check.Null(ScopedContext.Current);
            }).GetAwaiter().GetResult();
        }

        // ---------- Cli ----------

        public void AnsiStyle_CompoundResetWithColour_KeepsTheTextsColour()
        {
            var style = new AnsiStyle(true);
            string text = "a\u001b[0;32mb\u001b[0mc";
            Check.Equal("\u001b[31ma\u001b[0m\u001b[31m\u001b[32mb\u001b[0m\u001b[31mc\u001b[0m", style.Foreground(text, AnsiColor.Red));
        }

        public void ConsoleTable_UnicodeLineBreaks_BecomeSpaces()
        {
            var table = new ConsoleTable("h\u2028x");
            table.AddRow("a\u2028b").AddRow("c\u0085d").AddRow("e\u2029f\vg\fh");
            IReadOnlyList<string> lines = table.Render();
            Check.Equal(5, lines.Count);
            Check.Equal("h x", lines[0].TrimEnd());
            Check.Equal("a b", lines[2].TrimEnd());
            Check.Equal("c d", lines[3].TrimEnd());
            Check.Equal("e f g h", lines[4].TrimEnd());
            foreach (string line in lines)
                Check.True(line.IndexOfAny(new[] { '\u0085', '\u2028', '\u2029', '\v', '\f' }) < 0, line);
        }

        public void ConsoleSpinner_Columns_EmojiJamoAndNarrowAstral()
        {
            MethodInfo columns = Internal(typeof(ConsoleSpinner), "Columns");
            int Columns(string s) => (int)columns.Invoke(null, new object[] { s })!;
            Check.Equal(2, Columns("\u2705"), "check mark emoji");
            Check.Equal(2, Columns("\u274C"));
            Check.Equal(2, Columns("\u2615"));
            Check.Equal(2, Columns("\u26A1"));
            Check.Equal(2, Columns("\u2B50"));
            Check.Equal(1, Columns("\u2713"), "a text check mark stays narrow");
            Check.Equal(2, Columns("\u1100\u1161"), "conjoining jamo make one syllable");
            Check.Equal(2, Columns("\u1100\u1161\u11A8"));
            Check.Equal(1, Columns("\uD835\uDC00"), "U+1D400 is a narrow letter");
            Check.Equal(2, Columns("\uD83D\uDC4D\uD83C\uDFFD"), "a skin tone joins the thumb");
            Check.Equal(2, Columns("\uD840\uDC00"), "CJK extension B");
        }

        public void ExitException_NullMessage_PrintsNothing()
        {
            var err = new StringWriter();
            int code = ConsoleApp.Run(_ => throw new ExitException(3, null!), err);
            Check.Equal(3, code);
            Check.Equal("", err.ToString());
            Check.Equal("", new ExitException(4, null!).Message);
        }

        // ---------- Logging ----------

        public void Compact_ExceptionWhoseMessageThrows_StillFormats()
        {
            string line = LogFormatters.Compact(new LogEvent(T0, LogLevel.Error, "c", "m", new BadMessageException()));
            Check.True(line.Contains("Message threw"), line);
        }

        private static string NaiveSanitize(string value, string delimiter)
        {
            while (value.IndexOf(delimiter, StringComparison.Ordinal) >= 0) value = value.Replace(delimiter, " ");
            return value;
        }

        public void StructuredText_Delimiters_MatchRepeatedReplace()
        {
            var rng = new Random(7801);
            string[] delimiters = { ", ", " | ", " |", "||", "--", "ab", "aba" };
            const string alphabet = " |,-abx";
            foreach (string delimiter in delimiters)
            {
                var formatter = new StructuredTextFormatter(new[] { LogField.Message }, delimiter);
                for (int trial = 0; trial < 400; trial++)
                {
                    var chars = new char[rng.Next(0, 30)];
                    for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[rng.Next(alphabet.Length)];
                    string message = new string(chars);
                    string line = formatter.Format(Ev(message));
                    Check.True(line.IndexOf(delimiter, StringComparison.Ordinal) < 0, $"'{delimiter}' left in '{line}'");
                    Check.Equal(NaiveSanitize(message, delimiter), line, $"'{message}' with '{delimiter}'");
                }
            }
        }

        public void StructuredText_LongValue_SanitizesInLinearTime()
        {
            var formatter = new StructuredTextFormatter(new[] { LogField.Message }, ", ");
            string message = new string(',', 400000) + " ";
            var watch = Stopwatch.StartNew();
            string line = formatter.Format(Ev(message));
            watch.Stop();
            Check.Equal(" ", line);
            // Repeated Replace removed one delimiter per pass here: 400,000 passes over the value (about 30 s).
            Check.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
        }

        public void Decorators_EmitAfterDispose_ThrowObjectDisposed()
        {
            int delivered = 0;
            var inner = new DelegateSink(_ => delivered++);
            var filter = new FilterSink(inner, _ => true);
            filter.Dispose();
            Check.Throws<ObjectDisposedException>(() => filter.Emit(Ev("x")));
            var router = new RouterSink().AddRoute(_ => true, inner);
            router.Dispose();
            Check.Throws<ObjectDisposedException>(() => router.Emit(Ev("x")));
            var limited = new RateLimitedSink(inner, TimeSpan.FromMinutes(1), () => T0);
            limited.Emit(Ev("x"));
            limited.Dispose();
            Check.Throws<ObjectDisposedException>(() => limited.Emit(Ev("x")));
            Check.Throws<ObjectDisposedException>(() => limited.Emit(Ev("y")));
            Check.Equal(1, delivered);
        }

        public void RateLimitedSink_DisposeTwice_DisposesInnerOnce()
        {
            int disposed = 0;
            var limited = new RateLimitedSink(new DelegateSink(_ => { }, () => disposed++), TimeSpan.FromMinutes(1), () => T0, ownsInner: true);
            limited.Dispose();
            limited.Dispose();
            Check.Equal(1, disposed);
        }

        public void FileSink_BackupNumber_IgnoresThePrimaryFile()
        {
            MethodInfo backup = Internal(typeof(FileSink), "BackupNumber");
            int Number(string file) => (int)backup.Invoke(null, new object[] { file, "app.log" })!;
            Check.Equal(0, Number("app.log"), "the primary file matches app.log.* on Windows");
            Check.Equal(0, Number("app.log."));
            Check.Equal(3, Number("app.log.3"));
            Check.Equal(12, Number("app.log.12"));
            Check.Equal(0, Number("app.log.03"));
            Check.Equal(0, Number("app.log.x"));
            Check.Equal(0, Number("app.log.1234567890"));
            Check.Equal(2, Number("APP.LOG.2"));
        }

        public void SyslogSink_FailingUdpLookups_BackOffGrows()
        {
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
            using var sink = new SyslogSink("127.0.0.1", port);
            sink.Emit(Ev("first"));
            Type t = typeof(SyslogSink);
            FieldInfo F(string name) => t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("no field " + name);
            // A name over 255 characters fails the lookup at once, without DNS.
            F("_host").SetValue(sink, new string('h', 300));
            var backoffs = new List<TimeSpan>();
            for (int i = 0; i < 3; i++)
            {
                F("_udpRefreshAt").SetValue(sink, 0L);
                F("_retryAtTimestamp").SetValue(sink, 0L);
                sink.Emit(Ev("again " + i));
                backoffs.Add((TimeSpan)F("_backoff").GetValue(sink)!);
            }
            Check.Equal(TimeSpan.FromSeconds(1), backoffs[0]);
            Check.Equal(TimeSpan.FromSeconds(2), backoffs[1], "the fallback to the last address reset the back-off");
            Check.Equal(TimeSpan.FromSeconds(4), backoffs[2]);
            Check.Equal(0L, sink.FailedSends, "the last good address still worked");
        }

        public void AsyncLogSink_DisposeFromConsumer_ReportsAFailingClose()
        {
            var errors = new ConcurrentQueue<Exception>();
            AsyncLogSink sink = null!;
            var inner = new DelegateSink(_ => sink.Dispose(), () => throw new InvalidOperationException("close failed"));
            sink = new AsyncLogSink(inner, onError: ex => errors.Enqueue(ex), ownsInner: true);
            sink.Emit(Ev("x"));
            bool reported = SpinWait.SpinUntil(() => errors.Any(e => e.Message == "close failed"), TimeSpan.FromSeconds(10));
            Check.True(reported, "the inner sink's Dispose failure went unobserved");
        }

        // ---------- Diagnostics ----------

        public void Metrics_OverflowingSum_MeanAgreesUnderAndPastTheCap()
        {
            double[] values = { double.MaxValue, double.MaxValue, -double.MaxValue, -double.MaxValue, 6 };
            foreach (var registry in new[] { new MetricsRegistry(), new MetricsRegistry(1) })
            {
                foreach (double v in values) registry.Record("t", v);
                Check.Close(1.2, registry.Snapshot().Timers["t"].Mean, 1e-12);
            }
            var rng = new Random(7802);
            for (int trial = 0; trial < 100; trial++)
            {
                var all = new MetricsRegistry();
                var capped = new MetricsRegistry(1);
                int n = rng.Next(2, 20);
                for (int i = 0; i < n; i++)
                {
                    double v = (rng.NextDouble() * 2 - 1) * double.MaxValue;
                    if (rng.Next(4) == 0) v = rng.NextDouble() * 100;
                    all.Record("t", v);
                    capped.Record("t", v);
                }
                Check.Equal(capped.Snapshot().Timers["t"].Mean, all.Snapshot().Timers["t"].Mean, $"trial {trial}");
            }
        }

        public void FirstChanceMonitor_MessageThatThrows_IsCountedAndReported()
        {
            var records = new List<FirstChanceRecord>();
            using var monitor = new FirstChanceMonitor(records.Add, attach: false);
            monitor.Observe(new BadMessageException());
            Check.Equal(1, records.Count);
            string text = records[0].ToString();
            Check.True(text.Contains("BadMessageException") && text.Contains("Message threw"), text);
            Check.Equal(1, monitor.Counts().Count);
        }

        public void FirstChanceMonitor_ManyExceptionTypes_TableStaysBounded()
        {
            var records = 0;
            using var monitor = new FirstChanceMonitor(_ => records++, attach: false);
            for (int i = 0; i < FirstChanceMonitor.MaxSignatures; i++) monitor.Observe(new InvalidOperationException("m" + i));
            AssemblyBuilder asm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Review78Types"), AssemblyBuilderAccess.Run);
            ModuleBuilder module = asm.DefineDynamicModule("Review78Types");
            for (int i = 0; i < FirstChanceMonitor.MaxSignatures + 200; i++)
            {
                TypeBuilder tb = module.DefineType("Review78.E" + i, TypeAttributes.Public | TypeAttributes.Sealed, typeof(Exception));
                tb.DefineDefaultConstructor(MethodAttributes.Public);
                Type type = tb.CreateTypeInfo()!.AsType();
                monitor.Observe((Exception)Activator.CreateInstance(type)!);
            }
            int count = monitor.Counts().Count;
            Check.True(count <= 2 * FirstChanceMonitor.MaxSignatures + 1, count.ToString());
            Check.True(monitor.Counts().ContainsKey("(other types): (other messages)"));
        }

        public void HealthCheck_ExceptionWhoseMessageThrows_ReportsEveryProbe()
        {
            var check = new HealthCheck(TimeSpan.FromSeconds(10))
                .Add("faulted", _ => Task.FromException<ProbeResult>(new BadMessageException()))
                .Add("thrown", _ => throw new BadMessageException())
                .Add("ok", _ => Task.FromResult(ProbeResult.Healthy()));
            HealthReport report = check.RunAsync().GetAwaiter().GetResult();
            Check.Equal(3, report.Probes.Count);
            Check.Equal(HealthStatus.Unhealthy, report.Probes[0].Status);
            Check.True(report.Probes[0].Message!.Contains("Message threw"), report.Probes[0].Message);
            Check.Equal(HealthStatus.Unhealthy, report.Probes[1].Status);
            Check.Equal(HealthStatus.Healthy, report.Probes[2].Status);
        }

        public void HealthCheck_AsyncProbeBlockingBeforeAwait_StartsOnlyOnce()
        {
            using var gate = new ManualResetEventSlim(false);
            int entered = 0;
            var check = new HealthCheck(TimeSpan.FromMilliseconds(50)).Add("blocker", _ =>
            {
                Interlocked.Increment(ref entered);
                gate.Wait(TimeSpan.FromSeconds(30));
                return Task.FromResult(ProbeResult.Healthy());
            });
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    HealthReport report = check.RunAsync().GetAwaiter().GetResult();
                    Check.Equal(HealthStatus.Unhealthy, report.Status, "run " + i);
                }
                Check.Equal(1, Volatile.Read(ref entered), "each run took another pool thread");
            }
            finally { gate.Set(); }
        }

        public void EnvironmentReport_CaseVariantNames_KeptApartOffWindows()
        {
            MethodInfo collect = Internal(typeof(EnvironmentReport), "CollectVariables");
            Func<string, string?> get = n => n == "PATH" ? "upper" : n == "path" ? "lower" : null;
            var unix = (IReadOnlyDictionary<string, string>)collect.Invoke(null, new object[] { new[] { "PATH", "path", "none" }, get, false })!;
            Check.Equal(2, unix.Count);
            Check.Equal("upper", unix["PATH"]);
            Check.Equal("lower", unix["path"]);
            var windows = (IReadOnlyDictionary<string, string>)collect.Invoke(null, new object[] { new[] { "PATH", "path" }, get, true })!;
            Check.Equal(1, windows.Count);
            Check.True(windows.ContainsKey("Path"));
        }

        public void Benchmark_ZeroBaseline_IsNotReportedAsEqual()
        {
            MethodInfo relative = Internal(typeof(Benchmark), "Relative");
            double R(double mean, double baseline) => (double)relative.Invoke(null, new object[] { mean, baseline })!;
            Check.Equal(double.PositiveInfinity, R(5, 0));
            Check.Equal(1.0, R(0, 0));
            Check.Equal(2.0, R(4, 2));
            Check.Equal(0.5, R(1, 2));
        }
    }
}
