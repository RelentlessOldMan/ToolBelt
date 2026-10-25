// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// An async-friendly mutual-exclusion lock. Unlike <c>lock</c>/<see cref="Monitor"/>, it can be held
    /// across <c>await</c> points. Acquire with <c>using (await gate.LockAsync()) { ... }</c>; the
    /// returned handle releases the lock when disposed. It is <b>not reentrant</b> — a task that already
    /// holds the lock and tries to acquire it again will deadlock. Disposing it fails pending waiters with
    /// <see cref="ObjectDisposedException"/>; a handle held at the time can still be released.
    /// </summary>
    public sealed class AsyncLock : IDisposable
    {
        // Never disposed itself: a handle released after Dispose must still work.
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _disposing = new CancellationTokenSource();
        private volatile bool _disposed;

        /// <summary>Waits until the lock is available and returns a handle that releases it on dispose.</summary>
        public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
        {
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
                    throw new ObjectDisposedException(nameof(AsyncLock));
                }
                if (_disposed)
                {
                    // Granted the slot just as Dispose ran (the semaphore prefers a grant over the cancellation).
                    _semaphore.Release();
                    throw new ObjectDisposedException(nameof(AsyncLock));
                }
            }
            return new Releaser(this);
        }

        /// <summary>Attempts to acquire the lock without waiting. Returns null if it is already held.</summary>
        public IDisposable? TryLock()
        {
            ThrowIfDisposed();
            return _semaphore.Wait(0) ? new Releaser(this) : null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _disposing.Cancel(); // fail the pending waiters
        }

        private void Release() => _semaphore.Release();

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncLock));
        }

        private sealed class Releaser : IDisposable
        {
            private readonly AsyncLock _owner;
            private int _released;

            public Releaser(AsyncLock owner) => _owner = owner;

            public void Dispose()
            {
                // Idempotent, even for two threads at once: releasing twice would corrupt the semaphore count.
                if (Interlocked.Exchange(ref _released, 1) == 1)
                    return;
                _owner.Release();
            }
        }
    }
}
