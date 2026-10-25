// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
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
            throw new TimeoutException($"Operation timed out after {timeout}.");
        }

        private static Task StartDelay(TimeSpan timeout, CancellationToken cancellationToken)
        {
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
                    throw new OperationCanceledException(cancellationToken);
                return await task.ConfigureAwait(false);
            }
        }

        /// <summary>Observes the task's completion and routes any failure to <paramref name="onError"/>.</summary>
        public static void FireAndForget(this Task task, Action<Exception> onError)
        {
            if (task is null) throw new ArgumentNullException(nameof(task));
            if (onError is null) throw new ArgumentNullException(nameof(onError));
            task.ContinueWith(
                t => onError(t.Exception!.GetBaseException()),
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
                if (done.IsFaulted)
                    throw done.Exception!.GetBaseException();
                throw new TaskCanceledException(done);
            }
            var results = new TResult[all.Count];
            for (int i = 0; i < all.Count; i++)
                results[i] = all[i].Result;
            return results;
        }
    }
}
