// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// Ergonomic helpers around <see cref="Task"/>: timeouts, cancellation, fire-and-forget with a
    /// mandatory error sink (so swallowed exceptions stop being invisible), and "wait for all or throw the
    /// first exception". The timeout/cancellation helpers do not abort the underlying work — the framework
    /// cannot cancel an arbitrary task — they stop the caller waiting.
    /// </summary>
    public static class TaskExtensions
    {
        public static async Task<TResult> WithTimeout<TResult>(this Task<TResult> task, TimeSpan timeout)
        {
            if (task is null) throw new ArgumentNullException(nameof(task));
            using var cts = new CancellationTokenSource();
            var delay = StartDelay(timeout, cts.Token);
            if (await Task.WhenAny(task, delay).ConfigureAwait(false) == task)
            {
                cts.Cancel();
                return await task.ConfigureAwait(false);
            }
            Observe(task);
            throw new TimeoutException($"Operation timed out after {timeout}.");
        }

        public static async Task WithTimeout(this Task task, TimeSpan timeout)
        {
            if (task is null) throw new ArgumentNullException(nameof(task));
            using var cts = new CancellationTokenSource();
            var delay = StartDelay(timeout, cts.Token);
            if (await Task.WhenAny(task, delay).ConfigureAwait(false) == task)
            {
                cts.Cancel();
                await task.ConfigureAwait(false);
                return;
            }
            Observe(task);
            throw new TimeoutException($"Operation timed out after {timeout}.");
        }

        // A task given up on that faults later: its exception went unobserved (an UnobservedTaskException at a later GC).
        private static void Observe(Task task) =>
            task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        private static Task StartDelay(TimeSpan timeout, CancellationToken cancellationToken)
        {
            // Task.Delay truncates to whole milliseconds before checking: −0.5 ms timed out at once and −1.5 ms never did.
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                    "Timeout must be Timeout.InfiniteTimeSpan or between zero and the longest delay Task.Delay accepts.");
            try { return Task.Delay(timeout, cancellationToken); }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                    "Timeout must be Timeout.InfiniteTimeSpan or between zero and the longest delay Task.Delay accepts.");
            }
        }

        public static async Task<TResult> WithCancellation<TResult>(this Task<TResult> task, CancellationToken cancellationToken)
        {
            if (task is null) throw new ArgumentNullException(nameof(task));
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => tcs.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, tcs.Task).ConfigureAwait(false) != task)
                {
                    Observe(task);
                    throw new OperationCanceledException(cancellationToken);
                }
                return await task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Observes the task's completion and routes any failure to <paramref name="onError"/>. An exception thrown by
        /// <paramref name="onError"/> itself is dropped, as by the other error sinks here.
        /// </summary>
        public static void FireAndForget(this Task task, Action<Exception> onError)
        {
            if (task is null) throw new ArgumentNullException(nameof(task));
            if (onError is null) throw new ArgumentNullException(nameof(onError));
            task.ContinueWith(
                t =>
                {
                    // Escaping, it faulted the continuation, which nothing observes: an UnobservedTaskException at a later GC.
                    // What await would throw: GetBaseException also unwrapped an AggregateException the task threw itself.
                    AggregateException failure = t.Exception!;
                    try { onError(failure.InnerExceptions.Count == 1 ? failure.InnerException! : failure); }
                    catch { }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Completes when all tasks succeed, or throws as soon as any one faults (its exception) or is cancelled
        /// (a <see cref="TaskCanceledException"/>).
        /// </summary>
        public static async Task<TResult[]> WhenAllOrFirstException<TResult>(IEnumerable<Task<TResult>> tasks)
        {
            if (tasks is null) throw new ArgumentNullException(nameof(tasks));
            var all = new List<Task<TResult>>(tasks);
            if (all.Count == 0) return Array.Empty<TResult>();
            // One continuation per task into a shared completion: WhenAny over the pending list each round was quadratic
            // (40000 tasks took seconds).
            var first = new TaskCompletionSource<Task<TResult>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            int remaining = all.Count;
            foreach (Task<TResult> task in all)
            {
                if (task is null) throw new ArgumentException("Tasks must not be null.", nameof(tasks));
                _ = task.ContinueWith(t =>
                {
                    if (t.IsFaulted || t.IsCanceled) first.TrySetResult(t);
                    else if (Interlocked.Decrement(ref remaining) == 0) first.TrySetResult(null);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            Task<TResult>? done = await first.Task.ConfigureAwait(false);
            if (done != null)
            {
                // What await throws: the first inner exception. GetBaseException unwrapped an AggregateException the task
                // threw itself, and gave the wrapper for a task faulted with several.
                if (done.IsFaulted)
                    ExceptionDispatchInfo.Capture(done.Exception!.InnerException!).Throw();
                throw new TaskCanceledException(done);
            }
            var results = new TResult[all.Count];
            for (int i = 0; i < all.Count; i++)
                results[i] = all[i].Result;
            return results;
        }
    }
}
