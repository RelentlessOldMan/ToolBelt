using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class OrderedSetTests
    {
        public void PreservesInsertionOrder()
        {
            var s = new OrderedSet<int>();
            foreach (var x in new[] { 3, 1, 2 }) s.Add(x);
            Check.True(s.ToArray().SequenceEqual(new[] { 3, 1, 2 }));
        }

        public void Add_ReturnsFalseOnDuplicate_AndKeepsPosition()
        {
            var s = new OrderedSet<int>();
            Check.True(s.Add(1));
            Check.True(s.Add(2));
            Check.False(s.Add(1)); // duplicate
            Check.True(s.ToArray().SequenceEqual(new[] { 1, 2 }));
            Check.Equal(2, s.Count);
        }

        public void Remove()
        {
            var s = new OrderedSet<int>();
            s.Add(1); s.Add(2); s.Add(3);
            Check.True(s.Remove(2));
            Check.True(s.ToArray().SequenceEqual(new[] { 1, 3 }));
            Check.False(s.Remove(2));
        }

        public void Null_Throws()
        {
            var s = new OrderedSet<string>();
            Check.Throws<ArgumentNullException>(() => s.Add(null!));
        }

        // Differential: mirror random add/remove against (List order + HashSet membership) and compare
        // order and membership after each op.
        public void Differential_MatchesListPlusHashSet()
        {
            var rng = new Random(2025);
            for (int trial = 0; trial < 300; trial++)
            {
                var set = new OrderedSet<int>();
                var order = new List<int>();
                var member = new HashSet<int>();

                for (int op = 0; op < 200; op++)
                {
                    int value = rng.Next(0, 10);
                    if (rng.Next(2) == 0)
                    {
                        bool a = set.Add(value);
                        bool e = member.Add(value);
                        if (e) order.Add(value);
                        Check.Equal(e, a, $"trial {trial} op {op}: add {value}");
                    }
                    else
                    {
                        bool a = set.Remove(value);
                        bool e = member.Remove(value);
                        if (e) order.Remove(value);
                        Check.Equal(e, a, $"trial {trial} op {op}: remove {value}");
                    }

                    Check.Equal(order.Count, set.Count, $"trial {trial} op {op}: count");
                    Check.True(order.SequenceEqual(set.ToArray()), $"trial {trial} op {op}: order");
                }
            }
        }
    }
}
