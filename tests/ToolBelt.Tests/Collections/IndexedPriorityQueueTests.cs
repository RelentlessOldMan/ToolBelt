using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class IndexedPriorityQueueTests
    {
        public void DequeuesInPriorityOrder()
        {
            var pq = new IndexedPriorityQueue<string, int>();
            pq.Enqueue("c", 3);
            pq.Enqueue("a", 1);
            pq.Enqueue("b", 2);

            var order = new List<string>();
            while (pq.TryDequeueMin(out string k, out _))
                order.Add(k);
            Check.True(order.SequenceEqual(new[] { "a", "b", "c" }));
        }

        public void UpdatePriority_DecreaseKey_Reorders()
        {
            var pq = new IndexedPriorityQueue<string, int>();
            pq.Enqueue("a", 1);
            pq.Enqueue("b", 2);
            pq.Enqueue("c", 3);
            pq.UpdatePriority("c", 0); // c jumps to the front

            Check.True(pq.TryDequeueMin(out string k, out int p) && k == "c" && p == 0);
        }

        public void UpdatePriority_IncreaseKey_Reorders()
        {
            var pq = new IndexedPriorityQueue<string, int>();
            pq.Enqueue("a", 1);
            pq.Enqueue("b", 2);
            pq.UpdatePriority("a", 5); // a sinks below b
            Check.True(pq.TryDequeueMin(out string k, out _) && k == "b");
        }

        public void ContainsAndTryGetPriority()
        {
            var pq = new IndexedPriorityQueue<string, int>();
            pq.Enqueue("a", 7);
            Check.True(pq.Contains("a"));
            Check.True(pq.TryGetPriority("a", out int p) && p == 7);
            Check.False(pq.Contains("z"));
        }

        public void DuplicateEnqueue_Throws()
        {
            var pq = new IndexedPriorityQueue<string, int>();
            pq.Enqueue("a", 1);
            Check.Throws<ArgumentException>(() => pq.Enqueue("a", 2));
        }

        public void UpdateMissing_Throws()
        {
            var pq = new IndexedPriorityQueue<string, int>();
            Check.Throws<KeyNotFoundException>(() => pq.UpdatePriority("nope", 1));
        }

        public void Empty_Dequeue_False()
        {
            var pq = new IndexedPriorityQueue<int, int>();
            Check.False(pq.TryDequeueMin(out _, out _));
            Check.False(pq.TryPeekMin(out _, out _));
        }

        // Differential: mirror random enqueue/update/dequeue against a naive dictionary model where the
        // min is found by linear scan. Compare the full dequeue order.
        public void Differential_MatchesNaiveModel()
        {
            var rng = new Random(20260926);
            for (int trial = 0; trial < 300; trial++)
            {
                var pq = new IndexedPriorityQueue<int, int>();
                var model = new Dictionary<int, int>();

                for (int op = 0; op < 100; op++)
                {
                    int key = rng.Next(0, 20);
                    switch (rng.Next(3))
                    {
                        case 0: // enqueue-or-update
                            int pri = rng.Next(0, 100);
                            if (model.ContainsKey(key)) { pq.UpdatePriority(key, pri); model[key] = pri; }
                            else { pq.Enqueue(key, pri); model[key] = pri; }
                            break;
                        default: // dequeue min
                            bool a = pq.TryDequeueMin(out int dk, out int dp);
                            bool e = model.Count > 0;
                            Check.Equal(e, a, $"trial {trial} op {op}: dequeue had element");
                            if (e)
                            {
                                int minPri = model.Values.Min();
                                Check.Equal(minPri, dp, $"trial {trial} op {op}: min priority");
                                Check.Equal(minPri, model[dk], $"trial {trial} op {op}: returned key's priority is the min");
                                model.Remove(dk);
                            }
                            break;
                    }
                    Check.Equal(model.Count, pq.Count, $"trial {trial} op {op}: count");
                }
            }
        }
    }
}
