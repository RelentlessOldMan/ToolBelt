using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    // Regressions from review round 202 (logging, diagnostics, CLI, twelfth pass): each failed on the code before its fix.
    public sealed class LoggingReview202Tests
    {
        // ---------- AnsiStyle: bold and dim are separate attributes ----------

        // A terminal model of bold, dim and italic (0 resets all, 22 ends bold and dim, 23 ends italic). Returns the
        // attributes each visible character is drawn with, as "b", "d", "i" flags.
        private static List<string> Draw(string s)
        {
            bool bold = false, dim = false, italic = false;
            var drawn = new List<string>();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\u001b' && i + 1 < s.Length && s[i + 1] == '[')
                {
                    int j = s.IndexOf('m', i + 2);
                    foreach (string p in s.Substring(i + 2, j - i - 2).Split(';'))
                    {
                        int n = p.Length == 0 ? 0 : int.Parse(p, System.Globalization.CultureInfo.InvariantCulture);
                        if (n == 0) bold = dim = italic = false;
                        else if (n == 1) bold = true;
                        else if (n == 2) dim = true;
                        else if (n == 3) italic = true;
                        else if (n == 22) bold = dim = false;
                        else if (n == 23) italic = false;
                    }
                    i = j;
                }
                else drawn.Add((bold ? "b" : "") + (dim ? "d" : "") + (italic ? "i" : ""));
            }
            return drawn;
        }

        // Bold(text) draws every character of the text bold, with the text's own dim and italic; Dim(text) the mirror.
        // A sequence 22;2 inside Bold left the text dim but not bold (the 2 counted as setting bold's group), and 22;1
        // inside Dim the same way.
        public void AnsiStyle_PartialResetThenTheOtherOfBoldAndDim_KeepsTheOuterStyle()
        {
            var style = new AnsiStyle(true);
            Check.True(Draw(style.Bold("\u001b[22;2mx"))[0] == "bd", "Bold(22;2 x)");
            Check.True(Draw(style.Dim("\u001b[22;1mx"))[0] == "bd", "Dim(22;1 x)");
            string[] pool = { "0", "1", "2", "3", "22", "23", "" };
            var rng = new Random(202);
            for (int trial = 0; trial < 5000; trial++)
            {
                var text = new StringBuilder();
                int parts = 1 + rng.Next(4);
                for (int k = 0; k < parts; k++)
                {
                    int count = 1 + rng.Next(3);
                    text.Append("\u001b[").Append(string.Join(";", Enumerable.Range(0, count).Select(_ => pool[rng.Next(pool.Length)]))).Append('m');
                    text.Append((char)('a' + k));
                }
                string t = text.ToString();
                List<string> own = Draw(t);
                List<string> bold = Draw(style.Bold(t)), dim = Draw(style.Dim(t));
                for (int c = 0; c < own.Count; c++)
                {
                    string wantBold = "b" + (own[c].Contains("d") ? "d" : "") + (own[c].Contains("i") ? "i" : "");
                    string wantDim = (own[c].Contains("b") ? "b" : "") + "d" + (own[c].Contains("i") ? "i" : "");
                    string shown = t.Replace("\u001b", "ESC");
                    Check.True(bold[c] == wantBold, "Bold(" + shown + ") char " + c + ": " + bold[c] + ", want " + wantBold);
                    Check.True(dim[c] == wantDim, "Dim(" + shown + ") char " + c + ": " + dim[c] + ", want " + wantDim);
                }
            }
        }

        // ---------- Multi-line values in the text reports ----------

        // A value of several lines put its later lines at column 0, where they read as entries of their own.
        public void EnvironmentReport_MultiLineValue_IsIndented()
        {
            const string name = "TOOLBELT_R202_PROBE";
            Environment.SetEnvironmentVariable(name, "first\nFAKE = injected\r\nthird\rfourth");
            try
            {
                string text = EnvironmentReport.Capture(new[] { name }).ToText();
                string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                int at = Array.IndexOf(lines, "Environment:");
                Check.True(at >= 0, text);
                Check.Equal("  " + name + " = first", lines[at + 1]);
                Check.Equal("    FAKE = injected", lines[at + 2]);
                Check.Equal("    third", lines[at + 3]);
                Check.Equal("    fourth", lines[at + 4]);
                Check.Equal(at + 5, lines.Length);
            }
            finally { Environment.SetEnvironmentVariable(name, null); }
        }

        // A Data key of several lines was written as is (values were indented), so its second line read as a new root
        // exception.
        public void ExceptionUtils_Describe_MultiLineDataKey_IsIndented()
        {
            var ex = new InvalidOperationException("msg");
            ex.Data["key\nInvalidOperationException: fake"] = "v";
            string text = ExceptionUtils.Describe(ex, includeStackTrace: false);
            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            Check.Equal(3, lines.Length);
            Check.Equal("System.InvalidOperationException: msg", lines[0]);
            Check.Equal("  data[key", lines[1]);
            Check.Equal("    InvalidOperationException: fake] = v", lines[2]);
        }

        // An unexpected failure is documented as one line on stderr; a message of several lines was written over several,
        // and a usage error's ArgumentException took two lines on .NET Framework ("Parameter name: count").
        public void ConsoleApp_FailureMessages_AreOneLine()
        {
            var cases = new (Exception Thrown, int Code, string Want)[]
            {
                (new InvalidOperationException("line one\nline two\r\nthree\rfour"), ExitCodes.Software,
                    "error: line one line two three four (InvalidOperationException)"),
                (new ArgumentException("bad\nvalue"), ExitCodes.Usage, "error: bad value"),
            };
            foreach (var c in cases)
            {
                var err = new StringWriter();
                int code = ConsoleApp.RunAsync(_ => throw c.Thrown, err, false, hookConsole: false, externalToken: default).GetAwaiter().GetResult();
                Check.Equal(c.Code, code);
                Check.Equal(c.Want + Environment.NewLine, err.ToString());
            }
            var usage = new StringWriter();
            ConsoleApp.RunAsync(_ => throw new ArgumentException("bad value", "count"), usage, false, hookConsole: false, externalToken: default).GetAwaiter().GetResult();
            string written = usage.ToString();
            Check.True(written.EndsWith(Environment.NewLine, StringComparison.Ordinal)
                && written.IndexOfAny(new[] { '\r', '\n' }) == written.Length - Environment.NewLine.Length, written);
            Check.True(written.Contains("count"), written);
        }

        // ---------- ProgressBar ----------

        private static int Units(double fraction, int units)
            => (int)typeof(ProgressBar).GetMethod("Floor", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { fraction, units })!;

        // The tolerance for "a rounding short of a whole number" was a fixed 1e-9: too wide at 20 blocks (0.95 - 3e-11
        // drew 19 blocks beside a label of 94%) and too narrow at large widths (0.29 * 1e8 filled 28,999,999). k / w for
        // every k fills exactly k units, and a fraction at least 1e-14 short of a block boundary does not reach it.
        public void ProgressBar_RoundingTolerance_ScalesWithTheProduct()
        {
            string bar = ProgressBar.RenderWithPercent(0.95 - 3e-11, 20, '#', '.');
            Check.Equal("[##################..] 94%", bar);
            Check.Equal(29_000_000, Units(0.29, 100_000_000));
            var rng = new Random(2020);
            for (int trial = 0; trial < 20000; trial++)
            {
                int w = trial % 3 == 0 ? 1 + rng.Next(100) : trial % 3 == 1 ? 1 + rng.Next(100000) : 1 + rng.Next(int.MaxValue - 1);
                int k = (int)(rng.NextDouble() * w);
                double f = (double)k / w;
                Check.True(Units(f, w) == Math.Min(k, w - 1) || (k == w && f >= 1), "k / w: " + k + " / " + w + " gave " + Units(f, w));
                if (k > 0 && w <= 1_000_000)
                {
                    double shy = f * (1 - 1e-14 * (1 + rng.Next(1000)));
                    Check.True(Units(shy, w) == k - 1, "just below " + k + " / " + w + " (" + shy.ToString("R") + ") gave " + Units(shy, w));
                }
            }
            // The bar and its label agree: blocks of 20 are fifths of the percent.
            for (int trial = 0; trial < 20000; trial++)
            {
                double f = rng.Next(21) / 20.0 - rng.Next(2) * Math.Pow(10, -9 - rng.Next(7));
                f = Math.Max(0, f);
                int blocks = Units(f, 20), percent = Units(f, 100);
                Check.True(blocks * 5 <= percent && percent < blocks * 5 + 5, f.ToString("R") + ": " + blocks + " blocks, " + percent + "%");
            }
        }

        // ---------- MetricsRegistry: signed zeros ----------

        // Min, Max and P50 depended on the order the zeros came in ([+0, -0, +0] gave a P50 of -0 beside a Min and Max of
        // +0; [-0, +0, +0] a Max of -0). Every order of every mix of up to six zeros gives -0 below +0.
        public void MetricsRegistry_SignedZeros_OrderNoLongerMatters()
        {
            for (int count = 1; count <= 6; count++)
                for (int mask = 0; mask < 1 << count; mask++)
                {
                    var samples = Enumerable.Range(0, count).Select(i => (mask >> i & 1) == 1 ? -0.0 : 0.0).ToArray();
                    int negatives = samples.Count(IsNegativeZero);
                    var m = new MetricsRegistry();
                    foreach (double v in samples) m.Record("t", v);
                    var s = m.Snapshot().Timers["t"];
                    string what = string.Join(",", samples.Select(v => IsNegativeZero(v) ? "-0" : "+0"));
                    Check.True(IsNegativeZero(s.Min) == (negatives > 0), what + " min");
                    Check.True(IsNegativeZero(s.Max) == (negatives == count), what + " max");
                    // The median of the zeros sorted -0 first: -0 when the middle is a -0 (an even count interpolates).
                    var sorted = samples.OrderBy(v => IsNegativeZero(v) ? 0 : 1).ToArray();
                    double want = count % 2 == 1 ? sorted[count / 2] : (IsNegativeZero(sorted[count / 2 - 1]) && IsNegativeZero(sorted[count / 2]) ? -0.0 : 0.0);
                    Check.True(IsNegativeZero(s.P50) == IsNegativeZero(want), what + " p50");
                }
        }

        private static bool IsNegativeZero(double v) => v == 0 && BitConverter.DoubleToInt64Bits(v) < 0;

        // ---------- StructuredTextFormatter ----------

        // The previous code, as a reference: delimiters replaced at the end as the value is copied, then a tail that runs
        // into the delimiter blanked or dropped.
        private static string Reference(string value, string d, bool last)
        {
            value = value.Replace("\r", " ").Replace("\n", " ").Replace("\u0085", " ").Replace("\u2028", " ").Replace("\u2029", " ");
            if (d.Length > 0 && d != " " && value.IndexOf(d, StringComparison.Ordinal) >= 0)
            {
                if (d.Length == 1) value = value.Replace(d, " ");
                else
                {
                    var sb = new StringBuilder();
                    foreach (char c in value)
                    {
                        sb.Append(c);
                        while (sb.Length >= d.Length && sb.ToString(sb.Length - d.Length, d.Length) == d) { sb.Length -= d.Length; sb.Append(' '); }
                    }
                    value = sb.ToString();
                }
            }
            if (last || d.Length < 2 || value.Length == 0) return value;
            var t = new StringBuilder(value);
            bool changed = true;
            while (changed && t.Length > 0)
            {
                changed = false;
                for (int k = Math.Min(d.Length - 1, t.Length); k >= 1; k--)
                {
                    if (t.ToString(t.Length - k, k) != d.Substring(0, k) || string.CompareOrdinal(d, k, d, 0, d.Length - k) != 0) continue;
                    if (t[t.Length - 1] != ' ') t[t.Length - 1] = ' ';
                    else t.Length--;
                    changed = true;
                    break;
                }
            }
            return t.ToString();
        }

        private static LogEvent Event(string category, string message)
            => new LogEvent(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero), LogLevel.Info, category, message, null);

        // The escaping was rewritten to run in linear time; graded against the previous code over random values and
        // delimiters drawn from a small alphabet, where delimiters, overlaps and self-overlapping delimiters are common.
        public void StructuredTextFormatter_Escaping_MatchesTheReference()
        {
            var rng = new Random(2021);
            const string alphabet = "ab |-";
            for (int trial = 0; trial < 20000; trial++)
            {
                string d = new string(Enumerable.Range(0, 1 + rng.Next(5)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
                string msg = new string(Enumerable.Range(0, rng.Next(30)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
                string cat = new string(Enumerable.Range(0, rng.Next(12)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
                var f = new StructuredTextFormatter(new[] { LogField.Message, LogField.Category, LogField.Message }, d);
                string want = Reference(msg, d, false) + d + Reference(cat, d, false) + d + Reference(msg, d, true);
                string got = f.Format(Event(cat, msg));
                Check.True(got == want, "delimiter '" + d + "', message '" + msg + "', category '" + cat + "': '" + got + "', want '" + want + "'");
            }
        }

        // O(message x delimiter): a 16,384-character delimiter and a 100,000-character message took 4.8 s, and a message
        // of 'a' with no delimiter in it (the overlap trim alone) 0.4 s. Both are now milliseconds; 1.5 s allows for a
        // slow machine.
        public void StructuredTextFormatter_LongDelimiter_IsLinear()
        {
            string d = new string('a', 16383) + "b";
            var f = new StructuredTextFormatter(new[] { LogField.Message, LogField.Category }, d);
            var sb = new StringBuilder();
            while (sb.Length < 100_000) sb.Append(d, 0, 1 + sb.Length % 16384).Append(sb.Length % 3 == 0 ? d : "b");
            string withDelimiter = sb.ToString(), plain = new string('a', 100_000);
            f.Format(Event("c", "warm-up"));
            var watch = Stopwatch.StartNew();
            string line = f.Format(Event("c", withDelimiter));
            string line2 = f.Format(Event("c", plain));
            watch.Stop();
            Check.True(watch.Elapsed < TimeSpan.FromSeconds(1.5), "took " + watch.Elapsed);
            Check.Equal(2, line.Split(new[] { d }, StringSplitOptions.None).Length);
            Check.Equal(2, line2.Split(new[] { d }, StringSplitOptions.None).Length);
        }

        // ---------- FirstChanceMonitor ----------

        // A clock that threw left the signature counted at 0 and the exception unreported; it now gives way to the system
        // clock, as Logger's does.
        public void FirstChanceMonitor_ThrowingClock_StillReports()
        {
            var reports = new List<FirstChanceRecord>();
            using var m = new FirstChanceMonitor(reports.Add, attach: false, clock: () => throw new TimeoutException("clock"));
            m.Observe(new TimeoutException("t"));
            Check.Equal(1, reports.Count);
            Check.Equal(1, reports[0].Occurrence);
            Check.Equal(1, m.Counts()["System.TimeoutException: t"]);
        }
    }
}
