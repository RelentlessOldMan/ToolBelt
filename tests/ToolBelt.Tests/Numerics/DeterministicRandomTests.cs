using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class DeterministicRandomTests
    {
        public void SameSeedSameSequence()
        {
            var a = new DeterministicRandom(42);
            var b = new DeterministicRandom(42);
            for (int i = 0; i < 100; i++)
                Check.Equal(a.NextUInt64(), b.NextUInt64(), $"at {i}");
        }

        public void DifferentSeedsDiffer()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);
            bool anyDifferent = false;
            for (int i = 0; i < 10; i++)
                if (a.NextUInt64() != b.NextUInt64()) { anyDifferent = true; break; }
            Check.True(anyDifferent);
        }

        public void NextDoubleInRange()
        {
            var r = new DeterministicRandom(7);
            for (int i = 0; i < 10000; i++)
            {
                double d = r.NextDouble();
                Check.True(d >= 0 && d < 1, $"out of [0,1): {d}");
            }
        }

        public void NextIntInRange()
        {
            var r = new DeterministicRandom(9);
            for (int i = 0; i < 10000; i++)
            {
                int v = r.Next(10, 20);
                Check.True(v >= 10 && v < 20, $"out of [10,20): {v}");
            }
        }

        public void PlugsIntoRandomApi()
        {
            Random r = new DeterministicRandom(3); // usable anywhere a Random is expected
            var list = Enumerable.Range(0, 5).ToList();
            r.Shuffle(list);
            Check.True(list.OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 5)));
        }

        public void MeanIsAboutHalf()
        {
            var r = new DeterministicRandom(123);
            double sum = 0;
            const int n = 200000;
            for (int i = 0; i < n; i++) sum += r.NextDouble();
            double mean = sum / n;
            Check.True(Math.Abs(mean - 0.5) < 0.01, $"mean {mean} not near 0.5");
        }

        public void InvalidRange_Throws()
        {
            var r = new DeterministicRandom(1);
            Check.Throws<ArgumentOutOfRangeException>(() => r.Next(5, 1));
        }
    }
}
