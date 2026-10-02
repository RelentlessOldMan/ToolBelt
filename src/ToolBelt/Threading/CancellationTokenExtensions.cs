// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// Small helpers the BCL leaves out: awaiting a <see cref="CancellationToken"/> directly, and building a
    /// linked source that also fires after a timeout.
    /// </summary>
    public static class CancellationTokenExtensions
    {
        /// <summary>
        /// Returns a task that completes when <paramref name="token"/> is canceled. If the token is already
        /// canceled, the task is already completed; if the token can never be canceled
        /// (<see cref="CancellationToken.None"/>), the task never completes. Useful as one arm of a
        /// <see cref="Task.WhenAny(Task[])"/>. The internal registration is released as soon as the task
        /// completes, so there is no leak once cancellation fires — but a long-lived token whose
        /// <c>WhenCanceled</c> task is awaited and never cancels keeps that registration alive; prefer
        /// short-lived or linked tokens.
        /// </summary>
        public static Task WhenCanceled(this CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return Task.CompletedTask;
            if (!token.CanBeCanceled)
                return new TaskCompletionSource<bool>().Task; // never cancels -> never completes

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = token.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), tcs);

            // Dispose the registration once the task completes (i.e. on cancellation) to free the callback.
            tcs.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return tcs.Task;
        }

        /// <summary>
        /// Creates a <see cref="CancellationTokenSource"/> that is canceled when either <paramref name="token"/>
        /// is canceled or <paramref name="timeout"/> elapses, whichever comes first. The caller owns and must
        /// dispose the returned source.
        /// </summary>
        public static CancellationTokenSource CreateLinkedTimeout(this CancellationToken token, TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be non-negative or Timeout.InfiniteTimeSpan.");

            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);
            return cts;
        }
    }
}
