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
        /// Trims tail latency at the cost of occasional duplicate work. The attempt number (0-based) is passed in.
        /// </summary>
        public static async Task<T> Hedged<T>(Func<int, CancellationToken, Task<T>> attempt, TimeSpan hedgeDelay, int maxAttempts = 2,
            CancellationToken cancellationToken = default)
        {
            if (attempt is null) throw new ArgumentNullException(nameof(attempt));
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Need at least one attempt.");
            if (hedgeDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(hedgeDelay), hedgeDelay, "Delay must not be negative.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var running = new List<Task<T>>();
            var failures = new List<Exception>();
            try
            {
                running.Add(Start(() => attempt(0, cts.Token)));
                int started = 1;
                while (true)
                {
                    var waitFor = new List<Task>(running);
                    Task? timer = null;
                    if (started < maxAttempts)
                    {
                        timer = Task.Delay(hedgeDelay, cts.Token);
                        waitFor.Add(timer);
                    }
                    if (waitFor.Count == 0) throw new AggregateException("Every attempt failed.", failures);
                    Task done = await Task.WhenAny(waitFor).ConfigureAwait(false);
                    if (done == timer)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int n = started++;
                        running.Add(Start(() => attempt(n, cts.Token)));
                        continue;
                    }
                    var finished = (Task<T>)done;
                    running.Remove(finished);
                    if (finished.Status == TaskStatus.RanToCompletion) return finished.Result;
                    cancellationToken.ThrowIfCancellationRequested();
                    failures.Add(Unwrap(finished));
                    if (running.Count == 0 && started >= maxAttempts) throw new AggregateException("Every attempt failed.", failures);
                    if (running.Count == 0)
                    {
                        // Nothing in flight: start the next hedge immediately rather than waiting out the delay.
                        int n = started++;
                        running.Add(Start(() => attempt(n, cts.Token)));
                    }
                }
            }
            finally
            {
                cts.Cancel();
                await Quietly(running).ConfigureAwait(false);
            }
        }

        private static async Task<T> Race<T>(IEnumerable<Func<CancellationToken, Task<T>>> attempts, Func<T, bool>? accept,
            CancellationToken cancellationToken, bool successOnly)
        {
            if (attempts is null) throw new ArgumentNullException(nameof(attempts));
            var factories = attempts.ToList();
            if (factories.Count == 0) throw new ArgumentException("Need at least one attempt.", nameof(attempts));
            if (factories.Any(f => f is null)) throw new ArgumentException("Attempts must not be null.", nameof(attempts));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var running = factories.Select(f => Start(() => f(cts.Token))).ToList();
            var all = running.ToList();
            var failures = new List<Exception>();
            try
            {
                while (running.Count > 0)
                {
                    var done = await Task.WhenAny(running).ConfigureAwait(false);
                    running.Remove(done);
                    if (!successOnly) return await done.ConfigureAwait(false);
                    if (done.Status == TaskStatus.RanToCompletion)
                    {
                        if (accept == null || accept(done.Result)) return done.Result;
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
                cts.Cancel();
                await Quietly(all).ConfigureAwait(false);
            }
        }

        // Runs the factory so that a synchronous throw becomes a faulted task rather than escaping the race.
        private static Task<T> Start<T>(Func<Task<T>> factory)
        {
            try { return factory() ?? Task.FromException<T>(new InvalidOperationException("Attempt returned a null task.")); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }

        private static Exception Unwrap(Task t)
            => t.IsCanceled ? new TaskCanceledException(t) : (t.Exception!.InnerExceptions.Count == 1 ? t.Exception.InnerException! : t.Exception);

        private static async Task Quietly(IEnumerable<Task> tasks)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { /* losers' outcomes are irrelevant once there is a winner; observed here so none go unobserved */ }
        }
    }
}
