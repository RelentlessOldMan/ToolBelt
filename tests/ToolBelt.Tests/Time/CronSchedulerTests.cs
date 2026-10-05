using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using ToolBelt.Time;

namespace ToolBelt.Tests.Time
{
    public sealed class CronSchedulerTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 3, 2, 10, 0, 30, TimeSpan.Zero);   // a Monday

        // A scheduler driven by hand: fake clock, and a delay that never returns (so the loop is idle).
        private static (CronScheduler Scheduler, Func<DateTimeOffset, DateTimeOffset> SetTime) Manual(List<(string, Exception)>? errors = null)
        {
            DateTimeOffset now = T0;
            var s = new CronScheduler((j, e) => { lock (errors!) errors.Add((j.Name, e)); }, () => now, (t, ct) => Task.Delay(Timeout.Infinite, ct));
            return (s, t => now = t);
        }

        private static async Task Settle(CronScheduler s)
        {
            for (int i = 0; i < 200 && Array.Exists(System.Linq.Enumerable.ToArray(s.Jobs), j => j.IsRunning); i++) await Task.Delay(5);
        }

        public async Task JobsRunWhenDue()
        {
            var (s, set) = Manual(new List<(string, Exception)>());
            int fifteen = 0, daily = 0;
            var a = s.Add("every 15", "*/15 * * * *", () => Interlocked.Increment(ref fifteen));
            var b = s.Add("2:30 weekdays", "30 2 * * 1-5", () => Interlocked.Increment(ref daily));
            s.Start();
            Check.Equal(new DateTimeOffset(2026, 3, 2, 10, 15, 0, TimeSpan.Zero), a.NextRun);
            Check.Equal(new DateTimeOffset(2026, 3, 3, 2, 30, 0, TimeSpan.Zero), b.NextRun);

            Check.Equal(0, s.RunDue());
            set(new DateTimeOffset(2026, 3, 2, 10, 15, 0, TimeSpan.Zero));
            Check.Equal(1, s.RunDue());
            await Settle(s);
            Check.Equal(1, fifteen);
            Check.Equal(1, a.RunCount);
            Check.Equal(new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero), a.NextRun);

            // Sleeping through many occurrences fires once, not a burst.
            set(new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero));
            Check.Equal(2, s.RunDue());
            await Settle(s);
            Check.Equal(2, fifteen);
            Check.Equal(1, daily);
            Check.Equal(new DateTimeOffset(2026, 3, 3, 9, 15, 0, TimeSpan.Zero), a.NextRun);
            await s.DisposeAsync();
        }

        public async Task OverlappingRunsAreSkipped()
        {
            var (s, set) = Manual(new List<(string, Exception)>());
            var gate = new TaskCompletionSource<bool>();
            int starts = 0;
            var job = s.Add("slow", "* * * * *", async _ => { Interlocked.Increment(ref starts); await gate.Task; });
            s.Start();
            set(T0.AddMinutes(1));
            s.RunDue();
            await Task.Delay(20);
            set(T0.AddMinutes(2));
            Check.Equal(0, s.RunDue());
            Check.Equal(1, s.Skipped);
            gate.SetResult(true);
            await Settle(s);
            set(T0.AddMinutes(3));
            Check.Equal(1, s.RunDue());
            await Settle(s);
            Check.Equal(2, starts);
            Check.Equal(2, job.RunCount);
            await s.DisposeAsync();
        }

        public async Task ErrorsAreReportedAndDoNotStopTheJob()
        {
            var errors = new List<(string, Exception)>();
            var (s, set) = Manual(errors);
            var job = s.Add("flaky", "* * * * *", () => throw new InvalidOperationException("nope"));
            s.Start();
            for (int m = 1; m <= 3; m++) { set(T0.AddMinutes(m)); s.RunDue(); await Settle(s); }
            Check.Equal(3, errors.Count);
            Check.Equal("flaky", errors[0].Item1);
            Check.Equal(3, job.RunCount);
            await s.DisposeAsync();
        }

        public async Task DisposeCancelsAndWaitsForRunningJobs()
        {
            var (s, set) = Manual(new List<(string, Exception)>());
            bool sawCancel = false, finished = false;
            s.Add("long", "* * * * *", async ct =>
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { sawCancel = true; await Task.Delay(30); finished = true; throw; }
            });
            s.Start();
            set(T0.AddMinutes(1));
            s.RunDue();
            await Task.Delay(20);
            await s.DisposeAsync();
            Check.True(sawCancel && finished, "the running job saw cancellation and was awaited");
        }

        public async Task RealLoopUsesTheDelayToWake()
        {
            DateTimeOffset now = T0;
            var delays = new List<TimeSpan>();
            var ran = new TaskCompletionSource<bool>();
            var s = new CronScheduler(clock: () => now, delay: async (t, ct) =>
            {
                lock (delays) delays.Add(t);
                now += t;                                       // the fake delay advances the fake clock
                await Task.Yield();
            });
            s.Add("tick", "5 10 * * *", () => ran.TrySetResult(true));
            s.Start();
            Check.True(await Task.WhenAny(ran.Task, Task.Delay(5000)) == ran.Task, "job ran");
            await s.DisposeAsync();
            lock (delays) Check.True(delays.TrueForAll(d => d <= TimeSpan.FromMinutes(1)), "waits are capped at a minute");
            Check.Throws<InvalidOperationException>(() => s.Start());
        }

        public void Validation()
        {
            var s = new CronScheduler();
            Check.Throws<FormatException>(() => s.Add("bad", "* * *", () => { }));
            var j = s.Add("x", "0 0 * * *", () => { });
            Check.Null(j.NextRun);
            Check.True(s.Remove(j));
            s.Dispose();
        }
    }
}
