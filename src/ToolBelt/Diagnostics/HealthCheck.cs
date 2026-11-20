// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Diagnostics
{
    /// <summary>Health of a probe or of a whole check, in increasing severity.</summary>
    public enum HealthStatus
    {
        Healthy = 0,
        Degraded = 1,
        Unhealthy = 2,
    }

    /// <summary>What a probe reports: a status and an optional human-readable message.</summary>
    public readonly struct ProbeResult
    {
        public ProbeResult(HealthStatus status, string? message = null)
        {
            // An undefined status (−1) ranked below Healthy, so the report stayed Healthy.
            if (status < HealthStatus.Healthy || status > HealthStatus.Unhealthy)
                throw new ArgumentOutOfRangeException(nameof(status), status, "Status must be Healthy, Degraded or Unhealthy.");
            Status = status;
            Message = message;
        }

        public HealthStatus Status { get; }
        public string? Message { get; }

        public static ProbeResult Healthy(string? message = null) => new ProbeResult(HealthStatus.Healthy, message);
        public static ProbeResult Degraded(string? message = null) => new ProbeResult(HealthStatus.Degraded, message);
        public static ProbeResult Unhealthy(string? message = null) => new ProbeResult(HealthStatus.Unhealthy, message);
    }

    /// <summary>The outcome of one probe within a <see cref="HealthReport"/>.</summary>
    public sealed class ProbeReport
    {
        internal ProbeReport(string name, HealthStatus status, string? message, TimeSpan duration, bool timedOut, Exception? exception)
        {
            Name = name;
            Status = status;
            Message = message;
            Duration = duration;
            TimedOut = timedOut;
            Exception = exception;
        }

        public string Name { get; }
        public HealthStatus Status { get; }
        public string? Message { get; }

        /// <summary>How long the probe ran (capped at its timeout when it timed out).</summary>
        public TimeSpan Duration { get; }

        /// <summary>True if the probe did not finish within its timeout.</summary>
        public bool TimedOut { get; }

        /// <summary>The exception the probe threw, if any.</summary>
        public Exception? Exception { get; }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture, "{0}: {1} ({2:0} ms){3}",
            Name, Status, Duration.TotalMilliseconds, string.IsNullOrEmpty(Message) ? "" : " — " + Message);
    }

    /// <summary>The aggregated result of running a <see cref="HealthCheck"/>.</summary>
    public sealed class HealthReport
    {
        internal HealthReport(IReadOnlyList<ProbeReport> probes, TimeSpan duration)
        {
            Probes = probes;
            Duration = duration;
            var worst = HealthStatus.Healthy;
            foreach (var p in probes)
                if (p.Status > worst) worst = p.Status;
            Status = worst;
        }

        /// <summary>The worst status of any probe (Healthy when there are none).</summary>
        public HealthStatus Status { get; }

        /// <summary>Per-probe outcomes, in registration order.</summary>
        public IReadOnlyList<ProbeReport> Probes { get; }

        /// <summary>Wall-clock time for the whole run.</summary>
        public TimeSpan Duration { get; }

        public bool IsHealthy => Status == HealthStatus.Healthy;

        /// <summary>A multi-line summary: overall status, then one line per probe.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("Overall: ").Append(Status)
              .Append(" (").Append(Duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)).Append(" ms)");
            foreach (var p in Probes)
                sb.Append('\n').Append("  ").Append(p);
            return sb.ToString();
        }
    }

    /// <summary>
    /// A named set of health probes — "can I reach the output location, is the device present, is there disk
    /// space" — run together into one <see cref="HealthReport"/> with per-probe status, timing and messages. The
    /// overall status is the worst probe's.
    /// <para>
    /// Every probe has a timeout (the instance default unless given its own). The probe receives a token that is
    /// cancelled at the timeout, but the timeout is enforced by racing the probe rather than trusting it, so a
    /// probe that ignores its token still reports as timed out on schedule (it is abandoned and left to finish in
    /// the background; any later exception is observed so it is never raised as unobserved). A probe that throws
    /// or times out reports its <c>failureStatus</c> — <see cref="HealthStatus.Unhealthy"/> by default, or pass
    /// <see cref="HealthStatus.Degraded"/> for a non-critical dependency. Probes run concurrently, so they must
    /// not depend on one another's side effects. Cancelling the token passed to <see cref="RunAsync"/> abandons
    /// the whole run with <see cref="OperationCanceledException"/>. Register probes before running; registration
    /// is not thread-safe with a concurrent run.
    /// </para>
    /// </summary>
    public sealed class HealthCheck
    {
        private readonly List<Registration> _probes = new List<Registration>();
        private readonly TimeSpan _defaultTimeout;

        /// <summary>Creates a health check whose probes time out after <paramref name="defaultTimeout"/> (5 s if omitted).</summary>
        public HealthCheck(TimeSpan? defaultTimeout = null)
        {
            _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(5);
            ValidateTimeout(_defaultTimeout, nameof(defaultTimeout));
        }

        /// <summary>The number of registered probes.</summary>
        public int Count => _probes.Count;

        /// <summary>Registers an asynchronous probe.</summary>
        public HealthCheck Add(
            string name,
            Func<CancellationToken, Task<ProbeResult>> probe,
            TimeSpan? timeout = null,
            HealthStatus failureStatus = HealthStatus.Unhealthy)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (name.Length == 0) throw new ArgumentException("Name must not be empty.", nameof(name));
            if (probe is null) throw new ArgumentNullException(nameof(probe));
            if (failureStatus == HealthStatus.Healthy)
                throw new ArgumentException("A failure cannot be reported as Healthy.", nameof(failureStatus));
            // An undefined value ((HealthStatus)(-1)) ranked below Healthy, so a failing probe left the report healthy.
            if (failureStatus != HealthStatus.Degraded && failureStatus != HealthStatus.Unhealthy)
                throw new ArgumentOutOfRangeException(nameof(failureStatus), failureStatus, "A failure must be reported as Degraded or Unhealthy.");
            TimeSpan t = timeout ?? _defaultTimeout;
            ValidateTimeout(t, nameof(timeout));
            foreach (var existing in _probes)
                if (string.Equals(existing.Name, name, StringComparison.Ordinal))
                    throw new ArgumentException($"A probe named '{name}' is already registered.", nameof(name));
            _probes.Add(new Registration(name, probe, t, failureStatus));
            return this;
        }

        /// <summary>
        /// Registers a synchronous probe. It runs on a thread of its own so a blocking probe can still be timed out
        /// (and abandoned) without stalling the others. While an abandoned run has not returned, later checks don't start
        /// another: they report <paramref name="failureStatus"/> straight away.
        /// </summary>
        public HealthCheck Add(
            string name,
            Func<ProbeResult> probe,
            TimeSpan? timeout = null,
            HealthStatus failureStatus = HealthStatus.Unhealthy)
        {
            if (probe is null) throw new ArgumentNullException(nameof(probe));
            // A probe that hangs took one more pool thread on every check, without limit. While the last run is abandoned
            // (its token cancelled: timed out, or its check cancelled) and still going, the probe reports its failure status
            // at once. Only then: two checks at once reported a healthy probe failed, its other run merely queued.
            Tuple<Task<ProbeResult>, CancellationToken>? last = null;
            return Add(name, token =>
            {
                var previous = Volatile.Read(ref last);
                if (previous != null && !previous.Item1.IsCompleted && previous.Item2.IsCancellationRequested)
                    return Task.FromResult(new ProbeResult(failureStatus, "The previous run of this probe has not returned."));
                // Its own thread, as for the start: a blocking probe on the pool starved the timeout's timer of a thread.
                Task<ProbeResult> run = Task.Factory.StartNew(probe, CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Volatile.Write(ref last, Tuple.Create(run, token));
                return run;
            }, timeout, failureStatus);
        }

        /// <summary>
        /// Registers a pass/fail probe: true is Healthy, false reports <paramref name="failureStatus"/> with
        /// <paramref name="failureMessage"/>.
        /// </summary>
        public HealthCheck Add(
            string name,
            Func<bool> probe,
            string failureMessage,
            TimeSpan? timeout = null,
            HealthStatus failureStatus = HealthStatus.Unhealthy)
        {
            if (probe is null) throw new ArgumentNullException(nameof(probe));
            return Add(
                name,
                () => probe() ? ProbeResult.Healthy() : new ProbeResult(failureStatus, failureMessage),
                timeout,
                failureStatus);
        }

        /// <summary>Runs every probe concurrently and aggregates the results.</summary>
        public async Task<HealthReport> RunAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var total = Stopwatch.StartNew();
            var running = new Task<ProbeReport>[_probes.Count];
            for (int i = 0; i < running.Length; i++)
                running[i] = RunOneAsync(_probes[i], cancellationToken);
            ProbeReport[] reports = await Task.WhenAll(running).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            total.Stop();
            return new HealthReport(reports, total.Elapsed);
        }

        private static async Task<ProbeReport> RunOneAsync(Registration reg, CancellationToken outer)
        {
            var watch = Stopwatch.StartNew();
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            // Cancelled below when the timeout wins, not by CancelAfter: its timer could lose the race to the delay (then the
            // source was disposed with the timer pending, and the abandoned probe's token was never cancelled), and a callback
            // the probe registered that threw on the timer's thread ended the process.

            Task<ProbeResult> probeTask;
            try
            {
                // Started on another thread: an "async" probe that blocks before its first await would otherwise run on this
                // thread, defeating the timeout race and holding up every probe after it. While the last start has not
                // returned (the probe is still blocked before its first await), no new one starts, as for a synchronous
                // probe: each check took one more pool thread, without limit. Only an abandoned start (its token
                // cancelled) counts: two checks at once reported the other's start, merely queued, as hung.
                var previous = Volatile.Read(ref reg.Starting);
                if (previous != null && !previous.Item1.IsCompleted && previous.Item2.IsCancellationRequested)
                    return new ProbeReport(reg.Name, reg.FailureStatus, "The previous run of this probe has not returned.", watch.Elapsed, false, null);
                CancellationToken probeToken = probeCts.Token;
                // On a thread of its own (LongRunning), not a pool thread: with the pool starved (one core), a probe that
                // blocked held the only worker, the timeout's continuation waited behind it, and a probe hung 400 ms
                // against a 100 ms timeout reported Healthy.
                Task<Task<ProbeResult>> start = Task.Factory.StartNew(
                    () => reg.Probe(probeToken) ?? throw new InvalidOperationException("The probe returned a null task."),
                    CancellationToken.None, TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Volatile.Write(ref reg.Starting, Tuple.Create(start, probeToken));
                probeTask = start.Unwrap();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && outer.IsCancellationRequested))
            {
                // A probe that throws synchronously, before producing a task.
                return new ProbeReport(reg.Name, reg.FailureStatus, MessageOf(ex), watch.Elapsed, false, ex);
            }

            Task timeoutTask = Task.Delay(WholeMilliseconds(reg.Timeout), delayCts.Token);
            Task winner = await Task.WhenAny(probeTask, timeoutTask).ConfigureAwait(false);
            delayCts.Cancel(); // release the timer if the probe won

            if (winner != probeTask)
            {
                // A callback the probe registered may throw; the probe is abandoned, so that is dropped like its outcome.
                try { probeCts.Cancel(); }
                catch (AggregateException) { }
                ObserveLater(probeTask); // abandoned either way; never let its later fault go unobserved
                outer.ThrowIfCancellationRequested();
                return new ProbeReport(reg.Name, reg.FailureStatus,
                    "Timed out after " + reg.Timeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms.",
                    reg.Timeout, true, null);
            }

            watch.Stop();
            try
            {
                ProbeResult result = await probeTask.ConfigureAwait(false);
                return new ProbeReport(reg.Name, result.Status, result.Message, watch.Elapsed, false, null);
            }
            catch (OperationCanceledException) when (outer.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex) when (probeCts.IsCancellationRequested)
            {
                // The probe honoured its timeout token just before the race noticed — still a timeout.
                return new ProbeReport(reg.Name, reg.FailureStatus,
                    "Timed out after " + reg.Timeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms.",
                    watch.Elapsed, true, ex);
            }
            catch (Exception ex)
            {
                return new ProbeReport(reg.Name, reg.FailureStatus, MessageOf(ex), watch.Elapsed, false, ex);
            }
        }

        // An exception whose Message threw faulted the whole run, losing every probe's result.
        private static string MessageOf(Exception ex)
        {
            try { return ex.Message; }
            catch (Exception inner) { return "(its Message threw " + inner.GetType().FullName + ")"; }
        }

        private static void ObserveLater(Task task)
            => task.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        // Rounded up to a whole millisecond: the timers truncate, so a timeout under 1 ms raced a zero delay and a probe
        // that returned at once was sometimes reported as timed out.
        private static TimeSpan WholeMilliseconds(TimeSpan t)
            => t <= TimeSpan.Zero ? t : TimeSpan.FromTicks((t.Ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond);

        private static void ValidateTimeout(TimeSpan timeout, string paramName)
        {
            if (timeout == Timeout.InfiniteTimeSpan) return;
            // The timers behind the timeout (CancelAfter, Task.Delay) take at most int.MaxValue ms (~24.8 days) everywhere.
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(paramName, timeout,
                    "Timeout must be positive and at most int.MaxValue milliseconds (or Timeout.InfiniteTimeSpan).");
        }

        private sealed class Registration
        {
            public Registration(string name, Func<CancellationToken, Task<ProbeResult>> probe, TimeSpan timeout, HealthStatus failureStatus)
            {
                Name = name;
                Probe = probe;
                Timeout = timeout;
                FailureStatus = failureStatus;
            }

            public string Name { get; }
            public Func<CancellationToken, Task<ProbeResult>> Probe { get; }
            public TimeSpan Timeout { get; }
            public HealthStatus FailureStatus { get; }

            // The last start of the probe (the call that returns its task) and its run's token.
            public Tuple<Task<Task<ProbeResult>>, CancellationToken>? Starting;
        }
    }
}
