// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Resilience
{
    /// <summary>
    /// A bulkhead concurrency limiter: allows at most <c>maxConcurrency</c> operations to run at once,
    /// making additional callers wait their turn. This isolates a resource so a burst of work cannot
    /// exhaust threads or a downstream dependency. Thread-safe. Disposing it fails queued callers with
    /// <see cref="ObjectDisposedException"/>; operations already running finish normally.
    /// </summary>
    public sealed class Bulkhead : IDisposable
    {
        // Never disposed itself: an operation still running at Dispose must be able to release its slot.
        private readonly SemaphoreSlim _semaphore;
        private readonly CancellationTokenSource _disposing = new CancellationTokenSource();
        private readonly int _maxConcurrency;
        private volatile bool _disposed;

        public Bulkhead(int maxConcurrency)
        {
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "Max concurrency must be positive.");
            _maxConcurrency = maxConcurrency;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        public int MaxConcurrency => _maxConcurrency;

        /// <summary>Free execution slots right now (max minus in-flight and queued-acquired).</summary>
        public int AvailableSlots => _semaphore.CurrentCount;

        /// <summary>Runs an operation once a slot is free, releasing the slot when it completes.</summary>
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (operation is null) throw new ArgumentNullException(nameof(operation));
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (!_semaphore.Wait(0))
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposing.Token);
                try
                {
                    await _semaphore.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_disposing.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw new ObjectDisposedException(nameof(Bulkhead));
                }
                if (_disposed)
                {
                    // Granted the slot just as Dispose ran (the semaphore prefers a grant over the cancellation).
                    _semaphore.Release();
                    throw new ObjectDisposedException(nameof(Bulkhead));
                }
            }
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>Runs an operation with no return value under the bulkhead.</summary>
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            if (operation is null) throw new ArgumentNullException(nameof(operation));
            return ExecuteAsync(async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _disposing.Cancel(); // fail the queued callers
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Bulkhead));
        }
    }
}
