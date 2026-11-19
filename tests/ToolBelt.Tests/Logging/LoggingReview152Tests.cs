using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    /// Regressions from review round 152 (ninth pass over Logging, Diagnostics, Cli): each test failed before its fix.
    /// </summary>
    public sealed class LoggingReview152Tests
    {
        private static string OneLine(string s) =>
            (string)typeof(ConsoleSpinner).GetMethod("OneLine", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { s })!;

        private static int Columns(string line) =>
            (int)typeof(ConsoleSpinner).GetMethod("Columns", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { line })!;

        private sealed class CountingWriter : TextWriter
        {
            public int Writes;
            public Action? OnWrite;
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) { Interlocked.Increment(ref Writes); OnWrite?.Invoke(); }
            public override void Write(string? value) { Interlocked.Increment(ref Writes); OnWrite?.Invoke(); }
        }

        // ---------- ConsoleSpinner ----------

        // Under a millisecond the delay rounded down to Task.Delay(0) and the loop spun a whole core (630,000 writes in
        // half a second); 1 ms is the shortest interval a delay can wait.
        public void Spinner_AnIntervalUnderAMillisecondIsRefused()
        {
            foreach (TimeSpan bad in new[] { TimeSpan.FromTicks(1), TimeSpan.FromTicks(5000), TimeSpan.FromTicks(9999) })
                Check.Equal("interval", Check.Throws<ArgumentOutOfRangeException>(
                    () => new ConsoleSpinner("x", new CountingWriter(), true, ConsoleSpinner.Ascii, bad).Dispose()).ParamName);
            var w = new CountingWriter();
            using (new ConsoleSpinner("x", w, true, ConsoleSpinner.Ascii, TimeSpan.FromMilliseconds(1))) { }
        }

        // A writer that stops the spinner from inside a draw on the loop (a pipe-closed handler, say): Stop waited for the
        // loop, which was the thread waiting, so it hung for good.
        public void Spinner_StopFromInsideADrawDoesNotHang()
        {
            var w = new CountingWriter();
            var spinner = new ConsoleSpinner("x", w, true, ConsoleSpinner.Ascii, TimeSpan.FromMilliseconds(5));
            int draws = 0;
            var stopped = new ManualResetEventSlim();
            w.OnWrite = () =>
            {
                if (Interlocked.Increment(ref draws) == 3)
                {
                    spinner.Stop("done");
                    stopped.Set();
                }
            };
            Check.True(stopped.Wait(TimeSpan.FromSeconds(10)), "Stop from inside the draw never returned");
            spinner.Dispose();
            int after = Volatile.Read(ref w.Writes);
            Thread.Sleep(50);
            Check.Equal(after, Volatile.Read(ref w.Writes));   // the loop ended
        }

        // The colon form of a colour sequence (ESC[38:5:196m), which AnsiStyle and ConsoleTable read as one, was shown as
        // text: its ESC became a space and the line counted 14 columns for "red". Graded against the semicolon form.
        public void Spinner_TheColonFormOfAColourIsKeptAndHasNoWidth()
        {
            foreach (string code in new[] { "38:5:196", "38:2::255:0:0", "4:3", "58:5:1" })
            {
                string colon = "\u001B[" + code + "mred\u001B[0m";
                string semi = "\u001B[" + code.Replace(':', ';') + "mred\u001B[0m";
                Check.Equal(colon, OneLine(colon));
                Check.Equal(Columns(OneLine(semi)), Columns(OneLine(colon)));
                Check.Equal(3, Columns(OneLine(colon)));
            }
            Check.Equal(" [38x5mred", OneLine("\u001B[38x5mred"));   // not a colour code: the ESC is still a space
        }

        // ---------- FileSink ----------

        // The null device has no position or length: new FileSink("NUL") threw NotSupportedException, with rotation or a
        // preamble too, so "log nowhere" could not be configured.
        public void FileSink_TheNullDeviceIsAccepted()
        {
            string device = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "NUL" : "/dev/null";
            var e = new LogEvent(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", new string('x', 100), null);
            foreach (Func<FileSink> make in new Func<FileSink>[]
            {
                () => new FileSink(device),
                () => new FileSink(device, maxBytes: 10),
                () => new FileSink(device, encoding: new UTF8Encoding(true)),
            })
            {
                using (var sink = make())
                    for (int i = 0; i < 5; i++) sink.Emit(e);
            }
        }

        // ---------- Levels ----------

        // An undefined minimum (99) was taken, and every event, Fatal too, was dropped without a word; the Logger refused it.
        // ---------- AsyncLogSink ----------

        private sealed class DelegateSink : ILogSink
        {
            private readonly Action<LogEvent> _emit;
            private readonly Action? _dispose;
            public DelegateSink(Action<LogEvent> emit, Action? dispose = null) { _emit = emit; _dispose = dispose; }
            public void Emit(LogEvent logEvent) => _emit(logEvent);
            public void Dispose() => _dispose?.Invoke();
        }

        // Dispose from the inner sink returned at once (the pipeline cannot wait for its own consumer), and the close it
        // scheduled ran straight away: it cancelled the consumer, and the events queued behind the call were lost (seen
        // on .NET Framework; a sink that took a moment over the disposing event lost them every time).
        public void AsyncSink_DisposeFromTheInnerSinkStillWritesWhatWasQueued()
        {
            var written = new List<string>();
            var closed = new ManualResetEventSlim();
            var gate = new ManualResetEventSlim();
            AsyncLogSink? sink = null;
            var inner = new DelegateSink(e =>
            {
                if (e.Message == "a") gate.Wait(10000);
                lock (written) written.Add(e.Message);
                if (e.Message == "stop")
                {
                    sink!.Dispose();
                    Thread.Sleep(300);   // test code: the sink is still busy with the event that closed it
                }
            }, () => closed.Set());
            sink = new AsyncLogSink(inner, ownsInner: true);
            LogEvent Make(string m) => new LogEvent(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", m, null);
            foreach (string m in new[] { "a", "stop", "b", "c" }) sink.Emit(Make(m));
            gate.Set();
            Check.True(closed.Wait(10000), "the inner sink was never closed");
            lock (written) Check.Equal("a,stop,b,c", string.Join(",", written));
        }

        public void Decorators_AnUndefinedMinimumLevelIsRefused()
        {
            var got = new List<LogEvent>();
            foreach (var bad in new[] { (LogLevel)99, (LogLevel)(-1), (LogLevel)6 })
            {
                Check.Equal("minimum", Check.Throws<ArgumentOutOfRangeException>(() => FilterSink.MinimumLevel(new DelegateSink(got.Add), bad)).ParamName);
                Check.Equal("minimum", Check.Throws<ArgumentOutOfRangeException>(() => new RouterSink().AddLevelRoute(bad, new DelegateSink(got.Add))).ParamName);
            }
            var fatal = new LogEvent(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Fatal, "c", "m", null);
            var warn = new LogEvent(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Warning, "c", "m", null);
            foreach (LogLevel min in Enum.GetValues(typeof(LogLevel)))
            {
                got.Clear();
                FilterSink.MinimumLevel(new DelegateSink(got.Add), min).Emit(warn);
                new RouterSink().AddLevelRoute(min, new DelegateSink(got.Add)).Emit(fatal);
                Check.Equal((warn.Level >= min ? 1 : 0) + 1, got.Count);
            }
        }

        // Log((LogLevel)99, ...) went to every sink past any minimum and was written as "[99]"; the constructor and the
        // MinimumLevel setter refuse such a level.
        public void Logger_AnUndefinedLevelIsRefused()
        {
            var got = new List<LogEvent>();
            using var log = new Logger("c", LogLevel.Info);
            log.AddSink(new DelegateSink(got.Add));
            Check.Equal("level", Check.Throws<ArgumentOutOfRangeException>(() => log.Log((LogLevel)99, "m")).ParamName);
            Check.Equal("level", Check.Throws<ArgumentOutOfRangeException>(() => log.Log((LogLevel)(-1), "m")).ParamName);
            Check.Equal(0, got.Count);
            log.Log(LogLevel.Fatal, "m");
            log.Log(LogLevel.Debug, "m");
            Check.Equal(1, got.Count);
        }

        // ---------- EnvironmentReport and ExceptionUtils ----------

        // The text was trimmed of all trailing white space, which changed the last value ("value  " came out "value").
        public void EnvironmentReport_TheLastValueKeepsItsTrailingSpaces()
        {
            string name = "TB_R152_" + Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(name, "value  ");
            try
            {
                string text = EnvironmentReport.Capture(new[] { name }).ToText();
                Check.True(text.EndsWith(name + " = value  ", StringComparison.Ordinal), text);
                Check.False(text.EndsWith("\n", StringComparison.Ordinal));
            }
            finally { Environment.SetEnvironmentVariable(name, null); }
        }

        // A message of several lines was written with its later lines at column 0, where they read as a new root entry;
        // a data value likewise; and the last message lost its trailing spaces. Graded by the tree rule: every line of an
        // exception at depth d starts with 2d spaces, and only type lines start "System.".
        public void Describe_AMessageOfSeveralLinesStaysUnderItsException()
        {
            var inner = new ArgumentException("inner a\r\ninner b\rinner c  ");
            var outer = new InvalidOperationException("outer 1\nouter 2", inner);
            outer.Data["k"] = "v1\nv2";
            string text = ExceptionUtils.Describe(outer, false);
            Check.Equal(string.Join("|", new[]
            {
                "System.InvalidOperationException: outer 1",
                "  outer 2",
                "  data[k] = v1",
                "    v2",
                "  System.ArgumentException: inner a",
                "    inner b",
                "    inner c  ",
            }), string.Join("|", text.Split(new[] { Environment.NewLine }, StringSplitOptions.None)));
        }
    }
}
