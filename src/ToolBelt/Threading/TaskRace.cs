// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// Run several attempts at the same thing and keep the first good answer, cancelling the rest — the pattern behind
    /// "query every mirror", "try each device port", and hedged requests. <see cref="Task.WhenAny(Task[])"/> alone leaves
    /// the losers running and their exceptions unobserved; these helpers give every attempt a linked token that is cancelled
    /// as soon as there is a winner, and wait for the losers to wind down before returning so nothing outlives the call.
    /// </summary>
    public static class TaskRace
    {
        /// <summary>
        /// The result of the first attempt to <em>succeed</em>; failures are ignored while any attempt is still running. If
        /// all fail, throws an <see cref="AggregateException"/> of every failure.
        /// </summary>
        public static Task<T> FirstSuccessful<T>(IEnumerable<Func<CancellationToken, Task<T>>> attempts, CancellationToken cancellationToken = default)
            => Race(attempts, null, cancellationToken, successOnly: true);

        /// <summary>The first attempt to succeed <em>and</em> satisfy <paramref name="accept"/>; non-matching results count as misses.</summary>
        public static Task<T> FirstMatching<T>(IEnumerable<Func<CancellationToken, Task<T>>> attempts, Func<T, bool> accept, CancellationToken cancellationToken = default)
            => Race(attempts, accept ?? throw new ArgumentNullException(nameof(accept)), cancellationToken, successOnly: true);

        /// <summary>The outcome (result or exception) of whichever attempt finishes first; the rest are cancelled.</summary>
        public static Task<T> FirstToComplete<T>(IEnumerable<Func<CancellationToken, Task<T>>> attempts, CancellationToken cancellationToken = default)
            => Race(attempts, null, cancellationToken, successOnly: false);

        /// <summary>
        /// Hedged request: starts <paramref name="attempt"/>, and if it hasn't succeeded after <paramref name="hedgeDelay"/>
        /// starts another, up to <paramref name="maxAttempts"/> in flight; returns the first success and cancels the rest.
        /// Trims tail latency at the cost of occasional duplicate work. The attempt number (0-based) is passed in. Hedges
        /// start one <paramref name="hedgeDelay"/> apart whatever happens to the attempts in between; a delay of
        /// <see cref="Timeout.InfiniteTimeSpan"/>, or one longer than int.MaxValue milliseconds (about 24.8 days, the
        /// longest <see cref="Task.Delay(TimeSpan)"/> accepts on every target; .NET 6+ alone accepts twice that), never hedges (a further attempt then starts only when every running one has failed).
        /// </summary>
        public static async Task<T> Hedged<T>(Func<int, CancellationToken, Task<T>> attempt, TimeSpan hedgeDelay, int maxAttempts = 2,
            CancellationToken cancellationToken = default)
        {
            if (attempt is null) throw new ArgumentNullException(nameof(attempt));
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Need at least one attempt.");
            if (hedgeDelay < TimeSpan.Zero && hedgeDelay != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(hedgeDelay), hedgeDelay, "Delay must not be negative (use Timeout.InfiniteTimeSpan to never hedge).");
            cancellationToken.ThrowIfCancellationRequested();   // an attempt that ignored it made the call succeed
            bool hedges = hedgeDelay != Timeout.InfiniteTimeSpan && hedgeDelay.TotalMilliseconds <= int.MaxValue;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var all = new List<Task<T>>();
            var finished = new Finished<T>();
            int inFlight = 0;
            var failures = new List<Exception>();
            void Launch(int n)
            {
                var task = Start(() => attempt(n, cts.Token));
                all.Add(task);
                inFlight++;
                finished.Watch(task);
            }
            try
            {
                Launch(0);
                int started = 1;
                // One timer per hedge, started with the attempt before it: a failure in between used to restart it.
                Task? timer = null;
                // Its own source, so a timer dropped when an attempt failed is cancelled: left scheduled, one per failure
                // piled up until the call ended (200000 failed attempts held 200000 timers).
                CancellationTokenSource? timerCts = null;
                void DropTimer()
                {
                    timer = null;
                    if (timerCts is null) return;
                    timerCts.Cancel();
                    timerCts.Dispose();
                    timerCts = null;
                }
                try
                {
                    while (true)
                    {
                        // One wait on "an attempt finished" and the timer: a WhenAny over every running attempt, rebuilt each
                        // time, made many attempts quadratic (8000 hedges took 3.9 s).
                        Task next = finished.Next();
                        if (started < maxAttempts && hedges)
                        {
                            if (timer is null)
                            {
                                timerCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                                timer = Task.Delay(hedgeDelay, timerCts.Token);
                            }
                            if (await Task.WhenAny(next, timer).ConfigureAwait(false) == timer && !next.IsCompleted)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                DropTimer();
                                Launch(started++);
                                continue;
                            }
                        }
                        else if (inFlight == 0) throw new AggregateException("Every attempt failed.", failures);
                        else await next.ConfigureAwait(false);
                        var done = finished.Take();
                        inFlight--;
                        if (done.Status == TaskStatus.RanToCompletion) return done.Result;
                        // Read before the cancellation check, so a fault that met a cancel is observed.
                        Exception failure = Unwrap(done);
                        cancellationToken.ThrowIfCancellationRequested();
                        failures.Add(failure);
                        if (inFlight == 0 && started >= maxAttempts) throw new AggregateException("Every attempt failed.", failures);
                        if (inFlight == 0)
                        {
                            // Nothing in flight: start the next hedge immediately rather than waiting out the delay.
                            DropTimer();
                            Launch(started++);
                        }
                    }
                }
                finally { DropTimer(); }
            }
            finally
            {
                CancelLosers(cts);
                await Quietly(all).ConfigureAwait(false);
            }
        }

        // Attempts in the order they finish, each handed over once: a WhenAny over the pending ones per round, and a
        // List.Remove, made a race quadratic in its attempts (40000 failing attempts took 11 s).
        private sealed class Finished<T>
        {
            private readonly object _gate = new object();
            private readonly Queue<Task<T>> _done = new Queue<Task<T>>();
            private TaskCompletionSource<bool>? _waiter;

            public void Watch(Task<T> task) =>
                task.ContinueWith(Arrived, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            private void Arrived(Task<T> task)
            {
                TaskCompletionSource<bool>? waiter;
                lock (_gate)
                {
                    _done.Enqueue(task);
                    waiter = _waiter;
                    _waiter = null;
                }
                waiter?.TrySetResult(true);
            }

            // Completes once an attempt is ready to Take (at once if one is).
            public Task Next()
            {
                lock (_gate)
                {
                    if (_done.Count > 0) return Task.CompletedTask;
                    return (_waiter ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                }
            }

            public Task<T> Take() { lock (_gate) return _done.Dequeue(); }
        }

        private static async Task<T> Race<T>(IEnumerable<Func<CancellationToken, Task<T>>> attempts, Func<T, bool>? accept,
            CancellationToken cancellationToken, bool successOnly)
        {
            if (attempts is null) throw new ArgumentNullException(nameof(attempts));
            var factories = attempts.ToList();
            if (factories.Count == 0) throw new ArgumentException("Need at least one attempt.", nameof(attempts));
            if (factories.Any(f => f is null)) throw new ArgumentException("Attempts must not be null.", nameof(attempts));
            // Before starting: an attempt that ignored an already-cancelled token made the race succeed.
            cancellationToken.ThrowIfCancellationRequested();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var all = factories.Select(f => Start(() => f(cts.Token))).ToList();
            var finished = new Finished<T>();
            foreach (var task in all) finished.Watch(task);
            var failures = new List<Exception>();
            try
            {
                for (int left = all.Count; left > 0; left--)
                {
                    await finished.Next().ConfigureAwait(false);
                    var done = finished.Take();
                    if (!successOnly)
                    {
                        // The caller's token, not the linked one the attempt saw.
                        if (done.Status != TaskStatus.RanToCompletion && cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException(cancellationToken);
                        return await done.ConfigureAwait(false);
                    }
                    if (done.Status == TaskStatus.RanToCompletion)
                    {
                        if (accept == null || accept(done.Result)) return done.Result;
                        // Cancelled with only rejections left, it reported "no acceptable result".
                        cancellationToken.ThrowIfCancellationRequested();
                        failures.Add(new InvalidOperationException("Result rejected by the predicate."));
                        continue;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    failures.Add(Unwrap(done));
                }
                throw new AggregateException(accept == null ? "Every attempt failed." : "No attempt produced an acceptable result.", failures);
            }
            finally
            {
                CancelLosers(cts);
                await Quietly(all).ConfigureAwait(false);
            }
        }

        // A callback a loser registered on its token may throw; like the loser's outcome it is irrelevant. It replaced the
        // winner's result and skipped waiting for the losers.
        private static void CancelLosers(CancellationTokenSource cts)
        {
            try { cts.Cancel(); }
            catch (AggregateException) { }
        }

        // Runs the factory on the pool, so that a synchronous throw becomes a faulted task rather than escaping the race,
        // and a synchronous part does not hold up the rest: started in turn on the caller's thread, a 1 s synchronous first
        // attempt won FirstToComplete over an instant second, and Hedged never hedged it.
        private static Task<T> Start<T>(Func<Task<T>> factory)
            => Task.Run(() =>
            {
                try { return factory() ?? Task.FromException<T>(new InvalidOperationException("Attempt returned a null task.")); }
                catch (Exception ex) { return Task.FromException<T>(ex); }
            });

        private static Exception Unwrap(Task t)
            => t.IsCanceled ? new TaskCanceledException(t) : (t.Exception!.InnerExceptions.Count == 1 ? t.Exception.InnerException! : t.Exception);

        private static async Task Quietly(IEnumerable<Task> tasks)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { /* losers' outcomes are irrelevant once there is a winner; observed here so none go unobserved */ }
        }
    }
}
