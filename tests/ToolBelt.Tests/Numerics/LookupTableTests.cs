using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class LookupTableTests
    {
        private static readonly double[] X = { 0, 1, 2, 3 };
        private static readonly double[] Y = { 0, 10, 20, 30 };

        public void InterpolatesInterior()
        {
            var t = new LookupTable(X, Y);
            Check.Close(5, t.Interpolate(0.5), 1e-9);
            Check.Close(25, t.Interpolate(2.5), 1e-9);
            Check.Close(10, t.Interpolate(1), 1e-9); // exact knot
        }

        public void ClampExtrapolation()
        {
            var t = new LookupTable(X, Y, Extrapolation.Clamp);
            Check.Close(0, t.Interpolate(-5), 1e-9);
            Check.Close(30, t.Interpolate(99), 1e-9);
        }

        public void LinearExtrapolation()
        {
            var t = new LookupTable(X, Y, Extrapolation.Linear);
            Check.Close(-10, t.Interpolate(-1), 1e-9); // slope 10 continues
            Check.Close(40, t.Interpolate(4), 1e-9);
        }

        public void ThrowExtrapolation()
        {
            var t = new LookupTable(X, Y, Extrapolation.Throw);
            Check.Throws<ArgumentOutOfRangeException>(() => t.Interpolate(-1));
            Check.Throws<ArgumentOutOfRangeException>(() => t.Interpolate(5));
        }

        public void InverseLookup()
        {
            var t = new LookupTable(X, Y);
            Check.Close(1.5, t.InverseLookup(15), 1e-9);
            Check.Close(0, t.InverseLookup(0), 1e-9);
            Check.Close(3, t.InverseLookup(30), 1e-9);
        }

        public void InverseLookupNonMonotone_Throws()
        {
            var t = new LookupTable(new double[] { 0, 1, 2 }, new double[] { 0, 5, 3 }); // not monotone
            Check.Throws<InvalidOperationException>(() => t.InverseLookup(4));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => new LookupTable(new double[] { 0 }, new double[] { 0 }));       // too few
            Check.Throws<ArgumentException>(() => new LookupTable(new double[] { 0, 0 }, new double[] { 1, 2 })); // not increasing
            Check.Throws<ArgumentException>(() => new LookupTable(new double[] { 0, 1 }, new double[] { 1 }));    // length mismatch
        }
    }
}
