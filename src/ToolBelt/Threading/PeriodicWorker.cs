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
        [ThreadStatic] private static PeriodicWorker? t_cancelling; // the worker whose token this thread is cancelling
        [ThreadStatic] private static PeriodicWorker? t_working;    // the worker whose work this thread is starting

        public PeriodicWorker(
            Func<CancellationToken, Task> work,
            TimeSpan interval,
            Action<Exception>? onError = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _work = work ?? throw new ArgumentNullException(nameof(work));
            if (interval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must not be negative.");
            if (delay is null && interval.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be at most int.MaxValue milliseconds (about 24.8 days), the longest Task.Delay accepts on every target (.NET 6+ alone accepts twice that).");
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
                    // Marked while the work runs synchronously on the loop's thread: a Dispose there (from a continuation the
                    // work completed inline, say) waited for the loop it was running on, and hung.
                    Task work;
                    PeriodicWorker? outer = t_working;
                    t_working = this;
                    try { work = _work(ct); }
                    finally { t_working = outer; }
                    // A null task was reported as NullReferenceException.
                    await (work ?? throw new InvalidOperationException("The work returned a null task.")).ConfigureAwait(false);
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
                    await (_delay(_interval, ct) ?? throw new InvalidOperationException("The delay returned a null task.")).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) // a delay's own OCE is a failure, reported
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

        // The loop is this thread: onError, or the work's synchronous part (and what it runs inline).
        private bool LoopIsCaller => ReferenceEquals(t_reporting, this) || ReferenceEquals(t_working, this);

        /// <summary>
        /// Stops the loop and waits for the in-flight iteration to finish. Called from the <c>onError</c> callback, or
        /// from the work's synchronous part (directly or from a continuation it runs inline), it stops the loop
        /// without waiting, since the loop is the caller. Do not call it from the work after its first await: that
        /// waits on the loop that is calling it.
        /// </summary>
        public void Dispose()
        {
            Task? loop;
            bool first;
            lock (_gate)
            {
                first = Interlocked.Exchange(ref _disposed, 1) == 0;
                loop = _loop;
            }
            if (!first)
            {
                // A second Dispose waits for the loop too: it returned while the first was still waiting, with the
                // iteration still running (R97-3). Not from onError, whose loop is this thread, nor from a callback on the
                // token that the first Dispose is running: the loop's own cancellation may be queued behind it.
                if (!LoopIsCaller && !ReferenceEquals(t_cancelling, this))
                {
                    try { loop?.Wait(); }
                    catch (AggregateException) { /* cancellation surfaces here; ignore */ }
                }
                return;
            }
            // A callback the work registered on its token may throw here; it threw out of Dispose, skipping the wait.
            AggregateException? cancelError = null;
            PeriodicWorker? outerCancelling = t_cancelling;
            t_cancelling = this;
            try { _cts.Cancel(); }
            catch (AggregateException ex) { cancelError = ex; }
            finally { t_cancelling = outerCancelling; }
            // From onError the loop is this thread: waiting for it deadlocked. It stops once onError returns; the
            // token source stays undisposed because the loop still reads its token.
            if (!LoopIsCaller)
            {
                try { loop?.Wait(); }
                catch (AggregateException) { /* cancellation surfaces here; ignore */ }
                _cts.Dispose();
            }
            if (cancelError != null) Report(cancelError);
        }
    }
}
