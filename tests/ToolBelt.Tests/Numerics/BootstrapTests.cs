using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class BootstrapTests
    {
        private static double Mean(IReadOnlyList<double> xs)
        {
            double s = 0;
            for (int i = 0; i < xs.Count; i++) s += xs[i];
            return s / xs.Count;
        }

        public void IntervalBracketsTrueMean()
        {
            var sample = Enumerable.Range(0, 100).Select(i => (double)i).ToArray(); // true mean 49.5
            var result = Bootstrap.PercentileInterval(sample, Mean, new DeterministicRandom(49), resamples: 3000);
            Check.True(result.Lower < 49.5 && result.Upper > 49.5, $"interval ({result.Lower}, {result.Upper}) should bracket 49.5");
            Check.Equal(3000, result.Distribution.Count);
        }

        public void ReproducibleWithSameSeed()
        {
            var sample = new double[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var a = Bootstrap.PercentileInterval(sample, Mean, new DeterministicRandom(7), resamples: 1000);
            var b = Bootstrap.PercentileInterval(sample, Mean, new DeterministicRandom(7), resamples: 1000);
            Check.Equal(a.Lower, b.Lower);
            Check.Equal(a.Upper, b.Upper);
        }

        public void DistributionIsSorted()
        {
            var sample = new double[] { 3, 1, 4, 1, 5, 9, 2, 6 };
            var result = Bootstrap.PercentileInterval(sample, Mean, new DeterministicRandom(1), resamples: 500);
            for (int i = 1; i < result.Distribution.Count; i++)
                Check.True(result.Distribution[i] >= result.Distribution[i - 1], "distribution must be sorted");
            Check.True(result.Lower <= result.Upper);
        }

        public void WorksForAnyStatistic()
        {
            // Bootstrap the maximum — a statistic with no closed-form CI.
            var sample = new double[] { 10, 20, 30, 40, 50 };
            var result = Bootstrap.PercentileInterval(sample, xs => xs.Max(), new DeterministicRandom(2), resamples: 500);
            Check.True(result.Upper <= 50 && result.Lower >= 10);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Bootstrap.PercentileInterval(null!, Mean, new DeterministicRandom(1)));
            Check.Throws<ArgumentNullException>(() => Bootstrap.PercentileInterval(new double[] { 1 }, Mean, null!));
            Check.Throws<ArgumentOutOfRangeException>(() => Bootstrap.PercentileInterval(new double[] { 1 }, Mean, new DeterministicRandom(1), resamples: 0));
        }
    }
}
