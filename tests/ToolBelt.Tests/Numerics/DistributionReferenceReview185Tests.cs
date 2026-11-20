using System;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Review round 185 (tooling, eleventh pass): the generator wrote 1 - series for masses far below 1e-400, which is
    // cancellation noise: FSurvival(30; 300, 1e8) = -1.6e-394 and FCdf(1e-6; 1e9, 3) = -1.67e-393. They parse to -0.0,
    // which the relative check passed only because it accepts an exact 0; a correct tiny positive result would fail.
    public sealed class DistributionReferenceReview185Tests
    {
        public void ProbabilityReferences_AreNonNegativeAndAtMostOne()
        {
            int masses = 0;
            foreach (var r in DistributionReferenceData.Rows)
            {
                if (!r.Fn.EndsWith("Cdf", StringComparison.Ordinal) && !r.Fn.EndsWith("Survival", StringComparison.Ordinal))
                    continue;
                bool negative = BitConverter.DoubleToInt64Bits(r.Expected) < 0;
                Check.True(!negative && r.Expected <= 1, $"{r.Fn}(x={r.X:R}; {r.A:R}, {r.B:R}) = {r.Expected:R}");
                masses++;
            }
            Check.True(masses > 100, "masses present");
        }
    }
}
