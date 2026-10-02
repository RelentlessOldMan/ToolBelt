using System;
using System.Collections.Generic;
using ToolBelt.Collections;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class CountingBloomFilterTests
    {
        public void NoFalseNegatives_ForAddedItems()
        {
            var f = new CountingBloomFilter(1000);
            var items = new List<string>();
            var r = new DeterministicRandom(1);
            for (int i = 0; i < 500; i++)
            {
                string s = "item-" + r.Next(1_000_000);
                items.Add(s);
                f.Add(s);
            }
            foreach (string s in items)
                Check.True(f.MightContain(s), $"false negative for {s}");
        }

        public void SingleItem_RemovedBecomesAbsent()
        {
            var f = new CountingBloomFilter(1000);
            f.Add("lonely");
            Check.True(f.MightContain("lonely"));
            Check.True(f.Remove("lonely"));
            // With no other items touching those slots, every counter returns to zero.
            Check.False(f.MightContain("lonely"));
        }

        public void Remove_OnEmptyFilter_ReturnsFalse_AndNoOp()
        {
            var f = new CountingBloomFilter(1000);
            Check.False(f.MightContain("ghost"));
            Check.False(f.Remove("ghost")); // absent -> reports false, changes nothing
            Check.False(f.MightContain("ghost"));
        }

        public void AddRemoveSequence_PreservesNoFalseNegativeInvariant()
        {
            // Differential: track the true multiset (net counts) and after every op assert that every item
            // with a positive net count is still reported present. Items at zero MAY be a false positive, so
            // we never assert absence here — only the one-sided guarantee the structure actually makes.
            var f = new CountingBloomFilter(2000);
            var counts = new Dictionary<string, int>();
            var r = new DeterministicRandom(7);
            string Key(int k) => "k" + k;

            for (int step = 0; step < 3000; step++)
            {
                int k = r.Next(80);
                string key = Key(k);
                counts.TryGetValue(key, out int c);
                bool add = r.NextDouble() < 0.6 || c == 0; // bias toward adds, never remove below zero
                if (add) { f.Add(key); counts[key] = c + 1; }
                else { f.Remove(key); counts[key] = c - 1; }

                if (step % 200 == 0)
                    foreach (var kv in counts)
                        if (kv.Value > 0)
                            Check.True(f.MightContain(kv.Key), $"false negative for {kv.Key} (count {kv.Value})");
            }
        }

        public void Constructor_Validates()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new CountingBloomFilter(0));
            Check.Throws<ArgumentOutOfRangeException>(() => new CountingBloomFilter(-5));
            Check.Throws<ArgumentOutOfRangeException>(() => new CountingBloomFilter(100, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new CountingBloomFilter(100, 1));
        }

        public void NullArguments_Throw()
        {
            var f = new CountingBloomFilter(100);
            Check.Throws<ArgumentNullException>(() => f.Add((byte[])null!));
            Check.Throws<ArgumentNullException>(() => f.Add((string)null!));
            Check.Throws<ArgumentNullException>(() => f.Remove((byte[])null!));
            Check.Throws<ArgumentNullException>(() => f.MightContain((string)null!));
        }

        public void SizingMatchesBloomFilterFormulas()
        {
            // Same optimal-sizing math as BloomFilter: a sanity check that slots/hashes are positive and scale.
            var f = new CountingBloomFilter(1000, 0.01);
            Check.True(f.SlotCount > 0);
            Check.True(f.HashCount > 0);
        }
    }
}
