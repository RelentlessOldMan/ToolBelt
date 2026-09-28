using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class BitSetTests
    {
        public void SetGetClear()
        {
            var bs = new BitSet();
            bs.Set(3);
            bs.Set(100); // beyond initial word → grows
            Check.True(bs.Get(3));
            Check.True(bs[100]);
            Check.False(bs.Get(50));
            bs.Clear(3);
            Check.False(bs.Get(3));
            Check.Equal(1, bs.Count);
        }

        public void GetBeyondCapacity_IsFalse()
        {
            var bs = new BitSet(8);
            Check.False(bs.Get(9999));
        }

        public void EnumerateSetBits_Ascending()
        {
            var bs = new BitSet();
            foreach (var i in new[] { 200, 5, 63, 64, 0 })
                bs.Set(i);
            Check.True(bs.EnumerateSetBits().SequenceEqual(new[] { 0, 5, 63, 64, 200 }));
        }

        public void NegativeIndex_Throws()
        {
            var bs = new BitSet();
            Check.Throws<ArgumentOutOfRangeException>(() => bs.Set(-1));
        }

        public void SetOperations()
        {
            var a = Make(1, 2, 3, 64);
            var b = Make(3, 4, 64, 65);

            var union = Make(1, 2, 3, 64); union.UnionWith(b);
            Check.True(union.EnumerateSetBits().SequenceEqual(new[] { 1, 2, 3, 4, 64, 65 }));

            var inter = Make(1, 2, 3, 64); inter.IntersectWith(b);
            Check.True(inter.EnumerateSetBits().SequenceEqual(new[] { 3, 64 }));

            var except = Make(1, 2, 3, 64); except.ExceptWith(b);
            Check.True(except.EnumerateSetBits().SequenceEqual(new[] { 1, 2 }));

            var sym = Make(1, 2, 3, 64); sym.SymmetricExceptWith(b);
            Check.True(sym.EnumerateSetBits().SequenceEqual(new[] { 1, 2, 4, 65 }));
        }

        // Differential: mirror random set/clear against a HashSet<int>, and cross-check all four set ops.
        public void Differential_MatchesHashSet()
        {
            var rng = new Random(9090);
            for (int trial = 0; trial < 300; trial++)
            {
                var bs = new BitSet(rng.Next(1, 128));
                var model = new HashSet<int>();

                for (int op = 0; op < 200; op++)
                {
                    int index = rng.Next(0, 300);
                    if (rng.Next(3) != 0)
                    {
                        bs.Set(index);
                        model.Add(index);
                    }
                    else
                    {
                        bs.Clear(index);
                        model.Remove(index);
                    }
                    Check.Equal(model.Count, bs.Count, $"trial {trial} op {op}: count");
                }

                Check.True(model.OrderBy(x => x).SequenceEqual(bs.EnumerateSetBits()),
                    $"trial {trial}: set-bit enumeration");

                // Cross-check a random set operation against the HashSet equivalent.
                var otherModel = new HashSet<int>();
                var other = new BitSet();
                for (int k = 0; k < 50; k++)
                {
                    int idx = rng.Next(0, 300);
                    other.Set(idx);
                    otherModel.Add(idx);
                }

                var expected = new HashSet<int>(model);
                switch (rng.Next(4))
                {
                    case 0: bs.UnionWith(other); expected.UnionWith(otherModel); break;
                    case 1: bs.IntersectWith(other); expected.IntersectWith(otherModel); break;
                    case 2: bs.ExceptWith(other); expected.ExceptWith(otherModel); break;
                    default: bs.SymmetricExceptWith(other); expected.SymmetricExceptWith(otherModel); break;
                }
                Check.True(expected.OrderBy(x => x).SequenceEqual(bs.EnumerateSetBits()),
                    $"trial {trial}: after set op");
            }
        }

        private static BitSet Make(params int[] bits)
        {
            var bs = new BitSet();
            foreach (var b in bits)
                bs.Set(b);
            return bs;
        }
    }
}
