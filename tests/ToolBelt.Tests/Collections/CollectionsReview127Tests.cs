using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>
    /// Regressions from review round 127 (Collections, eighth pass): each test failed before its fix. The Deque and
    /// CircularBuffer index tests fail only in the overflow-checked build (scripts/checked-build.sh), where the old sum
    /// threw; each allocates an array of about 2 GB.
    /// </summary>
    public sealed class CollectionsReview127Tests
    {
        // ---------------- NearestBy: exact distances ----------------

        // x * 2^1074 as an integer: exact for every finite double.
        private static BigInteger Exact(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            BigInteger v = new BigInteger(mant) << (exp - 1);
            return bits < 0 ? -v : v;
        }

        private static int NaiveNearest(IReadOnlyList<double> list, double key)
        {
            BigInteger k = Exact(key);
            int best = 0;
            BigInteger bestD = BigInteger.Abs(Exact(list[0]) - k);
            for (int i = 1; i < list.Count; i++)
            {
                BigInteger d = BigInteger.Abs(Exact(list[i]) - k);
                if (d < bestD) { best = i; bestD = d; }
            }
            return best;
        }

        public void NearestBy_ComparesTheExactDistances()
        {
            Check.Equal(1, new[] { -1e17, 1e17 }.NearestBy(1.0, x => x));
            Check.Equal(0, new[] { -3.0, 1e16 }.NearestBy(5e15 - 2, x => x));                 // 5e15 + 1 against 5e15 + 2, exactly
            Check.Equal(1, new[] { -1.0, 18014398509481984.0 }.NearestBy(9007199254740992.0, x => x));   // 2^53 + 1 rounds to the tie 2^53
            Check.Equal(0, new[] { -1e17, 1e17 }.NearestBy(-1.0, x => x));
            Check.Equal(0, new[] { -1e17, 1e17 }.NearestBy(0.0, x => x));               // a true tie goes to the earlier
            Check.Equal(1, new[] { -double.MaxValue, double.MaxValue }.NearestBy(1e300, x => x));   // the left side overflows
            Check.Equal(0, new[] { -double.MaxValue, double.MaxValue }.NearestBy(-1e300, x => x));
            Check.Equal(0, new[] { -double.MaxValue, double.MaxValue }.NearestBy(0.0, x => x));
            Check.Equal(1, new[] { double.NegativeInfinity, 5.0 }.NearestBy(1e308, x => x));
            Check.Equal(0, new[] { double.NegativeInfinity, double.PositiveInfinity }.NearestBy(3.0, x => x));
            var rng = new Random(127);
            for (int trial = 0; trial < 4000; trial++)
            {
                int n = rng.Next(1, 8);
                var list = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double scale = Math.Pow(10, rng.Next(-5, 20));
                    list[i] = (rng.NextDouble() * 2 - 1) * scale;
                    if (rng.Next(4) == 0) list[i] = Math.Round(list[i]);
                }
                Array.Sort(list);
                double key = rng.Next(3) == 0
                    ? (list[rng.Next(n)] + list[rng.Next(n)]) / 2 + rng.Next(-2, 3)
                    : (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(-5, 20));
                Check.Equal(NaiveNearest(list, key), list.NearestBy(key, x => x), string.Join(",", list.Select(x => x.ToString("R"))) + " near " + key.ToString("R"));
            }
        }

        // ---------------- CartesianProduct: an input changed mid-enumeration ----------------

        public void CartesianProduct_AChangedInputThrowsInvalidOperation()
        {
            var a = new List<int> { 1, 2 };
            var b = new List<int> { 3, 4 };
            using (var e = CartesianProduct.Of<int>(a, b).GetEnumerator())
            {
                Check.True(e.MoveNext());
                Check.True(e.MoveNext());
                Check.True(e.MoveNext());
                b.Clear();
                Check.Throws<InvalidOperationException>(() => e.MoveNext());
            }
            a = new List<int> { 1, 2, 3 }; b = new List<int> { 4, 5, 6 };
            using (var e = CartesianProduct.Of<int>(a, b).GetEnumerator())
            {
                Check.True(e.MoveNext());
                b.RemoveAt(2); b.RemoveAt(1);
                Check.Throws<InvalidOperationException>(() => e.MoveNext());
            }
            a = new List<int> { 1, 2 }; b = new List<int> { 3, 4 };
            using (var e = CartesianProduct.Of<int>(a, b).GetEnumerator())
            {
                Check.True(e.MoveNext());
                b.Add(5);
                Check.Throws<InvalidOperationException>(() => e.MoveNext());
            }
            // Unchanged inputs still give the whole product, and a change after the last tuple is not seen.
            a = new List<int> { 1, 2 }; b = new List<int> { 3, 4, 5 };
            Check.Equal(6, CartesianProduct.Of<int>(a, b).Count());
            Check.Equal(1, CartesianProduct.Of(new List<IReadOnlyList<int>>()).Count());
        }

        // ---------------- Deque and CircularBuffer near Array.MaxLength (checked build) ----------------

        public void Deque_SlotDoesNotOverflowNearTheLargestArray()
        {
            var d = new Deque<byte>(0x7FFFFFC7);
            d.PushFront(1);
            for (int i = 0; i < 100; i++) d.PushBack((byte)(i + 2));
            Check.Equal(101, d.Count);
            Check.Equal((byte)1, d[0]);
            Check.Equal((byte)101, d[100]);
            Check.True(d.TryPopFront(out byte front)); Check.Equal((byte)1, front);
            Check.True(d.TryPopBack(out byte back)); Check.Equal((byte)101, back);
        }

        public void CircularBuffer_SlotDoesNotOverflowNearTheLargestArray()
        {
            const int cap = 0x7FFFFFC7;
            var c = new CircularBuffer<byte>(cap);
            for (int i = 0; i < 100; i++) c.Add(7);
            for (int i = 0; i < 100; i++) Check.True(c.TryRemoveOldest(out _));
            // The window now starts 100 slots in; filling it reaches the slots past the end of the array.
            for (long i = 0; i < cap; i++) c.Add(unchecked((byte)i));
            Check.Equal(cap, c.Count);
            Check.Equal((byte)0, c[0]);
            Check.Equal(unchecked((byte)(cap - 1)), c[cap - 1]);
            Check.Equal(unchecked((byte)(cap - 1)), c.Newest);
        }

        // ---------------- BitSet growth ----------------

        public void BitSet_GrowthStopsAtTheWordsIntMaxValueNeeds()
        {
            var words = typeof(BitSet).GetField("_words", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var b = new BitSet(3 * 64);
            b.Set(int.MaxValue);
            Check.Equal((int.MaxValue >> 6) + 1, ((ulong[])words.GetValue(b)!).Length);
            Check.True(b.Get(int.MaxValue));
            Check.Equal(1L, b.Count);
            b = new BitSet(64);
            b.Set(1000);
            Check.Equal(16, ((ulong[])words.GetValue(b)!).Length);   // below the cap, doubling as before
        }
    }
}
