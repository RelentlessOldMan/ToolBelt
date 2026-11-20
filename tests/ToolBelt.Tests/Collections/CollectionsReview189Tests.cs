using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>Regressions from review round 189 (Collections, eleventh pass): each test failed before its fix.</summary>
    public sealed class CollectionsReview189Tests
    {
        private static bool OnFramework => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            .StartsWith(".NET Framework", StringComparison.Ordinal);

        // ---------------- ParamName ----------------

        // Find's own "x" was reported for a bad a or b.
        public void DisjointSet_UnionAndConnected_NameTheBadArgument()
        {
            var set = new DisjointSet(5);
            Check.Equal("b", Check.Throws<ArgumentOutOfRangeException>(() => set.Union(0, -1)).ParamName);
            Check.Equal("a", Check.Throws<ArgumentOutOfRangeException>(() => set.Union(9, 0)).ParamName);
            Check.Equal("b", Check.Throws<ArgumentOutOfRangeException>(() => set.Connected(0, 7)).ParamName);
            Check.Equal("a", Check.Throws<ArgumentOutOfRangeException>(() => set.Connected(-3, 0)).ParamName);
            Check.Equal("x", Check.Throws<ArgumentOutOfRangeException>(() => set.Find(5)).ParamName);
            Check.Equal(5, set.SetCount);
        }

        // RangeSum's "from" was reported.
        public void FenwickTree_ValueAt_NamesIndex()
        {
            var tree = new FenwickTree(4);
            Check.Equal("index", Check.Throws<ArgumentOutOfRangeException>(() => tree.ValueAt(9)).ParamName);
            Check.Equal("index", Check.Throws<ArgumentOutOfRangeException>(() => tree.ValueAt(-1)).ParamName);
            Check.Equal(0L, tree.ValueAt(3));
        }

        // ---------------- A comparer that changes the structure it orders ----------------

        private sealed class Reentrant : IComparer<int>
        {
            public Action? OnCompare;
            public int Compare(int x, int y)
            {
                Action? once = OnCompare;
                OnCompare = null;
                once?.Invoke();
                return x.CompareTo(y);
            }
        }

        private static List<int> Drain(BinaryHeap<int> heap)
        {
            var list = new List<int>();
            while (heap.TryPop(out int x)) list.Add(x);
            return list;
        }

        // The insert used the slot found before the comparer's push and overwrote a live item: one lost, one duplicated.
        public void BinaryHeap_ComparerThatPushes_IsRefusedAndLosesNothing()
        {
            foreach (int size in new[] { 4, 6, 10 })
            {
                var comparer = new Reentrant();
                var heap = new BinaryHeap<int>(comparer);
                var expected = new List<int> { -1000 };
                for (int i = 1; i <= size; i++) { heap.Push(i * 10); expected.Add(i * 10); }
                comparer.OnCompare = () => heap.Push(-1000);
                Check.Throws<InvalidOperationException>(() => heap.Push(-5), "size " + size);
                Check.Equal(string.Join(",", expected), string.Join(",", Drain(heap)), "size " + size);
            }
        }

        public void BinaryHeap_ComparerThatPops_IsRefusedAndLosesNothing()
        {
            var comparer = new Reentrant();
            var heap = new BinaryHeap<int>(comparer);
            for (int i = 1; i <= 8; i++) heap.Push(i);
            comparer.OnCompare = () => heap.Pop();   // takes 1
            Check.Throws<InvalidOperationException>(() => heap.Pop());
            Check.Equal("2,3,4,5,6,7,8", string.Join(",", Drain(heap)));
        }

        public void TopN_ComparerThatAdds_IsRefusedAndLosesNothing()
        {
            // Room left: the insert overwrote a live item.
            var comparer = new Reentrant();
            var top = new TopN<int>(10, comparer);
            foreach (int v in new[] { 10, 20, 30, 40 }) top.Add(v);
            comparer.OnCompare = () => top.Add(1000);
            Check.Throws<InvalidOperationException>(() => top.Add(5));
            Check.Equal("1000,40,30,20,10", string.Join(",", top.ToArray()));

            // Full: the write landed out of range (IndexOutOfRangeException from inside the library) or on a live item.
            comparer = new Reentrant();
            top = new TopN<int>(4, comparer);
            foreach (int v in new[] { 10, 20, 30, 40 }) top.Add(v);
            comparer.OnCompare = () => top.Add(1000);
            Check.Throws<InvalidOperationException>(() => top.Add(50));
            Check.Equal("1000,40,30,20", string.Join(",", top.ToArray()));
        }

        public void IndexedPriorityQueue_ComparerThatEnqueues_IsRefusedAndLosesNothing()
        {
            var comparer = new Reentrant();
            var queue = new IndexedPriorityQueue<string, int>(comparer);
            queue.Enqueue("a", 10); queue.Enqueue("b", 20); queue.Enqueue("c", 30); queue.Enqueue("d", 40);
            comparer.OnCompare = () => queue.Enqueue("z", -1000);
            Check.Throws<InvalidOperationException>(() => queue.Enqueue("e", -5));
            Check.False(queue.Contains("e"));

            comparer.OnCompare = () => queue.Enqueue("y", -2000);
            Check.Throws<InvalidOperationException>(() => queue.UpdatePriority("d", -3000));
            Check.True(queue.TryGetPriority("d", out int d) && d == 40, "d keeps its priority");

            comparer.OnCompare = () => queue.Enqueue("x", -4000);
            Check.Throws<InvalidOperationException>(() => queue.TryDequeueMin(out _, out _));

            var order = new List<string>();
            while (queue.TryDequeueMin(out string key, out int priority)) order.Add(key + "=" + priority.ToString(CultureInfo.InvariantCulture));
            Check.Equal("x=-4000,y=-2000,z=-1000,a=10,b=20,c=30,d=40", string.Join(",", order));
        }

        // ---------------- DataTableLite.ToString ----------------

        private static string Cell(object value)
        {
            string text = new DataTableLite(new[] { "v" }).AddRow(value).ToString();
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines[lines.Length - 1].Trim();
        }

        // .NET Framework's "R" wrote 1.401298E-45 for the float 1E-45, and "0" for -0.
        public void DataTableLite_ToString_WritesFloatsAndZerosAsDotNetDoes()
        {
            Check.Equal("1E-45", Cell(1e-45f));
            Check.Equal("-0", Cell(-0.0));
            Check.Equal("-0", Cell(-0.0f));
            Check.Equal("0", Cell(0.0));
            Check.Equal("0.30000000000000004", Cell(0.1 + 0.2));
            Check.Equal("1000000000000000", Cell(1e15));
            Check.Equal("12345678901234568", Cell(12345678901234567.0));
            Check.Equal("1E+17", Cell(1e17));
            Check.Equal("123456789012345", Cell(123456789012345.0));
            Check.Equal("1E-05", Cell(1e-5));
            Check.Equal("0.0001", Cell(1e-4));
            Check.Equal("10000000", Cell(1e7f));
            Check.Equal("277985000", Cell(277985000f));
            Check.Equal("1E+09", Cell(1e9f));
            Check.Equal("1E-05", Cell(1e-5f));
            Check.Equal("1234567", Cell(1234567f));
            Check.Equal("3.4028235E+38", Cell(float.MaxValue));
            Check.Equal("1.7976931348623157E+308", Cell(double.MaxValue));
            Check.Equal("5E-324", Cell(double.Epsilon));
            Check.Equal("-1.5", Cell(-1.5));
            Check.Equal("0.1", Cell(0.1f));
        }

        private static double RandomDouble(Random rng)
        {
            switch (rng.Next(4))
            {
                case 0: return rng.Next(-1000000, 1000000) * Math.Pow(10, rng.Next(-8, 20));
                case 1: return Math.Round(rng.NextDouble() * 1000, rng.Next(0, 8));
                default:
                    var bytes = new byte[8];
                    rng.NextBytes(bytes);
                    double v = BitConverter.ToDouble(bytes, 0);
                    return double.IsNaN(v) || double.IsInfinity(v) ? 1.0 : v;
            }
        }

        // Every value reads back exactly; on .NET the text is "R"'s, on .NET Framework it is graded against the expected
        // shortest form above and the round trip.
        public void DataTableLite_ToString_RoundTripsAndMatchesR()
        {
            var rng = new Random(189);
            for (int trial = 0; trial < 4000; trial++)
            {
                double d = RandomDouble(rng);
                float f = (float)RandomDouble(rng);
                if (float.IsInfinity(f)) f = 1f;
                string ds = Cell(d), fs = Cell(f);
                Check.True(double.Parse(ds, NumberStyles.Float, CultureInfo.InvariantCulture) == d || (d == 0 && ds.TrimStart('-') == "0"), "double " + ds);
                Check.True((float)double.Parse(fs, NumberStyles.Float, CultureInfo.InvariantCulture) == f || (f == 0 && fs.TrimStart('-') == "0"), "float " + fs);
                if (!OnFramework)
                {
                    Check.Equal(d.ToString("R", CultureInfo.InvariantCulture), ds);
                    Check.Equal(f.ToString("R", CultureInfo.InvariantCulture), fs);
                }
            }
        }

        // ---------------- Aggregation ----------------

        // The aggregator got the list itself: one that cast it back and added to it changed the cell (103, not 3).
        public void Pivot_Aggregator_CannotChangeTheCell()
        {
            var items = new[] { ("a", "x", 1.0), ("a", "x", 2.0), ("b", "x", 5.0) };
            bool sawList = false;
            var pivot = Aggregation.Pivot(items, t => t.Item1, t => t.Item2, t => t.Item3, values =>
            {
                if (values is List<double> list) { sawList = true; list.Add(100); }
                if (values is IList<double> ilist && !ilist.IsReadOnly) { sawList = true; ilist.Add(100); }
                return values.Sum();
            });
            Check.False(sawList, "the aggregator could change the values");
            Check.Equal(3.0, pivot["a"]["x"]);
            Check.Equal(5.0, pivot["b"]["x"]);
        }

        // The exact mean of doubles, as a double within a relative 2^-60 (a BigInteger sum over 2^1074).
        private static double ExactMean(IReadOnlyList<double> values)
        {
            BigInteger sum = BigInteger.Zero;
            foreach (double v in values)
            {
                long bits = BitConverter.DoubleToInt64Bits(v);
                int exp = (int)((bits >> 52) & 0x7FF);
                long mant = bits & 0xFFFFFFFFFFFFFL;
                if (exp == 0) exp = 1; else mant |= 1L << 52;
                BigInteger m = new BigInteger(mant) << (exp - 1);   // v * 2^1074
                sum += bits < 0 ? -m : m;
            }
            if (sum.IsZero) return 0;
            BigInteger q = (sum << 64) / values.Count;   // mean * 2^(1074 + 64)
            int length = q.ToByteArray().Length * 8;
            int shift = Math.Max(0, length - 120);
            double head = (double)(q >> shift);
            return head * Math.Pow(2, -569) * Math.Pow(2, shift - 64 - 1074 + 569);
        }

        private static double Ulp(double x)
        {
            x = Math.Abs(x);
            long bits = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(bits + 1) - x;
        }

        // Welford's running mean was off by up to an ulp of the largest value, so a mean near zero after cancellation came
        // out with the wrong sign: [0.1, 0.2, -0.30000000000000004] gave +1.4e-17, the exact mean being -9.3e-18.
        public void Summarize_Mean_IsTheExactMeanEvenAfterCancellation()
        {
            var three = new[] { 0.1, 0.2, -0.30000000000000004 };
            double mean = Aggregation.Summarize(three, _ => 0, v => v)[0].Mean;
            double exact = ExactMean(three);
            Check.True(exact < 0, "reference sign");
            Check.True(Math.Abs(mean - exact) <= 2 * Ulp(exact), "mean " + mean.ToString("R", CultureInfo.InvariantCulture) + ", exact " + exact.ToString("R", CultureInfo.InvariantCulture));

            var rng = new Random(1890);
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = 2 + rng.Next(40);
                var values = new List<double>();
                for (int i = 0; i < n - 1; i++)
                    values.Add((rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-3, 4)));
                // The last value nearly cancels the rest, so the exact mean is far below the values' size.
                double s = 0;
                foreach (double v in values) s += v;
                values.Add(-s + (rng.Next(3) - 1) * Ulp(s) * rng.Next(4));
                values = values.OrderBy(_ => rng.Next()).ToList();

                double got = Aggregation.Summarize(values, _ => 0, v => v)[0].Mean;
                double want = ExactMean(values);
                double scale = values.Sum(Math.Abs) / n;
                // The compensated sum is good to about n * 2^-104 of the values' size; the division adds an ulp or two.
                double tolerance = 2 * Ulp(want) + n * scale * Math.Pow(2, -100);
                Check.True(Math.Abs(got - want) <= tolerance,
                    "trial " + trial + ": mean " + got.ToString("R", CultureInfo.InvariantCulture) + ", exact " + want.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }
}
