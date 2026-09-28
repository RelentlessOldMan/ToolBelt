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
    /// holds the lock and tries to acquire it again will deadlock.
    /// </summary>
    public sealed class AsyncLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        /// <summary>Waits until the lock is available and returns a handle that releases it on dispose.</summary>
        public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            _semaphore.Dispose();
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
            private bool _released;

            public Releaser(AsyncLock owner) => _owner = owner;

            public void Dispose()
            {
                if (_released)
                    return; // idempotent — releasing twice would corrupt the semaphore count
                _released = true;
                _owner.Release();
            }
        }
    }
}
