using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class TimeDelayEstimateTests
    {
        // Build a signal and a copy shifted right by `delay` samples (zero-filled at the front).
        private static (double[] a, double[] b) MakePair(int length, int delay, int seed)
        {
            var rng = new Random(seed);
            var a = new double[length];
            for (int i = 0; i < length; i++) a[i] = rng.NextDouble() * 2 - 1;
            var b = new double[length];
            for (int i = 0; i < length; i++) { int j = i - delay; if (j >= 0 && j < length) b[i] = a[j]; }
            return (a, b);
        }

        public void CrossCorrelation_RecoversPositiveDelay()
        {
            var (a, b) = MakePair(200, 17, 1);
            Check.Equal(17, TimeDelayEstimate.CrossCorrelationLag(a, b));
        }

        public void CrossCorrelation_RecoversNegativeDelay()
        {
            // b leads a: shift a instead.
            var (b, a) = MakePair(200, 12, 2);
            Check.Equal(-12, TimeDelayEstimate.CrossCorrelationLag(a, b));
        }

        public void CrossCorrelation_ZeroDelay()
        {
            var (a, b) = MakePair(128, 0, 3);
            Check.Equal(0, TimeDelayEstimate.CrossCorrelationLag(a, b));
        }

        public void GccPhat_RecoversDelay()
        {
            var (a, b) = MakePair(256, 23, 4);
            Check.Equal(23, TimeDelayEstimate.GccPhat(a, b));
        }

        public void GccPhat_RobustUnderNoise()
        {
            var (a, b) = MakePair(512, 40, 5);
            var rng = new Random(99);
            for (int i = 0; i < b.Length; i++) b[i] += (rng.NextDouble() * 2 - 1) * 0.2; // additive noise
            Check.Equal(40, TimeDelayEstimate.GccPhat(a, b));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => TimeDelayEstimate.CrossCorrelationLag(null!, new double[] { 1 }));
            Check.Throws<ArgumentException>(() => TimeDelayEstimate.CrossCorrelationLag(Array.Empty<double>(), new double[] { 1 }));
            Check.Throws<ArgumentException>(() => TimeDelayEstimate.GccPhat(new double[] { 1, 2 }, new double[] { 1 }));
        }
    }
}
