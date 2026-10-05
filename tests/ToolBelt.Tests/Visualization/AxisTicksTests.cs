using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    public sealed class AxisTicksTests
    {
        private static string Join(TickSet t) => string.Join(",", t.Labels);

        public void Linear_HandWorkedCases()
        {
            TickSet a = AxisTicks.Linear(0, 10, 6);
            Check.Equal("0,2,4,6,8,10", Join(a));
            Check.Close(2, a.Step);

            // Steps 0.1 (10 ticks) and 0.2 (6) are too many for 5; 0.5 gives 0, 0.5, 1.
            TickSet b = AxisTicks.Linear(0.13, 0.97, 5);
            Check.Equal("0.0,0.5,1.0", Join(b));
            Check.Close(0, b.Min);
            Check.Close(1, b.Max);

            Check.Equal("-5,0,5,10,15", Join(AxisTicks.Linear(-3.7, 12.2, 6)));
        }

        public void Linear_NoFloatingPointNoiseInValuesOrLabels()
        {
            TickSet t = AxisTicks.Linear(0, 1, 11);
            Check.Equal(0.3, t.Values[3]);
            Check.Equal("0.3", t.Labels[3]);
        }

        public void Linear_TightKeepsOnlyInRangeTicks()
        {
            TickSet t = AxisTicks.Linear(0.13, 0.97, 5, loose: false);
            Check.Equal("0.5", Join(t));
            Check.Close(0.13, t.Min);
            Check.Close(0.97, t.Max);
        }

        public void Linear_FlatAndReversedRanges()
        {
            TickSet flat = AxisTicks.Linear(5, 5);
            Check.True(flat.Min < 5 && flat.Max > 5, "a flat range is opened up");
            Check.Equal(Join(AxisTicks.Linear(0, 10)), Join(AxisTicks.Linear(10, 0)));
            Check.Equal("-1,0,1", Join(AxisTicks.Linear(0, 0, 3)));
        }

        public void Linear_LargeAndTinyUseExponentLabels()
        {
            Check.True(AxisTicks.Linear(0, 5e8).Labels.Last().Contains("E+"), "large");
            Check.True(AxisTicks.Linear(0, 5e-7).Labels.Last().Contains("E-"), "tiny");
        }

        public void Linear_Properties_Randomized()
        {
            foreach (long seed in new long[] { 20261005, 7, 99991 })
            {
            var rng = new DeterministicRandom(seed);
            for (int trial = 0; trial < 2000; trial++)
            {
                double scale = Math.Pow(10, rng.Next(-6, 7));
                double lo = (rng.NextDouble() * 2 - 1) * scale;
                double hi = lo + rng.NextDouble() * scale * 3 + scale * 1e-3;
                int maxTicks = rng.Next(2, 11);
                TickSet t = AxisTicks.Linear(lo, hi, maxTicks);

                // Bound holds, except 2 ticks cannot bracket a zero-straddling range on a zero-anchored grid.
                bool straddleException = maxTicks == 2 && lo < 0 && hi > 0 && t.Count == 3;
                Check.True(t.Count >= 2 && (t.Count <= maxTicks || straddleException), $"count {t.Count} vs {maxTicks} for [{lo},{hi}]");
                Check.True(t.Values[0] <= lo + Math.Abs(t.Step) * 1e-6 && t.Values[t.Count - 1] >= hi - Math.Abs(t.Step) * 1e-6, "brackets");
                for (int i = 1; i < t.Count; i++)
                    Check.Close(t.Step, t.Values[i] - t.Values[i - 1], t.Step * 1e-6);

                double mantissa = t.Step / Math.Pow(10, Math.Floor(Math.Log10(t.Step) + 1e-12));
                Check.True(Math.Abs(mantissa - 1) < 1e-9 || Math.Abs(mantissa - 2) < 1e-9 || Math.Abs(mantissa - 5) < 1e-9,
                    $"step {t.Step} is not 1/2/5 x 10^n");

                // Minimal: the next smaller nice step would not fit.
                double smaller = mantissa < 1.5 ? t.Step / 2 : (mantissa < 3 ? t.Step / 2 : t.Step * 2 / 5);
                long smallerCount = (long)Math.Ceiling(hi / smaller - 1e-9) - (long)Math.Floor(lo / smaller + 1e-9) + 1;
                Check.True(smallerCount > maxTicks, $"step {t.Step} is not the smallest that fits ({smaller} gives {smallerCount})");
            }
            }
        }

        public void Linear_TwoTicksAcrossZero_IsThreeNotOne()
        {
            // Regression: this once returned a single tick that did not bracket the data.
            TickSet t = AxisTicks.Linear(-9.6e-5, 6.35e-5, 2);
            Check.Equal(3, t.Count);
            Check.True(t.Values[0] <= -9.6e-5 && t.Values[2] >= 6.35e-5);
            Check.Equal("0,1", Join(AxisTicks.Linear(0.3, 0.7, 2)));
        }

        public void Log10_Decades()
        {
            Check.Equal("1,10,100,1000,10000", Join(AxisTicks.Log10(3, 4500)));
            Check.Equal("0.001,0.01,0.1,1", Join(AxisTicks.Log10(0.002, 0.5)));
            Check.Equal("1,10", Join(AxisTicks.Log10(2, 3)));
            Check.Equal("1e6,1e7", Join(AxisTicks.Log10(2e6, 3e6)));
            Check.Equal("10,100,1000", Join(AxisTicks.Log10(3, 4500, loose: false)));
            Check.Throws<ArgumentOutOfRangeException>(() => AxisTicks.Log10(0, 10));
        }

        public void Time_ClockFriendlySteps()
        {
            Check.Equal("0s,10s,20s,30s,40s,50s", Join(AxisTicks.Time(0, 50)));
            Check.Equal("0:00,2:00,4:00,6:00,8:00,10:00", Join(AxisTicks.Time(0, 600)));
            Check.Equal("0:00:00,0:30:00,1:00:00,1:30:00,2:00:00", Join(AxisTicks.Time(0, 7200, 5)));
            Check.Equal("0.0s,0.5s,1.0s,1.5s,2.0s,2.5s", Join(AxisTicks.Time(0, 2.5)));
        }

        public void Time_MultiDayFallsBackToWholeDays()
        {
            TickSet t = AxisTicks.Time(0, 86400 * 30, 6);
            Check.Close(864000, t.Step);
            Check.True(t.Count <= 6);
            Check.Equal("240:00:00", t.Labels[1]);
        }

        public void FormatTime_Cases()
        {
            Check.Equal("1:05.5", AxisTicks.FormatTime(65.5, 120, 1));
            Check.Equal("1:01:01", AxisTicks.FormatTime(3661, 7200));
            Check.Equal("-0:30", AxisTicks.FormatTime(-30, 600));
            Check.Equal("12s", AxisTicks.FormatTime(12, 30));
        }

        public void Validation()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => AxisTicks.Linear(0, 1, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => AxisTicks.Linear(double.NaN, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => AxisTicks.Time(0, double.PositiveInfinity));
        }
    }
}
