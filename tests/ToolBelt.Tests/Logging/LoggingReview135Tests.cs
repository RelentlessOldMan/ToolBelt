using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Regressions from review round 135 (eighth pass over Logging, Diagnostics, Cli): each test failed before its fix.
    /// </summary>
    public sealed class LoggingReview135Tests
    {
        private static int Columns(string line) =>
            (int)typeof(ConsoleSpinner).GetMethod("Columns", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { line })!;

        private static string Fit(string line, int columns) =>
            (string)typeof(ConsoleSpinner).GetMethod("Fit", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { line, columns })!;

        // ---------- ConsoleSpinner widths ----------

        // A flag counted 4 (each regional indicator 2) and a ZWJ family 6 (every member); narrow symbols in U+1F000-1FAFF
        // and in the CJK range counted 2; Hangul Jamo Extended-B counted 1. Expected values are terminal wcwidth.
        public void Spinner_FlagsJoinersAndNarrowSymbols_CountTheirColumns()
        {
            foreach (var (text, columns) in new[]
            {
                ("\U0001F1FA\U0001F1F8", 2), ("\U0001F468‍\U0001F469‍\U0001F467", 2),
                ("\U0001F468\U0001F3FD‍\U0001F4BB", 2), ("\U0001F1FA\U0001F1F8\U0001F1EC\U0001F1E7", 4),
                ("\U0001F700", 1), ("\U0001F800", 1), ("\U0001F10B", 1), ("\U0001F0A1", 1), ("〿", 1), ("䷀", 1),
                ("가ힰ", 2),
                // Guards: wide ones stay wide.
                ("\U0001F600", 2), ("\U0001F004", 2), ("\U0001F9FF", 2), ("ぁ〾", 4), ("䶿一", 4), ("\U0001F321️", 2),
            })
                Check.Equal(columns, Columns(text), string.Join(" ", text.Select(c => ((int)c).ToString("X4"))));
            Check.Equal("\U0001F1FA\U0001F1F8X", Fit("\U0001F1FA\U0001F1F8X", 3));
            Check.Equal("\U0001F1FA\U0001F1F8", Fit("\U0001F1FA\U0001F1F8X", 2));
        }

        // ---------- ConsoleSpinner writers ----------

        private sealed class Unsupported : TextWriter
        {
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
            public override void Write(char value) => throw new NotSupportedException();
            public override void Write(string? value) => throw new NotSupportedException();
            public override void WriteLine(string? value) => throw new NotSupportedException();
            public override void Flush() => throw new NotSupportedException();
        }

        // Only IOException and ObjectDisposedException were quiet: a writer throwing NotSupportedException made the
        // constructor (both modes) and Dispose throw, though the class promises drawing and Dispose never throw.
        public void Spinner_AnyWriterFailure_IsQuiet()
        {
            foreach (bool interactive in new[] { true, false })
            {
                var s = new ConsoleSpinner("m", new Unsupported(), interactive, interval: TimeSpan.FromHours(1));
                s.Message = "n";
                s.Tick();
                s.Dispose();
            }
        }

        // ---------- ConsoleTable ----------

        // A styled cell was padded by its escape characters: Bold("x") made its column ten wide.
        public void Table_StyledCells_AreMeasuredWithoutTheirEscapes()
        {
            var style = new AnsiStyle(true);
            string bold = style.Bold("x");
            var lines = new ConsoleTable("A", "B").AddRow(bold, "y").AddRow("longer", "z").Render();
            Check.Equal("A      | B", lines[0]);
            Check.Equal("-------+--", lines[1]);
            Check.Equal(bold + "      | y", lines[2]);
            Check.Equal("longer | z", lines[3]);
            var right = new ConsoleTable("Value").AddRow(style.Foreground("7", AnsiColor.Red)).AddRow("12345").AlignColumn(0, ColumnAlignment.Right).Render();
            Check.Equal("    " + style.Foreground("7", AnsiColor.Red), right[2]);
            // An ESC that starts no SGR sequence still counts.
            Check.Equal("\u001Bx | b", new ConsoleTable("\u001Bx", "b").Render()[0]);
        }

        // ---------- AnsiStyle partial resets ----------

        // Only a full reset re-applied the wrapped style: a 22 or 39 inside the text ended the outer bold or colour.
        public void Ansi_PartialResetOfOurAttribute_ReappliesIt()
        {
            var s = new AnsiStyle(true);
            Check.Equal("\u001b[1ma\u001b[22m\u001b[1mb\u001b[0m", s.Bold("a\u001b[22mb"));
            Check.Equal("\u001b[31ma\u001b[39m\u001b[31mb\u001b[0m", s.Foreground("a\u001b[39mb", AnsiColor.Red));
            Check.Equal("\u001b[44ma\u001b[49m\u001b[44mb\u001b[0m", s.Background("a\u001b[49mb", AnsiColor.Blue));
            Check.Equal("\u001b[38;5;200ma\u001b[39m\u001b[38;5;200mb\u001b[0m", s.Foreground256("a\u001b[39mb", 200));
            // Only the cancelled part comes back: 39 inside a bold red span re-applies red alone.
            Check.Equal("\u001b[1;31ma\u001b[39m\u001b[31mb\u001b[0m", s.Combine("a\u001b[39mb", AnsiColor.Red, bold: true));
            // Guards: a reset followed by the text's own colour is left alone; a reset of something we did not set too.
            Check.Equal("\u001b[31ma\u001b[39;32mb\u001b[0m", s.Foreground("a\u001b[39;32mb", AnsiColor.Red));
            Check.Equal("\u001b[1ma\u001b[39mb\u001b[0m", s.Bold("a\u001b[39mb"));
            Check.Equal("\u001b[31ma\u001b[38;5;39mb\u001b[0m", s.Foreground("a\u001b[38;5;39mb", AnsiColor.Red));
            // A full reset then a partial one: our style comes back after the reset, then the cancelled part again.
            Check.Equal("\u001b[1ma\u001b[0m\u001b[1m\u001b[22m\u001b[1mb\u001b[0m", s.Bold("a\u001b[0;22mb"));
        }

        // ---------- Logger ----------

        // A clock that threw sent its exception to the caller of Info.
        public void Logger_ThrowingClock_DoesNotEscape()
        {
            var got = new List<LogEvent>();
            using var log = new Logger("c", LogLevel.Trace, () => throw new InvalidOperationException("clock"));
            log.AddSink(new DelegateSink(got.Add));
            log.Info("x");
            log.ForCategory("d").Error("y");
            Check.Equal(2, got.Count);
            Check.Equal("x", got[0].Message);
            Check.True(Math.Abs((got[0].Timestamp - DateTimeOffset.Now).TotalMinutes) < 5, "stamped with the system clock");
        }

        // An undefined minimum level was accepted and dropped every event, Fatal included.
        public void Logger_UndefinedMinimumLevel_Throws()
        {
            Check.Equal("minimumLevel", Check.Throws<ArgumentOutOfRangeException>(() => new Logger("c", (LogLevel)99)).ParamName);
            Check.Equal("minimumLevel", Check.Throws<ArgumentOutOfRangeException>(() => new Logger("c", (LogLevel)(-1))).ParamName);
            using var log = new Logger();
            Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => log.MinimumLevel = (LogLevel)6).ParamName);
            log.MinimumLevel = LogLevel.Fatal;
            Check.Equal(LogLevel.Fatal, log.MinimumLevel);
        }

        // ---------- Diagnostics ----------

        // A negative window was accepted and silently turned throttling off.
        public void FirstChanceMonitor_NegativeThrottleWindow_Throws()
        {
            using var m = new FirstChanceMonitor(_ => { }, attach: false);
            Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => m.ThrottleWindow = TimeSpan.FromSeconds(-1)).ParamName);
            Check.Equal(TimeSpan.FromSeconds(10), m.ThrottleWindow);
            m.ThrottleWindow = TimeSpan.Zero;
            Check.Equal(TimeSpan.Zero, m.ThrottleWindow);
        }

        // int.MaxValue iterations threw OutOfMemoryException (past the largest array) instead of naming the argument.
        public void Benchmark_IterationsPastTheLargestArray_Throw()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => Benchmark.Run("x", () => { }, int.MaxValue, 0));
            Check.Equal("iterations", ex.ParamName);
            Check.Equal(3, Benchmark.Run("x", () => { }, 3, 0).Iterations);
        }

        // Dispose tested, then set, a plain flag: racing calls could both report. Many racing pairs, one report each.
        public void ScopedTimer_RacingDispose_ReportsOnce()
        {
            int reports = 0;
            for (int trial = 0; trial < 2000; trial++)
            {
                var t = new ScopedTimer("t", (_, __) => Interlocked.Increment(ref reports));
                using var go = new ManualResetEventSlim();
                Task a = Task.Run(() => { go.Wait(); t.Dispose(); }), b = Task.Run(() => { go.Wait(); t.Dispose(); });
                go.Set();
                Task.WaitAll(a, b);
            }
            Check.Equal(2000, reports);
        }
    }
}
