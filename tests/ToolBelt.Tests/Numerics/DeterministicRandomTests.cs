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

        public void Next_SingleArg_InRange()
        {
            var r = new DeterministicRandom(11);
            for (int i = 0; i < 10000; i++)
            {
                int v = r.Next(50);
                Check.True(v >= 0 && v < 50, $"out of [0,50): {v}");
            }
        }

        public void Next_SingleArg_Negative_Throws()
        {
            var r = new DeterministicRandom(1);
            Check.Throws<ArgumentOutOfRangeException>(() => r.Next(-1));
        }

        public void Next_NoArg_IsNonNegative()
        {
            var r = new DeterministicRandom(13);
            for (int i = 0; i < 10000; i++)
                Check.True(r.Next() >= 0, "Next() returned negative");
        }

        public void Next_MinEqualsMax_ReturnsBound()
        {
            // Degenerate empty range: returns the bound without consuming the generator pathologically.
            var r = new DeterministicRandom(1);
            Check.Equal(5, r.Next(5, 5));
        }

        public void NextBytes_Null_Throws()
        {
            var r = new DeterministicRandom(1);
            Check.Throws<ArgumentNullException>(() => r.NextBytes(null!));
        }

        public void NextBytes_SameSeedSameBytes()
        {
            var a = new DeterministicRandom(99);
            var b = new DeterministicRandom(99);
            var ba = new byte[64];
            var bb = new byte[64];
            a.NextBytes(ba);
            b.NextBytes(bb);
            Check.True(((ReadOnlySpan<byte>)ba).SequenceEqual(bb), "same seed must yield same bytes");
        }

        public void NextBytes_PartialTailBlock_FillsExactly()
        {
            // 13 is not a multiple of 8, so the final 64-bit block is emitted partially (5 bytes).
            // Those 13 bytes must equal the first 13 bytes of a full 16-byte fill from the same seed —
            // proving the tail loop stops at the right byte rather than over/under-running.
            var small = new byte[13];
            var large = new byte[16];
            new DeterministicRandom(7).NextBytes(small);
            new DeterministicRandom(7).NextBytes(large);
            for (int i = 0; i < 13; i++)
                Check.Equal(large[i], small[i], $"byte {i}");
        }
    }
}
