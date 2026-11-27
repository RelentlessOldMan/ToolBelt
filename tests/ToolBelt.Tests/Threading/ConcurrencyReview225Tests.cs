using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    // Round 225 (concurrency, fourteenth pass): each regression failed on the code before its fix.
    public sealed class ConcurrencyReview225Tests
    {
        // ---- Retry ----

        // A custom DelayAsync that threw, or returned a faulted task, came out alone: the operation's exception was lost
        // (R114-5), where a null delay task kept it as the inner exception. Graded over random failures: the operation's
        // exception comes out first, the delay's second, in every way a delay can fail.
        public async Task Retry_FailingDelay_KeepsTheOperationsException()
        {
            var rng = new Random(225);
            for (int trial = 0; trial < 200; trial++)
            {
                var opEx = new TimeoutException("op " + trial);
                var delayEx = trial % 3 == 0 ? new OperationCanceledException("own timeout") : (Exception)new FormatException("delay " + trial);
                bool sync = rng.Next(2) == 0;
                int fails = rng.Next(1, 4), calls = 0;
                var policy = new RetryPolicy
                {
                    MaxAttempts = fails + 2,
                    DelayAsync = (d, ct) => sync ? throw delayEx : Task.FromException(delayEx),
                };
                var agg = await Check.ThrowsAsync<AggregateException>(() => Retry.ExecuteAsync<int>(ct =>
                {
                    calls++;
                    return Task.FromException<int>(opEx);
                }, policy));
                Check.Equal(2, agg.InnerExceptions.Count, "trial " + trial);
                Check.True(ReferenceEquals(opEx, agg.InnerExceptions[0]), "trial " + trial + ": the operation's first");
                Check.True(ReferenceEquals(delayEx, agg.InnerExceptions[1]), "trial " + trial + ": the delay's second");
                Check.Equal(1, calls, "no attempt after the delay failed");
            }
        }

        // Guard: the caller's cancellation during a delay still comes out as the delay's cancellation, not wrapped.
        public async Task Retry_CancelledDuringTheDelay_IsACancellation()
        {
            using var cts = new CancellationTokenSource();
            var policy = new RetryPolicy
            {
                MaxAttempts = 3,
                DelayAsync = (d, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            };
            var ex = await Check.ThrowsAsync<OperationCanceledException>(() =>
                Retry.ExecuteAsync<int>(ct => Task.FromException<int>(new TimeoutException()), policy, cts.Token));
            Check.Equal(cts.Token, ex.CancellationToken);
        }

        // ---- AsyncLazy ----

        // Lazy<T>'s lock was held for the factory's synchronous part, so a second caller of Value blocked until the
        // factory returned its task (R114-4): 800 ms of synchronous start blocked every other caller 800 ms.
        public void AsyncLazy_SecondCaller_DoesNotWaitForTheFactorysSynchronousPart()
        {
            using var inFactory = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var tcs = new TaskCompletionSource<int>();
            int runs = 0;
            var lazy = new AsyncLazy<int>(() =>
            {
                Interlocked.Increment(ref runs);
                inFactory.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                return tcs.Task;
            });
            Task<int>? first = null;
            var starter = new Thread(() => first = lazy.Value) { IsBackground = true };
            starter.Start();
            Check.True(inFactory.Wait(TimeSpan.FromSeconds(30)), "the factory started");
            var sw = Stopwatch.StartNew();
            Task<int>? second = null;
            var reader = new Thread(() => second = lazy.Value) { IsBackground = true };
            reader.Start();
            bool returned = reader.Join(TimeSpan.FromSeconds(5));
            release.Set();
            Check.True(returned, $"the second caller was still blocked after {sw.ElapsedMilliseconds} ms");
            Check.True(lazy.IsStarted);
            Check.False(second!.IsCompleted);
            tcs.SetResult(42);
            Check.Equal(42, second.GetAwaiter().GetResult());
            Check.True(starter.Join(TimeSpan.FromSeconds(30)), "the first caller returned");
            Check.True(ReferenceEquals(second, first), "one shared task");
            Check.Equal(1, runs);
        }

        // Guards for the published task: many racing callers share one task and one run, each outcome (value, fault,
        // cancellation with its own exception and token, a throw before the first await) comes through as the factory's own task gives it, and a
        // factory that reads Value before returning faults rather than hanging.
        public void AsyncLazy_RacingCallers_ShareOneRunAndItsOutcome()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            for (int trial = 0; trial < 400; trial++)
            {
                int kind = trial % 4, runs = 0;
                var fault = new FormatException("f" + trial);
                var lazy = new AsyncLazy<int>(async () =>
                {
                    Interlocked.Increment(ref runs);
                    if (trial % 8 < 4) await Task.Yield();
                    if (kind == 1) throw fault;
                    if (kind == 2) cts.Token.ThrowIfCancellationRequested();
                    if (kind == 3) throw new InvalidOperationException("sync");
                    return trial;
                });
                var tasks = new Task<int>[8];
                Parallel.For(0, tasks.Length, i => tasks[i] = lazy.Value);
                foreach (var t in tasks) Check.True(ReferenceEquals(tasks[0], t), "trial " + trial + ": one task");
                try { ((IAsyncResult)tasks[0]).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(30)); } catch (ObjectDisposedException) { }
                Check.Equal(1, runs, "trial " + trial);
                if (kind == 0) Check.Equal(trial, tasks[0].Result);
                if (kind == 1) Check.True(ReferenceEquals(fault, tasks[0].Exception!.InnerException), "trial " + trial);
                if (kind == 2)
                {
                    Check.True(tasks[0].IsCanceled, "trial " + trial);
                    var oce = Check.Throws<OperationCanceledException>(() => tasks[0].GetAwaiter().GetResult());
                    Check.Equal(cts.Token, oce.CancellationToken);
                }
                if (kind == 3) Check.Equal("sync", tasks[0].Exception!.InnerException!.Message);
            }

            AsyncLazy<int>? self = null;
            self = new AsyncLazy<int>(() => self!.Value);
            var reentrant = self.Value;
            Check.True(reentrant.IsFaulted);
            Check.True(reentrant.Exception!.InnerException is InvalidOperationException);
        }
    }
}
