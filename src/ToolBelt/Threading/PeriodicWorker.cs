// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// Runs an asynchronous action repeatedly on an interval until disposed: it catches and reports handler
    /// exceptions (so one failure does not kill the loop), stops gracefully on dispose (waiting for the
    /// in-flight iteration), and takes an injectable delay so tests are deterministic. The loop runs on the
    /// thread pool; the first run happens immediately and subsequent runs wait one interval. If the delay
    /// itself fails (other than by cancellation), the failure is reported and the loop stops. Replaces the timer-plus-boolean pattern every tool
    /// improvises and gets subtly wrong.
    /// </summary>
    public sealed class PeriodicWorker : IDisposable
    {
        private readonly Func<CancellationToken, Task> _work;
        private readonly TimeSpan _interval;
        private readonly Action<Exception>? _onError;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _gate = new object();
        private Task? _loop;
        private int _started;
        private int _disposed;
        [ThreadStatic] private static PeriodicWorker? t_reporting; // the worker whose onError this thread is running

        public PeriodicWorker(
            Func<CancellationToken, Task> work,
            TimeSpan interval,
            Action<Exception>? onError = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _work = work ?? throw new ArgumentNullException(nameof(work));
            if (interval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must not be negative.");
            if (delay is null && interval.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be at most int.MaxValue milliseconds (about 24.8 days), the longest Task.Delay accepts.");
            _interval = interval;
            _onError = onError;
            _delay = delay ?? ((ts, ct) => Task.Delay(ts, ct));
        }

        /// <summary>Starts the loop. Throws if already started, or <see cref="ObjectDisposedException"/> once disposed.</summary>
        public void Start()
        {
            // Under the gate with Dispose: a Dispose between starting the loop and publishing it neither waited for
            // it nor stopped it before the token source was disposed.
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) == 1)
                    throw new ObjectDisposedException(nameof(PeriodicWorker));
                if (Interlocked.Exchange(ref _started, 1) == 1)
                    throw new InvalidOperationException("The worker has already been started.");
                // On the pool, not the caller's thread: a zero interval with synchronous work would otherwise never
                // return, and work resuming on a UI context would deadlock Dispose called from that thread.
                CancellationToken ct = _cts.Token;
                _loop = Task.Run(() => RunAsync(ct));
            }
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
                    Report(ex);
                }

                try
                {
                    await _delay(_interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Report(ex);
                    break;
                }
            }
        }

        private void Report(Exception ex)
        {
            PeriodicWorker? outer = t_reporting;
            t_reporting = this;
            try { _onError?.Invoke(ex); }
            catch { /* a throwing error sink must not kill the loop */ }
            finally { t_reporting = outer; }
        }

        /// <summary>
        /// Stops the loop and waits for the in-flight iteration to finish. Do not call from within the work
        /// callback itself — that would wait on the loop that is calling it. Called from the <c>onError</c> callback
        /// it stops the loop without waiting, since the loop is the caller.
        /// </summary>
        public void Dispose()
        {
            Task? loop;
            lock (_gate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
                loop = _loop;
            }
            _cts.Cancel();
            // From onError the loop is this thread: waiting for it deadlocked. It stops once onError returns; the
            // token source stays undisposed because the loop still reads its token.
            if (ReferenceEquals(t_reporting, this)) return;
            try { loop?.Wait(); }
            catch (AggregateException) { /* cancellation surfaces here; ignore */ }
            _cts.Dispose();
        }
    }
}
