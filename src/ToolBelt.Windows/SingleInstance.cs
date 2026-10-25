// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace ToolBelt.Windows
{
    /// <summary>
    /// A single-instance gate built on a named <see cref="Mutex"/>. The first process to
    /// <see cref="TryAcquire(string)"/> a given name owns the gate (<see cref="IsOwned"/> true); later
    /// callers get an instance with <see cref="IsOwned"/> false until the owner disposes. Prefix the name
    /// with <c>Global\</c> for a machine-wide gate or <c>Local\</c> (the default namespace) for one scoped
    /// to the current session. Dispose releases the mutex; if the owning process crashed, the next caller
    /// transparently inherits ownership (the abandoned-mutex case). A gate held by a process this one may not open
    /// (another user's, or an elevated one's), or a name already used by another kind of kernel object, counts as
    /// held by someone else: <see cref="IsOwned"/> is false.
    ///
    /// <para>A Win32 mutex belongs to the thread that acquired it and is abandoned when that thread exits, so the gate
    /// is held by a dedicated background thread instead of the caller's: acquiring on a thread that later ends (an
    /// <c>async Main</c> resuming on the pool, a <c>Task.Run</c>) keeps the gate, <see cref="Dispose"/> works from any
    /// thread, and two acquisitions of one name never both own it, whichever threads ask.</para>
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly ManualResetEventSlim? _release;
        private readonly Thread? _holder;
        private int _disposed;

        private SingleInstance(ManualResetEventSlim? release, Thread? holder)
        {
            _release = release;
            _holder = holder;
            IsOwned = holder != null;
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

            var release = new ManualResetEventSlim();
            using var ready = new ManualResetEventSlim();
            bool owned = false;
            ExceptionDispatchInfo? error = null;
            var holder = new Thread(() =>
            {
                Mutex? mutex = null;
                try
                {
                    try { mutex = new Mutex(initiallyOwned: false, name); }
                    catch (UnauthorizedAccessException) { return; }                // exists, but not ours to open
                    catch (WaitHandleCannotBeOpenedException) { return; }          // the name is some other kind of object
                    try { owned = mutex.WaitOne(TimeSpan.Zero, exitContext: false); }
                    catch (AbandonedMutexException) { owned = true; }              // the previous owner exited without releasing
                }
                catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
                finally
                {
                    if (!owned) mutex?.Dispose();
                    ready.Set();
                }
                if (!owned) return;
                release.Wait();
                try { mutex!.ReleaseMutex(); }
                catch (ApplicationException) { }
                mutex!.Dispose();
            })
            { IsBackground = true, Name = "ToolBelt.SingleInstance" };
            holder.Start();
            ready.Wait();
            if (!owned)
            {
                holder.Join();
                release.Dispose();
                error?.Throw();
                return new SingleInstance(null, null);
            }
            return new SingleInstance(release, holder);
        }

        /// <summary>Releases the gate (if owned). Safe from any thread, and more than once.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            IsOwned = false;
            if (_holder is null) return;
            _release!.Set();
            _holder.Join();
            _release.Dispose();
        }
    }
}
