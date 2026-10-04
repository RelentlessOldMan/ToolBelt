// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Threading;

namespace ToolBelt.Windows
{
    /// <summary>
    /// A single-instance gate built on a named <see cref="Mutex"/>. The first process to
    /// <see cref="TryAcquire(string)"/> a given name owns the gate (<see cref="IsOwned"/> true); later
    /// callers get an instance with <see cref="IsOwned"/> false until the owner disposes. Prefix the name
    /// with <c>Global\</c> for a machine-wide gate or <c>Local\</c> (the default namespace) for one scoped
    /// to the current session. Dispose releases the mutex; if the owning process crashed, the next caller
    /// transparently inherits ownership (the abandoned-mutex case).
    ///
    /// <para>Ownership is per-thread and reentrant (the underlying Win32 mutex semantics): the thread that
    /// owns the gate can re-acquire it, so this type distinguishes separate <b>processes/threads</b> — its
    /// intended single-instance-application use — not repeated calls on one thread.</para>
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;

        private SingleInstance(Mutex mutex, bool owned)
        {
            _mutex = mutex;
            IsOwned = owned;
        }

        /// <summary>True if this instance currently owns the gate.</summary>
        public bool IsOwned { get; private set; }

        /// <summary>
        /// Attempts to acquire the gate named <paramref name="name"/> without blocking. Always returns an
        /// instance; check <see cref="IsOwned"/> to see whether this caller won the gate.
        /// </summary>
        public static SingleInstance TryAcquire(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (name.Length == 0) throw new ArgumentException("Name must not be empty.", nameof(name));

            var mutex = new Mutex(initiallyOwned: false, name);
            bool owned;
            try
            {
                owned = mutex.WaitOne(TimeSpan.Zero, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner exited without releasing; WaitOne still transfers ownership to us.
                owned = true;
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
            return new SingleInstance(mutex, owned);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (IsOwned)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { /* not held on this thread — ignore */ }
                IsOwned = false;
            }
            _mutex.Dispose();
        }
    }
}
