using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class CounterTests
    {
        public void AddAndCount()
        {
            var c = new Counter<string>();
            c.Add("a");
            c.Add("a", 2);
            c.Add("b");
            Check.Equal(3, c["a"]);
            Check.Equal(1, c["b"]);
            Check.Equal(0, c["missing"]);
            Check.Equal(2, c.Count);
            Check.Equal(4L, c.Total);
        }

        public void Remove_ClampsAndDrops()
        {
            var c = new Counter<string>();
            c.Add("a", 3);
            Check.Equal(2, c.Remove("a", 2));
            Check.Equal(1, c["a"]);
            Check.Equal(1, c.Remove("a", 5)); // clamps at available
            Check.False(c.Contains("a"));      // dropped at zero
            Check.Equal(0L, c.Total);
            Check.Equal(0, c.Remove("gone"));  // absent
        }

        public void MostCommon()
        {
            var c = new Counter<string>();
            c.Add("a", 5);
            c.Add("b", 3);
            c.Add("c", 8);
            var top2 = c.MostCommon(2);
            Check.Equal("c", top2[0].Key);
            Check.Equal(8, top2[0].Value);
            Check.Equal("a", top2[1].Key);
        }

        public void InvalidArguments_Throw()
        {
            var c = new Counter<string>();
            Check.Throws<ArgumentNullException>(() => c.Add(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => c.Add("a", 0));
            Check.Throws<ArgumentOutOfRangeException>(() => c.Remove("a", -1));
        }

        public void Add_CountOverflow_Throws()
        {
            var c = new Counter<string>();
            c.Add("a", int.MaxValue);
            // One more would wrap the per-item int negative; must throw instead.
            Check.Throws<OverflowException>(() => c.Add("a"));
        }

        // Differential: mirror random add/remove against a plain Dictionary model and compare per-key
        // counts, total, and unique count after each op.
        public void Differential_MatchesDictionaryModel()
        {
            var rng = new Random(31);
            for (int trial = 0; trial < 300; trial++)
            {
                var counter = new Counter<int>();
                var model = new Dictionary<int, int>();

                for (int op = 0; op < 200; op++)
                {
                    int key = rng.Next(0, 6);
                    int amount = rng.Next(1, 4);
                    if (rng.Next(2) == 0)
                    {
                        counter.Add(key, amount);
                        model.TryGetValue(key, out int cur);
                        model[key] = cur + amount;
                    }
                    else
                    {
                        int actualRemoved = counter.Remove(key, amount);
                        int expectedRemoved = 0;
                        if (model.TryGetValue(key, out int cur))
                        {
                            expectedRemoved = Math.Min(cur, amount);
                            int rem = cur - expectedRemoved;
                            if (rem == 0) model.Remove(key);
                            else model[key] = rem;
                        }
                        Check.Equal(expectedRemoved, actualRemoved, $"trial {trial} op {op}: removed");
                    }

                    Check.Equal(model.Count, counter.Count, $"trial {trial} op {op}: unique");
                    Check.Equal((long)model.Values.Sum(), counter.Total, $"trial {trial} op {op}: total");
                    for (int k = 0; k < 6; k++)
                    {
                        int expected = model.TryGetValue(k, out int v) ? v : 0;
                        Check.Equal(expected, counter[k], $"trial {trial} op {op}: count[{k}]");
                    }
                }
            }
        }
    }
}
