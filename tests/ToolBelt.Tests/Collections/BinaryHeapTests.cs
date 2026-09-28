using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class BinaryHeapTests
    {
        public void PopsInAscendingOrder()
        {
            var heap = new BinaryHeap<int>();
            foreach (var x in new[] { 5, 1, 4, 2, 3 })
                heap.Push(x);

            var popped = new List<int>();
            while (heap.TryPop(out int v))
                popped.Add(v);

            Check.True(popped.SequenceEqual(new[] { 1, 2, 3, 4, 5 }));
        }

        public void CustomComparer_MakesMaxHeap()
        {
            var heap = new BinaryHeap<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            foreach (var x in new[] { 5, 1, 4, 2, 3 })
                heap.Push(x);
            Check.Equal(5, heap.Pop());
            Check.Equal(4, heap.Pop());
        }

        public void Heapify_FromCollection()
        {
            var heap = new BinaryHeap<int>(new[] { 9, 3, 7, 1, 8, 2 });
            Check.Equal(1, heap.Peek());
            Check.Equal(6, heap.Count);
        }

        public void Empty_PeekAndPop_Behavior()
        {
            var heap = new BinaryHeap<int>();
            Check.False(heap.TryPop(out _));
            Check.False(heap.TryPeek(out _));
            Check.Throws<InvalidOperationException>(() => heap.Pop());
            Check.Throws<InvalidOperationException>(() => heap.Peek());
        }

        // Differential: draining the heap must yield the same multiset in the same order as sorting the
        // pushed items with the heap's comparer.
        public void Differential_DrainEqualsSorted()
        {
            var rng = new Random(0x5EED);
            for (int trial = 0; trial < 500; trial++)
            {
                int n = rng.Next(0, 100);
                var items = new int[n];
                for (int i = 0; i < n; i++)
                    items[i] = rng.Next(-50, 50); // small range → many duplicates

                var heap = new BinaryHeap<int>(items); // exercise heapify
                var drained = new List<int>(n);
                while (heap.TryPop(out int v))
                    drained.Add(v);

                var expected = items.OrderBy(x => x).ToArray();
                Check.True(expected.SequenceEqual(drained), $"trial {trial} (n={n})");
            }
        }

        // Differential: interleave random pushes and pops against a sorted-list reference model.
        public void Differential_InterleavedOps()
        {
            var rng = new Random(24);
            for (int trial = 0; trial < 300; trial++)
            {
                var heap = new BinaryHeap<int>();
                var model = new List<int>();

                for (int op = 0; op < 200; op++)
                {
                    if (rng.Next(2) == 0)
                    {
                        int v = rng.Next(-20, 20);
                        heap.Push(v);
                        model.Add(v);
                    }
                    else
                    {
                        bool a = heap.TryPop(out int got);
                        bool e = model.Count > 0;
                        Check.Equal(e, a, $"trial {trial} op {op}: pop had element");
                        if (e)
                        {
                            model.Sort();
                            Check.Equal(model[0], got, $"trial {trial} op {op}: min value");
                            model.RemoveAt(0);
                        }
                    }
                    Check.Equal(model.Count, heap.Count, $"trial {trial} op {op}: count");
                }
            }
        }
    }
}
