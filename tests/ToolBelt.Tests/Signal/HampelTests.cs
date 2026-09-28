using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class HampelTests
    {
        public void ReplacesLoneSpike()
        {
            var x = new double[] { 1, 1, 1, 1, 50, 1, 1, 1, 1 };
            var y = Hampel.Filter(x, halfWindow: 3);
            Check.Close(1.0, y[4], 1e-9);          // the spike is pulled back to the local median
            Check.Equal(1, Hampel.CountOutliers(x, 3));
        }

        public void LeavesCleanSignalUntouched()
        {
            var rng = new Random(5);
            var x = new double[100];
            for (int i = 0; i < x.Length; i++) x[i] = Math.Sin(i * 0.2) + (rng.NextDouble() - 0.5) * 0.01;
            var y = Hampel.Filter(x, halfWindow: 3, nSigmas: 3);
            for (int i = 0; i < x.Length; i++) Check.Close(x[i], y[i], 1e-9, $"[{i}]");
        }

        public void PreservesGenuineStep()
        {
            // A real level change is not an outlier when the window sees both levels.
            var x = new double[20];
            for (int i = 0; i < 10; i++) x[i] = 0;
            for (int i = 10; i < 20; i++) x[i] = 5;
            var y = Hampel.Filter(x, halfWindow: 2, nSigmas: 3);
            Check.Close(0.0, y[9], 1e-9);
            Check.Close(5.0, y[10], 1e-9);
        }

        public void CatchesMultipleSpikes()
        {
            var x = new double[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 };
            x[2] = 40;
            x[7] = -30;
            Check.Equal(2, Hampel.CountOutliers(x, 2));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Hampel.Filter(null!, 2));
            Check.Throws<ArgumentOutOfRangeException>(() => Hampel.Filter(new double[] { 1 }, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => Hampel.Filter(new double[] { 1 }, 2, -1));
        }
    }
}
