using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class RunningStatisticsTests
    {
        public void Empty_YieldsNaN()
        {
            var stats = new RunningStatistics();
            Check.Equal(0L, stats.Count);
            Check.True(double.IsNaN(stats.Mean));
            Check.True(double.IsNaN(stats.PopulationVariance));
            Check.True(double.IsNaN(stats.SampleVariance));
        }

        public void SingleSample()
        {
            var stats = new RunningStatistics();
            stats.Push(5);
            Check.Close(5, stats.Mean);
            Check.Close(0, stats.PopulationVariance);
            Check.True(double.IsNaN(stats.SampleVariance)); // needs >= 2
            Check.Close(5, stats.Min);
            Check.Close(5, stats.Max);
            Check.Close(5, stats.Sum);
        }

        public void KnownValues()
        {
            var stats = new RunningStatistics();
            stats.PushRange(new double[] { 2, 4, 4, 4, 5, 5, 7, 9 });
            Check.Close(5, stats.Mean);
            Check.Close(4, stats.PopulationVariance);           // known textbook example
            Check.Close(2, stats.PopulationStandardDeviation);
            Check.Close(2, stats.Min);
            Check.Close(9, stats.Max);
        }

        // Differential: Welford's one-pass results must match a naive two-pass computation.
        public void Differential_MatchesTwoPassReference()
        {
            var rng = new Random(2024);
            for (int trial = 0; trial < 1000; trial++)
            {
                int n = rng.Next(1, 200);
                var data = new double[n];
                for (int i = 0; i < n; i++)
                    data[i] = rng.NextDouble() * 2000 - 1000;

                var stats = new RunningStatistics();
                stats.PushRange(data);

                double mean = data.Average();
                double popVar = data.Sum(x => (x - mean) * (x - mean)) / n;
                double sampVar = n > 1 ? data.Sum(x => (x - mean) * (x - mean)) / (n - 1) : double.NaN;

                Check.Equal(n, (int)stats.Count, $"trial {trial}: count");
                Check.Close(mean, stats.Mean, 1e-6, $"trial {trial}: mean");
                Check.Close(popVar, stats.PopulationVariance, 1e-4, $"trial {trial}: pop var");
                if (n > 1)
                    Check.Close(sampVar, stats.SampleVariance, 1e-4, $"trial {trial}: sample var");
                Check.Close(data.Min(), stats.Min, 1e-9, $"trial {trial}: min");
                Check.Close(data.Max(), stats.Max, 1e-9, $"trial {trial}: max");
                Check.Close(data.Sum(), stats.Sum, 1e-4, $"trial {trial}: sum");
            }
        }

        public void Clear_Resets()
        {
            var stats = new RunningStatistics();
            stats.PushRange(new double[] { 1, 2, 3 });
            stats.Clear();
            Check.Equal(0L, stats.Count);
            Check.True(double.IsNaN(stats.Mean));
        }
    }
}
