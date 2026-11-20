using System;
using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Process;
using ToolBelt.Resilience;
using ToolBelt.Runtime;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    // Round 165 (concurrency, tenth pass): each regression failed on the code before its fix (AtomicDouble's text on .NET
    // Framework only; the drain and slow-backlog tests are guards).
    public sealed class ConcurrencyReview165Tests
    {
        // ---- AtomicDouble.ToString round-trips ----

        // .NET Framework's "R" printed 0.84551240822557006 as "0.84551240822557", which reads back one ulp lower (4 in
        // 2,000,000 random values), and -0.0 as "0".
        public void AtomicDouble_TextReadsBackToTheSameBits()
        {
            var rng = new Random(165);
            var bytes = new byte[8];
            double[] fixedValues =
            {
                0.84551240822557006, -0.0, 0.0, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue,
                2.2250738585072009e-308, 2.2250738585072014e-308, 0.1 + 0.2, 1e23, 5e-324, 9007199254740993.0,
                double.PositiveInfinity, double.NegativeInfinity,
            };
            var values = fixedValues.Concat(Enumerable.Range(0, 300_000).Select(i =>
            {
                if (i % 2 == 0) return rng.NextDouble();
                rng.NextBytes(bytes);
                return BitConverter.ToDouble(bytes, 0);
            }));
            foreach (double v in values)
            {
                if (double.IsNaN(v)) continue;
                string s = new AtomicDouble(v).ToString();
                // .NET Framework parses "-0" as +0, so negative zero's text is checked as text.
                double back = s == "-0" ? -0.0 : double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (BitConverter.DoubleToInt64Bits(back) != BitConverter.DoubleToInt64Bits(v))
                    Check.Equal(BitConverter.DoubleToInt64Bits(v), BitConverter.DoubleToInt64Bits(back), v.ToString("G17", CultureInfo.InvariantCulture) + " printed " + s);
            }
            Check.Equal("-0", new AtomicDouble(-0.0).ToString(), "negative zero");
            Check.Equal("0", new AtomicDouble(0.0).ToString(), "zero");
            Check.Equal("NaN", new AtomicDouble(double.NaN).ToString());
        }

        // ---- StartupTiming.Report ----

        // A reference line: milliseconds to one decimal from whole ticks, rounded half away from zero, exactly (decimal).
        private static string Ms(long ticks, out bool negative)
        {
            decimal ms = Math.Round(Math.Abs((decimal)ticks) / 10000m, 1, MidpointRounding.AwayFromZero);
            negative = ticks < 0 && ms != 0;
            return ms.ToString("0.0", CultureInfo.InvariantCulture);
        }

        // The last name lost its trailing spaces to TrimEnd, and a clock that stepped back under 0.05 ms printed "-0.0"
        // on .NET Core ("0.0" on .NET Framework) with a "-" step of 0.0.
        public void StartupTiming_ReportMatchesAnExactReference()
        {
            var rng = new Random(1651);
            for (int trial = 0; trial < 2000; trial++)
            {
                var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                long now = 0;
                var timing = new StartupTiming(() => start.AddTicks(now));
                int n = rng.Next(1, 6);
                var expected = new System.Collections.Generic.List<string>();
                long previous = 0;
                for (int i = 0; i < n; i++)
                {
                    // Steps of every size, back and forward, many of them on a 0.05 ms boundary.
                    long step = rng.Next(4) switch
                    {
                        0 => rng.Next(-600, 600),
                        1 => (rng.Next(-20, 20) * 1000) + 500,
                        2 => rng.Next(-5_000_000, 50_000_000),
                        _ => (long)(rng.NextDouble() * 1e12),
                    };
                    now += step;
                    string name = (i == n - 1 && trial % 2 == 0) ? "last  " : "m" + i;
                    timing.Mark(name);
                    string total = Ms(now, out bool neg), stepText = Ms(now - previous, out bool stepNeg);
                    expected.Add(string.Format(CultureInfo.InvariantCulture, "{0,8} ms ({1}{2,7} ms)  {3}",
                        (neg ? "-" : "") + total, stepNeg ? "-" : "+", stepText, name));
                    previous = now;
                }
                Check.Equal(string.Join(Environment.NewLine, expected), timing.Report(), "trial " + trial);
            }
        }

        // ---- A cancelled token wins ----

        // A finished task's result came back through a cancelled token, where AsyncManualResetEvent and
        // AsyncCountdownEvent cancel.
        public void WithCancellation_ACancelledTokenWinsOverAFinishedTask()
        {
            var cancelled = new CancellationToken(true);
            Task<int> t = Task.FromResult(42).WithCancellation(cancelled);
            Check.True(t.IsCanceled, "status " + t.Status);
            Task<int> faulted = Task.FromException<int>(new InvalidOperationException("r165"));
            Check.True(faulted.WithCancellation(cancelled).IsCanceled, "faulted task");
            var ev = new AsyncManualResetEvent(true);
            Check.Equal(ev.WaitAsync(cancelled).IsCanceled, t.IsCanceled, "same rule as AsyncManualResetEvent");
            // A live token still gives the result.
            Check.Equal(42, Task.FromResult(42).WithCancellation(new CancellationTokenSource().Token).GetAwaiter().GetResult());
        }

        // An unpaused wait with a cancelled token succeeded.
        public void PauseToken_ACancelledTokenWinsWhenNotPaused()
        {
            var src = new PauseTokenSource();
            Check.True(src.Token.WaitWhilePausedAsync(new CancellationToken(true)).IsCanceled, "not paused");
            src.Pause();
            Check.True(src.Token.WaitWhilePausedAsync(new CancellationToken(true)).IsCanceled, "paused");
            src.Resume();
            Check.True(src.Token.WaitWhilePausedAsync().IsCompleted, "no token");
            Check.True(src.Token.WaitWhilePausedAsync(new CancellationTokenSource().Token).Status == TaskStatus.RanToCompletion, "live token");
        }

        // ---- ConcurrentPipeline wake-ups ----

        private static int SignalCount<T>(ConcurrentPipeline<T> p)
        {
            var f = typeof(ConcurrentPipeline<T>).GetField("_signal", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return ((SemaphoreSlim)f.GetValue(p)!).CurrentCount;
        }

        // One release per accepted item piled up while the consumer was busy (999 for 1,000 items), and at int.MaxValue
        // TryEnqueue threw SemaphoreFullException for an item it had queued.
        public void Pipeline_WakeUpsDoNotPileUpUnderABacklog()
        {
            foreach (var policy in new[] { PipelineOverflowPolicy.Block, PipelineOverflowPolicy.DropNewest, PipelineOverflowPolicy.DropOldest })
            {
                var gate = new ManualResetEventSlim(false);
                var started = new ManualResetEventSlim(false);
                var seen = new ConcurrentQueue<int>();
                using var p = new ConcurrentPipeline<int>((x, ct) =>
                {
                    seen.Enqueue(x);
                    started.Set();
                    gate.Wait(10_000);
                    return Task.CompletedTask;
                }, 2000, policy);
                Check.True(p.TryEnqueue(0), "first");
                Check.True(started.Wait(10_000), "consumer started");
                for (int i = 1; i <= 1000; i++) Check.True(p.TryEnqueue(i), "item " + i);
                Check.True(SignalCount(p) <= 1, policy + ": pending wake-ups " + SignalCount(p));
                gate.Set();
                Check.True(p.CompleteAsync().Wait(30_000), policy + " completes");
                Check.Equal(string.Join(",", Enumerable.Range(0, 1001)), string.Join(",", seen), policy + " order");
            }
        }

        // An item queued while the consumer drains is still consumed (the pending flag is cleared before the drain).
        public void Pipeline_ItemsQueuedDuringADrainAreConsumed()
        {
            var rng = new Random(1652);
            for (int trial = 0; trial < 200; trial++)
            {
                int count = 0;
                using var p = new ConcurrentPipeline<int>((x, ct) => { Interlocked.Increment(ref count); return Task.CompletedTask; },
                    rng.Next(1, 8));
                int n = rng.Next(1, 200);
                Parallel.For(0, n, i => p.TryEnqueue(i));
                Check.True(p.CompleteAsync().Wait(30_000), "trial " + trial);
                Check.Equal(n, Volatile.Read(ref count), "trial " + trial);
            }
        }

        // ---- A null task is the caller's bug ----

        private static bool IsNullTaskError(Exception? ex)
        {
            if (ex is AggregateException agg) return agg.InnerExceptions.Count == 1 && IsNullTaskError(agg.InnerExceptions[0]);
            return ex is InvalidOperationException && ex.Message.Contains("null task");
        }

        private static Exception? Caught(Func<Task> run)
        {
            try { run().GetAwaiter().GetResult(); return null; }
            catch (Exception ex) { return ex; }
        }

        // Each threw NullReferenceException: Retry retried it with backoff, and CircuitBreaker counted it as the
        // dependency failing, opening the breaker.
        public void NullTasks_AreReportedAsInvalidOperation()
        {
            int calls = 0;
            var policy = new RetryPolicy { MaxAttempts = 5, DelayAsync = (_, _) => Task.CompletedTask };
            var e = Caught(() => Retry.ExecuteAsync<int>(_ => { calls++; return null!; }, policy));
            Check.True(IsNullTaskError(e), "Retry<T>: " + e);
            Check.Equal(1, calls, "Retry<T> does not retry it");
            calls = 0;
            e = Caught(() => Retry.ExecuteAsync(_ => { calls++; return (Task)null!; }, policy));
            Check.True(IsNullTaskError(e), "Retry: " + e);
            Check.Equal(1, calls, "Retry does not retry it");

            var breaker = new CircuitBreaker(1, TimeSpan.FromMinutes(1));
            e = Caught(() => breaker.ExecuteAsync<int>(_ => null!));
            Check.True(IsNullTaskError(e), "CircuitBreaker<T>: " + e);
            e = Caught(() => breaker.ExecuteAsync(_ => (Task)null!));
            Check.True(IsNullTaskError(e), "CircuitBreaker: " + e);
            Check.Equal(CircuitState.Closed, breaker.State, "not counted as a failure");
            Check.Equal(0, breaker.ConsecutiveFailures, "failures");
            Check.Equal(7, breaker.ExecuteAsync(_ => Task.FromResult(7)).GetAwaiter().GetResult(), "still closed");

            using (var bulkhead = new Bulkhead(1))
            {
                Check.True(IsNullTaskError(Caught(() => bulkhead.ExecuteAsync<int>(_ => null!))), "Bulkhead<T>");
                Check.True(IsNullTaskError(Caught(() => bulkhead.ExecuteAsync(_ => (Task)null!))), "Bulkhead");
                Check.Equal(3, bulkhead.ExecuteAsync(_ => Task.FromResult(3)).GetAwaiter().GetResult(), "slot released");
            }

            Check.True(IsNullTaskError(Caught(() => ParallelUtils.ForEachAsync(new[] { 1 }, 2, (_, _) => null!))), "ForEachAsync");

            var ev = new AsyncEvent<int>();
            ev.Subscribe((_, _) => null!);
            Check.True(IsNullTaskError(Caught(() => ev.InvokeSequentialAsync(1))), "InvokeSequentialAsync");
            Check.True(IsNullTaskError(Caught(() => ev.InvokeParallelAsync(1))), "InvokeParallelAsync");
        }

        public void NullTasks_AreReportedThroughOnError()
        {
            var errors = new BlockingCollection<Exception>();
            using (var w = new PeriodicWorker(_ => null!, TimeSpan.FromMilliseconds(1), errors.Add))
            {
                w.Start();
                Check.True(errors.TryTake(out var ex, 10_000), "worker reported");
                Check.True(IsNullTaskError(ex), "work: " + ex);
            }
            var delayErrors = new BlockingCollection<Exception>();
            using (var w = new PeriodicWorker(_ => Task.CompletedTask, TimeSpan.FromMilliseconds(1), delayErrors.Add, (_, _) => null!))
            {
                w.Start();
                Check.True(delayErrors.TryTake(out var ex, 10_000), "delay reported");
                Check.True(IsNullTaskError(ex), "delay: " + ex);
            }
            var pipeErrors = new BlockingCollection<Exception>();
            using (var p = new ConcurrentPipeline<int>((_, _) => null!, 4, onError: pipeErrors.Add))
            {
                p.TryEnqueue(1);
                Check.True(pipeErrors.TryTake(out var ex, 10_000), "pipeline reported");
                Check.True(IsNullTaskError(ex), "consumer: " + ex);
            }
        }

        // ---- ParallelUtils.ForEachAsync and a late cancel ----

        // Every item finished, but a cancel that landed as the last body returned threw OperationCanceledException.
        public void ForEachAsync_ALateCancelAfterEveryItemFinishedIsNotReported()
        {
            for (int n = 1; n <= 8; n++)
            {
                using var cts = new CancellationTokenSource();
                int done = 0;
                ParallelUtils.ForEachAsync(Enumerable.Range(0, n), 3, (_, _) =>
                {
                    if (Interlocked.Increment(ref done) == n) cts.Cancel();
                    return Task.CompletedTask;
                }, cts.Token).GetAwaiter().GetResult();
                Check.Equal(n, done, "n " + n);
            }
            // Cut short, or cancelled up front: still OperationCanceledException.
            using (var cts = new CancellationTokenSource())
            {
                Check.ThrowsAsync<OperationCanceledException>(() => ParallelUtils.ForEachAsync(Enumerable.Range(0, 100), 1, (i, _) =>
                {
                    if (i == 3) cts.Cancel();
                    return Task.CompletedTask;
                }, cts.Token));
            }
            int ran = 0;
            Check.ThrowsAsync<OperationCanceledException>(() => ParallelUtils.ForEachAsync(new[] { 1 }, 1, (_, _) =>
            {
                ran++;
                return Task.CompletedTask;
            }, new CancellationToken(true)));
            Check.Equal(0, ran, "nothing runs with a cancelled token");
        }

        // ---- ProcessRunner and a stuck callback ----

        // With no timeout, Run waited for a callback however long it took (6 s here), and forever for one that never
        // returned, though the docs bound the wait to about a second.
        public void ProcessRunner_AStuckCallbackDoesNotHoldARunWithNoTimeout()
        {
            using var gate = new ManualResetEventSlim(false);
            try
            {
                var options = new ProcessRunOptions { OnOutputLine = _ => gate.Wait(8000) };
                var sw = Stopwatch.StartNew();
                ProcessRunner.Run("cmd.exe", "/c echo hi", options);
                Check.True(sw.ElapsedMilliseconds < 5000, "Run took " + sw.ElapsedMilliseconds + " ms");
                sw.Restart();
                ProcessRunner.RunAsync("cmd.exe", "/c echo hi", options).GetAwaiter().GetResult();
                Check.True(sw.ElapsedMilliseconds < 5000, "RunAsync took " + sw.ElapsedMilliseconds + " ms");
            }
            finally { gate.Set(); }
        }

        // A slow callback working through several lines is progress: none of the child's lines is lost.
        public void ProcessRunner_ASlowCallbackOverABacklogSeesEveryLine()
        {
            var seen = new ConcurrentQueue<string>();
            var options = new ProcessRunOptions { OnOutputLine = l => { Thread.Sleep(300); seen.Enqueue(l.Trim()); } };
            var r = ProcessRunner.Run("cmd.exe", "/c for /L %i in (1,1,6) do @echo line%i", options);
            Check.Equal(0, r.ExitCode, "exit");
            Check.Equal("line1,line2,line3,line4,line5,line6", string.Join(",", seen), "callback lines");
        }
    }
}
