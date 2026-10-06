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
            TimeSpan t = timeout ?? _defaultTimeout;
            ValidateTimeout(t, nameof(timeout));
            foreach (var existing in _probes)
                if (string.Equals(existing.Name, name, StringComparison.Ordinal))
                    throw new ArgumentException($"A probe named '{name}' is already registered.", nameof(name));
            _probes.Add(new Registration(name, probe, t, failureStatus));
            return this;
        }

        /// <summary>
        /// Registers a synchronous probe. It runs on the thread pool so a blocking probe can still be timed out
        /// (and abandoned) without stalling the others.
        /// </summary>
        public HealthCheck Add(
            string name,
            Func<ProbeResult> probe,
            TimeSpan? timeout = null,
            HealthStatus failureStatus = HealthStatus.Unhealthy)
        {
            if (probe is null) throw new ArgumentNullException(nameof(probe));
            return Add(name, _ => Task.Run(probe), timeout, failureStatus);
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
            probeCts.CancelAfter(reg.Timeout);

            Task<ProbeResult> probeTask;
            try
            {
                // Started on the thread pool: an "async" probe that blocks before its first await would otherwise run on this
                // thread, defeating the timeout race and holding up every probe after it.
                CancellationToken probeToken = probeCts.Token;
                probeTask = Task.Run(() => reg.Probe(probeToken) ?? throw new InvalidOperationException("The probe returned a null task."));
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && outer.IsCancellationRequested))
            {
                // A probe that throws synchronously, before producing a task.
                return new ProbeReport(reg.Name, reg.FailureStatus, ex.Message, watch.Elapsed, false, ex);
            }

            Task timeoutTask = Task.Delay(reg.Timeout, delayCts.Token);
            Task winner = await Task.WhenAny(probeTask, timeoutTask).ConfigureAwait(false);
            delayCts.Cancel(); // release the timer if the probe won

            if (winner != probeTask)
            {
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
                return new ProbeReport(reg.Name, reg.FailureStatus, ex.Message, watch.Elapsed, false, ex);
            }
        }

        private static void ObserveLater(Task task)
            => task.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        private static void ValidateTimeout(TimeSpan timeout, string paramName)
        {
            if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(paramName, timeout, "Timeout must be positive (or Timeout.InfiniteTimeSpan).");
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
        }
    }
}
