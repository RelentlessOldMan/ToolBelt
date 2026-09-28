using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class MovingAverageTests
    {
        public void AveragesWithinWindow()
        {
            var ma = new MovingAverage(3);
            Check.Close(1, ma.Add(1));       // [1]
            Check.Close(1.5, ma.Add(2));     // [1,2]
            Check.Close(2, ma.Add(3));       // [1,2,3]
            Check.Close(3, ma.Add(4));       // [2,3,4] -> 3
            Check.Close(4, ma.Add(5));       // [3,4,5] -> 4
        }

        public void Empty_IsNaN()
        {
            Check.True(double.IsNaN(new MovingAverage(3).Average));
        }

        public void IsFull_Transitions()
        {
            var ma = new MovingAverage(2);
            ma.Add(1);
            Check.False(ma.IsFull);
            ma.Add(2);
            Check.True(ma.IsFull);
        }

        public void InvalidWindow_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new MovingAverage(0));
        }

        // Differential: after each Add, the running-sum average must equal a freshly summed window
        // average. This is exactly the class of bug (subtracting the wrong slot after wrap) the house
        // style warns about.
        public void Differential_MatchesFreshWindowSum()
        {
            var rng = new Random(4004);
            for (int trial = 0; trial < 500; trial++)
            {
                int window = rng.Next(1, 12);
                var ma = new MovingAverage(window);
                var history = new List<double>();

                int n = rng.Next(0, 100);
                for (int i = 0; i < n; i++)
                {
                    double value = rng.NextDouble() * 200 - 100;
                    double actual = ma.Add(value);
                    history.Add(value);

                    var windowValues = history.Skip(Math.Max(0, history.Count - window)).ToList();
                    double expected = windowValues.Average();
                    Check.Close(expected, actual, 1e-9, $"trial {trial} step {i}");
                }
            }
        }
    }
}
