using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    public sealed class PauseAndRaceTests
    {
        // ---------- pause ----------

        public async Task Pause_BlocksUntilResumed()
        {
            var src = new PauseTokenSource();
            var token = src.Token;
            await token.WaitWhilePausedAsync();                                      // running: immediate
            src.Pause();
            src.Pause();                                                             // idempotent
            Check.True(token.IsPaused);
            Task wait = token.WaitWhilePausedAsync();
            await Task.Delay(30);
            Check.False(wait.IsCompleted);
            src.Resume();
            await wait.WithTimeout(TimeSpan.FromSeconds(5));
            Check.False(src.IsPaused);
        }

        public async Task Pause_WorkerStopsAtSafePoints()
        {
            var src = new PauseTokenSource();
            int processed = 0;
            var worker = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    await src.Token.WaitWhilePausedAsync();
                    Interlocked.Increment(ref processed);
                    await Task.Delay(1);
                }
            });
            await Task.Delay(20);
            src.Pause();
            await Task.Delay(30);                                                    // let an in-flight item finish
            int atPause = Volatile.Read(ref processed);
            await Task.Delay(60);
            Check.Equal(atPause, Volatile.Read(ref processed));
            Check.True(atPause < 30);
            src.Resume();
            await worker.WithTimeout(TimeSpan.FromSeconds(10));
            Check.Equal(30, processed);
        }

        public async Task Pause_CancellationAndEvents()
        {
            var src = new PauseTokenSource();
            var states = new List<bool>();
            src.StateChanged += s => states.Add(s);
            Check.True(src.Toggle());
            using var cts = new CancellationTokenSource(30);
            await Check.ThrowsAsync<OperationCanceledException>(() => src.Token.WaitWhilePausedAsync(cts.Token));
            Check.False(src.Toggle());
            src.Resume();                                                            // no-op: no event
            Check.Equal("True,False", string.Join(",", states));
            await default(PauseToken).WaitWhilePausedAsync();
            Check.False(default(PauseToken).IsPaused);
        }

        // ---------- race ----------

        private static Func<CancellationToken, Task<T>> After<T>(int ms, T value, List<string>? cancelled = null, string name = "")
            => async ct =>
            {
                try { await Task.Delay(ms, ct); }
                catch (OperationCanceledException) { lock (cancelled!) cancelled.Add(name); throw; }
                return value;
            };

        private static Func<CancellationToken, Task<T>> FailAfter<T>(int ms, string message)
            => async ct => { await Task.Delay(ms, ct); throw new InvalidOperationException(message); };

        public async Task FirstSuccessful_SkipsFailuresAndCancelsLosers()
        {
            var cancelled = new List<string>();
            int r = await TaskRace.FirstSuccessful(new[]
            {
                FailAfter<int>(5, "fast but broken"),
                After(40, 2, cancelled, "winner"),
                After(5000, 3, cancelled, "slow"),
            });
            Check.Equal(2, r);
            Check.Equal("slow", string.Join(",", cancelled));                       // loser saw cancellation before we returned
        }

        public async Task FirstSuccessful_AllFail()
        {
            var ex = await Check.ThrowsAsync<AggregateException>(() => TaskRace.FirstSuccessful(new[]
            {
                FailAfter<int>(5, "a"),
                FailAfter<int>(10, "b"),
                _ => throw new NotSupportedException("sync"),
            }));
            Check.Equal(3, ex.InnerExceptions.Count);
        }

        public async Task FirstMatching_AndFirstToComplete()
        {
            int m = await TaskRace.FirstMatching(new[] { After(5, 1), After(20, 7), After(40, 9) }, v => v > 5);
            Check.Equal(7, m);
            await Check.ThrowsAsync<AggregateException>(() => TaskRace.FirstMatching(new[] { After(5, 1) }, v => v > 5));
            await Check.ThrowsAsync<InvalidOperationException>(() => TaskRace.FirstToComplete(new[] { FailAfter<int>(5, "first"), After(200, 1) }));
            Check.Equal(1, await TaskRace.FirstToComplete(new[] { After(5, 1), After(200, 2) }));
        }

        public async Task Race_HonoursOuterCancellation()
        {
            using var cts = new CancellationTokenSource(20);
            await Check.ThrowsAsync<OperationCanceledException>(() => TaskRace.FirstSuccessful(new[] { After(5000, 1), After(5000, 2) }, cts.Token));
            Check.Throws<ArgumentException>(() => TaskRace.FirstSuccessful(Array.Empty<Func<CancellationToken, Task<int>>>()).GetAwaiter().GetResult());
        }

        public async Task Hedged_StartsBackupOnlyWhenSlow()
        {
            var starts = new List<int>();
            // Attempt 0 is slow, attempt 1 is fast: the hedge wins.
            int r = await TaskRace.Hedged(async (n, ct) =>
            {
                lock (starts) starts.Add(n);
                await Task.Delay(n == 0 ? 5000 : 10, ct);
                return n;
            }, TimeSpan.FromMilliseconds(30), maxAttempts: 3);
            Check.Equal(1, r);
            Check.Equal("0,1", string.Join(",", starts));

            // Fast primary: no hedge is ever started.
            starts.Clear();
            Check.Equal(0, await TaskRace.Hedged(async (n, ct) => { lock (starts) starts.Add(n); await Task.Delay(5, ct); return n; }, TimeSpan.FromSeconds(5)));
            Check.Equal("0", string.Join(",", starts));
        }

        public async Task Hedged_FailureTriggersTheNextAttemptAtOnce()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int r = await TaskRace.Hedged((n, ct) => n == 0 ? Task.FromException<int>(new Exception("down")) : Task.FromResult(42),
                TimeSpan.FromSeconds(10), maxAttempts: 2);
            Check.Equal(42, r);
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(5), "did not wait out the hedge delay");
            var all = await Check.ThrowsAsync<AggregateException>(() => TaskRace.Hedged<int>((n, ct) => throw new Exception("x" + n), TimeSpan.Zero, 3));
            Check.Equal(3, all.InnerExceptions.Count);
        }
    }
}
