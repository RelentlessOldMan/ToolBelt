using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using ToolBelt.Cli;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    /// <summary>
    /// Regressions from review round 161 (tenth pass over Logging, Diagnostics, Cli): each test failed before its fix,
    /// except the guards, which say what the fix keeps.
    /// </summary>
    public sealed class LoggingReview161Tests
    {
        private static Exception Chain(int depth)
        {
            Exception e = new InvalidOperationException("leaf");
            for (int i = 0; i < depth; i++) e = new Exception("lvl" + i, e);
            return e;
        }

        // a -> agg -> a, made by reflection.
        private static Exception Cycle()
        {
            var inner = new InvalidOperationException("inner");
            var agg = new AggregateException("agg", inner);
            FieldInfo? field = typeof(Exception).GetField("_innerException", BindingFlags.Instance | BindingFlags.NonPublic);
            Check.NotNull(field);
            field!.SetValue(inner, agg);
            return agg;
        }

        private static LogEvent Event(Exception ex) => new LogEvent(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Error, "cat", "boom", ex);

        private static string Exceptions(Exception ex) => new StructuredTextFormatter(new[] { LogField.Exception }).Format(Event(ex));

        private static string Verbose(Exception ex)
        {
            var err = new StringWriter();
            ConsoleApp.Run(_ => throw ex, err, verbose: true);
            return err.ToString();
        }

        private static IEnumerable<(string Name, Func<Exception, string> Format)> Formatters() => new (string, Func<Exception, string>)[]
        {
            ("Plain", ex => LogFormatters.Plain(Event(ex))),
            ("JsonLine", ex => LogFormatters.JsonLine(Event(ex))),
            ("StructuredTextFormatter", Exceptions),
            ("SyslogSink.Format", ex => SyslogSink.Format(Event(ex), SyslogFacility.User, "host", "app", "1")),
            ("ConsoleApp verbose", Verbose),
        };

        // Exception.ToString recursed per level: 20,000 levels overflowed the stack and ended the process from inside a
        // log call (no catch stops that), and 10,000 took seconds.
        public void DeepChains_AreDescribedQuicklyByEveryFormatter()
        {
            Exception deep = Chain(20_000);
            foreach (var (name, format) in Formatters())
            {
                var clock = Stopwatch.StartNew();
                string text = format(deep);
                Check.True(clock.ElapsedMilliseconds < 2000, name + ": " + clock.ElapsedMilliseconds + " ms");
                Check.True(text.Contains("lvl19999"), name);
                Check.True(text.Contains("more inner exceptions not shown"), name);
                Check.True(text.Length < 100_000, name + ": " + text.Length);
            }
        }

        // ConsoleApp is left out: throwing a cyclic exception hangs the runtime itself, so no body can hand it one.
        public void CyclicChains_AreDescribedByEveryFormatter()
        {
            Exception cycle = Cycle();
            foreach (var (name, format) in Formatters().Where(f => f.Name != "ConsoleApp verbose"))
            {
                string text = format(cycle);
                Check.True(text.Contains("inner") && text.Contains("appears again"), name + ": " + text);
            }
        }

        // Guard: a chain of ordinary depth, and a wide aggregate, keep the runtime's own text.
        public void OrdinaryChains_KeepTheRuntimesText()
        {
            var wide = new AggregateException(Enumerable.Range(0, 2000).Select(i => new InvalidOperationException("e" + i)));
            foreach (Exception ex in new[] { Chain(0), Chain(3), Chain(63), wide })
            {
                string verbose = Verbose(ex);              // the throw gives the outer exception its stack trace
                string expected = ex.ToString();
                Check.Equal(expected, LogFormatters.Plain(Event(ex)).Split(new[] { Environment.NewLine }, 2, StringSplitOptions.None)[1]);
                Check.Equal("error: " + expected + Environment.NewLine, verbose);
            }
        }

        // The four private copies of the describer agree.
        public void TheDescriberCopiesAgree()
        {
            foreach (Exception ex in new[] { Chain(5), Chain(500), Cycle() })
            {
                string? verbose = ex is AggregateException ? null : Verbose(ex);   // the cycle cannot be thrown
                string plain = LogFormatters.Plain(Event(ex)).Split(new[] { Environment.NewLine }, 2, StringSplitOptions.None)[1];
                if (verbose != null) Check.Equal("error: " + plain + Environment.NewLine, verbose);
                Check.Equal(plain.Replace("\r", " ").Replace("\n", " "), Exceptions(ex));
                string syslog = SyslogSink.Format(Event(ex), SyslogFacility.User, "host", "app", "1");
                string first = plain.Split(new[] { Environment.NewLine }, 2, StringSplitOptions.None)[0];
                Check.True(syslog.Contains(" | " + first), syslog.Substring(0, Math.Min(syslog.Length, 400)));
            }
        }

        // 4:0 is the colon form of 24 (no underline): it was read as 4, so the outer underline was not put back.
        public void AnsiStyle_ColonUnderlineOffIsAPartialReset()
        {
            var style = new AnsiStyle(true);
            Check.Equal("\u001b[4ma\u001b[4:0m\u001b[4mb\u001b[0m", style.Underline("a\u001b[4:0mb"));
            Check.Equal(style.Underline("a\u001b[24mb").Replace("[24m", "[4:0m"), style.Underline("a\u001b[4:0mb"));
            // Guard: 4:3 (curly) sets an underline style; nothing is cancelled.
            Check.Equal("\u001b[4ma\u001b[4:3mb\u001b[0m", style.Underline("a\u001b[4:3mb"));
        }

        // The OS reads a variable's name only up to a NUL: "PATH\0X" was reported with PATH's value.
        public void EnvironmentReport_ANameWithANulIsNotSet()
        {
            var report = EnvironmentReport.Capture(new[] { "PATH\0X", "PATH" });
            Check.False(report.EnvironmentVariables.ContainsKey("PATH\0X"));
            Check.True(report.EnvironmentVariables.ContainsKey("PATH"));
        }
    }
}
