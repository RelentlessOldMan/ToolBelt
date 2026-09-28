using System;
using System.Linq;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class ConvolutionTests
    {
        private static bool Close(double[] a, double[] b, double tol = 1e-9)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > tol) return false;
            return true;
        }

        public void ConvolveKnown()
        {
            Check.True(Close(Convolution.Convolve(new double[] { 1, 1 }, new double[] { 1, 1 }),
                             new double[] { 1, 2, 1 }));
            Check.True(Close(Convolution.Convolve(new double[] { 1, 2, 3 }, new double[] { 0, 1, 0.5 }),
                             new double[] { 0, 1, 2.5, 4, 1.5 }));
        }

        public void ConvolveWithDeltaIsIdentity()
        {
            var x = new double[] { 3, 1, 4, 1, 5 };
            Check.True(Close(Convolution.Convolve(x, new double[] { 1 }), x));
        }

        public void ConvolveIsCommutative()
        {
            var rng = new Random(40);
            var a = Enumerable.Range(0, 8).Select(_ => rng.NextDouble()).ToArray();
            var b = Enumerable.Range(0, 5).Select(_ => rng.NextDouble()).ToArray();
            Check.True(Close(Convolution.Convolve(a, b), Convolution.Convolve(b, a)));
        }

        public void CrossCorrelate()
        {
            // Auto-correlation of [1,2,3] peaks at the center (zero lag) with the energy 1+4+9 = 14.
            var r = Convolution.CrossCorrelate(new double[] { 1, 2, 3 }, new double[] { 1, 2, 3 });
            Check.Equal(5, r.Length);
            Check.Close(14, r[2], 1e-9); // center = zero lag
        }

        public void EmptyInputs()
        {
            Check.Equal(0, Convolution.Convolve(Array.Empty<double>(), new double[] { 1 }).Length);
        }

        public void NullInputs_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Convolution.Convolve(null!, new double[] { 1 }));
        }
    }
}
