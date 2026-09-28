using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class BiMapTests
    {
        public void AddAndLookupBothDirections()
        {
            var map = new BiMap<string, int>();
            map.Add("one", 1);
            map.Add("two", 2);
            Check.Equal(1, map.GetByKey("one"));
            Check.Equal("two", map.GetByValue(2));
            Check.Equal(2, map.Count);
        }

        public void DuplicateKeyOrValue_Throws()
        {
            var map = new BiMap<string, int>();
            map.Add("a", 1);
            Check.Throws<ArgumentException>(() => map.Add("a", 2)); // duplicate key
            Check.Throws<ArgumentException>(() => map.Add("b", 1)); // duplicate value
        }

        public void Remove_ClearsBothDirections()
        {
            var map = new BiMap<string, int>();
            map.Add("a", 1);
            Check.True(map.RemoveByKey("a"));
            Check.False(map.ContainsKey("a"));
            Check.False(map.ContainsValue(1));

            map.Add("b", 2);
            Check.True(map.RemoveByValue(2));
            Check.False(map.ContainsKey("b"));
        }

        public void TryGet()
        {
            var map = new BiMap<string, int>();
            map.Add("k", 5);
            Check.True(map.TryGetByKey("k", out int v) && v == 5);
            Check.True(map.TryGetByValue(5, out string key) && key == "k");
            Check.False(map.TryGetByKey("missing", out _));
        }

        public void NullArguments_Throw()
        {
            var map = new BiMap<string, string>();
            Check.Throws<ArgumentNullException>(() => map.Add(null!, "v"));
            Check.Throws<ArgumentNullException>(() => map.Add("k", null!));
        }

        // Property: forward and inverse maps stay consistent after every operation — for every key k,
        // GetByValue(GetByKey(k)) == k, and vice versa.
        public void Property_InverseConsistency_OverRandomOps()
        {
            var rng = new Random(2718);
            for (int trial = 0; trial < 300; trial++)
            {
                var map = new BiMap<int, int>();

                for (int op = 0; op < 200; op++)
                {
                    int key = rng.Next(0, 10);
                    int value = rng.Next(0, 10);
                    switch (rng.Next(3))
                    {
                        case 0:
                            // Only add when it keeps the bijection intact.
                            if (!map.ContainsKey(key) && !map.ContainsValue(value))
                                map.Add(key, value);
                            break;
                        case 1:
                            map.RemoveByKey(key);
                            break;
                        default:
                            map.RemoveByValue(value);
                            break;
                    }

                    // Consistency: every key round-trips through the inverse and counts agree.
                    var keys = map.Keys.ToList();
                    Check.Equal(keys.Count, map.Count, $"trial {trial} op {op}: count");
                    foreach (int k in keys)
                    {
                        int mapped = map.GetByKey(k);
                        Check.Equal(k, map.GetByValue(mapped), $"trial {trial} op {op}: inverse of {k}");
                    }
                    Check.Equal(map.Count, map.Values.Count(), $"trial {trial} op {op}: value count");
                }
            }
        }
    }
}
