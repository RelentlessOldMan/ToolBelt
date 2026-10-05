// ToolBelt drop-in — also copy Time/CronSchedule.cs.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Time
{
    /// <summary>A job registered with a <see cref="CronScheduler"/>.</summary>
    public sealed class ScheduledJob
    {
        internal ScheduledJob(string name, CronSchedule schedule, Func<CancellationToken, Task> action)
        {
            Name = name;
            Schedule = schedule;
            Action = action;
        }

        public string Name { get; }
        public CronSchedule Schedule { get; }
        internal Func<CancellationToken, Task> Action { get; }

        /// <summary>When the job is next due (null until the scheduler starts).</summary>
        public DateTimeOffset? NextRun { get; internal set; }

        /// <summary>When the last run started.</summary>
        public DateTimeOffset? LastRun { get; internal set; }

        /// <summary>Number of completed runs (successful or not).</summary>
        public int RunCount { get; internal set; }

        /// <summary>True while a run is in progress.</summary>
        public bool IsRunning { get; internal set; }
    }

    /// <summary>
    /// Runs jobs on cron schedules: "at 02:30 every weekday", "every 15 minutes". Each job's next time comes from its
    /// <see cref="CronSchedule"/>; jobs run concurrently with each other but never overlap themselves — if a run is still
    /// going when the next is due, that occurrence is skipped (and counted in <see cref="Skipped"/>) rather than queued.
    /// Exceptions are reported to <c>onError</c> and never stop the scheduler; waking is recomputed from the clock each
    /// time, so a long sleep or clock change doesn't fire a burst of missed runs. Clock and delay are injectable so tests
    /// are deterministic. Stop with <see cref="DisposeAsync"/>/<see cref="Dispose"/>, which waits for running jobs.
    /// </summary>
    public sealed class CronScheduler : IDisposable
    {
        private readonly List<ScheduledJob> _jobs = new List<ScheduledJob>();
        private readonly List<Task> _running = new List<Task>();
        private readonly object _gate = new object();
        private readonly Func<DateTimeOffset> _now;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Action<ScheduledJob, Exception>? _onError;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _loop;
        private int _skipped;

        public CronScheduler(Action<ScheduledJob, Exception>? onError = null, Func<DateTimeOffset>? clock = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _onError = onError;
            _now = clock ?? (() => DateTimeOffset.Now);
            _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
        }

        /// <summary>Occurrences skipped because the job's previous run was still in progress.</summary>
        public int Skipped => Volatile.Read(ref _skipped);

        public IReadOnlyList<ScheduledJob> Jobs { get { lock (_gate) return _jobs.ToArray(); } }

        /// <summary>Registers a job (before or after <see cref="Start"/>).</summary>
        public ScheduledJob Add(string name, string cronExpression, Func<CancellationToken, Task> action)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (action is null) throw new ArgumentNullException(nameof(action));
            var job = new ScheduledJob(name, new CronSchedule(cronExpression), action);
            lock (_gate)
            {
                if (_loop != null) job.NextRun = job.Schedule.GetNextOccurrence(_now());
                _jobs.Add(job);
            }
            return job;
        }

        /// <summary>Synchronous convenience overload.</summary>
        public ScheduledJob Add(string name, string cronExpression, Action action)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));
            return Add(name, cronExpression, _ => { action(); return Task.CompletedTask; });
        }

        public bool Remove(ScheduledJob job)
        {
            lock (_gate) return _jobs.Remove(job);
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_loop != null) throw new InvalidOperationException("The scheduler has already been started.");
                DateTimeOffset now = _now();
                foreach (var job in _jobs) job.NextRun = job.Schedule.GetNextOccurrence(now);
                _loop = Task.Run(() => LoopAsync(_cts.Token));
            }
        }

        /// <summary>
        /// Starts every job whose time has come (at or before the clock's now) and advances its next time. Called by the
        /// background loop; public so tests can drive the scheduler with a fake clock. Returns the number started.
        /// </summary>
        public int RunDue()
        {
            int started = 0;
            lock (_gate)
            {
                DateTimeOffset now = _now();
                foreach (var job in _jobs)
                {
                    if (job.NextRun is not DateTimeOffset due || due > now) continue;
                    job.NextRun = job.Schedule.GetNextOccurrence(now);
                    if (job.IsRunning) { Interlocked.Increment(ref _skipped); continue; }
                    job.IsRunning = true;
                    job.LastRun = now;
                    started++;
                    Task t = RunJobAsync(job);
                    _running.Add(t);
                    _running.RemoveAll(r => r.IsCompleted);
                }
            }
            return started;
        }

        private async Task RunJobAsync(ScheduledJob job)
        {
            await Task.Yield();
            try { await job.Action(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
            catch (Exception ex) { _onError?.Invoke(job, ex); }
            finally
            {
                lock (_gate)
                {
                    job.IsRunning = false;
                    job.RunCount++;
                }
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                RunDue();
                TimeSpan wait;
                lock (_gate)
                {
                    DateTimeOffset now = _now(), next = DateTimeOffset.MaxValue;
                    foreach (var job in _jobs) if (job.NextRun is DateTimeOffset n && n < next) next = n;
                    // Re-check at least once a minute so clock changes and newly added jobs are noticed.
                    wait = next == DateTimeOffset.MaxValue ? TimeSpan.FromMinutes(1) : next - now;
                    if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                    if (wait > TimeSpan.FromMinutes(1)) wait = TimeSpan.FromMinutes(1);
                }
                try { await _delay(wait, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>Stops scheduling, cancels the token passed to running jobs and waits for them to finish.</summary>
        public async Task DisposeAsync()
        {
            _cts.Cancel();
            Task[] pending;
            lock (_gate)
            {
                pending = _running.ToArray();
            }
            if (_loop != null) { try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { } }
            try { await Task.WhenAll(pending).ConfigureAwait(false); } catch { /* reported via onError */ }
        }

        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();
    }
}
