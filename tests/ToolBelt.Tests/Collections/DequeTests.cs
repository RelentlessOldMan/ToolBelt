using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class DequeTests
    {
        public void PushBothEnds_OrdersFrontToBack()
        {
            var d = new Deque<int>();
            d.PushBack(1);
            d.PushBack(2);
            d.PushFront(0);
            d.PushFront(-1);
            Check.True(d.ToArray().SequenceEqual(new[] { -1, 0, 1, 2 }));
            Check.Equal(-1, d.PeekFront);
            Check.Equal(2, d.PeekBack);
        }

        public void PopBothEnds()
        {
            var d = new Deque<int>();
            for (int i = 0; i < 5; i++) d.PushBack(i); // 0..4
            Check.True(d.TryPopFront(out int f) && f == 0);
            Check.True(d.TryPopBack(out int b) && b == 4);
            Check.True(d.ToArray().SequenceEqual(new[] { 1, 2, 3 }));
        }

        public void Empty_PopsReturnFalse_PeeksThrow()
        {
            var d = new Deque<int>();
            Check.False(d.TryPopFront(out _));
            Check.False(d.TryPopBack(out _));
            Check.Throws<InvalidOperationException>(() => _ = d.PeekFront);
            Check.Throws<InvalidOperationException>(() => _ = d.PeekBack);
        }

        public void Indexer_OutOfRange_Throws()
        {
            var d = new Deque<int>();
            d.PushBack(7);
            Check.Equal(7, d[0]);
            Check.Throws<ArgumentOutOfRangeException>(() => _ = d[1]);
        }

        public void GrowsBeyondInitialCapacity()
        {
            var d = new Deque<int>(2);
            for (int i = 0; i < 100; i++) d.PushBack(i);
            Check.Equal(100, d.Count);
            Check.True(d.ToArray().SequenceEqual(Enumerable.Range(0, 100)));
        }

        // Differential: mirror random front/back pushes and pops against a List<T> reference and compare
        // full contents plus peek values after every operation.
        public void Differential_MatchesListModel()
        {
            var rng = new Random(97531);
            for (int trial = 0; trial < 300; trial++)
            {
                var d = new Deque<int>(rng.Next(1, 5));
                var model = new List<int>();

                for (int op = 0; op < 300; op++)
                {
                    switch (rng.Next(4))
                    {
                        case 0:
                            int vb = rng.Next(1000);
                            d.PushBack(vb);
                            model.Add(vb);
                            break;
                        case 1:
                            int vf = rng.Next(1000);
                            d.PushFront(vf);
                            model.Insert(0, vf);
                            break;
                        case 2:
                        {
                            bool a = d.TryPopFront(out int gf);
                            bool e = model.Count > 0;
                            Check.Equal(e, a, $"trial {trial} op {op}: pop-front had element");
                            if (e) { Check.Equal(model[0], gf, $"trial {trial} op {op}: pop-front value"); model.RemoveAt(0); }
                            break;
                        }
                        default:
                        {
                            bool a = d.TryPopBack(out int gb);
                            bool e = model.Count > 0;
                            Check.Equal(e, a, $"trial {trial} op {op}: pop-back had element");
                            if (e) { Check.Equal(model[^1], gb, $"trial {trial} op {op}: pop-back value"); model.RemoveAt(model.Count - 1); }
                            break;
                        }
                    }

                    Check.Equal(model.Count, d.Count, $"trial {trial} op {op}: count");
                    Check.True(model.SequenceEqual(d.ToArray()),
                        $"trial {trial} op {op}: [{string.Join(",", d.ToArray())}] vs [{string.Join(",", model)}]");
                }
            }
        }
    }
}
