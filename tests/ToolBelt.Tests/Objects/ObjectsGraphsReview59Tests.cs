using System;
using System.Collections;
using System.Collections.Generic;
#if !NETFRAMEWORK
using System.Collections.Immutable;
#endif
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ToolBelt.Graphs;
using ToolBelt.Intervals;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Regressions from review round 59 (objects, functional, grids, graphs, intervals — fourth pass): each test failed
    // before its fix (the chain and box-cycle tests by hanging).
    public sealed class ObjectsGraphsReview59Tests
    {
        private static Exception? Run(Action action, string what, int seconds = 20)
        {
            Exception? error = null;
            var task = Task.Run(() => { try { action(); } catch (Exception e) { error = e; } });
            Check.True(task.Wait(TimeSpan.FromSeconds(seconds)), what + " did not finish");
            return error;
        }

        private static void NoThrow(Action action, string what)
        {
            Exception? e = Run(action, what);
            Check.True(e is null, $"{what} threw {e?.GetType().Name}: {e?.Message}");
        }

        // ---- Chains of made objects: no type equals its owner's, so the old owner-type rule never stopped them ----

        public sealed class Ping
        {
            public int Id { get; set; }
            public Pong Next => new Pong { Id = Id + 1 };
        }

        public sealed class Pong
        {
            public int Id { get; set; }
            public Ping Next => new Ping { Id = Id + 1 };
        }

        public struct SA
        {
            public int V { get; set; }
            public SB B => new SB { V = V };
        }

        public struct SB
        {
            public int V { get; set; }
            public SA A => new SA { V = V };
        }

        public class BaseLink
        {
            public int V { get; set; }
            public BaseLink Next => new SubLink { V = V };
        }

        public sealed class SubLink : BaseLink { }

        public sealed class Lazy1
        {
            public int V { get; set; }
            public List<Lazy1> Kids => new List<Lazy1> { new Lazy1 { V = V + 1 } };
        }

        private static void AllWalkersEnd(object a, object b, string what)
        {
            NoThrow(() => Check.True(DeepEquals.Equals(a, b)), what + " DeepEquals");
            NoThrow(() => Check.Equal(0, PropertyDiff.Compare(a, b).Count), what + " PropertyDiff");
            NoThrow(() => Check.True(FlattenObject.Flatten(a).Count > 0), what + " Flatten");
        }

        public void Walkers_APingPongChainOfMadeObjectsEnds()
            => AllWalkersEnd(new Ping { Id = 1 }, new Ping { Id = 1 }, "Ping/Pong");

        public void Walkers_TwoStructsReturningEachOtherEnd()
            => AllWalkersEnd(new SA { V = 1 }, new SA { V = 1 }, "SA/SB");

        public void Walkers_AGetterReturningANewSubclassInstanceEnds()
            => AllWalkersEnd(new BaseLink { V = 1 }, new BaseLink { V = 1 }, "BaseLink/SubLink");

        public void Walkers_AGetterReturningANewListOfNewObjectsEnds()
            => AllWalkersEnd(new Lazy1 { V = 1 }, new Lazy1 { V = 1 }, "Lazy1.Kids");

        public void Walkers_AChainStillFindsADifferenceNearTheTop()
        {
            Exception? e = Run(() =>
            {
                Check.False(DeepEquals.Equals(new Ping { Id = 1 }, new Ping { Id = 2 }));
                var diffs = PropertyDiff.Compare(new Ping { Id = 1 }, new Ping { Id = 2 });
                Check.True(diffs.Any(d => d.Path == "Id"), string.Join(",", diffs.Select(d => d.Path)));
                Check.True(diffs.Any(d => d.Path == "Next.Id"), string.Join(",", diffs.Select(d => d.Path)));
            }, "Ping difference");
            Check.True(e is null, e?.ToString());
        }

        public sealed class Tree
        {
            public int V { get; set; }
            public List<Tree> Children { get; } = new List<Tree>();
            public Tree? Parent { get; set; }
        }

        public void Walkers_StoredDataIsStillWalkedInFull()
        {
            // A deep stored chain has no repeats: every level is compared and flattened.
            Tree Build(int leafValue)
            {
                var root = new Tree { V = 0 };
                Tree t = root;
                for (int i = 1; i <= 12; i++)
                {
                    var c = new Tree { V = i == 12 ? leafValue : i, Parent = t };
                    t.Children.Add(c);
                    t = c;
                }
                return root;
            }
            Check.True(DeepEquals.Equals(Build(12), Build(12)));
            Check.False(DeepEquals.Equals(Build(12), Build(99)));
            var diffs = PropertyDiff.Compare(Build(12), Build(99));
            Check.Equal(1, diffs.Count);
            Check.True(diffs[0].Path.EndsWith("Children[0].V", StringComparison.Ordinal), diffs[0].Path);
            var flat = FlattenObject.Flatten(Build(12));
            string deepest = string.Concat(Enumerable.Repeat("Children[0].", 12)) + "V";
            Check.True(flat.ContainsKey(deepest), deepest);
        }

        // ---- A boxed struct that reaches itself ----

        public struct Boxy
        {
            public int V;
            public object? Self;
        }

        private static object SelfBox(int v)
        {
            object box = new Boxy { V = v };
            typeof(Boxy).GetField("Self")!.SetValue(box, box); // writes into the box itself
            return box;
        }

        public void Walkers_ABoxedStructThatReachesItselfEnds()
        {
            object a = SelfBox(1), b = SelfBox(1), c = SelfBox(2);
            NoThrow(() => Check.True(DeepEquals.Equals(a, b)), "DeepEquals");
            NoThrow(() => Check.False(DeepEquals.Equals(a, c)), "DeepEquals unequal");
            NoThrow(() => Check.Equal(0, PropertyDiff.Compare(a, b).Count), "PropertyDiff");
            NoThrow(() => Check.Equal(1, PropertyDiff.Compare(a, c).Count), "PropertyDiff unequal");
            NoThrow(() => Check.Equal(1, FlattenObject.Flatten(a, includeFields: true)["V"]), "Flatten");
        }

        // Used by this test alone: its reflection cache must start empty.
        public struct FirstReadOrder
        {
            public int V;
            public string? W;
            public object? X;
        }

        public void Walkers_PairMembersByNameOnATypesFirstRead()
        {
            // .NET Framework listed V,X,W on the first GetFields after a GetField("X"), then X,V,W: the two sides were
            // read in different orders and V was compared with X.
            Check.Equal("X", typeof(FirstReadOrder).GetField("X")!.Name);
            var a = new FirstReadOrder { V = 1, W = "w", X = null };
            var b = new FirstReadOrder { V = 1, W = "w", X = null };
            Check.True(DeepEquals.Equals(a, b));
            Check.Equal(0, PropertyDiff.Compare(a, b).Count);
            Check.True(string.Join(",", FlattenObject.Flatten(a, includeFields: true).Keys).StartsWith("V,W,X", StringComparison.Ordinal));
        }

        // ---- Default ImmutableArray and ArraySegment, which throw when enumerated ----

        public sealed class Segments
        {
            public ArraySegment<int> Seg { get; set; }
        }

        public void Walkers_ADefaultArraySegmentIsLikeNull()
        {
            var a = new Segments();
            var b = new Segments();
            Check.True(DeepEquals.Equals(a, b));
            Check.Equal(0, PropertyDiff.Compare(a, b).Count);
            var withData = new Segments { Seg = new ArraySegment<int>(new[] { 1 }) };
            Check.False(DeepEquals.Equals(a, withData));
            Check.Equal("Seg", PropertyDiff.Compare(a, withData).Single().Path);
            Check.True(FlattenObject.Flatten(a).ContainsKey("Seg"));
        }

#if !NETFRAMEWORK
        public sealed class Immutables
        {
            public ImmutableArray<int> Items { get; set; }
        }

        public void Walkers_ADefaultImmutableArrayIsLikeNull()
        {
            var a = new Immutables();
            var b = new Immutables();
            Check.True(DeepEquals.Equals(a, b));
            Check.Equal(0, PropertyDiff.Compare(a, b).Count);
            Check.False(DeepEquals.Equals(a, new Immutables { Items = ImmutableArray.Create(1) }));
            Check.True(FlattenObject.Flatten(a).ContainsKey("Items"));
        }
#endif

        // ---- A property hidden by `new` with another type ----

        public class HiddenBase
        {
            public int X { get; set; } = 1;
        }

        public sealed class HiddenDerived : HiddenBase
        {
            public new string X { get; set; } = "d";
        }

        public void Walkers_ReadOnlyTheMostDerivedOfAHiddenProperty()
        {
            Check.Equal("d", FlattenObject.Flatten(new HiddenDerived())["X"]);
            var diffs = PropertyDiff.Compare(new HiddenDerived(), new HiddenDerived { X = "e" });
            Check.Equal(1, diffs.Count);
            Check.Equal("X", diffs[0].Path);
            Check.Equal("d", diffs[0].OldValue);
        }

        // ---- Flatten: collisions, empty collections ----

        public sealed class Colliding
        {
            public int A_B { get; set; } = 1;
            public Inner A { get; set; } = new Inner();
        }

        public sealed class Inner
        {
            public int B { get; set; } = 2;
        }

        public void Flatten_PathsThatCollideThrowRatherThanOverwrite()
        {
            var ex = Check.Throws<InvalidOperationException>(() => FlattenObject.Flatten(new Colliding(), "_"));
            Check.True(ex.Message.Contains("A_B"), ex.Message);
            var table = new Hashtable { { 1, "int" }, { "1", "string" } };
            Check.Throws<InvalidOperationException>(() => FlattenObject.Flatten(new { T = table }));
            Check.Equal(2, FlattenObject.Flatten(new Colliding())["A.B"]);
        }

        public sealed class Nested
        {
            public List<List<int>> L { get; set; } = new List<List<int>> { new List<int>(), new List<int> { 1 } };
        }

        public void Flatten_AnEmptyCollectionIsKeptSoUnflattenRebuildsIt()
        {
            var flat = FlattenObject.Flatten(new Nested());
            Check.True(flat.ContainsKey("L[0]"), string.Join(",", flat.Keys));
            Check.Equal(1, flat["L[1][0]"]);
            var tree = UnflattenObject.Unflatten(flat);
            var list = (List<object?>)tree["L"]!;
            Check.Equal(2, list.Count);
        }

        // ---- PropertyPath ----

        public sealed class Thrower
        {
            public int Bad => throw new InvalidOperationException("getter failed");
            public int Fine { get; set; } = 3;
        }

        public void PropertyPath_AGettersOwnExceptionPropagatesFromGetAndTryGet()
        {
            var t = new Thrower();
            Check.Equal("getter failed", Check.Throws<InvalidOperationException>(() => PropertyPath.Get(t, "Bad")).Message);
            Check.Equal("getter failed", Check.Throws<InvalidOperationException>(() => PropertyPath.TryGet(t, "Bad", out _)).Message);
            Check.True(PropertyPath.TryGet(t, "Missing", out _) == false);
            Check.Equal(3, PropertyPath.Get(t, "Fine"));
        }

        public sealed class SetThrower
        {
            public int Value { get => 0; set => throw new InvalidOperationException("setter failed"); }
        }

        public void PropertyPath_ASettersOwnExceptionPropagates()
            => Check.Equal("setter failed", Check.Throws<InvalidOperationException>(() => PropertyPath.Set(new SetThrower(), "Value", 1)).Message);

        public void PropertyPath_AQuotedKeyMayHoldABracketAndKeepsInnerQuotes()
        {
            var d = new Dictionary<string, int> { ["a]b"] = 1, ["'x'"] = 2, ["x"] = 3, ["a.b"] = 4 };
            Check.Equal(1, PropertyPath.Get(d, "[\"a]b\"]"));
            Check.Equal(2, PropertyPath.Get(d, "[\"'x'\"]"));
            Check.Equal(3, PropertyPath.Get(d, "['x']"));
            Check.Equal(3, PropertyPath.Get(d, "[x]"));
            Check.Equal(4, PropertyPath.Get(d, "['a.b']"));
            Check.Throws<ArgumentException>(() => PropertyPath.Get(d, "[\"a]"));
        }

#if !NETFRAMEWORK
        public sealed class SpanHolder
        {
            private readonly int[] _data = { 1, 2 };
            public Span<int> Span => _data;
            public int Count => _data.Length;
            public int Plain { get; set; }
        }

        public void PropertyPath_ASpanPropertyIsAnArgumentExceptionAndTryGetFalse()
        {
            var ex = Check.Throws<ArgumentException>(() => PropertyPath.Get(new SpanHolder(), "Span"));
            Check.True(ex.Message.Contains("Span"), ex.Message);
            Check.False(PropertyPath.TryGet(new SpanHolder(), "Span", out _));
        }

        public sealed class SpanTarget
        {
            public int Count { get; set; }
            public Span<int> Span { get => default; set { } }
        }

        public void ObjectMapper_ASpanSourcePropertyIsSkipped()
        {
            var mapped = new ObjectMapper<SpanHolder, SpanTarget>().Map(new SpanHolder());
            Check.Equal(2, mapped.Count);
        }

        public void ExpressionAccessor_UnholdableMembersAreArgumentExceptions()
        {
            var span = Check.Throws<ArgumentException>(() => ExpressionAccessor.Getter(typeof(SpanHolder), "Span"));
            Check.Equal("memberName", span.ParamName);
            var r = Check.Throws<ArgumentException>(() => ExpressionAccessor.Getter(typeof(RefHolder), "Ref"));
            Check.Equal("memberName", r.ParamName);
            Check.Throws<ArgumentException>(() => ExpressionAccessor.Setter(typeof(RefHolder), "Ref"));
            var onSpan = Check.Throws<ArgumentException>(() => ExpressionAccessor.Getter(typeof(Span<int>), "Length"));
            Check.Equal("type", onSpan.ParamName);
            Check.Equal(0, ExpressionAccessor.Getter(typeof(SpanHolder), "Plain")(new SpanHolder()));
        }

        public sealed class RefHolder
        {
            private int _x = 5;
            public ref int Ref => ref _x;
        }
#endif

        // ---- ObjectMapper ----

        public sealed class MapSource
        {
            public int Value => throw new InvalidOperationException("source getter failed");
        }

        public sealed class MapTarget
        {
            public int Value { get; set; }
        }

        public sealed class MapThrowingTarget
        {
            public int Value { get => 0; set => throw new InvalidOperationException("target setter failed"); }
        }

        public sealed class MapPlainSource
        {
            public int Value { get; set; } = 1;
        }

        public void ObjectMapper_AccessorExceptionsPropagateAsThemselves()
        {
            Check.Equal("source getter failed",
                Check.Throws<InvalidOperationException>(() => new ObjectMapper<MapSource, MapTarget>().Map(new MapSource())).Message);
            Check.Equal("target setter failed",
                Check.Throws<InvalidOperationException>(() => new ObjectMapper<MapPlainSource, MapThrowingTarget>().Map(new MapPlainSource())).Message);
        }

        // ---- TypeUtils ----

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
        public sealed class TagAttribute : Attribute
        {
            public TagAttribute(string name) => Name = name;
            public string Name { get; set; }
        }

        [Tag("one"), Tag("two")]
        public sealed class TwiceTagged { }

        public void GetTypesWithAttribute_AnAttributeAppliedTwiceIsFound()
        {
            var found = TypeUtils.GetTypesWithAttribute<TagAttribute>(typeof(TwiceTagged).Assembly);
            Check.True(found.Contains(typeof(TwiceTagged)));
        }

        public sealed class RefusingConvertible : IConvertible
        {
            public TypeCode GetTypeCode() => TypeCode.Object;
            private static Exception No() => new NotSupportedException("no");
            public bool ToBoolean(IFormatProvider? provider) => throw No();
            public byte ToByte(IFormatProvider? provider) => throw No();
            public char ToChar(IFormatProvider? provider) => throw No();
            public DateTime ToDateTime(IFormatProvider? provider) => throw No();
            public decimal ToDecimal(IFormatProvider? provider) => throw No();
            public double ToDouble(IFormatProvider? provider) => throw No();
            public short ToInt16(IFormatProvider? provider) => throw No();
            public int ToInt32(IFormatProvider? provider) => throw No();
            public long ToInt64(IFormatProvider? provider) => throw No();
            public sbyte ToSByte(IFormatProvider? provider) => throw No();
            public float ToSingle(IFormatProvider? provider) => throw No();
            public string ToString(IFormatProvider? provider) => throw No();
            public object ToType(Type conversionType, IFormatProvider? provider) => throw No();
            public ushort ToUInt16(IFormatProvider? provider) => throw No();
            public uint ToUInt32(IFormatProvider? provider) => throw No();
            public ulong ToUInt64(IFormatProvider? provider) => throw No();
        }

        public void TryConvert_AnIConvertibleThatRefusesIsAFailure()
        {
            Check.False(TypeUtils.TryConvert(new RefusingConvertible(), typeof(int), out object? r));
            Check.Equal(0, r);
        }

        public void TryConvert_DoesNotLoseInformationSilently()
        {
            Check.False(TypeUtils.TryConvert(2.5, typeof(int), out _));
            Check.False(TypeUtils.TryConvert(-0.5f, typeof(long), out _));
            Check.False(TypeUtils.TryConvert(1.25m, typeof(byte), out _));
            Check.False(TypeUtils.TryConvert(1e300, typeof(float), out _));
            Check.True(TypeUtils.TryConvert(2.0, typeof(int), out object? two));
            Check.Equal(2, two);
            Check.True(TypeUtils.TryConvert(double.PositiveInfinity, typeof(float), out object? inf));
            Check.True(float.IsPositiveInfinity((float)inf!));
            Check.True(TypeUtils.TryConvert(1e30, typeof(float), out object? big));
            Check.Equal(1e30f, big);
        }

        public void TryConvert_FloatingToTextRoundTrips()
        {
            // .NET Framework's default format kept 15 digits: 0.1 + 0.2 came back as "0.3".
            var rng = new Random(59);
            for (int i = 0; i < 2000; i++)
            {
                double d = BitConverter.Int64BitsToDouble((long)(rng.NextDouble() * long.MaxValue));
                if (double.IsNaN(d)) continue;
                Check.True(TypeUtils.TryConvert(d, typeof(string), out object? s));
                Check.Equal(d, double.Parse((string)s!, NumberStyles.Float, CultureInfo.InvariantCulture), (string)s!);
                float f = (float)(rng.NextDouble() * 1e6);
                Check.True(TypeUtils.TryConvert(f, typeof(string), out object? fs));
                Check.Equal(f, float.Parse((string)fs!, NumberStyles.Float, CultureInfo.InvariantCulture), (string)fs!);
            }
            Check.True(TypeUtils.TryConvert(0.1 + 0.2, typeof(string), out object? sum));
            Check.Equal(0.1 + 0.2, double.Parse((string)sum!, CultureInfo.InvariantCulture));
        }

        public void FriendlyName_PointersAndByRefsUseFriendlyElementNames()
        {
            Check.Equal("int*", TypeUtils.FriendlyName(typeof(int).MakePointerType()));
            Check.Equal("List<int>&", TypeUtils.FriendlyName(typeof(List<int>).MakeByRefType()));
            Check.Equal("int*[]", TypeUtils.FriendlyName(typeof(int).MakePointerType().MakeArrayType()));
        }

        // ---- ActivatorUtils ----

        public interface IPlugin59 { int Id { get; } }

        public struct StructPlugin59 : IPlugin59 { public int Id => 7; }

        public sealed class FailingPlugin59 : IPlugin59
        {
            public FailingPlugin59() => throw new InvalidOperationException("plugin ctor failed");
            public int Id => 0;
        }

        public void ScanAndCreate_ReportsTheConstructorsOwnExceptionAndCreatesStructs()
        {
            var errors = new List<(Type, Exception)>();
            var made = ActivatorUtils.ScanAndCreate<IPlugin59>(typeof(IPlugin59).Assembly, (t, e) => errors.Add((t, e)));
            Check.True(made.Any(p => p.Id == 7), "the struct implementation was skipped");
            var (type, error) = errors.Single();
            Check.Equal(typeof(FailingPlugin59), type);
            Check.True(error is InvalidOperationException, error.GetType().Name);
            Check.Equal("plugin ctor failed", error.Message);
        }

        public void TryCreate_FrameworkByRefLikeTypesAreRefused()
        {
            // .NET Framework does not mark these with IsByRefLikeAttribute: TryCreate threw NotSupportedException there.
            Check.False(ActivatorUtils.TryCreate(typeof(TypedReference), Array.Empty<object?>(), out _));
            Check.False(ActivatorUtils.TryCreate(typeof(ArgIterator), Array.Empty<object?>(), out _));
            Check.False(ActivatorUtils.TryCreate(typeof(RuntimeArgumentHandle), Array.Empty<object?>(), out _));
            Check.Throws<ArgumentException>(() => TypeUtils.DefaultValue(typeof(TypedReference)));
        }

        // ---- AttributeCache ----

        [Tag("original")]
        public sealed class CacheTagged { }

        public void AttributeCache_AMutatedResultDoesNotChangeLaterLookups()
        {
            var first = AttributeCache.Get<TagAttribute>(typeof(CacheTagged))!;
            first.Name = "mutated";
            Check.Equal("original", AttributeCache.Get<TagAttribute>(typeof(CacheTagged))!.Name);
            Check.Equal("original", AttributeCache.GetAll<TagAttribute>(typeof(CacheTagged))[0].Name);
            Check.True(AttributeCache.Has<TagAttribute>(typeof(CacheTagged)));
        }

        // ---- Graphs ----

        public void DepthFirst_NullStartNamesStart()
        {
            var g = new Graph<string>();
            g.AddEdge("a", "b");
            Check.Equal("start", Check.Throws<ArgumentNullException>(() => g.DepthFirst(null!)).ParamName);
        }

        public void Kruskal_AnInfiniteEdgeIsNoEdge()
        {
            var g = new Graph<int>(directed: false);
            g.AddEdge(1, 2, double.NegativeInfinity);
            g.AddEdge(2, 3, double.PositiveInfinity);
            g.AddEdge(3, 4, 1.0);
            var r = MinimumSpanningTree.Kruskal(g);
            Check.True(double.IsNegativeInfinity(r.TotalWeight), r.TotalWeight.ToString(CultureInfo.InvariantCulture));
            Check.Equal(2, r.Edges.Count);
            Check.Equal(2, r.ComponentCount);
            Check.False(r.IsSpanningTree);
        }

        // ---- Intervals ----

#if !NETFRAMEWORK
        public void Interval_HalfNaNIsRejected()
        {
            Check.Throws<ArgumentException>(() => new Interval<Half>(Half.NaN, (Half)1));
            Check.Throws<ArgumentException>(() => new RangeMap<Half, int>().Set((Half)0, Half.NaN, 1));
            Check.Equal((Half)1, new Interval<Half>((Half)0, (Half)1).End);
        }
#endif

        private sealed class TrapComparer : IEqualityComparer<int>
        {
            public bool Armed;
            public bool Equals(int x, int y) => Armed ? throw new InvalidOperationException("comparer") : x == y;
            public int GetHashCode(int obj) => obj;
        }

        public void RangeMap_AThrowingValueComparerLeavesTheMapUnchanged()
        {
            var cmp = new TrapComparer();
            var map = new RangeMap<int, int>(cmp);
            map.Set(0, 10, 1);
            map.Set(20, 30, 2);
            var before = map.Segments.Select(s => (s.Key.Start, s.Key.End, s.Value)).ToList();
            cmp.Armed = true;
            Check.Throws<InvalidOperationException>(() => map.Set(10, 20, 1));
            Check.Throws<InvalidOperationException>(() => map.Set(5, 25, 3));
            cmp.Armed = false;
            Check.True(before.SequenceEqual(map.Segments.Select(s => (s.Key.Start, s.Key.End, s.Value))),
                string.Join(" ", map.Segments.Select(s => $"[{s.Key.Start},{s.Key.End})={s.Value}")));
            map.Set(10, 20, 1);
            Check.Equal(2, map.SegmentCount);
        }
    }
}
