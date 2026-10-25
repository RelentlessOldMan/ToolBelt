using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ToolBelt.Functional;
using ToolBelt.Graphs;
using ToolBelt.Grids;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Regressions from review round 20 (objects, functional, grids, graphs, intervals — second pass).
    public sealed class ObjectsGraphsReview2Tests
    {
        // Runs the action with a watchdog: a hang fails the test instead of stalling the run. Returns what it threw.
        private static Exception? Run(Action action, string what, int seconds = 10)
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

        public sealed class Diamond
        {
            public int V { get; set; }
            public Diamond? L { get; set; }
            public Diamond? R { get; set; }
        }

        // Node i + 1 points to node i twice: 2^depth paths through a graph of depth + 1 nodes.
        private static Diamond MakeDiamond(int depth, int top)
        {
            var node = new Diamond();
            for (int i = 0; i < depth; i++) node = new Diamond { L = node, R = node };
            node.V = top;
            return node;
        }

        // ---- PropertyDiff: shared subgraphs ----

        public void PropertyDiff_SharedSubgraphsAreWalkedOnce()
        {
            IReadOnlyList<Difference>? diffs = null;
            NoThrow(() => diffs = PropertyDiff.Compare(MakeDiamond(40, 1), MakeDiamond(40, 1)), "equal diamond chains");
            Check.Equal(0, diffs!.Count);
            NoThrow(() => diffs = PropertyDiff.Compare(MakeDiamond(40, 1), MakeDiamond(40, 2)), "diamond chains differing at the top");
            Check.Equal(1, diffs!.Count);
            Check.Equal("V", diffs![0].Path);
        }

        public void PropertyDiff_SharedNodeDifferenceIsReportedOnEveryPath()
        {
            // Skipping a pair already seen must not hide differences: each path to a differing shared node reports.
            var a = MakeDiamond(1, 0); a.L!.V = 1;
            var b = MakeDiamond(1, 0); b.L!.V = 2;
            var paths = PropertyDiff.Compare(a, b).Select(d => d.Path).ToArray();
            Check.Equal("L.V,R.V", string.Join(",", paths));
        }

        // ---- getters that return a new object every time ----

        public void FileSystemInfo_IsComparedAndFlattenedAsAValue()
        {
            bool same = false, different = true;
            NoThrow(() => same = DeepEquals.Equals(new DirectoryInfo(@"C:\x"), new DirectoryInfo(@"C:\x")), "DeepEquals DirectoryInfo");
            Check.True(same);
            NoThrow(() => different = DeepEquals.Equals(new DirectoryInfo(@"C:\x"), new DirectoryInfo(@"C:\y")), "DeepEquals different DirectoryInfo");
            Check.False(different);
            Check.False(DeepEquals.Equals(new FileInfo(@"C:\x"), new DirectoryInfo(@"C:\x")));

            IReadOnlyList<Difference>? diffs = null;
            NoThrow(() => diffs = PropertyDiff.Compare(new { D = new FileInfo(@"C:\a.txt") }, new { D = new FileInfo(@"C:\b.txt") }), "PropertyDiff FileInfo");
            Check.Equal(1, diffs!.Count);
            Check.Equal("D", diffs![0].Path);

            IDictionary<string, object?>? flat = null;
            var dir = new DirectoryInfo(@"C:\x");
            NoThrow(() => flat = FlattenObject.Flatten(new { Dir = dir }), "Flatten DirectoryInfo");
            Check.Equal(1, flat!.Count);
            Check.True(ReferenceEquals(dir, flat!["Dir"]));
        }

        // ---- structs whose data lives in public fields ----

        public void DeepEquals_StructFieldsDetermineEquality()
        {
            Check.False(DeepEquals.Equals(new Quaternion(1, 2, 3, 4), new Quaternion(5, 6, 7, 8)));
            Check.True(DeepEquals.Equals(new Quaternion(1, 2, 3, 4), new Quaternion(1, 2, 3, 4)));
            Check.False(DeepEquals.Equals(Matrix4x4.CreateRotationX(1), Matrix4x4.CreateRotationX(2)));
            Check.True(DeepEquals.Equals(new Quaternion(1, 2, 3, 4), new Quaternion(1, 2, 3, 4.05f), new ObjectComparisonOptions { FloatTolerance = 0.1 }));
            var diffs = PropertyDiff.Compare(new Quaternion(1, 2, 3, 4), new Quaternion(1, 2, 3, 5));
            Check.Equal(1, diffs.Count);
            Check.Equal("W", diffs[0].Path);
        }

        // ---- TypeUtils.TryConvert ----

        public void TryConvert_EnumFromNumbersConvertsByValue()
        {
            Check.True(TypeUtils.TryConvert(1.0, typeof(DayOfWeek), out object? v));
            Check.Equal((object)DayOfWeek.Monday, v);
            Check.True(TypeUtils.TryConvert(2m, typeof(DayOfWeek?), out v));
            Check.Equal((object)DayOfWeek.Tuesday, v);
            Check.True(TypeUtils.TryConvert(3L, typeof(DayOfWeek), out v));
            Check.Equal((object)DayOfWeek.Wednesday, v);
            Check.False(TypeUtils.TryConvert(1.5, typeof(DayOfWeek), out _));
            Check.False(TypeUtils.TryConvert(3.7f, typeof(DayOfWeek), out _));
            Check.False(TypeUtils.TryConvert(double.NaN, typeof(DayOfWeek), out _));
            Check.False(TypeUtils.TryConvert(1e30, typeof(DayOfWeek), out _));
            Check.False(TypeUtils.TryConvert(true, typeof(DayOfWeek), out _));
            Check.False(TypeUtils.TryConvert('a', typeof(DayOfWeek), out _));
        }

        public sealed class EnumTarget { public DayOfWeek Day { get; set; } = DayOfWeek.Friday; }
        public sealed class DoubleSource { public double Day { get; set; } }

        public void ObjectMapper_EnumFromDoubleIsNotReinterpreted()
        {
            var t = new ObjectMapper<DoubleSource, EnumTarget>().Map(new DoubleSource { Day = 2 });
            Check.Equal(DayOfWeek.Tuesday, t.Day);
        }

        public void TryConvert_DecimalCommaIsNotAThousandsSeparator()
        {
            Check.False(TypeUtils.TryConvert("1,5", typeof(double), out _));
            Check.False(TypeUtils.TryConvert("1,5", typeof(float), out _));
            Check.False(TypeUtils.TryConvert("1,5", typeof(decimal), out _));
            Check.True(TypeUtils.TryConvert("1.5", typeof(double), out object? v));
            Check.Equal((object)1.5, v);
            Check.True(TypeUtils.TryConvert("-2.5e3", typeof(decimal), out v));
            Check.Equal((object)(-2500m), v);
        }

        public void TryConvert_TypeWithoutADefaultReturnsFalse()
        {
            object? v = 1;
            Exception? e = Run(() => Check.False(TypeUtils.TryConvert("x", typeof(void), out v)), "TryConvert to void");
            Check.True(e is null, $"threw {e?.GetType().Name}: {e?.Message}");
            Check.Equal(null, v);
            e = Run(() => Check.False(TypeUtils.TryConvert("x", typeof(List<>), out v)), "TryConvert to an open generic");
            Check.True(e is null, $"threw {e?.GetType().Name}: {e?.Message}");
        }

        // ---- ActivatorUtils ----

        public void ActivatorUtils_NullableWithoutArgumentsIsNotCreated()
        {
            Check.False(ActivatorUtils.TryCreate(typeof(int?), Array.Empty<object?>(), out _));
            Check.Throws<MissingMethodException>(() => ActivatorUtils.Create(typeof(int?)));
            Check.Equal((object)5, ActivatorUtils.Create(typeof(int?), 5));
        }

        public void ActivatorUtils_RefLikeTypesAreNotCreated()
        {
            object? instance = null;
            bool created = true;
            Exception? e = Run(() => created = ActivatorUtils.TryCreate(typeof(Span<int>), new object?[] { new int[3] }, out instance), "TryCreate(Span<int>)");
            Check.True(e is null, $"threw {e?.GetType().Name}: {e?.Message}");
            Check.False(created);
            Check.False(ActivatorUtils.TryCreate(typeof(int).MakeByRefType(), Array.Empty<object?>(), out _));
            Check.False(ActivatorUtils.TryCreate(typeof(int).MakePointerType(), Array.Empty<object?>(), out _));
        }

        public sealed class Overloads
        {
            public string Chosen { get; }
            public Overloads(object o) => Chosen = "object";
            public Overloads(IEnumerable<char> chars) => Chosen = "IEnumerable<char>";
        }

        public sealed class NullableOverloads
        {
            public string Chosen { get; }
            public NullableOverloads(object o) => Chosen = "object";
            public NullableOverloads(int? n) => Chosen = "int?";
        }

        public void ActivatorUtils_ObjectParameterIsTheLeastSpecific()
        {
            Check.Equal("IEnumerable<char>", ((Overloads)ActivatorUtils.Create(typeof(Overloads), "abc")).Chosen);
            Check.Equal("int?", ((NullableOverloads)ActivatorUtils.Create(typeof(NullableOverloads), 3)).Chosen);
            Check.Equal("object", ((Overloads)ActivatorUtils.Create(typeof(Overloads), 3)).Chosen);
        }

        // ---- ExpressionAccessor ----

        public interface IBaseCount { int Count { get; } }
        public interface IDerivedCount : IBaseCount { }
        private sealed class Counted : IDerivedCount { public int Count => 7; }

        public void ExpressionAccessor_FindsMembersOfBaseInterfaces()
        {
            Func<IList<int>, int>? count = null;
            NoThrow(() => count = ExpressionAccessor.Getter<IList<int>, int>("Count"), "Getter<IList<int>, int>(Count)");
            Check.Equal(3, count!(new List<int> { 1, 2, 3 }));
            Func<IDerivedCount, int>? derived = null;
            NoThrow(() => derived = ExpressionAccessor.Getter<IDerivedCount, int>("Count"), "Getter<IDerivedCount, int>(Count)");
            Check.Equal(7, derived!(new Counted()));
            Func<object, object?>? untyped = null;
            NoThrow(() => untyped = ExpressionAccessor.Getter(typeof(IReadOnlyList<int>), "Count"), "Getter(IReadOnlyList<int>, Count)");
            Check.Equal((object)2, untyped!(new[] { 1, 2 }));
        }

        public struct MutableStruct { public int X { get; set; } public int F; }

        public void ExpressionAccessor_SetValueWritesIntoTheBox()
        {
            object boxed = new MutableStruct { X = 1 };
            ExpressionAccessor.SetValue(boxed, "X", 5);
            ExpressionAccessor.SetValue(boxed, "F", 6);
            Check.Equal(5, ((MutableStruct)boxed).X);
            Check.Equal(6, ((MutableStruct)boxed).F);
        }

        public sealed class IntHolder { public int N { get; set; } = 9; public int? M { get; set; } = 9; }

        public void ExpressionAccessor_TypedSetterStoresDefaultForNull()
        {
            var h = new IntHolder();
            Exception? e = Run(() => ExpressionAccessor.Setter<IntHolder, object?>("N")(h, null), "typed setter null → int");
            Check.True(e is null, $"threw {e?.GetType().Name}: {e?.Message}");
            Check.Equal(0, h.N);
            ExpressionAccessor.Setter<IntHolder, object?>("M")(h, null);
            Check.Equal(null, h.M);
        }

        // ---- ObjectMapper ----

        public class HiddenBase { public string Name { get; set; } = "base"; }
        public sealed class HiddenDerived : HiddenBase { public new object Name { get; set; } = "derived"; }
        public sealed class NameTarget { public string Name { get; set; } = ""; }

        public void ObjectMapper_HiddenSourcePropertyUsesTheDerivedOne()
        {
            var t = new ObjectMapper<HiddenDerived, NameTarget>().Map(new HiddenDerived());
            Check.Equal("derived", t.Name);
        }

        // ---- PropertyPath ----

        public sealed class ArrayHolder { public int[] Arr { get; set; } = { 1, 2, 3 }; }

        public void PropertyPath_ArrayIndexOutOfRangeIsAnArgumentError()
        {
            bool found = true;
            Exception? e = Run(() => found = PropertyPath.TryGet(new ArrayHolder(), "Arr[5]", out _), "TryGet Arr[5]");
            Check.True(e is null, $"threw {e?.GetType().Name}: {e?.Message}");
            Check.False(found);
            Check.False(PropertyPath.TryGet(new ArrayHolder(), "Arr[-1]", out _));
            Check.Throws<ArgumentException>(() => PropertyPath.Get(new ArrayHolder(), "Arr[3]"));
            Check.Throws<ArgumentException>(() => PropertyPath.Set(new ArrayHolder(), "Arr[3]", 1));
            Check.Throws<ArgumentException>(() => PropertyPath.Get(new { L = new List<int> { 1 } }, "L[1]"));
        }

        // ---- ShortestPath ----

        public void Dijkstra_InfiniteWeightIsUnreachable()
        {
            var g = new Graph<string>();
            g.AddEdge("a", "b", double.PositiveInfinity);
            var r = ShortestPath.Dijkstra(g, "a", "b");
            Check.False(r.Found);
            Check.Equal(0, r.Path.Count);
        }

        public void Dijkstra_NegativeWeightAnywhereThrows()
        {
            var g = new Graph<string>();
            g.AddEdge("s", "t", 1);
            g.AddEdge("s", "a", 5);
            g.AddEdge("a", "t", -10);
            Check.Throws<InvalidOperationException>(() => ShortestPath.Dijkstra(g, "s", "t"));
        }

        // ---- Grids: memory follows the cells visited ----

        public void FloodFill_SmallRegionOfAHugeGridIsCheap()
        {
            IReadOnlyList<Cell>? cells = null;
            NoThrow(() => cells = FloodFill.Region(100_000, 100_000, 0, 0, (r, c) => r < 3 && c < 3), "Region of a 1e10-cell grid");
            Check.Equal(9, cells!.Count);
        }

        public void GridPathfinding_ShortPathOnAHugeGridIsCheap()
        {
            GridPath? p = null;
            NoThrow(() => p = GridPathfinding.BreadthFirst(46_340, 46_340, new Cell(0, 0), new Cell(0, 1), (r, c) => true), "BreadthFirst on 46340²");
            Check.True(p!.Found);
            Check.Equal(1.0, p!.Cost);
            NoThrow(() => p = GridPathfinding.AStar(46_340, 46_340, new Cell(5, 5), new Cell(8, 9), (r, c) => true), "AStar on 46340²");
            Check.Equal(7.0, p!.Cost);
            NoThrow(() => p = GridPathfinding.Dijkstra(46_340, 46_340, new Cell(0, 0), new Cell(0, 2), (r, c) => true, (r, c) => 1), "Dijkstra on 46340²");
            Check.Equal(2.0, p!.Cost);
        }

        // ---- Result ----

        public void Result_DefaultReportsOneError()
        {
            Result<int> r = default;
            Check.Equal("Failure(" + r.Error + ")", r.ToString());
        }
    }
}
