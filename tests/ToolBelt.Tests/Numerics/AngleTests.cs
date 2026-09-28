using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class AngleTests
    {
        public void NormalizeDegrees()
        {
            Check.Close(10, Angle.NormalizeDegrees(370));
            Check.Close(350, Angle.NormalizeDegrees(-10));
            Check.Close(0, Angle.NormalizeDegrees(360));
            Check.Close(0, Angle.NormalizeDegrees(0));
            Check.Close(180, Angle.NormalizeDegrees(180));
        }

        public void NormalizeSigned()
        {
            Check.Close(-170, Angle.NormalizeSignedDegrees(190));
            Check.Close(180, Angle.NormalizeSignedDegrees(180));
            Check.Close(170, Angle.NormalizeSignedDegrees(-190));
            Check.Close(0, Angle.NormalizeSignedDegrees(360));
        }

        public void Difference_WrapsShortestWay()
        {
            Check.Close(20, Angle.DifferenceDegrees(350, 10));
            Check.Close(-20, Angle.DifferenceDegrees(10, 350));
            Check.Close(180, Angle.DifferenceDegrees(0, 180));
        }

        public void Lerp_ShortestArc()
        {
            Check.Close(0, Angle.LerpDegrees(350, 10, 0.5), 1e-9);   // midpoint across the wrap
            Check.Close(350, Angle.LerpDegrees(350, 10, 0), 1e-9);
            Check.Close(10, Angle.LerpDegrees(350, 10, 1), 1e-9);
        }

        public void DegreeRadianRoundTrip()
        {
            Check.Close(Math.PI, Angle.DegreesToRadians(180), 1e-12);
            var rng = new Random(180);
            for (int i = 0; i < 2000; i++)
            {
                double deg = rng.NextDouble() * 720 - 360;
                Check.Close(deg, Angle.RadiansToDegrees(Angle.DegreesToRadians(deg)), 1e-9, $"deg {deg}");
            }
        }

        // Property: NormalizeDegrees output is always in [0,360) and congruent mod 360 to the input.
        public void Property_NormalizeRangeAndCongruence()
        {
            var rng = new Random(360);
            for (int i = 0; i < 5000; i++)
            {
                double deg = rng.NextDouble() * 10000 - 5000;
                double n = Angle.NormalizeDegrees(deg);
                Check.True(n >= 0 && n < 360, $"deg {deg} -> {n} out of range");
                double diff = (deg - n) / 360.0;
                Check.Close(Math.Round(diff), diff, 1e-6, $"deg {deg}: not congruent mod 360");

                double s = Angle.NormalizeSignedDegrees(deg);
                Check.True(s > -180 - 1e-9 && s <= 180 + 1e-9, $"deg {deg} -> signed {s} out of range");
            }
        }
    }
}
