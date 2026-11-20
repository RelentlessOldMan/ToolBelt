using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>Regressions from review round 173 (Collections, tenth pass): each test failed before its fix.</summary>
    public sealed class CollectionsReview173Tests
    {
        // ---------------- NaN inside tuples and object keys ----------------

        private static double RandomNaN(Random rng)
        {
            long payload = (long)(rng.NextDouble() * (1L << 50)) + 1;
            long sign = rng.Next(2) == 0 ? 0 : long.MinValue;
            return BitConverter.Int64BitsToDouble(sign | 0x7FF8000000000000L | payload);
        }

        private static float RandomSingleNaN(Random rng)
            => BitConverter.ToSingle(BitConverter.GetBytes(0x7FC00000 | (rng.Next(1 << 21) + 1)), 0);

        private static double RandomValue(Random rng)
        {
            switch (rng.Next(5))
            {
                case 0: case 1: return RandomNaN(rng);
                case 2: return rng.Next(2) == 0 ? 0.0 : -0.0;
                default: return rng.Next(3) + 0.5;
            }
        }

        // The value the key stands for: every NaN one, both zeros one.
        private static string Canon(double v) => double.IsNaN(v) ? "NaN" : v == 0 ? "0" : v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        private static string Canon(float v) => float.IsNaN(v) ? "NaN" : v == 0 ? "0" : v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        // .NET Framework hashed a NaN inside a tuple or a boxed key by its bits: (NaN, 1) and (NaN with another payload, 1)
        // are Equals but were two keys there.
        public void KeyedCollections_NaNInsideTuplesAndObjectKeys_IsOneKey()
        {
            var rng = new Random(173);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = 1 + rng.Next(30);
                var keys = new List<(double, int)>();
                for (int i = 0; i < n; i++) keys.Add((RandomValue(rng), rng.Next(3)));
                int expected = keys.Select(k => Canon(k.Item1) + "|" + k.Item2).Distinct().Count();
                int expectedValues = keys.Select(k => Canon(k.Item1)).Distinct().Count();

                var counter = new Counter<(double, int)>();
                var boxed = new Counter<object>();
                var reference = new Counter<Tuple<double, int>>();
                var nested = new Counter<((double, int), string)>();
                var set = new OrderedSet<(double, int)>();
                var sketch = new CountMinSketch<(double, int)>(4096, 4);
                foreach (var k in keys)
                {
                    counter.Add(k);
                    boxed.Add(k.Item1);
                    reference.Add(Tuple.Create(k.Item1, k.Item2));
                    nested.Add((k, "x"));
                    set.Add(k);
                    sketch.Add(k);
                }
                Check.Equal(expected, counter.Count, "Counter<(double, int)>");
                Check.Equal(expectedValues, boxed.Count, "Counter<object>");
                Check.Equal(expected, reference.Count, "Counter<Tuple<double, int>>");
                Check.Equal(expected, nested.Count, "nested tuple");
                Check.Equal(expected, set.Count, "OrderedSet");
                foreach (var k in keys)
                {
                    long truth = keys.Count(o => Canon(o.Item1) == Canon(k.Item1) && o.Item2 == k.Item2);
                    Check.True(sketch.Estimate(k) >= truth, "CountMinSketch under-counted " + Canon(k.Item1));
                }
                Check.Equal(expected, Aggregation.Summarize(keys, k => k, k => 1.0).Count, "Summarize");
                Check.Equal(expectedValues, Aggregation.Pivot(keys, k => (object)k.Item1, k => 0, k => 1.0, v => v.Count).Count, "Pivot");

                var floats = new Counter<(float, int)>();
                var floatKeys = new List<(float, int)>();
                for (int i = 0; i < n; i++) floatKeys.Add((rng.Next(2) == 0 ? RandomSingleNaN(rng) : rng.Next(2), rng.Next(2)));
                foreach (var k in floatKeys) floats.Add(k);
                Check.Equal(floatKeys.Select(k => Canon(k.Item1) + "|" + k.Item2).Distinct().Count(), floats.Count, "Counter<(float, int)>");
            }
        }

        // SequenceComparer.Ordered<object> gave two Equals sequences different hash codes, and Unordered<object> called them
        // unequal, on .NET Framework.
        public void SequenceComparer_NaNsWithOtherPayloads_AreEqualWithOneHash()
        {
            var rng = new Random(1731);
            var ordered = SequenceComparer.Ordered<object>();
            var unordered = SequenceComparer.Unordered<object>();
            var tuples = SequenceComparer.Ordered<(double, int)>();
            for (int trial = 0; trial < 500; trial++)
            {
                int n = 1 + rng.Next(8);
                var a = new List<object>();
                var b = new List<object>();
                var ta = new List<(double, int)>();
                var tb = new List<(double, int)>();
                for (int i = 0; i < n; i++)
                {
                    double v = RandomValue(rng);
                    double w = double.IsNaN(v) ? RandomNaN(rng) : v == 0 ? -v : v;   // equal to v, other bits
                    a.Add(v); b.Add(w);
                    ta.Add((v, i)); tb.Add((w, i));
                }
                Check.True(ordered.Equals(a, b), "ordered Equals");
                Check.Equal(ordered.GetHashCode(a), ordered.GetHashCode(b), "ordered hash");
                b.Reverse();
                Check.True(unordered.Equals(a, b), "unordered Equals");
                Check.Equal(unordered.GetHashCode(a), unordered.GetHashCode(b), "unordered hash");
                Check.True(tuples.Equals(ta, tb), "tuple Equals");
                Check.Equal(tuples.GetHashCode(ta), tuples.GetHashCode(tb), "tuple hash");
            }
        }

        // ---------------- Aggregation.Summarize ----------------

        private static bool IsNegative(double v) => BitConverter.DoubleToInt64Bits(v) < 0;

        // The sum started at +0, so a group of −0 values had a sum of +0 (and a mean of −0).
        public void Summarize_ZeroSums_KeepTheSignOfTheExactSum()
        {
            var rng = new Random(1732);
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = 1 + rng.Next(6);
                var values = new List<double>();
                for (int i = 0; i < n; i++)
                {
                    switch (rng.Next(4))
                    {
                        case 0: values.Add(0.0); break;
                        case 1: case 2: values.Add(-0.0); break;
                        default:
                            double x = (rng.Next(2) == 0 ? 1e-300 : 3.0) * (rng.Next(4) + 1);
                            values.Add(x); values.Add(-x); break;
                    }
                }
                // IEEE: the sum of no values is −0; −0 + −0 is −0, and any other exact zero sum is +0.
                double expected = -0.0;
                foreach (double v in values) expected += v;
                var g = Aggregation.Summarize(values, v => 1, v => v).Single();
                Check.Equal(0.0, g.Sum);
                Check.Equal(IsNegative(expected), IsNegative(g.Sum), "sum sign of " + string.Join(", ", values.Select(v => IsNegative(v) && v == 0 ? "-0" : v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
                if (values.All(v => v == 0))
                    Check.Equal(IsNegative(g.Sum), IsNegative(g.Mean), "mean sign");
            }
        }

        // ---------------- Counter.MostCommon ----------------

        // Ties came out in dictionary order: a, b, c, remove a, add d gave d, b, c (d took a's slot).
        public void Counter_MostCommon_BreaksTiesByFirstAdded()
        {
            var rng = new Random(1733);
            for (int trial = 0; trial < 500; trial++)
            {
                var counter = new Counter<string>();
                var model = new List<KeyValuePair<string, int>>();   // insertion order
                int ops = 1 + rng.Next(40);
                for (int op = 0; op < ops; op++)
                {
                    string item = ((char)('a' + rng.Next(8))).ToString();
                    int amount = 1 + rng.Next(3);
                    int at = model.FindIndex(p => p.Key == item);
                    if (rng.Next(3) > 0)
                    {
                        counter.Add(item, amount);
                        if (at < 0) model.Add(new KeyValuePair<string, int>(item, amount));
                        else model[at] = new KeyValuePair<string, int>(item, model[at].Value + amount);
                    }
                    else
                    {
                        counter.Remove(item, amount);
                        if (at >= 0)
                        {
                            int left = model[at].Value - amount;
                            if (left <= 0) model.RemoveAt(at);
                            else model[at] = new KeyValuePair<string, int>(item, left);
                        }
                    }
                }
                int k = rng.Next(10);
                var expected = model.OrderByDescending(p => p.Value).Take(k).Select(p => p.Key + "=" + p.Value);   // stable
                Check.Equal(string.Join(" ", expected), string.Join(" ", counter.MostCommon(k).Select(p => p.Key + "=" + p.Value)));
            }

            var c = new Counter<string>();
            c.Add("a"); c.Add("b"); c.Add("c"); c.Remove("a"); c.Add("d");
            Check.Equal("b c d", string.Join(" ", c.MostCommon(3).Select(p => p.Key)));
        }

        // ---------------- CountMinSketch ----------------

        // Width and depth were checked alone, so 0x7FEFFFFF by 0x7FEFFFFF asked for about 2^62 counters (OutOfMemoryException).
        public void CountMinSketch_WidthTimesDepthPastArrayMaxLength_IsRejected()
        {
            var e = Check.Throws<ArgumentOutOfRangeException>(() => new CountMinSketch<int>(0x7FEFFFFF, 0x7FEFFFFF));
            Check.Equal("depth", e.ParamName);
            e = Check.Throws<ArgumentOutOfRangeException>(() => new CountMinSketch<int>(0x40000000, 0x40000000));
            Check.Equal("depth", e.ParamName);
            Check.Equal(0L, new CountMinSketch<int>(1 << 10, 1 << 10).Estimate(5));
        }
    }
}
