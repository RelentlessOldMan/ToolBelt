using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class CircularBufferTests
    {
        public void FillsThenOverwritesOldest()
        {
            var buf = new CircularBuffer<int>(3);
            buf.Add(1); buf.Add(2); buf.Add(3);
            Check.True(buf.ToArray().SequenceEqual(new[] { 1, 2, 3 }));
            buf.Add(4); // overwrites 1
            Check.True(buf.ToArray().SequenceEqual(new[] { 2, 3, 4 }));
            Check.Equal(2, buf.Oldest);
            Check.Equal(4, buf.Newest);
        }

        public void IndexerAndEnumerationAreOldestToNewest()
        {
            var buf = new CircularBuffer<int>(3);
            buf.Add(10); buf.Add(20); buf.Add(30); buf.Add(40); // 20,30,40
            Check.Equal(20, buf[0]);
            Check.Equal(30, buf[1]);
            Check.Equal(40, buf[2]);
            Check.True(buf.SequenceEqual(new[] { 20, 30, 40 }));
        }

        public void TryRemoveOldest_DrainsInOrder()
        {
            var buf = new CircularBuffer<int>(3);
            buf.Add(1); buf.Add(2); buf.Add(3);
            Check.True(buf.TryRemoveOldest(out int a) && a == 1);
            Check.True(buf.TryRemoveOldest(out int b) && b == 2);
            Check.True(buf.TryRemoveOldest(out int c) && c == 3);
            Check.False(buf.TryRemoveOldest(out _));
        }

        public void Indexer_OutOfRange_Throws()
        {
            var buf = new CircularBuffer<int>(3);
            buf.Add(1);
            Check.Throws<ArgumentOutOfRangeException>(() => _ = buf[1]);
        }

        public void Empty_PeeksThrow()
        {
            var buf = new CircularBuffer<int>(2);
            Check.Throws<InvalidOperationException>(() => _ = buf.Oldest);
            Check.Throws<InvalidOperationException>(() => _ = buf.Newest);
        }

        public void InvalidCapacity_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<int>(0));
        }

        // Differential: a naive List<T> model where Add appends and trims the front past capacity, and
        // remove pops the front. Compare full contents after every random operation.
        public void Differential_MatchesNaiveModel()
        {
            var rng = new Random(13579);
            for (int trial = 0; trial < 300; trial++)
            {
                int capacity = rng.Next(1, 8);
                var buf = new CircularBuffer<int>(capacity);
                var model = new List<int>();

                for (int op = 0; op < 200; op++)
                {
                    if (rng.Next(3) != 0) // bias toward Add
                    {
                        int v = rng.Next(1000);
                        buf.Add(v);
                        model.Add(v);
                        if (model.Count > capacity)
                            model.RemoveAt(0);
                    }
                    else
                    {
                        bool actual = buf.TryRemoveOldest(out int got);
                        bool expected = model.Count > 0;
                        Check.Equal(expected, actual, $"trial {trial} op {op}: remove had element");
                        if (expected)
                        {
                            Check.Equal(model[0], got, $"trial {trial} op {op}: removed value");
                            model.RemoveAt(0);
                        }
                    }

                    Check.Equal(model.Count, buf.Count, $"trial {trial} op {op}: count");
                    Check.True(model.SequenceEqual(buf.ToArray()),
                        $"trial {trial} op {op}: contents [{string.Join(",", buf.ToArray())}] vs [{string.Join(",", model)}]");
                }
            }
        }
    }
}
