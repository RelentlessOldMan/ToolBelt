using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ToolBelt.Graphs;
using ToolBelt.Grids;
using ToolBelt.Intervals;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Regressions from review round 38 (objects, functional, grids, graphs, intervals — third pass): each test failed
    // before its fix.
    public sealed class ObjectsGraphsReview3Tests
    {
        // Runs the action with a watchdog: a hang fails the test instead of stalling the run. Returns what it threw.
        private static Exception? Run(Action action, string what, int seconds = 10)
        {
            Exception? error = null;
            var task = Task.Run(() => { try { action(); } catch (Exception e) { error = e; } });
            Check.True(task.Wait(TimeSpan.FromSeconds(seconds)), what + " did not finish");
            return error;
        }

        private static void NoThrow(Action action, string what, int seconds = 10)
        {
            Exception? e = Run(action, what, seconds);
            Check.True(e is null, $"{what} threw {e?.GetType().Name}: {e?.Message}");
        }

        public struct Vec
        {
            public Vec(double x, double y) { X = x; Y = y; }
            public double X { get; }
            public double Y { get; }
            public Vec Perpendicular => new Vec(-Y, X);
        }

        public sealed class Fraction
        {
            public Fraction(int num, int den) { Num = num; Den = den; }
            public int Num { get; }
            public int Den { get; }
            public Fraction Reciprocal => new Fraction(Den, Num);
        }

        public void ComputedSelfTypedProperties_WalksEnd()
        {
            bool equal = false;
            NoThrow(() => equal = DeepEquals.Equals(new Vec(1, 2), new Vec(1, 2)), "DeepEquals of Vec", 3);
            Check.True(equal, "equal vectors");
            NoThrow(() => equal = DeepEquals.Equals(new Fraction(1, 2), new Fraction(1, 3)), "DeepEquals of Fraction", 3);
            Check.False(equal, "different fractions");

            IReadOnlyList<Difference>? diffs = null;
            NoThrow(() => diffs = PropertyDiff.Compare(new Fraction(1, 2), new Fraction(1, 3)), "PropertyDiff of Fraction", 3);
            Check.True(diffs!.Any(d => d.Path == "Den"), "Den reported");

            IDictionary<string, object?>? flat = null;
            NoThrow(() => flat = FlattenObject.Flatten(new Fraction(1, 2)), "Flatten of Fraction", 3);
            Check.Equal((object)1, flat!["Num"]);
            Check.Equal((object)1, flat["Reciprocal.Den"]);
            Check.True(flat.Count < 50, "keys: " + flat.Count);
        }

        public class Base { public virtual int P { get; set; } }
        public class SetOnlyOverride : Base { public override int P { set => base.P = value; } }
        public class GetOnlyOverride : Base { public override int P => base.P; }
        public class Plain { public int P { get; set; } }

        public void OverrideWithOneAccessor_KeepsTheInheritedOne()
        {
            Check.False(DeepEquals.Equals(new SetOnlyOverride { P = 1 }, new SetOnlyOverride { P = 2 }), "DeepEquals");
            Check.Equal(1, PropertyDiff.Compare(new SetOnlyOverride { P = 1 }, new SetOnlyOverride { P = 2 }).Count);
            Check.Equal((object)3, FlattenObject.Flatten(new SetOnlyOverride { P = 3 })["P"]);
            Check.Equal((object)4, ExpressionAccessor.GetValue(new SetOnlyOverride { P = 4 }, "P"));
            Check.Equal((object)5, PropertyPath.Get(new SetOnlyOverride { P = 5 }, "P"));
            Check.Equal(6, new ObjectMapper<SetOnlyOverride, Plain>().Map(new SetOnlyOverride { P = 6 }).P);

            var g = new GetOnlyOverride();
            ExpressionAccessor.SetValue(g, "P", 7);
            Check.Equal(7, g.P);
            PropertyPath.Set(g, "P", 8);
            Check.Equal(8, g.P);
            Check.Equal(9, new ObjectMapper<Plain, GetOnlyOverride>().Map(new Plain { P = 9 }).P);
        }

        public interface INamed { string Name { get; } }
        public interface IPerson : INamed { int Age { get; } }
        public sealed class Person : IPerson { public string Name { get; set; } = ""; public int Age { get; set; } }
        public sealed class PersonDto { public string? Name { get; set; } public int Age { get; set; } }

        public void Mapper_InterfaceSourceIncludesInheritedProperties()
        {
            PersonDto dto = new ObjectMapper<IPerson, PersonDto>().Map(new Person { Name = "Ada", Age = 36 });
            Check.Equal("Ada", dto.Name);
            Check.Equal(36, dto.Age);
        }

        public sealed class PrivateGetter { public int P { private get; set; } public int Q { get; set; } }

        public void PrivateGetter_IsNotReadable()
        {
            Check.False(PropertyPath.TryGet(new PrivateGetter { P = 1 }, "P", out _), "PropertyPath");
            Check.False(FlattenObject.Flatten(new PrivateGetter { P = 1 }).ContainsKey("P"), "Flatten");
            Check.Equal(0, new ObjectMapper<PrivateGetter, Plain>().Map(new PrivateGetter { P = 1 }).P);
        }

        public sealed class Node
        {
            public int V { get; set; }
            public Node? Kid { get; set; }
            public Node? Up { get; set; }
        }

        public sealed class Holder
        {
            public Node? P { get; set; }
            public Node? C2 { get; set; }
        }

        private static Holder MakeHolder(int v)
        {
            var p = new Node { V = v };
            var c = new Node { Up = p };
            p.Kid = c;
            return new Holder { P = p, C2 = c };
        }

        public void PropertyDiff_CycleDoesNotHideASecondPath()
        {
            var paths = PropertyDiff.Compare(MakeHolder(1), MakeHolder(2)).Select(d => d.Path).ToList();
            Check.True(paths.Contains("P.V"), string.Join(", ", paths));
            Check.True(paths.Contains("C2.Up.V"), string.Join(", ", paths));
        }

        public void DeepEquals_DictionariesWithDifferentComparersAreSymmetric()
        {
            var ordinal = new Dictionary<string, int> { ["x"] = 1 };
            var ignoreCase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["X"] = 1 };
            Check.False(DeepEquals.Equals(ordinal, ignoreCase), "ordinal first");
            Check.False(DeepEquals.Equals(ignoreCase, ordinal), "ignore-case first");
        }

        public enum Small : byte { A, B }

        public void TryConvert_Conversions()
        {
            Check.False(TypeUtils.TryConvert(4294967297L, typeof(DayOfWeek), out _), "2^32 + 1 to DayOfWeek");
            Check.False(TypeUtils.TryConvert(256, typeof(Small), out _), "256 to a byte enum");
            Check.False(TypeUtils.TryConvert(-1, typeof(Small), out _), "-1 to a byte enum");
            Check.True(TypeUtils.TryConvert(1, typeof(Small), out object? v));
            Check.Equal((object)Small.B, v);

            Check.False(TypeUtils.TryConvert("1e400", typeof(double), out _), "1e400");
            Check.False(TypeUtils.TryConvert("1e39", typeof(float), out _), "1e39 float");
            Check.True(TypeUtils.TryConvert("-Infinity", typeof(double), out v));
            Check.Equal((object)double.NegativeInfinity, v);

            var guid = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff");
            Check.True(TypeUtils.TryConvert(guid.ToString(), typeof(Guid), out v));
            Check.Equal((object)guid, v);
            Check.True(TypeUtils.TryConvert(guid, typeof(string), out v));
            Check.Equal((object)guid.ToString(), v);
            Check.True(TypeUtils.TryConvert("01:02:03", typeof(TimeSpan?), out v));
            Check.Equal((object)new TimeSpan(1, 2, 3), v);
            Check.True(TypeUtils.TryConvert("2020-01-02T03:04:05+01:00", typeof(DateTimeOffset), out v));
            Check.Equal((object)new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)), v);
            Check.True(TypeUtils.TryConvert("1.2.3", typeof(Version), out v));
            Check.Equal((object)new Version(1, 2, 3), v);
            Check.True(TypeUtils.TryConvert("http://example.com/", typeof(Uri), out v));
            Check.Equal((object)new Uri("http://example.com/"), v);

            Check.False(TypeUtils.TryConvert("x", typeof(Span<int>), out v), "Span");
            Check.True(v is null, "Span result");
            Check.Equal("type", Check.Throws<ArgumentException>(() => TypeUtils.DefaultValue(typeof(Span<int>))).ParamName);
        }

        public sealed class WithAddress { public IPAddress? Address { get; set; } public int N { get; set; } }
        public sealed class Throwing { public int P => throw new InvalidOperationException("boom"); }

        public void IPAddress_IsAValue()
        {
            Check.True(DeepEquals.Equals(IPAddress.Parse("::1"), IPAddress.Parse("::1")), "equal v6");
            Check.False(DeepEquals.Equals(IPAddress.Parse("1.2.3.4"), IPAddress.Parse("1.2.3.5")), "different v4");
            var diffs = PropertyDiff.Compare(new WithAddress { Address = IPAddress.Parse("1.2.3.4") },
                new WithAddress { Address = IPAddress.Parse("1.2.3.5") });
            Check.Equal(1, diffs.Count);
            Check.Equal("Address", diffs[0].Path);
            Check.Equal((object)IPAddress.Loopback, FlattenObject.Flatten(new WithAddress { Address = IPAddress.Loopback })["Address"]);
            Check.True(DeepEquals.Equals(new IPEndPoint(IPAddress.Loopback, 80), new IPEndPoint(IPAddress.Loopback, 80)), "endpoint");
            // A getter's own exception, not reflection's wrapper.
            Check.Equal("boom", Check.Throws<InvalidOperationException>(() => DeepEquals.Equals(new Throwing(), new Throwing())).Message);
        }

        public void ShortestPaths_TotalOverflowingToInfinityIsStillAPath()
        {
            var g = new Graph<string>();
            g.AddEdge("a", "b", 1e308);
            g.AddEdge("b", "c", 1e308);
            var r = ShortestPath.Dijkstra(g, "a", "c");
            Check.True(r.Found, "graph");
            Check.Equal(3, r.Path.Count);
            var p = GridPathfinding.Dijkstra(1, 3, new Cell(0, 0), new Cell(0, 2), (row, col) => true, (row, col) => 1e308);
            Check.True(p.Found, "grid");
            Check.Equal(3, p.Cells.Count);
            // An infinite weight is still no edge.
            var h = new Graph<string>();
            h.AddEdge("a", "b", double.PositiveInfinity);
            Check.False(ShortestPath.Dijkstra(h, "a", "b").Found, "infinite edge");
            Check.False(GridPathfinding.Dijkstra(1, 2, new Cell(0, 0), new Cell(0, 1), (row, col) => true,
                (row, col) => double.PositiveInfinity).Found, "infinite cost");
        }

        public void Intervals_RejectNaNAndFormatInvariantly()
        {
            Check.Equal("start", Check.Throws<ArgumentException>(() => new Interval<double>(double.NaN, 1)).ParamName);
            Check.Equal("end", Check.Throws<ArgumentException>(() => new Interval<float>(0, float.NaN)).ParamName);
            Check.Equal("start", Check.Throws<ArgumentException>(() => new RangeMap<double, int>().Set(double.NaN, 1, 5)).ParamName);
            Check.Equal("end", Check.Throws<ArgumentException>(() => new RangeMap<double, int>().Remove(0, double.NaN)).ParamName);

            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Check.Equal("[1.5, 2.5)", new Interval<double>(1.5, 2.5).ToString());
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        public void Graph_NullVerticesAreNamed()
        {
            var g = new Graph<string>();
            Check.Equal("to", Check.Throws<ArgumentNullException>(() => g.AddEdge("a", null!)).ParamName);
            Check.Equal(0, g.VertexCount);
            Check.Equal("from", Check.Throws<ArgumentNullException>(() => g.AddEdge(null!, "a")).ParamName);
            Check.False(g.ContainsVertex(null!), "ContainsVertex(null)");
            g.AddVertex("a");
            Check.Equal("source", Check.Throws<ArgumentNullException>(() => ShortestPath.BreadthFirst(g, null!, "a")).ParamName);
            Check.Equal("target", Check.Throws<ArgumentNullException>(() => ShortestPath.Dijkstra(g, "a", null!)).ParamName);
        }

        public void Grids_ValidateConnectivityAndLowerBounds()
        {
            foreach (Action a in new Action[]
            {
                () => FloodFill.Region(new int[2, 2], 0, 0, (Connectivity)3),
                () => FloodFill.Fill(new int[2, 2], 0, 0, 0, (Connectivity)3),
                () => FloodFill.Region(2, 2, 0, 0, (r, c) => true, (Connectivity)0),
                () => ConnectedComponents2D.Label(new bool[2, 2], (Connectivity)3),
                () => ConnectedComponents2D.LabelByValue(new int[2, 2], (Connectivity)0),
                () => GridPathfinding.BreadthFirst(new bool[2, 2], new Cell(0, 0), new Cell(1, 1), (Connectivity)3),
                () => GridPathfinding.AStar(2, 2, new Cell(0, 0), new Cell(1, 1), (r, c) => true, (Connectivity)3),
                () => GridPathfinding.Dijkstra(2, 2, new Cell(0, 0), new Cell(1, 1), (r, c) => true, (r, c) => 1, (Connectivity)3),
            })
                Check.Equal("connectivity", Check.Throws<ArgumentOutOfRangeException>(a).ParamName);

            var grid = (int[,])Array.CreateInstance(typeof(int), new[] { 2, 2 }, new[] { 1, 1 });
            var mask = (bool[,])Array.CreateInstance(typeof(bool), new[] { 2, 2 }, new[] { 1, 1 });
            Check.Equal("grid", Check.Throws<ArgumentException>(() => FloodFill.Region(grid, 0, 0)).ParamName);
            Check.Equal("grid", Check.Throws<ArgumentException>(() => FloodFill.Fill(grid, 0, 0, 5)).ParamName);
            Check.Equal("mask", Check.Throws<ArgumentException>(() => ConnectedComponents2D.Label(mask)).ParamName);
            Check.Equal("grid", Check.Throws<ArgumentException>(() => ConnectedComponents2D.LabelByValue(grid)).ParamName);
            Check.Equal("passable", Check.Throws<ArgumentException>(() => GridPathfinding.BreadthFirst(mask, new Cell(0, 0), new Cell(1, 1))).ParamName);
            Check.Equal("passable", Check.Throws<ArgumentException>(() => GridPathfinding.AStar(mask, new Cell(0, 0), new Cell(1, 1))).ParamName);
        }

        public sealed class Arrays { public int[] Numbers { get; set; } = new int[2]; public ExpandoObject Bag { get; set; } = new ExpandoObject(); }
        public sealed class Typed { public int N { get; set; } }

        public void PathsAndAccessors_ReportTheirArguments()
        {
            var e = Check.Throws<ArgumentException>(() => PropertyPath.Set(new Arrays(), "Numbers[0]", "text"));
            Check.Equal("value", e.ParamName);

            var holder = new Arrays();
            PropertyPath.Set(holder, "Bag[k]", 7);
            Check.Equal((object)7, PropertyPath.Get(holder, "Bag[k]"));
            Check.Throws<KeyNotFoundException>(() => PropertyPath.Get(holder, "Bag[missing]"));

            Check.Equal("TValue", Check.Throws<ArgumentException>(() => ExpressionAccessor.Getter<Typed, Guid>("N")).ParamName);
            Check.Equal("TValue", Check.Throws<ArgumentException>(() => ExpressionAccessor.Setter<Typed, Guid>("N")).ParamName);
            Check.Equal("flat", Check.Throws<ArgumentException>(() =>
                UnflattenObject.Unflatten(new[] { new KeyValuePair<string, object?>(null!, 1) })).ParamName);
        }
    }
}
