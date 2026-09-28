using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class ToleranceCheckTests
    {
        public void AllWithinLimitsPasses()
        {
            var r = ToleranceCheck.Evaluate(new double[] { 1, 2, 3, 4 }, 0, 5);
            Check.True(r.Pass);
            Check.Equal(0, r.ViolationCount);
            Check.Equal(-1, r.FirstViolationIndex);
            Check.Equal(0.0, r.WorstDeviation);
        }

        public void DetectsViolations()
        {
            //                              idx: 0   1    2   3    4
            var r = ToleranceCheck.Evaluate(new double[] { 1, 12, 3, -5, 4 }, 0, 10);
            Check.False(r.Pass);
            Check.Equal(2, r.ViolationCount);      // 12 (over) and -5 (under)
            Check.Equal(1, r.FirstViolationIndex); // 12 first
            Check.Equal(3, r.WorstViolationIndex); // -5 is 5 below vs 12 only 2 above
            Check.Close(5, r.WorstDeviation, 1e-9);
        }

        public void BoundaryValuesPass()
        {
            var r = ToleranceCheck.Evaluate(new double[] { 0, 10 }, 0, 10); // inclusive limits
            Check.True(r.Pass);
        }

        public void EmptySeriesPasses()
        {
            Check.True(ToleranceCheck.Evaluate(Array.Empty<double>(), 0, 1).Pass);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => ToleranceCheck.Evaluate(new double[] { 1 }, 5, 1));
            Check.Throws<ArgumentNullException>(() => ToleranceCheck.Evaluate(null!, 0, 1));
        }
    }
}
