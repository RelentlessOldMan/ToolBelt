// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// Cooperative pause/resume for long-running async work — the pause counterpart of
    /// <see cref="CancellationTokenSource"/>. The controller calls <see cref="Pause"/> / <see cref="Resume"/>; the worker
    /// holds a <see cref="PauseToken"/> and awaits <see cref="PauseToken.WaitWhilePausedAsync"/> at safe points (between
    /// items, between chunks), which completes immediately when running and otherwise waits until resumed or cancelled.
    /// Thread-safe; pausing twice is the same as once.
    /// </summary>
    public sealed class PauseTokenSource
    {
        private readonly object _gate = new object();
        private TaskCompletionSource<bool>? _resumed;     // non-null while paused

        public bool IsPaused { get { lock (_gate) return _resumed != null; } }

        public PauseToken Token => new PauseToken(this);

        /// <summary>
        /// Raised (outside the lock) when the state changes; the argument is the new <see cref="IsPaused"/>. With Pause and
        /// Resume racing on different threads, notifications can arrive out of order — read <see cref="IsPaused"/> for the
        /// current state rather than trusting the last event.
        /// </summary>
        public event Action<bool>? StateChanged;

        public void Pause()
        {
            lock (_gate)
            {
                if (_resumed != null) return;
                _resumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            StateChanged?.Invoke(true);
        }

        public void Resume()
        {
            TaskCompletionSource<bool>? tcs;
            lock (_gate)
            {
                tcs = _resumed;
                _resumed = null;
            }
            if (tcs == null) return;
            tcs.SetResult(true);
            StateChanged?.Invoke(false);
        }

        /// <summary>Pauses if running, resumes if paused; returns the new state.</summary>
        public bool Toggle()
        {
            bool pause;
            lock (_gate) pause = _resumed == null;
            if (pause) Pause(); else Resume();
            return pause;
        }

        internal Task WaitAsync(CancellationToken cancellationToken)
        {
            Task? wait;
            lock (_gate) wait = _resumed?.Task;
            if (wait == null) return Task.CompletedTask;
            if (!cancellationToken.CanBeCanceled) return wait;
            return WaitWithCancellation(wait, cancellationToken);
        }

        private static async Task WaitWithCancellation(Task wait, CancellationToken ct)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(wait, cancelled.Task).ConfigureAwait(false) != wait)
                    ct.ThrowIfCancellationRequested();
            }
        }
    }

    /// <summary>The worker's view of a <see cref="PauseTokenSource"/>. The default token is never paused.</summary>
    public readonly struct PauseToken
    {
        private readonly PauseTokenSource? _source;

        internal PauseToken(PauseTokenSource source) => _source = source;

        public bool IsPaused => _source?.IsPaused ?? false;

        /// <summary>Completes at once when not paused; otherwise when resumed (or throws when <paramref name="cancellationToken"/> fires).</summary>
        public Task WaitWhilePausedAsync(CancellationToken cancellationToken = default)
            => _source?.WaitAsync(cancellationToken) ?? Task.CompletedTask;
    }
}
