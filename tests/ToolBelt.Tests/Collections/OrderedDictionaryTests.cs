using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class OrderedDictionaryTests
    {
        public void PreservesInsertionOrder()
        {
            var d = new OrderedDictionary<string, int>();
            d.Add("c", 3);
            d.Add("a", 1);
            d.Add("b", 2);
            Check.True(d.Keys.SequenceEqual(new[] { "c", "a", "b" }));
            Check.True(d.Values.SequenceEqual(new[] { 3, 1, 2 }));
        }

        public void UpdateKeepsPosition()
        {
            var d = new OrderedDictionary<string, int>();
            d.Add("x", 1);
            d.Add("y", 2);
            d["x"] = 99; // update, must not move to end
            Check.True(d.Keys.SequenceEqual(new[] { "x", "y" }));
            Check.Equal(99, d["x"]);
        }

        public void IndexerSet_AddsWhenAbsent()
        {
            var d = new OrderedDictionary<string, int>();
            d["new"] = 5;
            Check.Equal(5, d["new"]);
            Check.Equal(1, d.Count);
        }

        public void Add_Duplicate_Throws()
        {
            var d = new OrderedDictionary<string, int>();
            d.Add("k", 1);
            Check.Throws<ArgumentException>(() => d.Add("k", 2));
        }

        public void Remove_UpdatesOrder()
        {
            var d = new OrderedDictionary<string, int>();
            d.Add("a", 1); d.Add("b", 2); d.Add("c", 3);
            Check.True(d.Remove("b"));
            Check.True(d.Keys.SequenceEqual(new[] { "a", "c" }));
            Check.False(d.Remove("b"));
        }

        public void MissingKey_Throws()
        {
            var d = new OrderedDictionary<string, int>();
            Check.Throws<KeyNotFoundException>(() => _ = d["nope"]);
        }

        // Differential: mirror random set/remove ops against a (List order + Dictionary) reference and
        // compare ordered keys/values, count, and lookups after every op.
        public void Differential_MatchesListPlusDictModel()
        {
            var rng = new Random(4711);
            for (int trial = 0; trial < 300; trial++)
            {
                var d = new OrderedDictionary<int, int>();
                var order = new List<int>();
                var values = new Dictionary<int, int>();

                for (int op = 0; op < 200; op++)
                {
                    int key = rng.Next(0, 8);
                    if (rng.Next(3) != 0)
                    {
                        int val = rng.Next(1000);
                        d[key] = val;
                        if (!values.ContainsKey(key))
                            order.Add(key);
                        values[key] = val;
                    }
                    else
                    {
                        bool a = d.Remove(key);
                        bool e = values.Remove(key);
                        if (e) order.Remove(key);
                        Check.Equal(e, a, $"trial {trial} op {op}: remove {key}");
                    }

                    Check.Equal(order.Count, d.Count, $"trial {trial} op {op}: count");
                    Check.True(order.SequenceEqual(d.Keys), $"trial {trial} op {op}: key order");
                    Check.True(order.Select(k => values[k]).SequenceEqual(d.Values),
                        $"trial {trial} op {op}: value order");
                }
            }
        }
    }
}
