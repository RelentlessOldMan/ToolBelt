// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (System.Diagnostics, kernel32).
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SystemProcess = System.Diagnostics.Process;

namespace ToolBelt.Windows
{
    /// <summary>A CPU + memory usage reading for a process over a measured interval.</summary>
    public readonly struct ResourceSample
    {
        internal ResourceSample(double cpuPercent, long workingSet, long privateBytes, TimeSpan interval)
        {
            CpuPercent = cpuPercent;
            WorkingSetBytes = workingSet;
            PrivateBytes = privateBytes;
            Interval = interval;
        }

        /// <summary>Average CPU utilisation over <see cref="Interval"/>, normalised across all logical cores (0–100).</summary>
        public double CpuPercent { get; }

        /// <summary>Physical memory in use (working set) at the end of the interval, in bytes.</summary>
        public long WorkingSetBytes { get; }

        /// <summary>Private (committed) memory at the end of the interval, in bytes.</summary>
        public long PrivateBytes { get; }

        /// <summary>The wall-clock span the CPU figure was averaged over.</summary>
        public TimeSpan Interval { get; }

        public override string ToString() =>
            $"CPU {CpuPercent:0.0}% over {Interval.TotalMilliseconds:0} ms, WS {WorkingSetBytes:N0} B";
    }

    /// <summary>
    /// Samples a process's CPU and memory use. Construct one against a process to capture a baseline, then
    /// call <see cref="Sample"/> whenever you want a reading — the CPU figure covers the span since the
    /// previous call (or since construction), so the caller controls the timing and nothing blocks. The
    /// static <see cref="Measure(System.Diagnostics.Process, TimeSpan, CancellationToken)"/> helpers block for a fixed interval
    /// when a one-shot reading is more convenient. CPU is normalised across logical processors: 100% means
    /// every core was fully busy. Disposing a sampler from <see cref="ForCurrentProcess"/> releases the process object it
    /// opened; a sampler over a caller's process leaves that process to the caller.
    /// </summary>
    public sealed class ResourceSampler : IDisposable
    {
        private readonly SystemProcess _process;
        private bool _ownsProcess;
        private readonly int _cores;
        private TimeSpan _lastCpu;
        private long _lastTicks;

        /// <summary>Creates a sampler for <paramref name="process"/> and captures the baseline immediately.</summary>
        public ResourceSampler(SystemProcess process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
            // The machine's logical processors: Environment.ProcessorCount is only this process's affinity / job CPU limit,
            // which overstates the target's share when the sampler itself runs pinned or in a job.
            _cores = (int)Math.Max(1, GetActiveProcessorCount(ALL_PROCESSOR_GROUPS));
            _lastCpu = _process.TotalProcessorTime;
            _lastTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>Creates a sampler for the current process.</summary>
        public static ResourceSampler ForCurrentProcess()
        {
            var process = SystemProcess.GetCurrentProcess();
            try { return new ResourceSampler(process) { _ownsProcess = true }; }
            catch { process.Dispose(); throw; }
        }

        /// <summary>Releases the process object if this sampler opened it (<see cref="ForCurrentProcess"/>).</summary>
        public void Dispose()
        {
            if (_ownsProcess) _process.Dispose();
        }

        /// <summary>
        /// Returns a reading covering the span since the previous <see cref="Sample"/> (or construction) and
        /// resets the baseline. If no measurable time has elapsed, <see cref="ResourceSample.CpuPercent"/> is 0.
        /// Throws if the target process has exited (its processor-time/memory counters are no longer available).
        /// </summary>
        public ResourceSample Sample()
        {
            _process.Refresh();
            long nowTicks = Stopwatch.GetTimestamp();
            TimeSpan nowCpu = _process.TotalProcessorTime;

            double elapsedSeconds = (nowTicks - _lastTicks) / (double)Stopwatch.Frequency;
            double cpuSeconds = (nowCpu - _lastCpu).TotalSeconds;
            double percent = elapsedSeconds > 0 ? cpuSeconds / (elapsedSeconds * _cores) * 100.0 : 0.0;
            if (percent < 0) percent = 0; // guard against counter resets
            // The processor-time counters tick coarsely (about 15.6 ms), so a short sample of a busy process overshot.
            if (percent > 100) percent = 100;

            _lastCpu = nowCpu;
            _lastTicks = nowTicks;

            return new ResourceSample(percent, _process.WorkingSet64, _process.PrivateMemorySize64,
                TimeSpan.FromSeconds(elapsedSeconds));
        }

        /// <summary>
        /// Blocks for <paramref name="interval"/> and returns a single CPU/memory reading for <paramref name="process"/>.
        /// Cancelling <paramref name="cancellationToken"/> ends the wait at once with <see cref="OperationCanceledException"/>.
        /// </summary>
        public static ResourceSample Measure(SystemProcess process, TimeSpan interval, CancellationToken cancellationToken = default)
        {
            if (process is null) throw new ArgumentNullException(nameof(process));
            if (interval <= TimeSpan.Zero || interval.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive and at most about 24.8 days.");
            cancellationToken.ThrowIfCancellationRequested();
            var sampler = new ResourceSampler(process);
            // On the token's handle, not Thread.Sleep, so a long interval can be cut short.
            if (cancellationToken.WaitHandle.WaitOne(interval)) cancellationToken.ThrowIfCancellationRequested();
            return sampler.Sample();
        }

        /// <summary>Blocks for <paramref name="interval"/> and returns a single reading for the current process.</summary>
        public static ResourceSample MeasureCurrent(TimeSpan interval, CancellationToken cancellationToken = default)
        {
            using var process = SystemProcess.GetCurrentProcess();
            return Measure(process, interval, cancellationToken);
        }

        private const ushort ALL_PROCESSOR_GROUPS = 0xFFFF;

        [DllImport("kernel32.dll")]
        private static extern uint GetActiveProcessorCount(ushort groupNumber);
    }
}
