// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (System.Diagnostics).
using System;
using System.Diagnostics;
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
    /// static <see cref="Measure(System.Diagnostics.Process, TimeSpan)"/> helpers block for a fixed interval
    /// when a one-shot reading is more convenient. CPU is normalised across logical processors: 100% means
    /// every core was fully busy.
    /// </summary>
    public sealed class ResourceSampler
    {
        private readonly SystemProcess _process;
        private readonly int _cores;
        private TimeSpan _lastCpu;
        private long _lastTicks;

        /// <summary>Creates a sampler for <paramref name="process"/> and captures the baseline immediately.</summary>
        public ResourceSampler(SystemProcess process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
            _cores = Math.Max(1, Environment.ProcessorCount);
            _lastCpu = _process.TotalProcessorTime;
            _lastTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>Creates a sampler for the current process.</summary>
        public static ResourceSampler ForCurrentProcess() => new ResourceSampler(SystemProcess.GetCurrentProcess());

        /// <summary>
        /// Returns a reading covering the span since the previous <see cref="Sample"/> (or construction) and
        /// resets the baseline. If no measurable time has elapsed, <see cref="ResourceSample.CpuPercent"/> is 0.
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

            _lastCpu = nowCpu;
            _lastTicks = nowTicks;

            return new ResourceSample(percent, _process.WorkingSet64, _process.PrivateMemorySize64,
                TimeSpan.FromSeconds(elapsedSeconds));
        }

        /// <summary>Blocks for <paramref name="interval"/> and returns a single CPU/memory reading for <paramref name="process"/>.</summary>
        public static ResourceSample Measure(SystemProcess process, TimeSpan interval)
        {
            if (process is null) throw new ArgumentNullException(nameof(process));
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
            var sampler = new ResourceSampler(process);
            System.Threading.Thread.Sleep(interval);
            return sampler.Sample();
        }

        /// <summary>Blocks for <paramref name="interval"/> and returns a single reading for the current process.</summary>
        public static ResourceSample MeasureCurrent(TimeSpan interval) => Measure(SystemProcess.GetCurrentProcess(), interval);
    }
}
