using System;
using System.Linq;
using ToolBelt.Diagnostics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Diagnostics
{
    public sealed class BenchmarkTests
    {
        public void ResultStructureIsSane()
        {
            var r = Benchmark.Run("noop", () => { }, iterations: 500, warmupIterations: 50);
            Check.Equal(500, r.Iterations);
            Check.True(r.MeanNanoseconds >= 0);
            Check.True(r.MinNanoseconds <= r.MedianNanoseconds + 1e-9, "min <= median");
            Check.True(r.MedianNanoseconds <= r.MaxNanoseconds + 1e-9, "median <= max");
            Check.True(r.Percentile95Nanoseconds <= r.MaxNanoseconds + 1e-9, "p95 <= max");
            Check.True(r.StdDevNanoseconds >= 0);
            Check.True(r.OperationsPerSecond >= 0);
        }

        public void HeavierWorkIsSlower()
        {
            // A candidate doing ~1000x the work should measure a larger mean (robust to timer noise).
            var results = Benchmark.Compare(new (string, Action)[]
            {
                ("light", () => { int x = 0; x++; GC.KeepAlive(x); }),
                ("heavy", () => { long s = 0; for (int i = 0; i < 5000; i++) s += i; GC.KeepAlive(s); }),
            }, iterations: 2000, warmupIterations: 200);

            Check.Equal(1.0, results[0].RelativeToBaseline);         // baseline
            Check.True(results[1].RelativeToBaseline > 1.0, $"heavy relative {results[1].RelativeToBaseline} should exceed 1");
        }

        public void MeasuresAllocation()
        {
            // Allocating a sizable array should register non-zero bytes-per-op (accurate on net8.0).
            var r = Benchmark.Run("alloc", () => { var buf = new byte[4096]; GC.KeepAlive(buf); },
                iterations: 2000, warmupIterations: 200);
            Check.True(r.AllocatedBytesPerOperation >= 4000, $"expected ~4096 B/op, got {r.AllocatedBytesPerOperation}");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Benchmark.Run("x", null!));
            Check.Throws<ArgumentOutOfRangeException>(() => Benchmark.Run("x", () => { }, iterations: 0));
            Check.Throws<ArgumentNullException>(() => Benchmark.Compare(null!));
        }
    }
}
