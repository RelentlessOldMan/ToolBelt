using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>Regressions from review round 199 (Collections, twelfth pass): each test failed before its fix.</summary>
    public sealed class CollectionsReview199Tests
    {
        private static bool OnFramework => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            .StartsWith(".NET Framework", StringComparison.Ordinal);

        // ---------------- DataTableLite.ToString ----------------

        private static string Cell(object value)
        {
            string text = new DataTableLite(new[] { "v" }).AddRow(value).ToString();
            string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines[1].Trim();
        }

        private static float Single(int bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);

        // .NET Framework's "E" formatting and Parse are not correctly rounded, so these came out with a digit too many,
        // not the nearest, a tie broken the other way, or as text that reads back as another double.
        public void DataTableLite_ToString_ReviewerCasesMatchDotNet()
        {
            Check.Equal("7.3599791361962955E+248", Cell(BitConverter.Int64BitsToDouble(0x739a50e6726f8878)));
            Check.Equal("3.351544564949413E-254", Cell(BitConverter.Int64BitsToDouble(0x0b4f73c4ca4912e2)));
            Check.Equal("853041653452.9261", Cell(BitConverter.Int64BitsToDouble(0x4268d3a7d4d99da3)));
            Check.Equal("2397517.2", Cell(Single(0x4a125535)));
        }

        // Graded against an independent reference: the exact decimal expansion, cut to p digits either way, each
        // candidate read back with an exact parser; the fewest digits, the nearest, an exact tie to the even digit.
        public void DataTableLite_ToString_ShortestNearestOnEveryRuntime()
        {
            var random = new Random(199);
            var doubles = new List<double>
            {
                BitConverter.Int64BitsToDouble(0x739a50e6726f8878), BitConverter.Int64BitsToDouble(0x0b4f73c4ca4912e2),
                BitConverter.Int64BitsToDouble(0x4268d3a7d4d99da3), double.MaxValue, double.Epsilon, -double.Epsilon,
                2.2250738585072014E-308, 2.2250738585072009E-308, 1e23, 9007199254740993.0, 0.1, 1.0 / 3, 5e-324 * 3,
            };
            var floats = new List<float>
            {
                Single(unchecked((int)0xa6d29f7b)), Single(unchecked((int)0xa4146f66)), Single(0x4a125535),
                float.MaxValue, float.Epsilon, 1.17549435E-38f, 16777217f, 0.1f, 1e-45f * 7,
            };
            for (int i = 0; i < 3000; i++)
            {
                var b = new byte[8];
                random.NextBytes(b);
                double d = BitConverter.ToDouble(b, 0);
                if (!double.IsNaN(d) && !double.IsInfinity(d)) doubles.Add(d);
                float f = BitConverter.ToSingle(b, 0);
                if (!float.IsNaN(f) && !float.IsInfinity(f)) floats.Add(f);
            }
            int bad = 0;
            string first = "";
            foreach (double d in doubles)
            {
                if (d == 0) continue;
                string got = Cell(d);
                bool ok = Agrees(got, Reference(d, single: false), d < 0);
#if !NETFRAMEWORK
                ok &= got == d.ToString("R", CultureInfo.InvariantCulture);
#endif
                if (!ok && bad++ == 0) first = d.ToString("R", CultureInfo.InvariantCulture) + " -> " + got;
            }
            foreach (float f in floats)
            {
                if (f == 0) continue;
                string got = Cell(f);
                bool ok = Agrees(got, Reference(f, single: true), f < 0);
#if !NETFRAMEWORK
                ok &= got == f.ToString("R", CultureInfo.InvariantCulture);
#endif
                if (!ok && bad++ == 0) first = "float " + f.ToString("R", CultureInfo.InvariantCulture) + " -> " + got;
            }
            Check.True(bad == 0, bad + " mismatches; first " + first);
        }

        // The text's significant digits and the decimal exponent of the first one, compared with the reference.
        private static bool Agrees(string text, (string Digits, int Exponent) expected, bool negative)
        {
            if (negative != text.StartsWith("-", StringComparison.Ordinal)) return false;
            string s = text.TrimStart('-');
            int exp = 0, at = s.IndexOf('E');
            if (at >= 0) { exp = int.Parse(s.Substring(at + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); s = s.Substring(0, at); }
            int dot = s.IndexOf('.');
            int intLen = dot >= 0 ? dot : s.Length;
            string all = s.Replace(".", "");
            int lead = 0;
            while (lead < all.Length && all[lead] == '0') lead++;
            string digits = all.Substring(lead).TrimEnd('0');
            return digits == expected.Digits && intLen - 1 - lead + exp == expected.Exponent;
        }

        // Reads decimal text exactly, rounded once to a double (or float): .NET Framework's Parse is not correctly rounded.
        internal static double ReadExact(string text, bool single)
        {
            bool negative = text.StartsWith("-", StringComparison.Ordinal);
            string s = text.TrimStart('-');
            int exp = 0, at = s.IndexOfAny(new[] { 'E', 'e' });
            if (at >= 0) { exp = int.Parse(s.Substring(at + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); s = s.Substring(0, at); }
            int dot = s.IndexOf('.');
            if (dot >= 0) { exp -= s.Length - dot - 1; s = s.Remove(dot, 1); }
            BigInteger digits = BigInteger.Parse(s, CultureInfo.InvariantCulture);
            double r = digits.IsZero ? 0 : ExactParse(digits, exp, single);
            return negative ? -r : r;
        }

        private static (string Digits, int Exponent) Reference(double v, bool single)
        {
            double a = Math.Abs(v);
            long bits = BitConverter.DoubleToInt64Bits(a);
            int be = (int)(bits >> 52);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else m |= 1L << 52;
            int e = be - 1075;
            // |v| = n*10^e0 exactly.
            BigInteger n = e >= 0 ? new BigInteger(m) << e : m * BigInteger.Pow(5, -e);
            int e0 = Math.Min(e, 0);
            string str = n.ToString(CultureInfo.InvariantCulture);
            for (int p = 1; ; p++)
            {
                if (p >= str.TrimEnd('0').Length) return Result(n, e0);
                BigInteger scale = BigInteger.Pow(10, str.Length - p);
                BigInteger d = BigInteger.DivRem(n, scale, out BigInteger rem);
                int ce = e0 + str.Length - p;
                bool down = ExactParse(d, ce, single) == a, up = ExactParse(d + 1, ce, single) == a;
                if (!down && !up) continue;
                int c = (2 * rem).CompareTo(scale);
                bool pickUp = up && (!down || c > 0 || (c == 0 && !d.IsEven));
                return Result(pickUp ? d + 1 : d, ce);
            }
        }

        private static (string, int) Result(BigInteger d, int e)
        {
            string s = d.ToString(CultureInfo.InvariantCulture);
            return (s.TrimEnd('0'), e + s.Length - 1);
        }

        // The double (or float) nearest D*10^E, ties to even.
        private static double ExactParse(BigInteger digits, int exp10, bool single)
        {
            BigInteger num = digits, den = BigInteger.One;
            if (exp10 >= 0) num *= BigInteger.Pow(10, exp10); else den = BigInteger.Pow(10, -exp10);
            int width = single ? 24 : 53, minExp = single ? -149 : -1074;
            int s = Math.Max(BitLength(num) - BitLength(den) - width, minExp);
            while (true)
            {
                BigInteger n2 = s >= 0 ? num : num << -s, d2 = s >= 0 ? den << s : den;
                BigInteger q = BigInteger.DivRem(n2, d2, out BigInteger rem);
                if (q >= BigInteger.One << width) { s++; continue; }
                if (q < BigInteger.One << (width - 1) && s > minExp) { s--; continue; }
                int c = (2 * rem).CompareTo(d2);
                if (c > 0 || (c == 0 && !q.IsEven)) q++;
                double r = (double)q * Pow2(s);
                return single ? (float)r : r;
            }
        }

        private static int BitLength(BigInteger x)
        {
            int n = 0;
            while (x > ulong.MaxValue) { x >>= 64; n += 64; }
            for (ulong u = (ulong)x; u != 0; u >>= 1) n++;
            return n;
        }

        private static double Pow2(int s)
            => s > 1023 ? double.PositiveInfinity
             : s >= -1022 ? BitConverter.Int64BitsToDouble((long)(s + 1023) << 52)
             : BitConverter.Int64BitsToDouble(1L << (s + 1074));

        // ---------------- DataTableLite.OrderBy ----------------

        // A column mixing int, long, float, double and decimal threw "Object must be of type Double." It sorts by
        // exact value: long 2^53 + 1 above double 2^53, 0.1m below 0.1 below 0.1f, int 2 equal to 2.0 (stable).
        public void DataTableLite_OrderBy_MixedNumbersCompareByValue()
        {
            var table = new DataTableLite(new[] { "v", "tag" });
            object[] cells = { 9007199254740993L, 2, 0.1f, double.PositiveInfinity, 9007199254740992.0, 0.1, double.NaN,
                               2.0, 0.1m, -1.5m, double.NegativeInfinity, (byte)1, 1e-320, 0L, -0.0 };
            for (int i = 0; i < cells.Length; i++) table.AddRow(cells[i], i);
            var sorted = table.OrderBy("v");
            var order = new List<int>();
            for (int r = 0; r < sorted.RowCount; r++) order.Add((int)sorted[r, "tag"]!);
            // NaN, -inf, -1.5m, 0L, -0.0 (equal, input order), 1e-320, 0.1m, 0.1, 0.1f, (byte)1, 2, 2.0, 2^53, 2^53 + 1, +inf
            Check.Equal("6,10,9,13,14,12,8,5,2,11,1,7,4,0,3", string.Join(",", order));
            var desc = table.OrderByDescending("v");
            Check.Equal(3, (int)desc[0, "tag"]!);
            Check.Equal(6, (int)desc[desc.RowCount - 1, "tag"]!);
        }

        // ---------------- ObjectPool.Return ----------------

        // Two Returns for the last slot both reset, and the later one was dropped after its reset, where the docs say a
        // dropped instance is not reset. Here the reset of one returns another, re-entrantly.
        public void ObjectPool_Return_DroppedInstanceIsNotReset()
        {
            var resets = new List<object>();
            ObjectPool<object>? pool = null;
            object a = new object(), b = new object();
            pool = new ObjectPool<object>(() => new object(), x =>
            {
                resets.Add(x);
                if (ReferenceEquals(x, a)) pool!.Return(b);
            }, maxRetained: 1);
            pool.Return(a);
            object rented = pool.Rent();
            Check.True(ReferenceEquals(rented, a) || ReferenceEquals(rented, b));
            Check.True(resets.All(x => ReferenceEquals(x, rented)), "an instance was reset and then dropped");
        }

        // ---------------- BitSet enumeration ----------------

        // Setting a bit already set (or clearing a clear one, or a set operation that changes nothing) stopped running
        // enumerators, though no bit changed; HashSet.Add of an existing item doesn't.
        public void BitSet_NoOpChange_DoesNotInvalidateEnumerators()
        {
            var set = new BitSet();
            set.Set(3); set.Set(70); set.Set(130);
            var subset = new BitSet();
            subset.Set(70);
            var seen = new List<int>();
            foreach (int i in set.EnumerateSetBits())
            {
                seen.Add(i);
                set.Set(3);
                set.Clear(5);
                set.Clear(100000);
                set.SetTo(70, true);
                set.UnionWith(subset);
                set.IntersectWith(set);
                set.ExceptWith(new BitSet());
                set.SymmetricExceptWith(new BitSet());
            }
            Check.Equal("3,70,130", string.Join(",", seen));
            Check.Throws<InvalidOperationException>(() => { foreach (int i in set.EnumerateSetBits()) set.Set(4); });
            Check.Throws<InvalidOperationException>(() => { foreach (int i in set.EnumerateSetBits()) set.Clear(70); });
        }
    }
}
