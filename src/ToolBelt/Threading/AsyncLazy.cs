// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// Lazily runs an asynchronous factory exactly once and caches the resulting task. Concurrent callers
    /// all observe the same in-flight (or completed) task, so the factory never runs twice. The result —
    /// including a faulted outcome — is cached; the factory is not retried after a failure. A factory that throws
    /// before returning its task gives the same cached faulted task. Supports <c>await lazy</c> directly.
    /// The first caller runs the factory's synchronous part; the others get the shared task at once rather than
    /// waiting for that part to return. A factory that reads <see cref="Value"/> before returning gets an
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public sealed class AsyncLazy<T>
    {
        private Func<Task<T>>? _factory;
        private Task<T>? _task;
        // The thread running the factory's synchronous part (0 when none): its own read of Value would wait on itself.
        private int _startingThread;

        public AsyncLazy(Func<Task<T>> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>The shared task producing the value; starts the factory on first access.</summary>
        public Task<T> Value
        {
            get
            {
                Task<T>? task = Volatile.Read(ref _task);
                if (task != null)
                {
                    if (Volatile.Read(ref _startingThread) == Environment.CurrentManagedThreadId)
                        throw new InvalidOperationException("The factory read Value before returning its task.");
                    return task;
                }
                // Lazy<T>'s ExecutionAndPublication held its lock for the factory's synchronous part, so every other
                // caller blocked until it returned (an 800 ms synchronous start blocked them all 800 ms). The shared task
                // is published first, and Unwrap gives it the factory task's own outcome (the very exceptions and
                // cancellation, as awaiting it would report them).
                var gate = new TaskCompletionSource<Task<T>>();
                Task<T> shared = gate.Task.Unwrap();
                task = Interlocked.CompareExchange(ref _task, shared, null);
                if (task != null) return Value;
                Func<Task<T>> factory = _factory!;
                _factory = null;
                Task<T>? inner;
                Volatile.Write(ref _startingThread, Environment.CurrentManagedThreadId);
                try
                {
                    // A null task was cached, and every await threw NullReferenceException.
                    inner = factory() ?? Task.FromException<T>(new InvalidOperationException("The factory returned a null task."));
                }
                catch (Exception ex) { inner = Task.FromException<T>(ex); } // a synchronous throw is the faulted task
                finally { Volatile.Write(ref _startingThread, 0); }
                gate.SetResult(inner);
                return shared;
            }
        }

        /// <summary>Whether the factory has been started (the value task created) yet.</summary>
        public bool IsStarted => Volatile.Read(ref _task) != null;

        /// <summary>Enables <c>await lazy</c>.</summary>
        public TaskAwaiter<T> GetAwaiter() => Value.GetAwaiter();
    }
}
