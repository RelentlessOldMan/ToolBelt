// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// An async countdown latch: starts at a count and completes <see cref="WaitAsync"/> once
    /// <see cref="Signal"/> has driven the count to zero. <see cref="AddCount"/> raises the count while
    /// it is still positive. Thread-safe. (Deliberately does not reuse the manual-reset event — each
    /// drop-in file stands alone.)
    /// </summary>
    public sealed class AsyncCountdownEvent
    {
        private readonly object _gate = new object();
        private readonly TaskCompletionSource<bool> _tcs
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public AsyncCountdownEvent(int initialCount)
        {
            if (initialCount < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCount), initialCount, "Initial count must not be negative.");
            _count = initialCount;
            if (initialCount == 0)
                _tcs.TrySetResult(true);
        }

        public int CurrentCount
        {
            get { lock (_gate) return _count; }
        }

        public bool IsSet => _tcs.Task.IsCompleted;

        /// <summary>Decrements the count by <paramref name="signalCount"/>, completing waiters when it reaches zero.</summary>
        public void Signal(int signalCount = 1)
        {
            if (signalCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(signalCount), signalCount, "Signal count must be positive.");

            bool completed = false;
            lock (_gate)
            {
                if (_count == 0)
                    throw new InvalidOperationException("The event is already set.");
                if (signalCount > _count)
                    throw new InvalidOperationException("Signal count exceeds the current count.");
                _count -= signalCount;
                if (_count == 0)
                    completed = true;
            }

            if (completed)
                _tcs.TrySetResult(true);
        }

        /// <summary>Increases the count. Throws if the event has already reached zero.</summary>
        public void AddCount(int addCount = 1)
        {
            if (addCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(addCount), addCount, "Add count must be positive.");

            lock (_gate)
            {
                if (_count == 0)
                    throw new InvalidOperationException("Cannot add to an event that is already set.");
                _count += addCount;
            }
        }

        /// <summary>Completes when the count reaches zero. Honors cancellation.</summary>
        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            var task = _tcs.Task;
            if (task.IsCompleted || !cancellationToken.CanBeCanceled)
                return task;
            return WaitWithCancellation(task, cancellationToken);
        }

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
