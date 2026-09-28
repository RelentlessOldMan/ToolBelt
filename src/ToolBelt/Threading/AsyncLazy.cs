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
    /// including a faulted outcome — is cached; the factory is not retried after a failure. Supports
    /// <c>await lazy</c> directly.
    /// </summary>
    public sealed class AsyncLazy<T>
    {
        private readonly Lazy<Task<T>> _lazy;

        public AsyncLazy(Func<Task<T>> factory)
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));
            // ExecutionAndPublication guarantees the factory is invoked at most once across threads.
            _lazy = new Lazy<Task<T>>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>The shared task producing the value; starts the factory on first access.</summary>
        public Task<T> Value => _lazy.Value;

        /// <summary>Whether the factory has been started (the value task created) yet.</summary>
        public bool IsStarted => _lazy.IsValueCreated;

        /// <summary>Enables <c>await lazy</c>.</summary>
        public TaskAwaiter<T> GetAwaiter() => _lazy.Value.GetAwaiter();
    }
}
