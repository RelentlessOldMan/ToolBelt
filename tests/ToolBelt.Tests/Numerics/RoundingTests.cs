using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class RoundingTests
    {
        public void RoundToMultiple_Known()
        {
            Check.Close(5, Rounding.RoundToMultiple(7, 5));
            Check.Close(10, Rounding.RoundToMultiple(8, 5));
            Check.Close(2.5, Rounding.RoundToMultiple(2.3, 0.5));
            Check.Close(0, Rounding.RoundToMultiple(0, 5));
            Check.Close(-10, Rounding.RoundToMultiple(-8, 5));
        }

        public void RoundToSignificantDigits_Known()
        {
            Check.Close(123000, Rounding.RoundToSignificantDigits(123456, 3), 1e-6);
            Check.Close(1.23, Rounding.RoundToSignificantDigits(1.2345, 3), 1e-9);
            Check.Close(0.00012, Rounding.RoundToSignificantDigits(0.000123456, 2), 1e-12);
            Check.Close(0, Rounding.RoundToSignificantDigits(0, 3));
            Check.Close(-46000, Rounding.RoundToSignificantDigits(-45678, 2), 1e-6);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Rounding.RoundToMultiple(1, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => Rounding.RoundToMultiple(1, -2));
            Check.Throws<ArgumentOutOfRangeException>(() => Rounding.RoundToSignificantDigits(1, 0));
        }

        // Property: the result of RoundToMultiple is a multiple, and within half a step of the input.
        public void Property_RoundToMultiple()
        {
            var rng = new Random(99);
            for (int trial = 0; trial < 5000; trial++)
            {
                double value = rng.NextDouble() * 2000 - 1000;
                double multiple = rng.NextDouble() * 10 + 0.01;
                double rounded = Rounding.RoundToMultiple(value, multiple);

                double quotient = rounded / multiple;
                Check.Close(Math.Round(quotient), quotient, 1e-6, $"trial {trial}: not a multiple");
                Check.True(Math.Abs(value - rounded) <= multiple / 2 + 1e-6, $"trial {trial}: too far");
            }
        }
    }
}
