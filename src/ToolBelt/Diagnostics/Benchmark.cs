// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ToolBelt.Diagnostics
{
    /// <summary>The timing and allocation statistics for one benchmarked candidate.</summary>
    public sealed class BenchmarkResult
    {
        internal BenchmarkResult(string name, int iterations, double meanNs, double minNs, double maxNs,
            double medianNs, double p95Ns, double stdDevNs, long allocatedBytesPerOperation)
        {
            Name = name;
            Iterations = iterations;
            MeanNanoseconds = meanNs;
            MinNanoseconds = minNs;
            MaxNanoseconds = maxNs;
            MedianNanoseconds = medianNs;
            Percentile95Nanoseconds = p95Ns;
            StdDevNanoseconds = stdDevNs;
            AllocatedBytesPerOperation = allocatedBytesPerOperation;
            OperationsPerSecond = meanNs > 0 ? 1e9 / meanNs : 0;
        }

        public string Name { get; }
        public int Iterations { get; }
        public double MeanNanoseconds { get; }
        public double MinNanoseconds { get; }
        public double MaxNanoseconds { get; }
        public double MedianNanoseconds { get; }
        public double Percentile95Nanoseconds { get; }
        public double StdDevNanoseconds { get; }
        public double OperationsPerSecond { get; }
        /// <summary>Bytes allocated per operation. Accurate on modern runtimes; a best-effort estimate on the legacy target.</summary>
        public long AllocatedBytesPerOperation { get; }
        /// <summary>Mean time relative to the baseline candidate (1.0 for the baseline). Set only by <see cref="Benchmark.Compare"/>.</summary>
        public double RelativeToBaseline { get; internal set; } = 1.0;

        public override string ToString()
            => $"{Name}: {MeanNanoseconds:F1} ns/op, {OperationsPerSecond:N0} ops/s, {AllocatedBytesPerOperation} B/op";
    }

    /// <summary>
    /// A micro-benchmark harness: warms up, times many iterations, and reports per-iteration mean, min, max,
    /// median, 95th percentile, standard deviation, operations per second and bytes allocated per operation,
    /// plus a relative-to-baseline comparison of several named candidates. Explicitly INDICATIVE rather than
    /// publication-grade — enough to settle "is the new implementation actually faster?" Being timing-based,
    /// results vary run to run.
    /// </summary>
    public static class Benchmark
    {
        public static BenchmarkResult Run(string name, Action action, int iterations = 10000, int warmupIterations = 1000)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (action is null) throw new ArgumentNullException(nameof(action));
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Iterations must be positive.");
            if (warmupIterations < 0) throw new ArgumentOutOfRangeException(nameof(warmupIterations), warmupIterations, "Warmup must be non-negative.");

            for (int i = 0; i < warmupIterations; i++) action();

            var samples = new double[iterations];
            var sw = new Stopwatch();
            double ticksToNs = 1e9 / Stopwatch.Frequency;
            for (int i = 0; i < iterations; i++)
            {
                sw.Restart();
                action();
                sw.Stop();
                samples[i] = sw.ElapsedTicks * ticksToNs;
            }

            // Allocation in a separate, un-timed pass so the timer's own work does not pollute the figure.
            long allocBefore = AllocatedBytes();
            for (int i = 0; i < iterations; i++) action();
            long allocAfter = AllocatedBytes();
            long allocatedPerOp = Math.Max(0, allocAfter - allocBefore) / iterations;

            return BuildResult(name, samples, allocatedPerOp);
        }

        /// <summary>Runs several candidates and reports each one's mean time relative to the first (the baseline).</summary>
        public static IReadOnlyList<BenchmarkResult> Compare(
            IEnumerable<(string Name, Action Action)> candidates, int iterations = 10000, int warmupIterations = 1000)
        {
            if (candidates is null) throw new ArgumentNullException(nameof(candidates));
            var results = new List<BenchmarkResult>();
            foreach (var (name, action) in candidates)
                results.Add(Run(name, action, iterations, warmupIterations));

            if (results.Count > 0)
            {
                double baseline = results[0].MeanNanoseconds;
                foreach (var r in results)
                    r.RelativeToBaseline = baseline > 0 ? r.MeanNanoseconds / baseline : 1.0;
            }
            return results;
        }

        private static BenchmarkResult BuildResult(string name, double[] samples, long allocatedPerOp)
        {
            var sorted = (double[])samples.Clone();
            Array.Sort(sorted);
            int n = sorted.Length;

            double sum = 0;
            for (int i = 0; i < n; i++) sum += sorted[i];
            double mean = sum / n;

            double variance = 0;
            for (int i = 0; i < n; i++) { double d = sorted[i] - mean; variance += d * d; }
            variance = n > 1 ? variance / (n - 1) : 0;

            return new BenchmarkResult(
                name, n, mean, sorted[0], sorted[n - 1],
                Quantile(sorted, 0.50), Quantile(sorted, 0.95), Math.Sqrt(variance), allocatedPerOp);
        }

        private static double Quantile(double[] sorted, double p)
        {
            if (sorted.Length == 1) return sorted[0];
            double position = p * (sorted.Length - 1);
            int lo = (int)Math.Floor(position);
            int hi = (int)Math.Ceiling(position);
            if (lo == hi) return sorted[lo];
            double frac = position - lo;
            return sorted[lo] * (1 - frac) + sorted[hi] * frac;
        }

        private static long AllocatedBytes()
        {
#if NETSTANDARD2_0
            return GC.GetTotalMemory(false); // best-effort proxy; may be perturbed by collections
#else
            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }
    }
}
