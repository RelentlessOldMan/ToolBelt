using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class ChangePointTests
    {
        public void DetectsCleanStep()
        {
            var series = new double[] { 0, 0, 0, 0, 10, 10, 10, 10 };
            var (index, magnitude) = ChangePoint.DetectShift(series);
            Check.Equal(4, index);                 // second segment begins at index 4
            Check.Close(10, magnitude, 1e-9);
        }

        public void DetectsShiftInNoisyData()
        {
            var series = new double[] { 1, 2, 1, 2, 20, 21, 19, 20 };
            var (index, _) = ChangePoint.DetectShift(series);
            Check.Equal(4, index);
        }

        public void CusumSumsToZero()
        {
            // The CUSUM of deviations from the mean returns to (approximately) zero at the end.
            var cusum = ChangePoint.Cusum(new double[] { 1, 2, 3, 4, 5 });
            Check.Equal(5, cusum.Length);
            Check.Close(0, cusum[cusum.Length - 1], 1e-9);
        }

        public void CusumPeaksAtChange()
        {
            // For a step up, the CUSUM reaches its minimum right before the step.
            var cusum = ChangePoint.Cusum(new double[] { 0, 0, 0, 10, 10, 10 });
            int argmin = 0;
            for (int i = 1; i < cusum.Length; i++) if (cusum[i] < cusum[argmin]) argmin = i;
            Check.Equal(2, argmin); // last low sample before the shift
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => ChangePoint.DetectShift(new double[] { 1 }));
            Check.Throws<ArgumentNullException>(() => ChangePoint.DetectShift(null!));
            Check.Equal(0, ChangePoint.Cusum(Array.Empty<double>()).Length);
        }
    }
}
