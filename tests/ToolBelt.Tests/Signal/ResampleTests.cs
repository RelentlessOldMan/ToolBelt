using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class ResampleTests
    {
        public void Linear_PreservesEndpoints()
        {
            var x = new double[] { 0, 1, 2, 3, 4 };
            var y = Resample.Linear(x, 9);
            Check.Close(0.0, y[0], 1e-12);
            Check.Close(4.0, y[8], 1e-12);
        }

        public void Linear_OfALineStaysLinear()
        {
            // Resampling a straight line must reproduce the same line at the new sample positions.
            var x = new double[11];
            for (int i = 0; i < x.Length; i++) x[i] = 3 + 2 * i; // slope 2, intercept 3 over index 0..10
            int newLen = 21;
            var y = Resample.Linear(x, newLen);
            double scale = (double)(x.Length - 1) / (newLen - 1);
            for (int i = 0; i < newLen; i++)
                Check.Close(3 + 2 * (i * scale), y[i], 1e-9, $"[{i}]");
        }

        public void Linear_Downsample()
        {
            var x = new double[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            var y = Resample.Linear(x, 5); // scale 2 -> exact even samples
            Check.Equal(5, y.Length);
            Check.Close(0.0, y[0], 1e-12);
            Check.Close(2.0, y[1], 1e-12);
            Check.Close(8.0, y[4], 1e-12);
        }

        public void ToRate_ComputesLength()
        {
            var x = new double[100];
            var y = Resample.ToRate(x, 1000, 500); // halve the rate -> ~50 samples
            Check.Equal(50, y.Length);
        }

        public void Decimate_AveragesBlocks()
        {
            var x = new double[] { 0, 2, 4, 6, 8, 10 };
            var y = Resample.Decimate(x, 2); // (0+2)/2, (4+6)/2, (8+10)/2
            Check.Equal(3, y.Length);
            Check.Close(1.0, y[0], 1e-12);
            Check.Close(5.0, y[1], 1e-12);
            Check.Close(9.0, y[2], 1e-12);
        }

        public void SingleSampleAndConstantLength()
        {
            Check.Equal(4, Resample.Linear(new double[] { 7 }, 4).Length);
            Check.Close(7.0, Resample.Linear(new double[] { 7 }, 4)[2], 1e-12);
            Check.Close(3.0, Resample.Linear(new double[] { 3, 9 }, 1)[0], 1e-12); // newLength 1 -> first sample
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Resample.Linear(null!, 4));
            Check.Throws<ArgumentOutOfRangeException>(() => Resample.Linear(new double[] { 1 }, 0));
            Check.Throws<ArgumentException>(() => Resample.Linear(Array.Empty<double>(), 4));
            Check.Throws<ArgumentOutOfRangeException>(() => Resample.ToRate(new double[] { 1 }, 0, 100));
            Check.Throws<ArgumentOutOfRangeException>(() => Resample.Decimate(new double[] { 1 }, 0));
        }
    }
}
