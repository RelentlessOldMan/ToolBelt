using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class ExponentialMovingAverageTests
    {
        public void FirstSampleSeeds()
        {
            var ema = new ExponentialMovingAverage(0.5);
            Check.False(ema.HasValue);
            Check.Close(10, ema.Add(10));
            Check.True(ema.HasValue);
        }

        public void KnownRecurrence()
        {
            var ema = new ExponentialMovingAverage(0.5);
            ema.Add(10);                 // 10
            Check.Close(15, ema.Add(20)); // 0.5*20 + 0.5*10
            Check.Close(17.5, ema.Add(20)); // 0.5*20 + 0.5*15
        }

        public void ConstantInput_ConvergesToConstant()
        {
            var ema = new ExponentialMovingAverage(0.3);
            for (int i = 0; i < 100; i++) ema.Add(7);
            Check.Close(7, ema.Value, 1e-6);
        }

        public void Empty_IsNaN()
        {
            Check.True(double.IsNaN(new ExponentialMovingAverage(0.5).Value));
        }

        public void InvalidAlpha_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new ExponentialMovingAverage(0));
            Check.Throws<ArgumentOutOfRangeException>(() => new ExponentialMovingAverage(1.5));
        }

        // Differential: independent recurrence reference, plus the invariant that the EMA always stays
        // within the running [min, max] of the samples seen (it is a convex combination).
        public void Differential_MatchesRecurrence_AndStaysBounded()
        {
            var rng = new Random(700);
            for (int trial = 0; trial < 500; trial++)
            {
                double alpha = rng.NextDouble() * 0.99 + 0.01; // (0.01, 1.0)
                var ema = new ExponentialMovingAverage(alpha);

                double refValue = 0;
                bool first = true;
                double runMin = double.PositiveInfinity, runMax = double.NegativeInfinity;

                int n = rng.Next(1, 100);
                for (int i = 0; i < n; i++)
                {
                    double x = rng.NextDouble() * 200 - 100;
                    double actual = ema.Add(x);

                    if (first) { refValue = x; first = false; }
                    else refValue = alpha * x + (1 - alpha) * refValue;

                    runMin = Math.Min(runMin, x);
                    runMax = Math.Max(runMax, x);

                    Check.Close(refValue, actual, 1e-9, $"trial {trial} step {i}");
                    Check.True(actual >= runMin - 1e-9 && actual <= runMax + 1e-9,
                        $"trial {trial} step {i}: {actual} outside [{runMin},{runMax}]");
                }
            }
        }
    }
}
