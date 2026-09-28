// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Resilience
{
    /// <summary>
    /// A bulkhead concurrency limiter: allows at most <c>maxConcurrency</c> operations to run at once,
    /// making additional callers wait their turn. This isolates a resource so a burst of work cannot
    /// exhaust threads or a downstream dependency. Thread-safe.
    /// </summary>
    public sealed class Bulkhead : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxConcurrency;
        private bool _disposed;

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

            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            _semaphore.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Bulkhead));
        }
    }
}
