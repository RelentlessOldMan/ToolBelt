using System;
using System.Linq;
using ToolBelt.Functional;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Round 145 (objects, functional, grids, graphs, intervals, ninth pass): each failed on the code before its fix.
    public sealed class ObjectsReview145Tests
    {
        // ---- DeepEquals, PropertyDiff, FlattenObject: a pair first reached past a chain of made objects ----

        // Next makes a new W on every read; the last one's Target is t.
        public sealed class W
        {
            private readonly int _n;
            private readonly object? _t;
            public W(int n, object? t) { _n = n; _t = t; }
            public W? Next => _n > 0 ? new W(_n - 1, _t) : null;
            public object? Target => _n == 0 ? _t : null;
        }

        // Its state shows only through a made object.
        public sealed class T2
        {
            private readonly int _s;
            public T2(int s) { _s = s; }
            public W Calc => new W(0, _s);
        }

        public sealed class Root
        {
            public W? A { get; set; }
            public T2? B { get; set; }
        }

        private static Root Make(int s)
        {
            var t = new T2(s);
            return new Root { A = new W(4, t), B = t };
        }

        // The pair reached first at the end of A's chain had its Calc cut as too deep, and was skipped when B reached it.
        public void DeepEquals_APairReachedFirstPastAChainIsComparedAgain()
        {
            Check.False(DeepEquals.Equals(Make(1), Make(2)));
            Check.True(DeepEquals.Equals(Make(1), Make(1)));
        }

        public void PropertyDiff_APairReachedFirstPastAChainIsComparedAgain()
        {
            var diffs = PropertyDiff.Compare(Make(1), Make(2));
            Check.Equal(1, diffs.Count);
            Check.True(diffs[0].Path.EndsWith("Calc.Target", StringComparison.Ordinal), diffs[0].Path);
            Check.Equal(0, PropertyDiff.Compare(Make(1), Make(1)).Count);
        }

        public void Flatten_AnObjectFirstExpandedPastAChainIsExpandedAgain()
        {
            var flat = FlattenObject.Flatten(Make(7));
            Check.True(flat.TryGetValue("B.Calc.Target", out object? v), string.Join(", ", flat.Keys));
            Check.Equal((object)7, v);
            // Guard: a cycle still ends.
            var a = new Cyc();
            a.Self = a;
            Check.True(FlattenObject.Flatten(a).ContainsKey("Self"));
        }

        public sealed class Cyc { public Cyc? Self { get; set; } }

        // ---- The walks: a type with many properties that each make a new one of itself ----

        public sealed class Fan
        {
            private readonly int _d;
            public Fan() { }
            private Fan(int d) { _d = d; }
            public int Depth => _d;
            public Fan P0 => new Fan(_d + 1); public Fan P1 => new Fan(_d + 1); public Fan P2 => new Fan(_d + 1);
            public Fan P3 => new Fan(_d + 1); public Fan P4 => new Fan(_d + 1); public Fan P5 => new Fan(_d + 1);
            public Fan P6 => new Fan(_d + 1); public Fan P7 => new Fan(_d + 1); public Fan P8 => new Fan(_d + 1);
            public Fan P9 => new Fan(_d + 1); public Fan P10 => new Fan(_d + 1); public Fan P11 => new Fan(_d + 1);
            public Fan P12 => new Fan(_d + 1); public Fan P13 => new Fan(_d + 1); public Fan P14 => new Fan(_d + 1);
            public Fan P15 => new Fan(_d + 1); public Fan P16 => new Fan(_d + 1); public Fan P17 => new Fan(_d + 1);
            public Fan P18 => new Fan(_d + 1); public Fan P19 => new Fan(_d + 1);
        }

        // The depth limit alone let k such properties grow about k^5 values: k = 20 flattened to 3.4 million keys, and
        // k = 30 ran out of memory.
        public void Walks_AFanOfMadeObjectsIsBounded()
        {
            var flat = FlattenObject.Flatten(new Fan());
            Check.True(flat.Count < 200_000, flat.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Check.True(flat.ContainsKey("P3.P4.Depth"));
            Check.True(DeepEquals.Equals(new Fan(), new Fan()));
            Check.Equal(0, PropertyDiff.Compare(new Fan(), new Fan()).Count);
        }

        // ---- TypeUtils.TryConvert: enums and floating values ----

        private enum IntEnum { A = 1 }
        private enum LongEnum : long { A = 1 }
        private enum ULongEnum : ulong { A = 1 }

        // An enum to double or float rounded silently, where its number is refused.
        public void TryConvert_AnEnumThatDoesNotFitAFloatIsRefused()
        {
            Check.False(TypeUtils.TryConvert((LongEnum)9007199254740993L, typeof(double), out _));
            Check.False(TypeUtils.TryConvert((IntEnum)16777217, typeof(float), out _));
            Check.False(TypeUtils.TryConvert((ULongEnum)ulong.MaxValue, typeof(float), out _));
            // Guards: one that fits converts.
            Check.True(TypeUtils.TryConvert((LongEnum)9007199254740992L, typeof(double), out object? d));
            Check.Equal((object)9007199254740992.0, d);
            Check.True(TypeUtils.TryConvert(IntEnum.A, typeof(float), out object? f));
            Check.Equal((object)1f, f);
        }

        // A ulong-backed enum's upper half was refused from a decimal or a double.
        public void TryConvert_AULongEnumTakesItsUpperHalf()
        {
            Check.True(TypeUtils.TryConvert(1e19, typeof(ULongEnum), out object? a));
            Check.Equal((object)(ULongEnum)10000000000000000000UL, a);
            Check.True(TypeUtils.TryConvert(10000000000000000000m, typeof(ULongEnum), out object? b));
            Check.Equal((object)(ULongEnum)10000000000000000000UL, b);
            Check.True(TypeUtils.TryConvert(18446744073709551615m, typeof(ULongEnum), out object? c));
            Check.Equal((object)(ULongEnum)ulong.MaxValue, c);
            // Guards: past the range, a fraction, or a signed enum is refused.
            Check.False(TypeUtils.TryConvert(18446744073709551616m, typeof(ULongEnum), out _));
            Check.False(TypeUtils.TryConvert(18446744073709551616.0, typeof(ULongEnum), out _));
            Check.False(TypeUtils.TryConvert(1e19, typeof(LongEnum), out _));
            Check.False(TypeUtils.TryConvert(10000000000000000000.5m, typeof(ULongEnum), out _));
            Check.False(TypeUtils.TryConvert(-1.0, typeof(ULongEnum), out _));
        }

        // ---- ObjectMapper: Ignore and ForMember name an exact property first ----

        public sealed class Twin
        {
            public string? Name { get; set; }
            public string? NAME { get; set; }
        }

        public sealed class OneName
        {
            public string? Name { get; set; }
        }

        // With Name and NAME on the target, Ignore("NAME") ignored both and ForMember("NAME") set both.
        public void ObjectMapper_AnExactNameWinsOverCase()
        {
            var src = new Twin { Name = "a", NAME = "b" };
            Twin ignored = new ObjectMapper<Twin, Twin>().Ignore("NAME").Map(src);
            Check.Equal("a", ignored.Name);
            Check.Equal(null, ignored.NAME);
            Twin custom = new ObjectMapper<Twin, Twin>().ForMember("NAME", s => "z").Map(src);
            Check.Equal("a", custom.Name);
            Check.Equal("z", custom.NAME);
            // Guards: with no exact match, case is ignored.
            Check.Equal(null, new ObjectMapper<Twin, OneName>().Ignore("name").Map(src).Name);
            Check.Equal("q", new ObjectMapper<Twin, OneName>().ForMember("NAME", s => "q").Map(src).Name);
        }

        // ---- ExpressionAccessor: a null instance ----

        public sealed class Holder
        {
            public string? Name { get; set; }
        }

        // The compiled getters and setters threw NullReferenceException; GetValue throws ArgumentNullException.
        public void ExpressionAccessor_ANullInstanceIsAnArgumentNullException()
        {
            Check.Equal("instance", Check.Throws<ArgumentNullException>(() => ExpressionAccessor.Getter(typeof(Holder), "Name")(null!)).ParamName);
            Check.Equal("instance", Check.Throws<ArgumentNullException>(() => ExpressionAccessor.Setter(typeof(Holder), "Name")(null!, "x")).ParamName);
            Check.Equal("instance", Check.Throws<ArgumentNullException>(() => ExpressionAccessor.Getter<Holder, string?>("Name")(null!)).ParamName);
            Check.Equal("instance", Check.Throws<ArgumentNullException>(() => ExpressionAccessor.Setter<Holder, string?>("Name")(null!, "x")).ParamName);
            // Guard: an instance still reads and writes.
            var h = new Holder();
            ExpressionAccessor.Setter<Holder, string?>("Name")(h, "y");
            Check.Equal("y", ExpressionAccessor.Getter(typeof(Holder), "Name")(h));
        }

        // ---- Result.Try: an exception whose Message throws ----

        private sealed class BadMessage : Exception
        {
            public override string Message => throw new InvalidOperationException("message getter");
        }

        public void ResultTry_AMessageThatThrowsIsStillAFailure()
        {
            Result<int> r = Result.Try<int>(() => throw new BadMessage());
            Check.True(r.IsFailure);
            Check.True(r.Error.Contains(nameof(BadMessage)), r.Error);
        }
    }
}
