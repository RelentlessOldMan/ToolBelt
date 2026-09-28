using System;
using System.Linq;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class ZeroCrossingTests
    {
        public void InterpolatesMidpoint()
        {
            var c = ZeroCrossing.Find(new double[] { 1, -1 });
            Check.Equal(1, c.Count);
            Check.Close(0.5, c[0], 1e-9);
        }

        public void AsymmetricInterpolation()
        {
            // 3 then -1 crosses at 3/(3-(-1)) = 0.75 past index 0.
            var c = ZeroCrossing.Find(new double[] { 3, -1 });
            Check.Close(0.75, c[0], 1e-9);
        }

        public void ExactZeroSample()
        {
            var c = ZeroCrossing.Find(new double[] { 1, 0, -1 });
            Check.Equal(1, c.Count);
            Check.Close(1.0, c[0], 1e-9); // the zero sample itself
        }

        public void MultipleCrossings()
        {
            // A triangle wave crossing zero twice.
            var c = ZeroCrossing.Find(new double[] { -1, 1, -1 });
            Check.Equal(2, c.Count);
            Check.Close(0.5, c[0], 1e-9);
            Check.Close(1.5, c[1], 1e-9);
        }

        public void NoCrossings()
        {
            Check.Equal(0, ZeroCrossing.Find(new double[] { 1, 2, 3 }).Count);
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => ZeroCrossing.Find(null!));
        }
    }
}
