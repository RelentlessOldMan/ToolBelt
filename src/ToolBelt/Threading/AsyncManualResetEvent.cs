// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// An async manual-reset event. <see cref="WaitAsync"/> completes once the event is signaled with
    /// <see cref="Set"/>; while signaled, waits complete immediately. <see cref="Reset"/> returns it to
    /// the non-signaled state so future waiters block again. Continuations run asynchronously, so
    /// signaling never runs waiter callbacks inline on the setting thread.
    /// </summary>
    public sealed class AsyncManualResetEvent
    {
        // Set and Reset are serialized so a Reset cannot swap in a fresh source between a concurrent
        // Set's field-read and its completion (which would complete a detached source and lose the
        // signal). WaitAsync still reads the volatile field lock-free.
        private readonly object _gate = new object();
        private volatile TaskCompletionSource<bool> _tcs;

        public AsyncManualResetEvent(bool initiallySet = false)
        {
            _tcs = CreateSource();
            if (initiallySet)
                _tcs.TrySetResult(true);
        }

        /// <summary>Whether the event is currently signaled.</summary>
        public bool IsSet => _tcs.Task.IsCompleted;

        /// <summary>Completes when the event is (or becomes) signaled. Honors cancellation.</summary>
        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            var task = _tcs.Task;
            if (task.IsCompleted || !cancellationToken.CanBeCanceled)
                return task;
            return WaitWithCancellation(task, cancellationToken);
        }

        /// <summary>Signals the event, releasing all current and future waiters until <see cref="Reset"/>.</summary>
        public void Set()
        {
            lock (_gate)
                _tcs.TrySetResult(true);
        }

        /// <summary>Returns the event to the non-signaled state (a no-op if already non-signaled).</summary>
        public void Reset()
        {
            lock (_gate)
            {
                if (_tcs.Task.IsCompleted)
                    _tcs = CreateSource();
            }
        }

        private static TaskCompletionSource<bool> CreateSource()
            => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private static async Task WaitWithCancellation(Task task, CancellationToken cancellationToken)
        {
            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), cancelTcs))
            {
                var winner = await Task.WhenAny(task, cancelTcs.Task).ConfigureAwait(false);
                if (winner != task)
                    cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
