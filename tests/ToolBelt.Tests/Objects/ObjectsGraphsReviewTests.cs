using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ToolBelt.Functional;
using ToolBelt.Graphs;
using ToolBelt.Intervals;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Test types for TypeUtils / ActivatorUtils: nested generics and an open generic derived type.
    public class ReviewGenOuter<T>
    {
        public class Inner<U> { }
        public class Plain { }
    }

    public abstract class ReviewScanBase { }
    public sealed class ReviewScanClosed : ReviewScanBase { }
    public sealed class ReviewScanOpen<T> : ReviewScanBase { }

    /// <summary>Regression tests for review round 14 (objects, functional, graphs, intervals).</summary>
    public sealed class ObjectsGraphsReviewTests
    {
        public sealed class Chain
        {
            public int V { get; set; }
            public Chain? Next { get; set; }
        }

        private static Chain MakeChain(int length, int lastValue)
        {
            var head = new Chain { V = 0 };
            Chain cur = head;
            for (int i = 1; i < length; i++)
            {
                cur.Next = new Chain { V = i == length - 1 ? lastValue : 0 };
                cur = cur.Next;
            }
            return head;
        }

        // ---- DeepEquals / PropertyDiff / Flatten / Unflatten: cycles and depth ----

        public void DeepEquals_SelfContainingList_DoesNotOverflow()
        {
            var a = new List<object>(); a.Add(a);
            var b = new List<object>(); b.Add(b);
            Check.True(DeepEquals.Equals(a, b));
        }

        public void PropertyDiff_SelfContainingList_DoesNotOverflow()
        {
            var a = new List<object>(); a.Add(a);
            var b = new List<object>(); b.Add(b);
            Check.Equal(0, PropertyDiff.Compare(a, b).Count);
        }

        public void DeepEquals_DeepChain_DoesNotOverflow()
        {
            Check.True(DeepEquals.Equals(MakeChain(100_000, 1), MakeChain(100_000, 1)));
            Check.False(DeepEquals.Equals(MakeChain(100_000, 1), MakeChain(100_000, 2)));
        }

        public void PropertyDiff_DeepChain_DoesNotOverflow()
        {
            var diffs = PropertyDiff.Compare(MakeChain(20_000, 1), MakeChain(20_000, 2));
            Check.Equal(1, diffs.Count);
            Check.True(diffs[0].Path.EndsWith(".Next.V", StringComparison.Ordinal));
            Check.Equal(1, diffs[0].OldValue);
        }

        public void PropertyDiff_KeepsPreOrder()
        {
            var a = new { A = 1, B = new { C = 2, D = 3 }, E = new[] { 4, 5 } };
            var b = new { A = 9, B = new { C = 8, D = 7 }, E = new[] { 6, 5, 1 } };
            string paths = string.Join(",", PropertyDiff.Compare(a, b).Select(d => d.Path));
            Check.Equal("A,B.C,B.D,E[0],E[2]", paths);
        }

        public void Flatten_DeepChain_DoesNotOverflow()
        {
            var flat = FlattenObject.Flatten(MakeChain(10_000, 7));
            Check.Equal(10_001, flat.Count); // 10,000 V values plus the final null Next
        }

        public void Flatten_KeepsOrder()
        {
            var flat = FlattenObject.Flatten(new { A = 1, B = new { C = 2 }, D = new[] { 3, 4 } });
            Check.Equal("A,B.C,D[0],D[1]", string.Join(",", flat.Keys));
        }

        public void Unflatten_DeepKey_DoesNotOverflow()
        {
            string key = string.Join(".", Enumerable.Repeat("a", 100_000));
            var tree = UnflattenObject.Unflatten(new[] { new KeyValuePair<string, object?>(key, 1) });
            object? node = tree;
            int depth = 0;
            while (node is Dictionary<string, object?> d) { node = d["a"]; depth++; }
            Check.Equal(100_000, depth);
            Check.Equal(1, node);
        }

        public void Unflatten_TextAfterBracket_IsRejected()
        {
            Check.Throws<FormatException>(() => UnflattenObject.Unflatten(new[] { new KeyValuePair<string, object?>("a[0]b", 1) }));
        }

        // ---- DeepEquals / PropertyDiff: value semantics ----

        public void FloatTolerance_InfinityAndNaN_EqualThemselves()
        {
            var opt = new ObjectComparisonOptions { FloatTolerance = 0.1 };
            Check.True(DeepEquals.Equals(double.PositiveInfinity, double.PositiveInfinity, opt));
            Check.True(DeepEquals.Equals(double.NaN, double.NaN, opt));
            Check.True(DeepEquals.Equals(float.NegativeInfinity, float.NegativeInfinity, opt));
            Check.False(DeepEquals.Equals(double.PositiveInfinity, double.NegativeInfinity, opt));
            Check.Equal(0, PropertyDiff.Compare(double.PositiveInfinity, double.PositiveInfinity, opt).Count);
            Check.Equal(0, PropertyDiff.Compare(double.NaN, double.NaN, opt).Count);
        }

        public void HashSets_CompareAsSets()
        {
            var a = new HashSet<int> { 1, 2, 3 };
            var b = new HashSet<int> { 3, 2, 1 };
            // Force a different enumeration order in b.
            b.Remove(1); b.Remove(2); b.Add(2); b.Add(1);
            Check.True(DeepEquals.Equals(a, b));
            Check.Equal(0, PropertyDiff.Compare(a, b).Count);
            Check.False(DeepEquals.Equals(a, new HashSet<int> { 1, 2, 4 }));
            Check.Equal(1, PropertyDiff.Compare(a, new HashSet<int> { 1, 2, 4 }).Count);
        }

        public void MultiDimensionalArrays_CompareShape()
        {
            Check.False(DeepEquals.Equals(new int[2, 3], new int[3, 2]));
            Check.False(DeepEquals.Equals(new int[6], new int[2, 3]));
            Check.True(DeepEquals.Equals(new int[2, 3], new int[2, 3]));
            Check.True(PropertyDiff.Compare(new int[2, 3], new int[3, 2]).Count > 0);
        }

        public void BigInteger_ComparesByValue()
        {
            Check.False(DeepEquals.Equals(new BigInteger(3), new BigInteger(5)));
            Check.Equal(1, PropertyDiff.Compare(new BigInteger(3), new BigInteger(5)).Count);
            Check.Equal(new BigInteger(3), FlattenObject.Flatten(new { N = new BigInteger(3) })["N"]);
        }

        public void FieldOnlyStructs_CompareByValue()
        {
            Check.False(DeepEquals.Equals((1, 2), (3, 4)));
            Check.True(DeepEquals.Equals((1, "x"), (1, "x")));
            // Since round 20 a struct's fields are always compared, so each differing field is reported.
            Check.Equal("Item1,Item2", string.Join(",", PropertyDiff.Compare((1, 2), (3, 4)).Select(d => d.Path)));
            Check.True(DeepEquals.Equals((1, 2), (3, 4), new ObjectComparisonOptions { IncludeFields = true }) == false);
        }

        public sealed class HasType
        {
            public Type? T { get; set; }
        }

        public void TypeMembers_AreLeaves()
        {
            Check.True(DeepEquals.Equals(new HasType { T = typeof(int) }, new HasType { T = typeof(int) }));
            Check.False(DeepEquals.Equals(new HasType { T = typeof(int) }, new HasType { T = typeof(string) }));
            Check.Equal(1, PropertyDiff.Compare(new HasType { T = typeof(int) }, new HasType { T = typeof(string) }).Count);
            var flat = FlattenObject.Flatten(new HasType { T = typeof(int) });
            Check.Equal(1, flat.Count);
            Check.Equal(typeof(int), flat["T"]);
        }

        public sealed class HasMemory
        {
            public ReadOnlyMemory<byte> M { get; set; }
        }

        public void ReadOnlyMemory_ComparesContent()
        {
            var a = new HasMemory { M = new byte[] { 1, 2 } };
            Check.True(DeepEquals.Equals(a, new HasMemory { M = new byte[] { 1, 2 } }));
            Check.False(DeepEquals.Equals(a, new HasMemory { M = new byte[] { 1, 3 } }));
            Check.Equal(1, PropertyDiff.Compare(a, new HasMemory { M = new byte[] { 1, 3 } }).Count);
            var flat = FlattenObject.Flatten(a);
            Check.Equal((byte)2, flat["M[1]"]);
        }

        // ---- PropertyPath ----

        public sealed class IntKeyed : Dictionary<int, string> { }
        public sealed class StrKeyed<T> : Dictionary<string, T> { }

        public void PropertyPath_MissingKey_IsNotFound()
        {
            var d = new Dictionary<string, int> { ["a"] = 1 };
            Check.Throws<KeyNotFoundException>(() => PropertyPath.Get(d, "[b]"));
            Check.False(PropertyPath.TryGet(d, "[b]", out _));
        }

        public void PropertyPath_TryGet_BadKey_ReturnsFalse()
        {
            var d = new Dictionary<int, string> { [1] = "x" };
            Check.False(PropertyPath.TryGet(d, "[abc]", out _));
            Check.False(PropertyPath.TryGet(d, "[99999999999999999999]", out _));
        }

        public void PropertyPath_KeyTypeFromDictionaryInterface()
        {
            Check.Equal("five", PropertyPath.Get(new IntKeyed { [5] = "five" }, "[5]"));
            Check.Equal(1, PropertyPath.Get(new StrKeyed<int> { ["a"] = 1 }, "[a]"));
        }

        public struct Pt
        {
            public int X { get; set; }
        }

        public sealed class PtHolder
        {
            public Pt P { get; set; }
        }

        public void PropertyPath_SetThroughStruct_WritesBack()
        {
            var h = new PtHolder();
            PropertyPath.Set(h, "P.X", 5);
            Check.Equal(5, h.P.X);
        }

        public class HidBase { public object? Value { get; set; } }
        public sealed class HidDerived : HidBase { public new string? Value { get; set; } }

        public void PropertyPath_HiddenProperty_UsesMostDerived()
        {
            var d = new HidDerived { Value = "derived" };
            Check.Equal("derived", PropertyPath.Get(d, "Value"));
            PropertyPath.Set(d, "Value", "set");
            Check.Equal("set", d.Value);
        }

        // ---- ExpressionAccessor ----

        public sealed class Accessible
        {
#pragma warning disable CS0414
            private int _secret = 42;
#pragma warning restore CS0414
            public string Name { get; set; } = "n";
            public int Locked { get; private set; } = 1;
            public int Count { get; set; } = 3;
        }

        public sealed class TwoCase
        {
            public int Id { get; set; } = 1;
            public int ID { get; set; } = 2;
        }

        public void ExpressionAccessor_MatchesPublicMembersExactly()
        {
            var a = new Accessible();
            Check.Throws<ArgumentException>(() => ExpressionAccessor.GetValue(a, "_secret"));
            Check.Throws<ArgumentException>(() => ExpressionAccessor.GetValue(a, "name"));
            Check.Throws<InvalidOperationException>(() => ExpressionAccessor.SetValue(a, "Locked", 5));
            Check.Equal(1, a.Locked);
            Check.Equal(1, ExpressionAccessor.GetValue(new TwoCase(), "Id"));
            Check.Equal(2, ExpressionAccessor.GetValue(new TwoCase(), "ID"));
            var ex = Check.Throws<ArgumentException>(() => ExpressionAccessor.Getter(typeof(Accessible), "Missing"));
            Check.Equal("memberName", ex.ParamName);
            Check.Throws<ArgumentException>(() => ExpressionAccessor.Getter<Accessible, int>("_secret"));
            Check.Throws<InvalidOperationException>(() => ExpressionAccessor.Setter<Accessible, int>("Locked"));
        }

        public void ExpressionAccessor_SetNullOnValueType_SetsDefault()
        {
            var a = new Accessible();
            ExpressionAccessor.SetValue(a, "Count", null);
            Check.Equal(0, a.Count);
        }

        // ---- ObjectMapper ----

        public sealed class MapSrc
        {
            public int X { get; set; } = 3;
            public int? N { get; set; }
            public int Hidden { get; set; } = 5;
        }

        public struct MapStruct
        {
            public int X { get; set; }
        }

        public sealed class MapTgt
        {
            public int N { get; set; } = 7;
            public int Hidden { get; private set; }
        }

        public void ObjectMapper_StructTarget_KeepsValues()
        {
            Check.Equal(3, new ObjectMapper<MapSrc, MapStruct>().Map(new MapSrc()).X);
        }

        public void ObjectMapper_SkipsNullIntoValueTypeAndNonPublicSetters()
        {
            var t = new ObjectMapper<MapSrc, MapTgt>().Map(new MapSrc());
            Check.Equal(7, t.N);
            Check.Equal(0, t.Hidden);
        }

        // ---- TypeUtils ----

        public void TypeUtils_FriendlyName_JaggedAndNestedGenerics()
        {
            Check.Equal("int[][,]", TypeUtils.FriendlyName(typeof(int[][,])));
            Check.Equal("int[,][]", TypeUtils.FriendlyName(typeof(int[,][])));
            Check.Equal("ReviewGenOuter<int>.Inner<string>", TypeUtils.FriendlyName(typeof(ReviewGenOuter<int>.Inner<string>)));
            Check.Equal("ReviewGenOuter<int>.Plain", TypeUtils.FriendlyName(typeof(ReviewGenOuter<int>.Plain)));
        }

        public void TypeUtils_DefaultValue_RejectsOpenAndVoid()
        {
            Check.Throws<ArgumentException>(() => TypeUtils.DefaultValue(typeof(void)));
            Check.Throws<ArgumentException>(() => TypeUtils.DefaultValue(typeof(KeyValuePair<,>)));
            Check.Throws<ArgumentException>(() => TypeUtils.DefaultValue(typeof(List<>)));
        }

        public void TypeUtils_GetDerivedTypes_ExcludesOpenGenerics()
        {
            var types = TypeUtils.GetDerivedTypes(typeof(ReviewScanBase), typeof(ReviewScanBase).Assembly);
            Check.True(types.Contains(typeof(ReviewScanClosed)));
            Check.False(types.Any(t => t.ContainsGenericParameters));
            var errors = new List<Type>();
            ActivatorUtils.ScanAndCreate<ReviewScanBase>(typeof(ReviewScanBase).Assembly, (t, _) => errors.Add(t));
            Check.Equal(0, errors.Count);
        }

        // ---- ActivatorUtils ----

        public sealed class Overloaded
        {
            public string Chosen { get; }
            public Overloaded(object o) => Chosen = "object";
            public Overloaded(string s) => Chosen = "string";
        }

        public sealed class Boom
        {
            public Boom() => throw new FormatException("boom");
        }

        public void ActivatorUtils_PicksMostSpecificConstructor()
        {
            Check.Equal("string", ((Overloaded)ActivatorUtils.Create(typeof(Overloaded), "x")).Chosen);
            Check.Equal("object", ((Overloaded)ActivatorUtils.Create(typeof(Overloaded), 5)).Chosen);
        }

        public void ActivatorUtils_StructOpenGenericAndThrowingConstructor()
        {
            Check.True(ActivatorUtils.TryCreate(typeof(Pt), Array.Empty<object?>(), out object? pt));
            Check.True(pt is Pt);
            Check.False(ActivatorUtils.TryCreate(typeof(List<>), Array.Empty<object?>(), out _));
            Check.Throws<FormatException>(() => ActivatorUtils.Create(typeof(Boom)));
        }

        // ---- AttributeCache ----

        [AttributeUsage(AttributeTargets.Property, Inherited = true)]
        public sealed class MarkAttribute : Attribute { }

        public class AttrBase { [Mark] public virtual int P { get; set; } }
        public sealed class AttrDerived : AttrBase { public override int P { get; set; } }

        public void AttributeCache_InheritWorksForProperties()
        {
            var prop = typeof(AttrDerived).GetProperty("P")!;
            Check.True(AttributeCache.Has<MarkAttribute>(prop, inherit: true));
            Check.False(AttributeCache.Has<MarkAttribute>(prop, inherit: false));
        }

        // ---- Memoize ----

        public void Memoize_NullArgument_NamesArg()
        {
            var f = Memoize.Function<string, int>(s => s.Length);
            Check.Equal("arg", Check.Throws<ArgumentNullException>(() => f(null!)).ParamName);
            var g = Memoize.ThreadSafe<string, int>(s => s.Length);
            Check.Equal("arg", Check.Throws<ArgumentNullException>(() => g(null!)).ParamName);
        }

        // ---- Graphs ----

        public void TopologicalSort_LongCycle_DoesNotOverflow()
        {
            const int n = 100_000;
            var g = new Graph<int>();
            for (int i = 0; i < n; i++) g.AddEdge(i, (i + 1) % n);
            var r = TopologicalSort.Sort(g);
            Check.False(r.IsAcyclic);
            Check.Equal(n, r.Cycle.Count);
        }

        public void TopologicalSort_CycleUsesGraphComparer()
        {
            var g = new Graph<string>(directed: true, comparer: StringComparer.OrdinalIgnoreCase);
            g.AddEdge("a", "b");
            g.AddEdge("b", "A");
            var r = TopologicalSort.Sort(g);
            Check.False(r.IsAcyclic);
            Check.Equal(2, r.Cycle.Count);
        }

        public void Graph_Traversals_ValidateEagerly()
        {
            var g = new Graph<string>();
            g.AddEdge("a", "b");
            Check.Throws<KeyNotFoundException>(() => g.BreadthFirst("zz"));
            Check.Throws<KeyNotFoundException>(() => g.DepthFirst("zz"));
        }

        public void Graph_AddEdge_RejectsNaNWeight()
        {
            var g = new Graph<string>();
            Check.Throws<ArgumentException>(() => g.AddEdge("a", "b", double.NaN));
            Check.Equal(0, g.EdgeCount);
        }

        // ---- Intervals ----

        public readonly struct CiKey : IComparable<CiKey>
        {
            public CiKey(string s) => S = s;
            public string S { get; }
            public int CompareTo(CiKey other) => string.Compare(S, other.S, StringComparison.OrdinalIgnoreCase);
        }

        public void Interval_EqualsAgreesWithHashCode()
        {
            var a = new Interval<CiKey>(new CiKey("a"), new CiKey("b"));
            var b = new Interval<CiKey>(new CiKey("A"), new CiKey("B"));
            Check.True(!a.Equals(b) || a.GetHashCode() == b.GetHashCode());
            Check.True(a.Equals(new Interval<CiKey>(new CiKey("a"), new CiKey("b"))));
        }

        public void RangeSet_Except_IsLinear()
        {
            const int n = 100_000;
            var a = new RangeSet<int>(Enumerable.Range(0, n).Select(i => new Interval<int>(4 * i, 4 * i + 2)));
            var b = new RangeSet<int>(Enumerable.Range(0, n).Select(i => new Interval<int>(4 * i + 1, 4 * i + 3)));
            var sw = Stopwatch.StartNew();
            var r = a.Except(b);
            sw.Stop();
            Check.Equal(n, r.Count);
            Check.Equal(new Interval<int>(0, 1), r.Intervals[0]);
            Check.True(sw.ElapsedMilliseconds < 3000, $"Except took {sw.ElapsedMilliseconds} ms");
        }

        public void RangeSet_Except_MatchesNaive()
        {
            var rng = new Random(14);
            for (int trial = 0; trial < 300; trial++)
            {
                var a = RandomSet(rng);
                var b = RandomSet(rng);
                var r = a.Except(b);
                for (int p = -2; p < 62; p++)
                    Check.Equal(a.Contains(p) && !b.Contains(p), r.Contains(p), $"trial {trial} point {p}");
                for (int i = 1; i < r.Count; i++)
                    Check.True(r.Intervals[i - 1].End.CompareTo(r.Intervals[i].Start) < 0);
            }
        }

        private static RangeSet<int> RandomSet(Random rng)
        {
            var list = new List<Interval<int>>();
            int count = rng.Next(0, 8);
            for (int i = 0; i < count; i++)
            {
                int s = rng.Next(0, 60);
                list.Add(new Interval<int>(s, s + rng.Next(0, 10)));
            }
            return new RangeSet<int>(list);
        }

        public void RangeMap_ManySets_AreFast()
        {
            const int n = 50_000;
            var map = new RangeMap<int, int>();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) map.Set(2 * i, 2 * i + 1, i);
            sw.Stop();
            Check.Equal(n, map.SegmentCount);
            Check.True(sw.ElapsedMilliseconds < 3000, $"Set took {sw.ElapsedMilliseconds} ms");
        }

        public void RangeMap_MatchesNaive()
        {
            var rng = new Random(1414);
            for (int trial = 0; trial < 300; trial++)
            {
                var map = new RangeMap<int, int>();
                var naive = new int?[60];
                for (int op = 0; op < 12; op++)
                {
                    int s = rng.Next(0, 55), e = s + rng.Next(0, 6);
                    if (rng.Next(4) == 0)
                    {
                        map.Remove(s, e);
                        for (int k = s; k < e; k++) naive[k] = null;
                    }
                    else
                    {
                        int v = rng.Next(3);
                        map.Set(s, e, v);
                        for (int k = s; k < e; k++) naive[k] = v;
                    }
                    for (int k = 0; k < 60; k++)
                    {
                        bool has = map.TryGet(k, out int got);
                        Check.Equal(naive[k].HasValue, has, $"trial {trial} op {op} key {k}");
                        if (has) Check.Equal(naive[k]!.Value, got);
                    }
                    var segs = map.Segments.ToList();
                    for (int i = 1; i < segs.Count; i++)
                    {
                        Check.True(segs[i - 1].Key.End.CompareTo(segs[i].Key.Start) <= 0, "sorted and disjoint");
                        Check.False(segs[i - 1].Key.End == segs[i].Key.Start && segs[i - 1].Value == segs[i].Value, "coalesced");
                    }
                }
            }
        }
    }
}
