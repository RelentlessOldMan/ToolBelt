// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// Runs an asynchronous action repeatedly on an interval until disposed: it catches and reports handler
    /// exceptions (so one failure does not kill the loop), stops gracefully on dispose (waiting for the
    /// in-flight iteration), and takes an injectable delay so tests are deterministic. The first run happens
    /// immediately; subsequent runs wait one interval. Replaces the timer-plus-boolean pattern every tool
    /// improvises and gets subtly wrong.
    /// </summary>
    public sealed class PeriodicWorker : IDisposable
    {
        private readonly Func<CancellationToken, Task> _work;
        private readonly TimeSpan _interval;
        private readonly Action<Exception>? _onError;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _loop;
        private int _started;

        public PeriodicWorker(
            Func<CancellationToken, Task> work,
            TimeSpan interval,
            Action<Exception>? onError = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _work = work ?? throw new ArgumentNullException(nameof(work));
            if (interval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must not be negative.");
            _interval = interval;
            _onError = onError;
            _delay = delay ?? ((ts, ct) => Task.Delay(ts, ct));
        }

        /// <summary>Starts the loop. Throws if already started.</summary>
        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
                throw new InvalidOperationException("The worker has already been started.");
            _loop = RunAsync(_cts.Token);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _work(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _onError?.Invoke(ex);
                }

                try
                {
                    await _delay(_interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Stops the loop and waits for the in-flight iteration to finish. Do not call from within the work
        /// callback itself — that would wait on the loop that is calling it.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            try { _loop?.Wait(); }
            catch (AggregateException) { /* cancellation surfaces here; ignore */ }
            _cts.Dispose();
        }
    }
}
