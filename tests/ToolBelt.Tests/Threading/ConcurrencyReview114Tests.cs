using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    // Round 114 (concurrency, seventh pass): each regression failed on the code before its fix (the hangs by a bounded
    // wait, so the old code fails rather than stalling the run).
    public sealed class ConcurrencyReview114Tests
    {
        // ---- Retry: the caller's own cancellation ----

        public void Retry_TheCallersCancelIsNotRetried()
        {
            // The operation cancels the caller's token and throws for it: that went through ShouldRetry and a backoff, and
            // came out as the delay's TaskCanceledException, or waited out a delay that ignored the token.
            foreach (bool customDelay in new[] { false, true })
            {
                using var cts = new CancellationTokenSource();
                int asked = 0, delayed = 0;
                var policy = new RetryPolicy { MaxAttempts = 5, BaseDelay = TimeSpan.FromSeconds(5), ShouldRetry = _ => { asked++; return true; } };
                if (customDelay) policy.DelayAsync = (d, ct) => { delayed++; return Task.Delay(d); };
                OperationCanceledException? thrown = null;
                Task<int> run = Retry.ExecuteAsync<int>(ct =>
                {
                    cts.Cancel();
                    thrown = new OperationCanceledException(ct);
                    throw thrown;
                }, policy, cts.Token);
                Check.True(((IAsyncResult)run).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)), $"custom {customDelay}: waited out the backoff");
                var ex = Check.Throws<OperationCanceledException>(() => run.GetAwaiter().GetResult());
                Check.True(ReferenceEquals(thrown, ex), $"custom {customDelay}: {ex.GetType().Name} replaced the operation's exception");
                Check.Equal(0, asked);
                Check.Equal(0, delayed);
            }
        }

        public void Retry_AFailureAfterTheCallersCancelIsACancel()
        {
            // An operation that fails some other way once the caller cancelled comes out as a cancellation for that token,
            // without ShouldRetry or a backoff (it waited out a delay that ignored the token).
            using var cts = new CancellationTokenSource();
            int asked = 0, delayed = 0;
            var policy = new RetryPolicy
            {
                MaxAttempts = 5,
                BaseDelay = TimeSpan.FromSeconds(5),
                ShouldRetry = _ => { asked++; return true; },
                DelayAsync = (d, ct) => { delayed++; return Task.Delay(d); },
            };
            Task<int> run = Retry.ExecuteAsync<int>(ct => { cts.Cancel(); throw new InvalidOperationException("transient"); }, policy, cts.Token);
            Check.True(((IAsyncResult)run).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)), "waited out the backoff");
            var ex = Check.Throws<OperationCanceledException>(() => run.GetAwaiter().GetResult());
            Check.Equal(cts.Token, ex.CancellationToken);
            Check.Equal(0, asked);
            Check.Equal(0, delayed);
        }

        public void Retry_FailuresBeforeACancelAreStillRetried()
        {
            // Guard: without a cancel the policy still decides, and a cancel after a failure stops at the next attempt.
            int calls = 0;
            var policy = new RetryPolicy { MaxAttempts = 4, BaseDelay = TimeSpan.Zero };
            int result = Retry.ExecuteAsync(ct => ++calls < 3 ? throw new InvalidOperationException() : Task.FromResult(calls), policy).GetAwaiter().GetResult();
            Check.Equal(3, result);
        }

        // ---- ConcurrentPipeline: work the consumer started ----

        public void Pipeline_CompleteFromATaskTheConsumerStartedWaitsForTheDrain()
        {
            // The consumer mark flowed into the task, so CompleteAsync returned at once with 0 of 5 items processed.
            var seen = new ConcurrentQueue<int>();
            Task? completion = null;
            ConcurrentPipeline<int>? p = null;
            p = new ConcurrentPipeline<int>(async (item, ct) =>
            {
                if (item == 0)
                {
                    completion = Task.Run(async () => { await Task.Delay(50); await p!.CompleteAsync(); });
                    return;
                }
                await Task.Delay(60);
                seen.Enqueue(item);
            }, 8);
            try
            {
                for (int i = 0; i <= 5; i++) p.TryEnqueue(i);
                var sw = Stopwatch.StartNew();
                while (completion is null && sw.Elapsed < TimeSpan.FromSeconds(10)) Thread.Yield();
                Check.True(completion!.Wait(TimeSpan.FromSeconds(10)), "CompleteAsync hung");
                Check.Equal("1,2,3,4,5", string.Join(",", seen));
            }
            finally { Task.Run(() => p.Dispose()).Wait(TimeSpan.FromSeconds(10)); }
        }

        public void Pipeline_DisposeFromATaskTheConsumerStartedWaitsForTheConsumer()
        {
            // Dispose returned while the callback was still running and left its resources undisposed; a later Dispose was a
            // no-op, so nothing could get the documented wait.
            int finished = 0;
            Task? disposing = null;
            var started = new ManualResetEventSlim();
            ConcurrentPipeline<int>? p = null;
            p = new ConcurrentPipeline<int>((item, ct) =>
            {
                if (item == 0)
                {
                    disposing = Task.Run(() => { started.Wait(); p!.Dispose(); });
                    return Task.CompletedTask;
                }
                started.Set();
                Thread.Sleep(300);                                           // ignores the token
                Volatile.Write(ref finished, 1);
                return Task.CompletedTask;
            }, 8);
            p.TryEnqueue(0);
            p.TryEnqueue(1);
            Check.True(started.Wait(TimeSpan.FromSeconds(10)), "item 1 never started");
            var sw = Stopwatch.StartNew();
            while (disposing is null && sw.Elapsed < TimeSpan.FromSeconds(10)) Thread.Yield();
            Check.True(disposing!.Wait(TimeSpan.FromSeconds(10)), "Dispose hung");
            Check.Equal(1, Volatile.Read(ref finished));
            var ex = Check.Throws<ObjectDisposedException>(() => p.TryEnqueue(2));
            Check.Equal("ConcurrentPipeline`1", ex.ObjectName);
        }

        public void Pipeline_DisposeFromTheCallbackStillDoesNotWait()
        {
            // Guard: from the callback itself Dispose still returns at once (waiting would wait on the caller).
            var done = new ManualResetEventSlim();
            ConcurrentPipeline<int>? p = null;
            p = new ConcurrentPipeline<int>(async (item, ct) =>
            {
                await Task.Yield();
                p!.Dispose();
                done.Set();
            }, 4);
            p.TryEnqueue(1);
            Check.True(done.Wait(TimeSpan.FromSeconds(10)), "Dispose from the callback hung");
            Check.Throws<ObjectDisposedException>(() => p.TryEnqueue(2));
        }

        // ---- ConcurrentPipeline: the consumer enqueuing into a full pipeline ----

        public void Pipeline_TheConsumerFillingAFullPipelineThrowsInsteadOfHanging()
        {
            // Capacity 1 and the consumer re-queues two items: the second waited for a slot only the consumer could free,
            // forever, and CompleteAsync hung behind it. The same from onError.
            foreach (bool fromOnError in new[] { false, true })
            {
                var seen = new ConcurrentQueue<int>();
                Exception? refused = null;
                ConcurrentPipeline<int>? p = null;
                void Requeue()
                {
                    p!.TryEnqueue(10);
                    try { p.TryEnqueue(11); }
                    catch (Exception ex) { refused = ex; }
                }
                p = new ConcurrentPipeline<int>((item, ct) =>
                {
                    if (item == 0)
                    {
                        if (fromOnError) throw new InvalidOperationException("boom");
                        Requeue();
                    }
                    else seen.Enqueue(item);
                    return Task.CompletedTask;
                }, 1, PipelineOverflowPolicy.Block, onError: _ => Requeue());
                try
                {
                    p.TryEnqueue(0);
                    Check.True(SpinWait.SpinUntil(() => Volatile.Read(ref refused) != null, TimeSpan.FromSeconds(10)), $"onError {fromOnError}: the consumer hung waiting for space");
                    Check.True(refused is InvalidOperationException && refused.Message.Contains("full"), refused!.ToString());
                    Check.True(p.CompleteAsync().Wait(TimeSpan.FromSeconds(10)), $"onError {fromOnError}: CompleteAsync hung");
                    Check.Equal("10", string.Join(",", seen));
                }
                finally { Task.Run(() => p.Dispose()).Wait(TimeSpan.FromSeconds(10)); }
            }
        }

        public void Pipeline_TheConsumerCanStillRequeueWithSpace()
        {
            // Guard: a consumer re-queuing into a pipeline with room is not refused.
            var seen = new ConcurrentQueue<int>();
            ConcurrentPipeline<int>? p = null;
            p = new ConcurrentPipeline<int>((item, ct) =>
            {
                seen.Enqueue(item);
                if (item < 5) p!.TryEnqueue(item + 1);
                return Task.CompletedTask;
            }, 2);
            p.TryEnqueue(0);
            Check.True(SpinWait.SpinUntil(() => seen.Count == 6, TimeSpan.FromSeconds(10)), string.Join(",", seen));
            Check.True(p.CompleteAsync().Wait(TimeSpan.FromSeconds(10)), "CompleteAsync hung");
            Check.Equal("0,1,2,3,4,5", string.Join(",", seen));
            p.Dispose();
        }

        // ---- ConcurrentPipeline: calls after Dispose ----

        public void Pipeline_CompletingADrainedThenDisposedPipelineSucceeds()
        {
            // The second CompleteAsync threw the semaphore's ObjectDisposedException; the buffer had drained before Dispose.
            var p = new ConcurrentPipeline<int>((item, ct) => Task.CompletedTask, 4);
            p.TryEnqueue(1);
            Check.True(p.CompleteAsync().Wait(TimeSpan.FromSeconds(10)), "CompleteAsync hung");
            p.Dispose();
            Check.True(p.CompleteAsync().Wait(TimeSpan.FromSeconds(10)), "second CompleteAsync hung");
        }

        public void Pipeline_EnqueueAfterDisposeNamesThePipeline()
        {
            // Under Block the disposed source's Token threw ObjectDisposedException with no name; the drop policies named it.
            foreach (var policy in new[] { PipelineOverflowPolicy.Block, PipelineOverflowPolicy.DropNewest, PipelineOverflowPolicy.DropOldest })
            {
                var p = new ConcurrentPipeline<int>((item, ct) => Task.CompletedTask, 4, policy);
                p.Dispose();
                var ex = Check.Throws<ObjectDisposedException>(() => p.TryEnqueue(1), policy.ToString());
                Check.Equal("ConcurrentPipeline`1", ex.ObjectName);
            }
        }

        public void Pipeline_ProducersBlockedDuringDisposeAlwaysGetObjectDisposed()
        {
            // On .NET Framework the semaphore, disposed under a waiting producer, threw ArgumentNullException (4 runs in 25).
            var errors = new ConcurrentDictionary<string, int>();
            for (int run = 0; run < 150; run++)
            {
                var pipeline = new ConcurrentPipeline<int>((_, ct) => Task.Delay(Timeout.Infinite, ct), 1);
                var producers = Enumerable.Range(0, 3).Select(_ => new Thread(() =>
                {
                    try { for (int i = 0; i < 10; i++) pipeline.TryEnqueue(i); errors.AddOrUpdate("none", 1, (k, v) => v + 1); }
                    catch (Exception ex) { errors.AddOrUpdate(ex.GetType().Name, 1, (k, v) => v + 1); }
                }) { IsBackground = true }).ToList();
                producers.ForEach(t => t.Start());
                Check.True(SpinWait.SpinUntil(() => producers.All(t => (t.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0), TimeSpan.FromSeconds(10)), "the producers never blocked");
                if (run % 2 == 0) Thread.Sleep(1);
                pipeline.Dispose();
                Check.True(producers.All(t => t.Join(10000)), "a producer never woke");
            }
            Check.Equal("ObjectDisposedException=450", string.Join(",", errors.Select(kv => kv.Key + "=" + kv.Value)));
        }

        // ---- TaskRace: many attempts ----

        public void Race_ManyFailingAttemptsTakeLinearTime()
        {
            // A WhenAny over every pending attempt per round: 40000 failing attempts took 11 s.
            const int n = 40000;
            var attempts = Enumerable.Range(0, n).Select(i => (Func<CancellationToken, Task<int>>)(async ct =>
            {
                await Task.Yield();
                throw new InvalidOperationException(i.ToString());
            })).ToList();
            var sw = Stopwatch.StartNew();
            var ex = Check.Throws<AggregateException>(() => TaskRace.FirstSuccessful(attempts).GetAwaiter().GetResult());
            Check.Equal(n, ex.InnerExceptions.Count);
            var ex2 = Check.Throws<AggregateException>(() => TaskRace.FirstMatching(attempts, _ => true).GetAwaiter().GetResult());
            Check.Equal(n, ex2.InnerExceptions.Count);
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"{sw.Elapsed.TotalSeconds:F1} s for two races of {n}");
        }

        public void Hedged_ManyAttemptsTakeLinearTime()
        {
            // Every tick copied the running list into a new WhenAny: 8000 hedges took 3.9 s.
            const int n = 20000;
            var sw = Stopwatch.StartNew();
            int result = TaskRace.Hedged<int>(async (i, ct) =>
            {
                if (i == n - 1) return i;
                await Task.Delay(Timeout.Infinite, ct);
                return -1;
            }, TimeSpan.Zero, n).GetAwaiter().GetResult();
            Check.Equal(n - 1, result);
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"{sw.Elapsed.TotalSeconds:F1} s for {n} hedges");
        }

        public void Race_ResultsFollowCompletionOrder()
        {
            // Guard: against a model, with attempts finished one at a time in a random order. The winner is the first to
            // succeed (and match), and when none does, the failures come in the order they finished. The sources run their
            // continuations inline, so each completion is handed to the race before the next is set.
            var rng = new Random(114);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = rng.Next(1, 12);
                var sources = Enumerable.Range(0, n).Select(_ => new TaskCompletionSource<int>()).ToArray();
                var outcome = Enumerable.Range(0, n).Select(_ => rng.Next(3)).ToArray();   // 0 fail, 1 reject, 2 accept
                var order = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).ToArray();
                bool matching = rng.Next(2) == 0;
                var attempts = sources.Select(s => (Func<CancellationToken, Task<int>>)(ct => s.Task)).ToList();
                Task<int> race = matching ? TaskRace.FirstMatching(attempts, v => v % 2 == 0) : TaskRace.FirstSuccessful(attempts);
                int? want = null;
                var failures = new List<string>();
                foreach (int i in order)
                {
                    if (outcome[i] == 0) sources[i].SetException(new InvalidOperationException("f" + i));
                    else sources[i].SetResult(outcome[i] == 2 ? 2 * i : 2 * i + 1);
                    if (want is null)
                    {
                        if (outcome[i] == 0) failures.Add("f" + i);
                        else if (!matching || outcome[i] == 2) want = outcome[i] == 2 ? 2 * i : 2 * i + 1;
                        else failures.Add("Result rejected by the predicate.");
                    }
                }
                Check.True(((IAsyncResult)race).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(10)), $"trial {trial} hung");
                if (want is int w) Check.Equal(w, race.Result, $"trial {trial}");
                else
                {
                    var ex = Check.Throws<AggregateException>(() => race.GetAwaiter().GetResult());
                    Check.Equal(string.Join("|", failures), string.Join("|", ex.InnerExceptions.Select(e => e.Message)), $"trial {trial}");
                }
            }
        }
    }
}
