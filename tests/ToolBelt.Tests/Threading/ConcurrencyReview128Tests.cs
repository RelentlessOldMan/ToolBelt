using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Process;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    /// <summary>Regressions from review round 128 (concurrency, eighth pass): each test failed before its fix.</summary>
    public sealed class ConcurrencyReview128Tests
    {
        private sealed class FakeClock
        {
            public DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public Func<DateTimeOffset> Func => () => Now;
        }

        // ---------------- TokenBucketRateLimiter: polling a large bucket ----------------

        public void TokenBucket_PollingDoesNotLoseRefill()
        {
            // 1e9 tokens: each 1 ms poll adds 1e-6, which rounded at that size and lost 4.6% of the rate.
            var clock = new FakeClock();
            var small = new TokenBucketRateLimiter(1e9, 1e-3, clock.Func);
            Check.True(small.TryAcquire(1));
            for (int ms = 0; ms < 1_000_000; ms++)
            {
                clock.Now += TimeSpan.FromMilliseconds(1);
                _ = small.AvailableTokens;
            }
            Check.Equal(1e9, small.AvailableTokens);
            Check.True(small.TryAcquire(1));

            // 1e16 tokens: one token a millisecond was lost whole each time (the spacing there is 2).
            clock = new FakeClock();
            var big = new TokenBucketRateLimiter(1e16, 1000, clock.Func);
            Check.True(big.TryAcquire(int.MaxValue));
            double start = big.AvailableTokens;
            for (int ms = 0; ms < 100_000; ms++)
            {
                clock.Now += TimeSpan.FromMilliseconds(1);
                _ = big.AvailableTokens;
            }
            double gained = big.AvailableTokens - start;
            Check.True(Math.Abs(gained - 100_000) <= 4, "gained " + gained);

            // A clock stepped back keeps the refill up to the latest time seen, then refills from the new time.
            clock = new FakeClock();
            var t = new TokenBucketRateLimiter(10, 1, clock.Func);
            Check.True(t.TryAcquire(10));
            clock.Now += TimeSpan.FromSeconds(5);
            Check.Equal(5.0, t.AvailableTokens);
            clock.Now -= TimeSpan.FromHours(1);
            Check.Equal(5.0, t.AvailableTokens);
            clock.Now += TimeSpan.FromSeconds(2);
            Check.Equal(7.0, t.AvailableTokens);
            clock.Now += TimeSpan.FromHours(2);
            Check.Equal(10.0, t.AvailableTokens);
            Check.True(t.TryAcquire(4));
            clock.Now += TimeSpan.FromSeconds(1);
            Check.Equal(7.0, t.AvailableTokens);
        }

        // ---------------- WhichExe: a trailing dot, as the launcher reads it ----------------

        private static bool Launches(string name)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new ProcessStartInfo(name, "/c exit 7")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
                Check.True(p.WaitForExit(30_000), name + " did not exit");
                return true;
            }
            catch (Win32Exception) { return false; }
        }

        public void WhichExe_ATrailingDotAgreesWithTheLauncher()
        {
            if (!TestPlatform.IsWindows) return;
            string? cmd = WhichExe.Find("cmd.exe");
            Check.True(cmd != null, "cmd.exe is on the PATH");
            Check.Equal(cmd!.ToUpperInvariant(), WhichExe.Find("cmd.exe.")?.ToUpperInvariant());
            Check.True(WhichExe.Find("cmd.") is null, "cmd. names a file cmd, not cmd.exe");
            Check.True(WhichExe.Find(cmd.Substring(0, cmd.Length - 4) + ".") is null, "the same with a directory");
            Check.Equal(cmd.ToUpperInvariant(), WhichExe.Find(cmd + ".")?.ToUpperInvariant());
            foreach (string name in new[] { "cmd.exe.", "cmd.", "cmd", "cmd.exe" })
                Check.Equal(Launches(name), WhichExe.Find(name) != null, name);
        }

        // ---------------- RetryPolicy.DelayForAttempt: the cap against the exact product ----------------

        public void Retry_TheCapIsComparedExactly()
        {
            var p = new RetryPolicy
            {
                Backoff = BackoffStrategy.Linear,
                BaseDelay = TimeSpan.FromTicks((1L << 62) - 100),
                MaxDelay = TimeSpan.FromTicks(long.MaxValue - 1),
            };
            Check.Equal(TimeSpan.FromTicks(long.MaxValue - 199), p.DelayForAttempt(2));
            var rng = new Random(128);
            for (int trial = 0; trial < 20_000; trial++)
            {
                var strategy = (BackoffStrategy)rng.Next(3);
                long baseTicks = rng.Next(4) == 0 ? rng.Next(1, 1000) : (long)(rng.NextDouble() * long.MaxValue / Math.Pow(2, rng.Next(0, 63)));
                if (baseTicks <= 0) baseTicks = 1;
                long capTicks = rng.Next(3) == 0 ? long.MaxValue : Math.Max(baseTicks, (long)(rng.NextDouble() * long.MaxValue));
                int attempt = rng.Next(1, 70);
                var policy = new RetryPolicy { Backoff = strategy, BaseDelay = TimeSpan.FromTicks(baseTicks), MaxDelay = TimeSpan.FromTicks(capTicks) };
                BigInteger factor = strategy == BackoffStrategy.Constant ? 1 : strategy == BackoffStrategy.Linear ? attempt : BigInteger.Pow(2, attempt - 1);
                BigInteger exact = BigInteger.Min(capTicks, baseTicks * factor);
                Check.Equal(TimeSpan.FromTicks((long)exact), policy.DelayForAttempt(attempt), strategy + " " + baseTicks + " x" + attempt + " cap " + capTicks);
            }
        }
        // ---------------- ConcurrentPipeline: a continuation run inline on the consumer's thread ----------------

        public void Pipeline_DisposeFromAnInlineContinuationDoesNotHang()
        {
            var processed = new TaskCompletionSource<bool>();   // default: continuations run inline
            var pipe = new ConcurrentPipeline<int>((i, ct) => { processed.TrySetResult(true); return Task.CompletedTask; }, 4);
            var disposed = processed.Task.ContinueWith(_ => pipe.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
            pipe.TryEnqueue(1);
            Check.True(disposed.Wait(10_000), "Dispose from the inline continuation hung");
            Check.True(disposed.Status == TaskStatus.RanToCompletion, disposed.Exception?.ToString() ?? "");
        }

        public void Pipeline_AFullEnqueueFromAnInlineContinuationThrows()
        {
            var processed = new TaskCompletionSource<bool>();
            using var release = new ManualResetEventSlim();
            var pipe = new ConcurrentPipeline<int>((i, ct) =>
            {
                if (i == 1) { release.Wait(10_000); processed.TrySetResult(true); }
                return Task.CompletedTask;
            }, 1);
            try
            {
                Exception? seen = null;
                var enqueued = processed.Task.ContinueWith(_ =>
                {
                    try { pipe.TryEnqueue(4); } catch (Exception ex) { seen = ex; }
                }, TaskContinuationOptions.ExecuteSynchronously);
                pipe.TryEnqueue(1);
                pipe.TryEnqueue(2);   // waits for item 1 to leave the buffer, then fills the only slot
                // Item 1 is in the callback and item 2 holds the only slot.
                release.Set();
                Check.True(enqueued.Wait(10_000), "TryEnqueue from the inline continuation waited");
                Check.True(seen is InvalidOperationException, seen?.ToString() ?? "no exception");
            }
            finally { release.Set(); pipe.Dispose(); }
        }

        // ---------------- AsyncEvent.InvokeParallelAsync: synchronous handlers run concurrently ----------------

        public void AsyncEvent_ParallelHandlersRunConcurrently()
        {
            var ev = new AsyncEvent<int>();
            using var barrier = new Barrier(3);
            int met = 0;
            for (int h = 0; h < 3; h++)
                ev.Subscribe((x, ct) =>
                {
                    if (barrier.SignalAndWait(10_000)) Interlocked.Increment(ref met);   // synchronous, no await
                    return Task.CompletedTask;
                });
            var call = Task.Run(() => ev.InvokeParallelAsync(1));
            Check.True(call.Wait(30_000), "InvokeParallelAsync hung");
            Check.Equal(3, met);
        }

        // ---------------- TaskRace: a synchronous attempt does not hold up the others ----------------

        public void TaskRace_ASynchronousAttemptDoesNotHoldUpTheRest()
        {
            // The race waits for its losers, so it ends when the slow attempt does; the fast one must win all the same.
            using var gate = new ManualResetEventSlim();
            int fastRan = 0;
            var slow = new Func<CancellationToken, Task<int>>(ct => { gate.Wait(3_000); return Task.FromResult(1); });
            var fast = new Func<CancellationToken, Task<int>>(ct => { Volatile.Write(ref fastRan, 1); return Task.FromResult(2); });
            var race = TaskRace.FirstToComplete(new[] { slow, fast });
            Check.True(SpinWait.SpinUntil(() => Volatile.Read(ref fastRan) == 1, 5_000), "the fast attempt never ran");
            gate.Set();
            Check.Equal(2, race.GetAwaiter().GetResult());

            using var gate2 = new ManualResetEventSlim();
            int started = 0;
            var hedged = TaskRace.Hedged((n, ct) =>
            {
                Interlocked.Increment(ref started);
                if (n == 0) gate2.Wait(3_000);
                return Task.FromResult(n);
            }, TimeSpan.FromMilliseconds(20), 2);
            Check.True(SpinWait.SpinUntil(() => Volatile.Read(ref started) == 2, 5_000), "the hedge never started");
            gate2.Set();
            Check.Equal(1, hedged.GetAwaiter().GetResult());
        }
    }
}
