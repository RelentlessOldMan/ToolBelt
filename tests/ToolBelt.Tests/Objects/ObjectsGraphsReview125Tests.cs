using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Intervals;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Round 125 (objects, functional, grids, graphs, intervals, seventh pass): each failed on the code before its fix.
    public sealed class ObjectsGraphsReview125Tests
    {
        // ---- DeepEquals and PropertyDiff: state no property shows ----

        private sealed class Money
        {
            private readonly int _cents;
            public Money(int cents) => _cents = cents;
            public override bool Equals(object? obj) => obj is Money m && m._cents == _cents;
            public override int GetHashCode() => _cents;
        }

        private sealed class Blank { }

        public void DeepEquals_AClassWithOnlyPrivateStateUsesItsOwnEquals()
        {
            Check.True(!DeepEquals.Equals(new Money(1), new Money(2)), "Money(1) and Money(2) are equal");
            Check.True(DeepEquals.Equals(new Money(1), new Money(1)));
            Check.Equal(1, PropertyDiff.Compare(new Money(1), new Money(2)).Count);
            Check.Equal(0, PropertyDiff.Compare(new Money(1), new Money(1)).Count);
            Check.True(!DeepEquals.Equals(new { M = new Money(1) }, new { M = new Money(2) }));
            // A class with no members and no Equals of its own still compares by its (empty) structure.
            Check.True(DeepEquals.Equals(new Blank(), new Blank()));
            Check.Equal(0, PropertyDiff.Compare(new Blank(), new Blank()).Count);
        }

        public void DeepEquals_StringBuildersAndRegexesCompareByWhatTheyHold()
        {
            Check.True(!DeepEquals.Equals(new StringBuilder("abc"), new StringBuilder("xyz")), "two builders of one length");
            Check.True(DeepEquals.Equals(new StringBuilder("abc"), new StringBuilder("abc", 100)));
            Check.True(!DeepEquals.Equals(new { S = new StringBuilder("abc") }, new { S = new StringBuilder("abd") }));
            Check.Equal(1, PropertyDiff.Compare(new StringBuilder("abc"), new StringBuilder("xyz")).Count);
            Check.True(!DeepEquals.Equals(new Regex("a+"), new Regex("b+")), "two patterns");
            Check.True(!DeepEquals.Equals(new Regex("a"), new Regex("a", RegexOptions.IgnoreCase)));
            Check.True(DeepEquals.Equals(new Regex("a+"), new Regex("a+")));
            Check.Equal(1, PropertyDiff.Compare(new { R = new Regex("a") }, new { R = new Regex("b") }).Count);
            // A builder flattens as one value, not as its Capacity, MaxCapacity and Length.
            IDictionary<string, object?> flat = FlattenObject.Flatten(new { S = new StringBuilder("abc") });
            Check.Equal("S", string.Join(",", flat.Keys));
            Check.Equal("abc", flat["S"]!.ToString());
        }

#if NET8_0_OR_GREATER
        // ---- A Memory stored in its own array ----

        public void Walks_AMemoryStoredInItsOwnArrayEnd()
        {
            object[] Make(string tail)
            {
                var a = new object[2];
                a[0] = new Memory<object>(a);
                a[1] = tail;
                return a;
            }
            Check.True(DeepEquals.Equals(Make("x"), Make("x")));
            Check.True(!DeepEquals.Equals(Make("x"), Make("y")));
            Check.Equal(0, PropertyDiff.Compare(Make("x"), Make("x")).Count);
            Check.True(PropertyDiff.Compare(Make("x"), Make("y")).Count > 0);
            IDictionary<string, object?> flat = FlattenObject.Flatten(new { A = Make("x") });
            Check.True(flat.Count < 10, $"{flat.Count} keys");
        }
#endif

        // ---- ObjectMapper: an exact-case name wins ----

        public sealed class ExactFirst { public string Name { get; set; } = "exact"; public string NAME { get; set; } = "upper"; }
        public sealed class UpperFirst { public string NAME { get; set; } = "upper"; public string Name { get; set; } = "exact"; }
        public sealed class Named { public string Name { get; set; } = ""; public string NAME { get; set; } = ""; public string name { get; set; } = ""; }
        public sealed class OnlyName { public string Name { get; set; } = ""; }

        public void ObjectMapper_AnExactCaseSourceNameWins()
        {
            // Whichever reflection listed last filled the target's Name.
            Check.Equal("exact", new ObjectMapper<ExactFirst, OnlyName>().Map(new ExactFirst()).Name);
            Check.Equal("exact", new ObjectMapper<UpperFirst, OnlyName>().Map(new UpperFirst()).Name);
            Named n = new ObjectMapper<UpperFirst, Named>().Map(new UpperFirst());
            Check.Equal("exact", n.Name);
            Check.Equal("upper", n.NAME);
            Check.Equal("upper", n.name);   // no exact match: the ordinal-first source name ("NAME" < "Name")
        }

        // ---- PropertyPath ----

        public sealed class Holder
        {
            public int X { get; set; } = 7;
            public int? N { get; set; } = 7;
            public int[] Arr { get; set; } = { 7 };
            public object?[] Objs { get; set; } = { 7 };
            public Holder[] Kids { get; set; } = Array.Empty<Holder>();
        }

        public void PropertyPath_NullIntoAValueTypeThrows()
        {
            var h = new Holder();
            var e = Check.Throws<ArgumentException>(() => PropertyPath.Set(h, "Arr[0]", null));
            Check.Equal("value", e.ParamName);
            Check.Equal(7, h.Arr[0]);
            e = Check.Throws<ArgumentException>(() => PropertyPath.Set(h, "X", null));
            Check.Equal("value", e.ParamName);
            Check.Equal(7, h.X);
            PropertyPath.Set(h, "N", null);
            Check.True(h.N is null);
            PropertyPath.Set(h, "Objs[0]", null);
            Check.True(h.Objs[0] is null);
        }

        public void PropertyPath_ANameStraightAfterABracketIsAnError()
        {
            var h = new Holder { Kids = new[] { new Holder { X = 3 } } };
            Check.Equal(3, PropertyPath.Get(h, "Kids[0].X"));
            var e = Check.Throws<ArgumentException>(() => PropertyPath.Get(h, "Kids[0]X"));
            Check.Equal("path", e.ParamName);
            Check.True(!PropertyPath.TryGet(h, "Kids[0]X", out _));
            Check.Throws<ArgumentException>(() => PropertyPath.Set(h, "Kids[0]X", 4));
            Check.Equal(3, h.Kids[0].X);
        }

        // ---- TypeUtils.TryConvert: decimal to a floating type ----

        public void TryConvert_DecimalToAFloatingTypeIsExact()
        {
            Check.True(!TypeUtils.TryConvert(16777217m, typeof(float), out _), "16777217m to float");
            Check.True(!TypeUtils.TryConvert(1.0000000000000000000000000001m, typeof(double), out _));
            Check.True(!TypeUtils.TryConvert(decimal.MaxValue, typeof(double), out _));
            Check.True(TypeUtils.TryConvert(0.1m, typeof(double), out object? d) && d is double dv && dv == 0.1);
            Check.True(TypeUtils.TryConvert(0.1m, typeof(float), out object? f) && f is float fv && fv == 0.1f);
            Check.True(TypeUtils.TryConvert(1.50m, typeof(double?), out object? h) && h is double hv && hv == 1.5);
            // As an integer does: a whole decimal converts exactly when the same long would.
            var rng = new Random(125);
            for (int t = 0; t < 4000; t++)
            {
                long n = (rng.Next(2) == 0 ? 1 : -1) * (long)(rng.NextDouble() * (t % 2 == 0 ? 1L << 30 : 1L << 60));
                foreach (Type target in new[] { typeof(float), typeof(double) })
                {
                    bool asLong = TypeUtils.TryConvert(n, target, out object? viaLong);
                    bool asDecimal = TypeUtils.TryConvert((decimal)n, target, out object? viaDecimal);
                    Check.True(asLong == asDecimal, $"{n} to {target.Name}: long {asLong}, decimal {asDecimal}");
                    if (asLong) Check.Equal(viaLong, viaDecimal);
                }
            }
        }

        // ---- ExpressionAccessor: typed accessors narrow with a check ----

        public sealed class Numbers
        {
            public int I;
            public long L;
            public double D;
        }

        public void ExpressionAccessor_TypedNarrowingIsChecked()
        {
            var h = new Numbers { I = 9 };
            Check.Throws<OverflowException>(() => ExpressionAccessor.Setter<Numbers, long>("I")(h, 4294967297L));
            Check.Equal(9, h.I);
            ExpressionAccessor.Setter<Numbers, long>("I")(h, -5L);
            Check.Equal(-5, h.I);
            h.L = 4294967297L;
            Check.Throws<OverflowException>(() => ExpressionAccessor.Getter<Numbers, int>("L")(h));
            h.D = double.NaN;
            Check.Throws<OverflowException>(() => ExpressionAccessor.Getter<Numbers, int>("D")(h));
            h.D = 2.5;
            Check.Equal(2, ExpressionAccessor.Getter<Numbers, int>("D")(h));   // a cast truncates
        }

        // ---- Intervals over strings compare ordinally ----

        public void Interval_StringKeysCompareOrdinally()
        {
            string soft = "a" + (char)0xAD + "b";
            // The culture ignores a soft hyphen: an empty interval whose endpoints were not equal.
            var i = new Interval<string>("ab", soft);
            Check.True(!i.IsEmpty);
            Check.True(i.Contains("ab"));
            var e = Check.Throws<ArgumentException>(() => new Interval<string>("a", "B"));   // 'a' is after 'B'
            Check.Equal("start", e.ParamName);
            var set = new RangeSet<string>(new[] { new Interval<string>("A", "a") });
            Check.True(set.Contains("B") && set.Contains("Z") && !set.Contains("a") && !set.Contains("b"));
            var map = new RangeMap<string, int>();
            map.Set("A", "a", 1);
            Check.True(map.TryGet("B", out int v) && v == 1);
            Check.True(!map.ContainsKey("b"));
            // IsEmpty agrees with equality, and Contains with ordinal order, over a pool the culture treats differently.
            string[] pool = { "", "a", "A", "b", "B", "ab", soft, "a-b", "é", "é", "ss", "ß", "İ", "i" };
            foreach (string s in pool)
                foreach (string t in pool)
                {
                    if (string.CompareOrdinal(s, t) > 0) { Check.Throws<ArgumentException>(() => new Interval<string>(s, t)); continue; }
                    var iv = new Interval<string>(s, t);
                    Check.True(iv.IsEmpty == (s == t), $"[{s}, {t})");
                    foreach (string p in pool)
                        Check.True(iv.Contains(p) == (string.CompareOrdinal(s, p) <= 0 && string.CompareOrdinal(p, t) < 0), $"[{s}, {t}) holds {p}");
                }
        }
    }
}
