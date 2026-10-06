using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Collections;
using ToolBelt.Diagnostics;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;
using ToolBelt.Time;

namespace ToolBelt.Tests.Text
{
    /// <summary>Regression tests for the 2026-10-05 review findings in Text/Cli/Threading/Time/Diagnostics/Collections.</summary>
    public sealed class MiscReviewRegressionTests
    {
        public async Task CronScheduler_DisposeFromInsideAJobCompletes()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 3, 2, 10, 0, 30, TimeSpan.Zero);
            CronScheduler? s = null;
            var disposed = new TaskCompletionSource<bool>();
            s = new CronScheduler(clock: () => now, delay: (t, ct) => Task.Delay(Timeout.Infinite, ct));
            s.Add("once", "* * * * *", async ct =>
            {
                await s.DisposeAsync();                                                   // "run once, then shut down"
                disposed.TrySetResult(true);
            });
            s.Start();
            now = now.AddMinutes(1);
            s.RunDue();
            Check.True(await Task.WhenAny(disposed.Task, Task.Delay(5000)) == disposed.Task, "DisposeAsync from inside the job hung");
            var outside = s.DisposeAsync();
            Check.True(await Task.WhenAny(outside, Task.Delay(5000)) == outside, "a later DisposeAsync hung");
            Check.Equal(0, s.RunDue());                                                   // nothing starts once disposed
        }

        public void PercentEncoding_NonBmpLiteralsAndLoneSurrogates()
        {
            Check.Equal("a😀b", PercentEncoding.Decode("a😀b"));
            Check.True(PercentEncoding.TryDecode("x%20😀", out string? r) && r == "x 😀");
            Check.False(PercentEncoding.TryDecode("bad\uD800", out _), "a lone surrogate is malformed input, not an exception");
            Check.Throws<FormatException>(() => PercentEncoding.Decode("bad\uDC00x"));
            Check.Throws<ArgumentException>(() => PercentEncoding.Encode("a\uD800"));
        }

        public async Task HealthCheck_BlockingAsyncProbeStillTimesOut()
        {
            var check = new HealthCheck()
                .Add("blocks", ct => { Thread.Sleep(1500); return Task.FromResult(ProbeResult.Healthy()); }, TimeSpan.FromMilliseconds(100))
                .Add("fast", ct => Task.FromResult(ProbeResult.Healthy()), TimeSpan.FromSeconds(5));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var report = await check.RunAsync();
            Check.True(sw.ElapsedMilliseconds < 1200, $"took {sw.ElapsedMilliseconds} ms");
            Check.True(report.Probes.Single(p => p.Name == "blocks").TimedOut);
            Check.Equal(HealthStatus.Healthy, report.Probes.Single(p => p.Name == "fast").Status);
        }

        public void TextAlign_SupplementaryWidths()
        {
            Check.Equal(2, TextAlign.DisplayWidth("\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F"));   // England flag
            Check.Equal(1, TextAlign.DisplayWidth("e\U000E0100"));                     // variation selector supplement
            Check.Equal(1, TextAlign.DisplayWidth("a\U00011001"));                     // Brahmi combining sign
            Check.Equal(2, TextAlign.DisplayWidth("😀"));
            Check.Equal(2, TextAlign.DisplayWidth("\U00020000"));                      // CJK Ext-B
            Check.Equal(1, TextAlign.DisplayWidth("\U0001D400"));                      // mathematical bold A: narrow
        }

        public void FirstChanceMonitor_SignaturesAreBounded()
        {
            using var m = new FirstChanceMonitor(_ => { }, attach: false);
            for (int i = 0; i < FirstChanceMonitor.MaxSignatures + 500; i++) m.Observe(new FormatException("bad value " + i));
            var counts = m.Counts();
            Check.True(counts.Count <= FirstChanceMonitor.MaxSignatures + 1, $"{counts.Count} signatures");
            Check.Equal(500, counts["System.FormatException: (other messages)"]);                 // the first 1000 fill the map
        }

        public void FirstChanceMonitor_ClockSteppingBackDoesNotMute()
        {
            var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var reports = new List<FirstChanceRecord>();
            using var m = new FirstChanceMonitor(reports.Add, attach: false, clock: () => t);
            m.Observe(new Exception("x"));
            t = t.AddHours(-1);                                                            // clock corrected backwards
            m.Observe(new Exception("x"));
            Check.Equal(2, reports.Count);
        }

        public void BestMatch_IgnoresNaNScores()
        {
            var scores = new Dictionary<string, double> { ["nan"] = double.NaN, ["good"] = 0.9 };
            var best = StringSimilarity.BestMatch("x", new[] { "nan", "good" }, (_, c) => scores[c]);
            Check.Equal("good", best!.Value.Value);
        }

        public void DataTable_DescendingWithMinValueComparer()
        {
            var t = new DataTableLite(new[] { "v" }).AddRow(1).AddRow(3).AddRow(2);
            // A comparer that returns int.MinValue for "less" — negating it would leave it unchanged.
            var odd = Comparer<object?>.Create((a, b) => { int x = (int)a!, y = (int)b!; return x < y ? int.MinValue : x > y ? 1 : 0; });
            var sorted = t.OrderByDescending("v", odd);
            Check.Equal("3,2,1", string.Join(",", Enumerable.Range(0, 3).Select(i => sorted[i, 0])));
        }
    }
}
