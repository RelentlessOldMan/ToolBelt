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

        /// <summary>When the job is next due (null until the scheduler starts, or if the schedule has no later occurrence).</summary>
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
    /// time, so a long sleep or clock change doesn't fire a burst of missed runs. Schedules are read in a time zone
    /// (default <see cref="TimeZoneInfo.Local"/>), so "at 02:30" stays 02:30 across daylight-saving changes; see
    /// <see cref="CronSchedule"/> for what happens to times a transition skips or repeats. Clock, delay and time zone are
    /// injectable so tests are deterministic. Stop with <see cref="DisposeAsync"/>/<see cref="Dispose"/>, which waits for
    /// running jobs.
    /// </summary>
    public sealed class CronScheduler : IDisposable
    {
        private readonly List<ScheduledJob> _jobs = new List<ScheduledJob>();
        private readonly List<(ScheduledJob Job, Task Task)> _running = new List<(ScheduledJob, Task)>();
        private static readonly AsyncLocal<ScheduledJob?> CurrentJob = new AsyncLocal<ScheduledJob?>();   // the job this flow is running, if any
        private readonly object _gate = new object();
        private readonly Func<DateTimeOffset> _now;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Action<ScheduledJob, Exception>? _onError;
        private readonly TimeZoneInfo _zone;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _loop;
        private int _skipped;
        private int _loopThread;   // the managed thread id while the loop is inside RunDue, else 0

        /// <param name="onError">Receives exceptions from jobs, and the failure when a job's schedule has no later occurrence.</param>
        /// <param name="clock">The current time; default <see cref="DateTimeOffset.Now"/>.</param>
        /// <param name="delay">How the loop waits; default <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
        /// <param name="timeZone">The zone whose wall clock the cron fields describe; default <see cref="TimeZoneInfo.Local"/>.</param>
        public CronScheduler(Action<ScheduledJob, Exception>? onError = null, Func<DateTimeOffset>? clock = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null, TimeZoneInfo? timeZone = null)
        {
            _onError = onError;
            _zone = timeZone ?? TimeZoneInfo.Local;
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
                if (_loop != null) job.NextRun = job.Schedule.GetNextOccurrence(_now(), _zone);
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

        /// <summary>
        /// Starts the background loop. A job whose schedule has no occurrence gets a null <see cref="ScheduledJob.NextRun"/>
        /// and the failure goes to <c>onError</c>; the other jobs still run.
        /// </summary>
        public void Start()
        {
            List<(ScheduledJob, Exception)>? failures = null;
            lock (_gate)
            {
                if (_loop != null) throw new InvalidOperationException("The scheduler has already been started.");
                DateTimeOffset now = _now();
                foreach (var job in _jobs)
                {
                    // One schedule with no occurrence (Feb 30) must not keep the others from running.
                    try { job.NextRun = job.Schedule.GetNextOccurrence(now, _zone); }
                    catch (Exception ex)
                    {
                        job.NextRun = null;
                        (failures ??= new List<(ScheduledJob, Exception)>()).Add((job, ex));
                    }
                }
                _loop = Task.Run(() => LoopAsync(_cts.Token));
            }
            if (failures != null)
                foreach (var (job, ex) in failures) Report(job, ex);   // outside the lock
        }

        /// <summary>
        /// Starts every job whose time has come (at or before the clock's now) and advances its next time. Called by the
        /// background loop; public so tests can drive the scheduler with a fake clock. Returns the number started. A job
        /// whose schedule has no later occurrence gets a null <see cref="ScheduledJob.NextRun"/> (it never runs again) and
        /// the failure goes to <c>onError</c>.
        /// </summary>
        public int RunDue()
        {
            int started = 0;
            List<(ScheduledJob, Exception)>? failures = null;
            lock (_gate)
            {
                if (_cts.IsCancellationRequested) return 0;          // disposing: start nothing new
                DateTimeOffset now = _now();
                foreach (var job in _jobs)
                {
                    if (job.NextRun is not DateTimeOffset due || due > now) continue;
                    try { job.NextRun = job.Schedule.GetNextOccurrence(now, _zone); }
                    catch (Exception ex)
                    {
                        job.NextRun = null;
                        (failures ??= new List<(ScheduledJob, Exception)>()).Add((job, ex));
                    }
                    if (job.IsRunning) { Interlocked.Increment(ref _skipped); continue; }
                    job.IsRunning = true;
                    job.LastRun = now;
                    started++;
                    Task t = RunJobAsync(job);
                    _running.Add((job, t));
                    _running.RemoveAll(r => r.Task.IsCompleted);
                }
            }
            if (failures != null)
                foreach (var (job, ex) in failures) Report(job, ex);   // outside the lock
            return started;
        }

        private async Task RunJobAsync(ScheduledJob job)
        {
            await Task.Yield();
            CurrentJob.Value = job;
            try { await job.Action(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
            catch (Exception ex) { Report(job, ex); }
            finally
            {
                lock (_gate)
                {
                    job.IsRunning = false;
                    job.RunCount++;
                }
            }
        }

        // An onError that throws must not stop the scheduler (or fault a job's task); its exception is dropped.
        private void Report(ScheduledJob job, Exception ex)
        {
            try { _onError?.Invoke(job, ex); }
            catch { }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                // Marks the loop's own synchronous call, so a Dispose from onError inside it doesn't wait for the loop.
                Volatile.Write(ref _loopThread, Environment.CurrentManagedThreadId);
                try { RunDue(); }
                finally { Volatile.Write(ref _loopThread, 0); }
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
        /// <remarks>
        /// Safe to call from inside a job (a "run once, then shut down" job): that job's own run is not waited for. Also
        /// safe from <c>onError</c>, where the loop itself may be the caller.
        /// </remarks>
        public async Task DisposeAsync()
        {
            _cts.Cancel();
            bool fromLoop = Volatile.Read(ref _loopThread) == Environment.CurrentManagedThreadId;
            if (_loop != null && !fromLoop) { try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { } }
            Task[] pending;
            ScheduledJob? self = CurrentJob.Value;
            lock (_gate)
            {
                // Snapshot after the loop has stopped (RunDue starts nothing once cancelled), minus the caller's own run —
                // waiting for it would wait forever.
                var list = new List<Task>();
                foreach (var r in _running) if (!ReferenceEquals(r.Job, self)) list.Add(r.Task);
                pending = list.ToArray();
            }
            try { await Task.WhenAll(pending).ConfigureAwait(false); } catch { /* reported via onError */ }
        }

        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();
    }
}
