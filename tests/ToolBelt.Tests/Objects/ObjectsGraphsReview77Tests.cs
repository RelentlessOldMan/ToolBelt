using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Functional;
using ToolBelt.Graphs;
using ToolBelt.Grids;
using ToolBelt.Intervals;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Round 77 (objects, functional, grids, graphs, intervals, fifth pass): each failed on the code before its fix.
    public sealed class ObjectsGraphsReview77Tests
    {
        // Runs action on a worker, failing if it has not finished within the limit (a walk that never ends).
        private static void Ends(Action action, string what, int seconds = 30)
        {
            Exception? error = null;
            var task = Task.Run(() => { try { action(); } catch (Exception e) { error = e; } });
            Check.True(task.Wait(TimeSpan.FromSeconds(seconds)), what + " did not finish");
            if (error != null) throw new InvalidOperationException(what + " threw " + error.GetType().Name + ": " + error.Message, error);
        }

        // ---- C1: a diagonal step whose cost overflows is still a move ----

        public void Dijkstra_ADiagonalStepWithAHugeFiniteCostIsAMove()
        {
            // Only the diagonal is passable; entering row 1 or 2 costs 1.5e308, times sqrt(2) overflows.
            var path = GridPathfinding.Dijkstra(3, 3, new Cell(0, 0), new Cell(2, 2), (r, c) => r == c,
                (r, c) => r == 0 ? 1.0 : 1.5e308, Connectivity.Eight);
            Check.True(path.Found, "the diagonal path");
            Check.Equal(3, path.Cells.Count);
            var small = GridPathfinding.Dijkstra(2, 2, new Cell(0, 0), new Cell(1, 1), (r, c) => r == c,
                (r, c) => 1.5e308, Connectivity.Eight);
            Check.True(small.Found, "2x2");
            // An infinite entry cost is still no move.
            var blocked = GridPathfinding.Dijkstra(2, 2, new Cell(0, 0), new Cell(1, 1), (r, c) => r == c,
                (r, c) => r == 1 ? double.PositiveInfinity : 1.0, Connectivity.Eight);
            Check.False(blocked.Found, "infinite cost");
        }

        // ---- C2: generic-only dictionaries compare and flatten by key ----

        private static ExpandoObject Expando(params (string Key, object? Value)[] members)
        {
            var e = new ExpandoObject();
            var d = (IDictionary<string, object?>)e;
            foreach (var (k, v) in members) d[k] = v;
            return e;
        }

        public sealed class HasExpando
        {
            public ExpandoObject? E { get; set; }
        }

        public void Walkers_ExpandoObjectsCompareByKeyNotOrder()
        {
            var a = new HasExpando { E = Expando(("a", 1), ("b", 2)) };
            var b = new HasExpando { E = Expando(("b", 2), ("a", 1)) };
            Check.True(DeepEquals.Equals(a, b), "same members, other order");
            Check.Equal(0, PropertyDiff.Compare(a, b).Count, "no differences");
            var c = new HasExpando { E = Expando(("b", 2), ("a", 3)) };
            Check.False(DeepEquals.Equals(a, c));
            Check.Equal("E[a]", string.Join(",", PropertyDiff.Compare(a, c).Select(d => d.Path)));
            var flat = FlattenObject.Flatten(a);
            Check.Equal(1, flat["E[a]"]);
            Check.Equal(2, flat["E[b]"]);
            Check.Equal(1, PropertyPath.Get(a, "E[a]"));
        }

        // ---- C3, C10, C11, C12: PropertyPath keys, ParamName, empty segments ----

        public sealed class Keyed
        {
            public Dictionary<double, string> D { get; } = new Dictionary<double, string> { [15] = "fifteen", [1.5] = "one and a half" };
            public Dictionary<decimal, string> M { get; } = new Dictionary<decimal, string> { [15m] = "fifteen", [1.5m] = "one and a half" };
            public Dictionary<TimeSpan, int> T { get; } = new Dictionary<TimeSpan, int> { [TimeSpan.FromHours(1)] = 1 };
            public Dictionary<DateTimeOffset, int> O { get; } = new Dictionary<DateTimeOffset, int> { [new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2))] = 2 };
            public Dictionary<Version, int> V { get; } = new Dictionary<Version, int> { [new Version(1, 2, 3)] = 3 };
            public Dictionary<Uri, int> U { get; } = new Dictionary<Uri, int> { [new Uri("http://x/y")] = 4 };
            public Dictionary<int, int> I { get; } = new Dictionary<int, int> { [1] = 1 };
            public List<int> L { get; } = new List<int> { 1, 2 };
            public int A { get; set; } = 7;
            public Keyed? Next { get; set; }
        }

        public void PropertyPath_FloatingKeysTakeNoThousandsSeparators()
        {
            var k = new Keyed();
            Check.False(PropertyPath.TryGet(k, "D[1,5]", out _), "D[1,5] is not 15");
            Check.False(PropertyPath.TryGet(k, "M[1,5]", out _), "M[1,5] is not 15");
            Check.Equal("one and a half", PropertyPath.Get(k, "D[1.5]"));
            Check.Equal("one and a half", PropertyPath.Get(k, "M[1.5]"));
            Check.Equal("fifteen", PropertyPath.Get(k, "D[15]"));
        }

        public void PropertyPath_ConvertsTimeSpanOffsetVersionAndUriKeys()
        {
            var k = new Keyed();
            Check.Equal(1, PropertyPath.Get(k, "T[01:00:00]"));
            Check.Equal(2, PropertyPath.Get(k, "O[2020-01-02T03:04:05+02:00]"));
            Check.Equal(3, PropertyPath.Get(k, "V[1.2.3]"));
            Check.Equal(4, PropertyPath.Get(k, "U[http://x/y]"));
        }

        public void PropertyPath_ArgumentErrorsNamePath()
        {
            var k = new Keyed();
            Check.Equal("path", Check.Throws<ArgumentOutOfRangeException>(() => PropertyPath.Get(k, "L[5]")).ParamName);
            Check.Equal("path", Check.Throws<ArgumentException>(() => PropertyPath.Get(k, "I[x]")).ParamName);
        }

        public void PropertyPath_RejectsEmptySegments()
        {
            var k = new Keyed { Next = new Keyed() };
            foreach (string bad in new[] { "Next..A", ".A", "A.", "Next.", "Next.[0]" })
            {
                var ex = Check.Throws<ArgumentException>(() => PropertyPath.Get(k, bad), bad);
                Check.Equal("path", ex.ParamName, bad);
                Check.False(PropertyPath.TryGet(k, bad, out _), bad);
            }
            Check.Equal(7, PropertyPath.Get(k, "Next.A"));
            Check.Equal(2, PropertyPath.Get(k, "L[1]"));
        }

        // ---- C4: dictionary keys print as they parse back ----

        public sealed class DateKeyed
        {
            public Dictionary<DateTime, int> D { get; } = new Dictionary<DateTime, int>();
        }

        public sealed class DoubleKeyed
        {
            public Dictionary<double, int> D { get; } = new Dictionary<double, int>();
        }

        public void Flatten_DateKeysAMillisecondApartStayDistinctAndReadBack()
        {
            var t = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var o = new DateKeyed();
            o.D[t] = 2;
            o.D[t.AddMilliseconds(6)] = 3;
            var flat = FlattenObject.Flatten(o);
            Check.Equal(2, flat.Count);
            foreach (var pair in flat)
                Check.Equal(pair.Value, PropertyPath.Get(o, pair.Key), pair.Key);
            var a = new DateKeyed(); a.D[t] = 1; a.D[t.AddMilliseconds(6)] = 2;
            var b = new DateKeyed(); b.D[t] = 1; b.D[t.AddMilliseconds(6)] = 3;
            string path = PropertyDiff.Compare(a, b).Single().Path;
            Check.Equal(3, PropertyPath.Get(b, path), path);
        }

        public void Flatten_RandomDoubleKeysReadBackThroughPropertyPath()
        {
            var rng = new Random(77);
            var o = new DoubleKeyed();
            for (int i = 0; i < 500; i++)
            {
                double key = rng.Next(4) switch
                {
                    0 => rng.NextDouble(),
                    1 => BitConverter.Int64BitsToDouble((long)(rng.NextDouble() * long.MaxValue)),
                    2 => 0.1 * rng.Next(1000) + 0.2,
                    _ => -rng.NextDouble() * 1e300,
                };
                if (double.IsNaN(key) || double.IsInfinity(key) || o.D.ContainsKey(key)) continue;
                o.D[key] = i;
            }
            o.D[0.3] = -1;
            if (!o.D.ContainsKey(0.1 + 0.2)) o.D[0.1 + 0.2] = -2;
            var flat = FlattenObject.Flatten(o);
            Check.Equal(o.D.Count, flat.Count);
            foreach (var pair in flat)
                Check.Equal(pair.Value, PropertyPath.Get(o, pair.Key), pair.Key);
        }

        // ---- C5, C6, C14, C15: TypeUtils ----

        public void TryConvert_DoesNotLoseInformationSilently()
        {
            Check.True(TypeUtils.TryConvert(0.1 + 0.2, typeof(decimal), out object? m));
            Check.Equal(0.30000000000000004m, (decimal)m!);
            Check.True(TypeUtils.TryConvert(123456789012345678.0, typeof(decimal), out m));
            Check.Equal(123456789012345680m, (decimal)m!);
            Check.False(TypeUtils.TryConvert(1e-30, typeof(decimal), out _), "1e-30 to decimal");
            Check.False(TypeUtils.TryConvert(1e30, typeof(decimal), out _), "1e30 to decimal");
            Check.True(TypeUtils.TryConvert(0.0, typeof(decimal), out m));
            Check.Equal(0m, (decimal)m!);
            Check.True(TypeUtils.TryConvert(2.5f, typeof(decimal), out m));
            Check.Equal(2.5m, (decimal)m!);

            var t = new DateTime(2020, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            Check.True(TypeUtils.TryConvert(t, typeof(string), out object? s));
            Check.Equal(t, DateTime.Parse((string)s!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
            var dto = new DateTimeOffset(2020, 1, 2, 3, 4, 5, 6, TimeSpan.FromHours(-3));
            Check.True(TypeUtils.TryConvert(dto, typeof(string), out s));
            Check.True(TypeUtils.TryConvert(s, typeof(DateTimeOffset), out object? back));
            Check.Equal(dto, (DateTimeOffset)back!);
            Check.Equal(dto.Offset, ((DateTimeOffset)back!).Offset);

            Check.False(TypeUtils.TryConvert("1e-400", typeof(double), out _), "1e-400 underflows");
            Check.False(TypeUtils.TryConvert("1e-50", typeof(float), out _), "1e-50 underflows float");
            Check.False(TypeUtils.TryConvert("1e-40", typeof(decimal), out _), "1e-40 underflows decimal");
            Check.False(TypeUtils.TryConvert(1e-50, typeof(float), out _), "1e-50 to float");
            Check.True(TypeUtils.TryConvert("0e10", typeof(double), out object? z));
            Check.Equal(0.0, (double)z!);
            Check.True(TypeUtils.TryConvert("0.000", typeof(float), out z));

            Check.False(TypeUtils.TryConvert((1L << 53) + 1, typeof(double), out _), "2^53 + 1");
            Check.True(TypeUtils.TryConvert(1L << 53, typeof(double), out object? d));
            Check.Equal((double)(1L << 53), (double)d!);
            Check.False(TypeUtils.TryConvert(long.MaxValue, typeof(double), out _), "long.MaxValue");
            Check.True(TypeUtils.TryConvert(long.MinValue, typeof(double), out _), "long.MinValue is -2^63");
            Check.False(TypeUtils.TryConvert(ulong.MaxValue, typeof(double), out _), "ulong.MaxValue");
            Check.False(TypeUtils.TryConvert(16777217, typeof(float), out _), "2^24 + 1 to float");
            Check.True(TypeUtils.TryConvert(16777216, typeof(float), out _), "2^24 to float");
            Check.False(TypeUtils.TryConvert(int.MaxValue, typeof(float), out _), "int.MaxValue to float");
            Check.False(TypeUtils.TryConvert(uint.MaxValue, typeof(float), out _), "uint.MaxValue to float");
            Check.True(TypeUtils.TryConvert(int.MaxValue, typeof(double), out _), "int to double is exact");
        }

        public void TryConvert_MinusZeroTextIsNegativeZero()
        {
            Check.True(TypeUtils.TryConvert("-0", typeof(double), out object? d));
            Check.True(double.IsNegativeInfinity(1 / (double)d!), "double -0");
            Check.True(TypeUtils.TryConvert(" -0.0 ", typeof(double), out d));
            Check.True(double.IsNegativeInfinity(1 / (double)d!), "double -0.0");
            Check.True(TypeUtils.TryConvert("-0", typeof(float), out object? f));
            Check.True(float.IsNegativeInfinity(1 / (float)f!), "float -0");
            Check.True(TypeUtils.TryConvert(-0.0, typeof(string), out object? s));
            Check.Equal("-0", s);
            Check.True(TypeUtils.TryConvert(-0.0f, typeof(string), out s));
            Check.Equal("-0", s);
            var keyed = new DoubleKeyed();
            keyed.D[-0.0] = 1;
            Check.Equal("D[-0]", FlattenObject.Flatten(keyed).Keys.Single());
            Check.True(TypeUtils.TryConvert(-0.0, typeof(string), out s));
            Check.True(TypeUtils.TryConvert(s, typeof(double), out d));
            Check.True(double.IsNegativeInfinity(1 / (double)d!), "round trip of -0.0");
        }

        public void FriendlyName_RankOneMultiDimensionalArrayIsNotAVector()
        {
            Check.Equal("int[*]", TypeUtils.FriendlyName(typeof(int).MakeArrayType(1)));
            Check.Equal("int[]", TypeUtils.FriendlyName(typeof(int[])));
            Check.Equal("int[*][]", TypeUtils.FriendlyName(typeof(int[]).MakeArrayType(1)));
            Check.Equal("int[,]", TypeUtils.FriendlyName(typeof(int[,])));
        }

#if NET5_0_OR_GREATER
        public void TryConvert_HalfConvertsToOtherNumbers()
        {
            Half h = (Half)0.1;
            Check.True(TypeUtils.TryConvert(h, typeof(double), out object? d), "Half to double");
            Check.Equal((double)h, (double)d!);
            Check.True(TypeUtils.TryConvert(h, typeof(float), out object? f), "Half to float");
            Check.Equal((float)h, (float)f!);
            Check.True(TypeUtils.TryConvert((Half)3, typeof(int), out object? i), "Half 3 to int");
            Check.Equal(3, (int)i!);
            Check.False(TypeUtils.TryConvert((Half)2.5, typeof(int), out _), "Half 2.5 to int");
        }
#endif

        // ---- C7: typed setter, null Nullable into a value member ----

        public sealed class NP
        {
            public int V { get; set; } = 5;
            public int F = 5;
        }

        public void TypedSetter_NullNullableStoresTheDefault()
        {
            var o = new NP();
            ExpressionAccessor.Setter<NP, int?>("V")(o, null);
            Check.Equal(0, o.V);
            ExpressionAccessor.Setter<NP, int?>("F")(o, null);
            Check.Equal(0, o.F);
            ExpressionAccessor.Setter<NP, int?>("V")(o, 4);
            Check.Equal(4, o.V);
        }

        // ---- C8: a field hiding a property is one member ----

        public class FB
        {
            public int X { get; set; } = 1;
        }

        public sealed class FD : FB
        {
            public new string X = "field";
        }

        public class GB
        {
            public int Y = 1;
        }

        public sealed class GD : GB
        {
            public new string Y { get; set; } = "property";
        }

        public void Walkers_AFieldHidingAPropertyIsOneMember()
        {
            var flat = FlattenObject.Flatten(new FD(), includeFields: true);
            Check.Equal("field", flat["X"]);
            Check.Equal(1, flat.Count);
            var flat2 = FlattenObject.Flatten(new GD(), includeFields: true);
            Check.Equal("property", flat2["Y"]);
            Check.Equal(1, flat2.Count);

            var opt = new ObjectComparisonOptions { IncludeFields = true };
            var a = new FD();
            var b = new FD();
            ((FB)b).X = 2; // the hidden member
            Check.True(DeepEquals.Equals(a, b, opt), "only the hidden member differs");
            Check.Equal(0, PropertyDiff.Compare(a, b, opt).Count);
            b.X = "other";
            Check.Equal(1, PropertyDiff.Compare(a, b, opt).Count);

            Check.Equal("field", ExpressionAccessor.GetValue(new FD(), "X"));
            Check.Equal("property", ExpressionAccessor.GetValue(new GD(), "Y"));
            // PropertyPath reads properties only: the field hides the property, so there is none.
            Check.False(PropertyPath.TryGet(new FD(), "X", out _), "PropertyPath X");
            Check.Equal("property", PropertyPath.Get(new GD(), "Y"));
        }

        // ---- C9: ActivatorUtils prefers int? to ValueType ----

        public sealed class Overloads
        {
            public string Chosen { get; }
            public Overloads(int? value) => Chosen = "int?";
            public Overloads(ValueType value) => Chosen = "ValueType";
        }

        public sealed class Overloads2
        {
            public string Chosen { get; }
            public Overloads2(int? value) => Chosen = "int?";
            public Overloads2(int value) => Chosen = "int";
        }

        public void Activator_PrefersNullableOfTheArgumentToValueType()
        {
            Check.Equal("int?", ((Overloads)ActivatorUtils.Create(typeof(Overloads), 5)).Chosen);
            Check.Equal("int", ((Overloads2)ActivatorUtils.Create(typeof(Overloads2), 5)).Chosen);
        }

        // ---- C13: invariant ToString ----

        public void ToStrings_AreInvariant()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Check.Equal("a -> b (1.5)", new Edge<string>("a", "b", 1.5).ToString());
                Check.Equal("Some(1.5)", Option<double>.Some(1.5).ToString());
                Check.Equal("Success(1.5)", Result<double>.Success(1.5).ToString());
                Check.Equal("Right(1.5)", Either<int, double>.FromRight(1.5).ToString());
                Check.Equal("Left(1.5)", Either<double, int>.FromLeft(1.5).ToString());
                var diff = PropertyDiff.Compare(new { X = 1.5 }, new { X = 2.5 }).Single();
                Check.Equal("X: 1.5 -> 2.5", diff.ToString());
                var close = PropertyDiff.Compare(new { X = 0.3 }, new { X = 0.1 + 0.2 }).Single();
                Check.Equal("X: 0.3 -> 0.30000000000000004", close.ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        // ---- R1, R2: tasks, Lazy and streams are leaves ----

        public sealed class HoldTask
        {
            public Task<int>? T { get; set; }
        }

        public sealed class HoldLazy
        {
            public int Runs;
            public Lazy<int> L { get; }
            public HoldLazy() => L = new Lazy<int>(() => ++Runs);
        }

        public sealed class HoldStream
        {
            public Stream S { get; set; } = new MemoryStream();
            public TextReader R { get; set; } = new StringReader("x");
        }

        public void Walkers_DoNotReadAPendingTask()
        {
            var pending = new TaskCompletionSource<int>().Task;
            var a = new HoldTask { T = pending };
            var b = new HoldTask { T = new TaskCompletionSource<int>().Task };
            Ends(() => Check.False(DeepEquals.Equals(a, b)), "DeepEquals", 10);
            Ends(() => Check.True(DeepEquals.Equals(a, new HoldTask { T = pending })), "DeepEquals same task", 10);
            Ends(() => Check.Equal(1, PropertyDiff.Compare(a, b).Count), "PropertyDiff", 10);
            Ends(() => Check.True(ReferenceEquals(pending, FlattenObject.Flatten(a)["T"])), "Flatten", 10);
        }

        public void Walkers_DoNotRunALazyFactory()
        {
            var a = new HoldLazy();
            var b = new HoldLazy();
            DeepEquals.Equals(a, b);
            PropertyDiff.Compare(a, b);
            FlattenObject.Flatten(a);
            Check.Equal(0, a.Runs);
            Check.Equal(0, b.Runs);
        }

        public void Walkers_TreatStreamsAndReadersAsLeaves()
        {
            var a = new HoldStream();
            var flat = FlattenObject.Flatten(a);
            Check.True(ReferenceEquals(a.S, flat["S"]));
            Check.True(ReferenceEquals(a.R, flat["R"]));
            Check.True(DeepEquals.Equals(a, a));
            Check.Equal(2, PropertyDiff.Compare(a, new HoldStream()).Count);
        }

        // ---- R3, R4, R5: walks that would never end ----

        public sealed class CachedChain
        {
            private CachedChain? _next;
            public int V { get; set; }
            public CachedChain Next => _next ??= new CachedChain { V = V + 1 };
        }

        public sealed class W<T>
        {
            public int V { get; set; }
            public W<W<T>> Next => new W<W<T>>();
        }

        public sealed class Endless
        {
            public IEnumerable<int> Numbers { get; } = Count();

            private static IEnumerable<int> Count()
            {
                for (int i = 0; ; i++) yield return 0;
            }
        }

        // Each walk reaches the 2,097,152-object budget, which takes seconds (more on .NET Framework).
        public void Walkers_ALazilyCachedEndlessChainThrows()
        {
            Ends(() => Check.Throws<InvalidOperationException>(() => DeepEquals.Equals(new CachedChain(), new CachedChain())), "DeepEquals", 120);
            Ends(() => Check.Throws<InvalidOperationException>(() => PropertyDiff.Compare(new CachedChain(), new CachedChain())), "PropertyDiff", 120);
            Ends(() => Check.Throws<InvalidOperationException>(() => FlattenObject.Flatten(new CachedChain())), "Flatten", 120);
        }

        public void Walkers_AGenericNestingChainEnds()
        {
            Ends(() => Check.True(DeepEquals.Equals(new W<int>(), new W<int>())), "DeepEquals");
            Ends(() => Check.Equal(0, PropertyDiff.Compare(new W<int>(), new W<int>()).Count), "PropertyDiff");
            Ends(() => FlattenObject.Flatten(new W<int>()), "Flatten");
        }

        public void Walkers_AnEndlessLazySequenceThrows()
        {
            Ends(() => Check.Throws<InvalidOperationException>(() => DeepEquals.Equals(new Endless(), new Endless())), "DeepEquals");
            Ends(() => Check.Throws<InvalidOperationException>(() => PropertyDiff.Compare(new Endless(), new Endless())), "PropertyDiff");
            Ends(() => Check.Throws<InvalidOperationException>(() => FlattenObject.Flatten(new Endless())), "Flatten");
        }

        // ---- R6: a shared differing pair is walked once ----

        public sealed class Diamond
        {
            public int V { get; set; }
            public Diamond? L { get; set; }
            public Diamond? R { get; set; }
        }

        private static Diamond Chain(int depth, int bottom)
        {
            var node = new Diamond { V = bottom };
            for (int i = 0; i < depth; i++) node = new Diamond { L = node, R = node };
            return node;
        }

        public void PropertyDiff_ADiamondChainWithADifferenceAtTheBottomIsLinear()
        {
            IReadOnlyList<Difference>? diffs = null;
            Ends(() => diffs = PropertyDiff.Compare(Chain(40, 1), Chain(40, 2)), "depth 40", 20);
            Check.Equal(1, diffs!.Count);
            Check.Equal(string.Concat(Enumerable.Repeat("L.", 40)) + "V", diffs[0].Path);
        }

        // ---- R7, R8: PropertyPath array bounds and indexer exceptions ----

        public sealed class Bounded
        {
            public Array A { get; } = Array.CreateInstance(typeof(int), new[] { 3 }, new[] { 5 });
        }

        public void PropertyPath_IndexesAnArrayFromItsLowerBound()
        {
            var o = new Bounded();
            o.A.SetValue(42, 6);
            Check.Equal(42, PropertyPath.Get(o, "A[6]"));
            Check.True(PropertyPath.TryGet(o, "A[5]", out object? v));
            Check.Equal(0, v);
            Check.False(PropertyPath.TryGet(o, "A[0]", out _), "below the lower bound");
            Check.False(PropertyPath.TryGet(o, "A[8]", out _), "past the end");
            PropertyPath.Set(o, "A[7]", 9);
            Check.Equal(9, o.A.GetValue(7));
            Check.Equal("path", Check.Throws<ArgumentOutOfRangeException>(() => PropertyPath.Set(o, "A[2]", 1)).ParamName);
        }

        public sealed class BadList : List<int>, IList
        {
            object? IList.this[int index]
            {
                get => throw new InvalidOperationException("indexer bug");
                set => throw new InvalidOperationException("indexer bug");
            }
        }

        public sealed class HoldBad
        {
            public BadList L { get; } = new BadList { 1 };
        }

        public void PropertyPath_AnIndexersOwnExceptionPropagates()
        {
            var ex = Check.Throws<InvalidOperationException>(() => PropertyPath.TryGet(new HoldBad(), "L[0]", out _));
            Check.Equal("indexer bug", ex.Message);
            Check.Throws<InvalidOperationException>(() => PropertyPath.Get(new HoldBad(), "L[0]"));
        }

        // ---- R9, R10: Memoize reentrancy, Result.Try with a null message ----

        public void Memoize_AReentrantFactoryThrows()
        {
            Func<int>? f = null;
            f = Memoize.Factory(() => f!() + 1);
            Check.Throws<InvalidOperationException>(() => f());
            Func<int, int>? g = null;
            g = Memoize.Function<int, int>(n => n <= 0 ? 0 : g!(n));
            Check.Throws<InvalidOperationException>(() => g(3));
            Func<int, int>? h = null;
            h = Memoize.ThreadSafe<int, int>(n => n <= 0 ? 0 : h!(n));
            Check.Throws<InvalidOperationException>(() => h(3));
            // A recursion on other arguments is fine, and a failed call does not poison the argument.
            Func<int, long>? fib = null;
            fib = Memoize.Function<int, long>(n => n < 2 ? n : fib!(n - 1) + fib!(n - 2));
            Check.Equal(832040L, fib(30));
            Func<int, long>? fibSafe = null;
            fibSafe = Memoize.ThreadSafe<int, long>(n => n < 2 ? n : fibSafe!(n - 1) + fibSafe!(n - 2));
            Check.Equal(832040L, fibSafe(30));
            int calls = 0;
            var once = Memoize.Factory(() => { if (++calls == 1) throw new FormatException(); return 7; });
            Check.Throws<FormatException>(() => once());
            Check.Equal(7, once());
        }

        public sealed class NullMessage : Exception
        {
            public override string Message => null!;
        }

        public void Result_TryCapturesAnExceptionWithANullMessage()
        {
            var r = Result.Try<int>(() => throw new NullMessage());
            Check.True(r.IsFailure);
            Check.True(r.Error.Contains(nameof(NullMessage)), r.Error);
        }

        // ---- R11: A* on an open grid ----

        public void AStar_OnAnOpenGridExpandsAboutThePath()
        {
            long calls = 0;
            var path = GridPathfinding.AStar(1000, 1000, new Cell(0, 0), new Cell(999, 999), (r, c) => { calls++; return true; });
            Check.True(path.Found);
            Check.Equal(1998.0, path.Cost);
            Check.True(calls < 50_000, "passable calls " + calls);
            calls = 0;
            var eight = GridPathfinding.AStar(1000, 1000, new Cell(0, 0), new Cell(999, 500), (r, c) => { calls++; return true; }, Connectivity.Eight);
            Check.True(eight.Found);
            Check.True(calls < 100_000, "8-connected passable calls " + calls);
        }

        public void AStar_StaysOptimalAgainstDijkstraOnRandomGrids()
        {
            var rng = new Random(7711);
            for (int trial = 0; trial < 200; trial++)
            {
                int rows = rng.Next(1, 12), cols = rng.Next(1, 12);
                var open = new bool[rows, cols];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++) open[r, c] = rng.NextDouble() < 0.75;
                var s = new Cell(rng.Next(rows), rng.Next(cols));
                var g = new Cell(rng.Next(rows), rng.Next(cols));
                open[s.Row, s.Col] = open[g.Row, g.Col] = true;
                var conn = rng.Next(2) == 0 ? Connectivity.Four : Connectivity.Eight;
                var a = GridPathfinding.AStar(rows, cols, s, g, (r, c) => open[r, c], conn);
                var d = GridPathfinding.Dijkstra(rows, cols, s, g, (r, c) => open[r, c], (r, c) => 1.0, conn);
                Check.Equal(d.Found, a.Found, "trial " + trial);
                if (a.Found) Check.Close(d.Cost, a.Cost, 1e-9, "trial " + trial);
            }
        }

        // ---- R12: FloodFill on a large T[,] keeps a dense map ----

#if NET5_0_OR_GREATER
        public void FloodFill_ALargeGridDoesNotSwitchToASet()
        {
            var grid = new byte[4097, 4097];
            long before = GC.GetAllocatedBytesForCurrentThread();
            int filled = FloodFill.Fill(grid, 0, 0, (byte)1);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal(4097 * 4097, filled);
            Check.True(allocated < 700L << 20, "allocated " + (allocated >> 20) + " MB");
        }
#endif

        public void FloodFill_RegionAgreesWithANaiveSearch()
        {
            var rng = new Random(7712);
            for (int trial = 0; trial < 100; trial++)
            {
                int rows = rng.Next(1, 15), cols = rng.Next(1, 15);
                var grid = new int[rows, cols];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++) grid[r, c] = rng.Next(2);
                int sr = rng.Next(rows), sc = rng.Next(cols);
                var conn = rng.Next(2) == 0 ? Connectivity.Four : Connectivity.Eight;
                var got = new HashSet<(int, int)>(FloodFill.Region(grid, sr, sc, conn).Select(x => (x.Row, x.Col)));
                // Naive: repeat until no cell joins.
                var want = new HashSet<(int, int)> { (sr, sc) };
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            if (want.Contains((r, c)) || grid[r, c] != grid[sr, sc]) continue;
                            for (int dr = -1; dr <= 1; dr++)
                                for (int dc = -1; dc <= 1; dc++)
                                {
                                    if ((dr == 0 && dc == 0) || (conn == Connectivity.Four && dr != 0 && dc != 0)) continue;
                                    if (want.Contains((r + dr, c + dc))) { want.Add((r, c)); grew = true; dr = 2; break; }
                                }
                        }
                }
                Check.True(want.SetEquals(got), "trial " + trial);
            }
        }

        // ---- R13: RangeMap in descending order ----

        public void RangeMap_DescendingInsertsAreNotQuadratic()
        {
            const int n = 300_000;
            var map = new RangeMap<int, int>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = n - 1; i >= 0; i--) map.Set(2 * i, 2 * i + 1, i);
            watch.Stop();
            Check.Equal(n, map.SegmentCount);
            Check.True(watch.Elapsed < TimeSpan.FromSeconds(4), "descending took " + watch.Elapsed);
            Check.True(map.TryGet(2 * 1234, out int v) && v == 1234);
            Check.False(map.TryGet(2 * 1234 + 1, out _));
        }

        public void RangeMap_AgreesWithANaiveArrayOverRandomEdits()
        {
            var rng = new Random(7713);
            for (int trial = 0; trial < 40; trial++)
            {
                const int span = 3000;
                var naive = new int?[span];
                var map = new RangeMap<int, int>();
                for (int op = 0; op < 1500; op++)
                {
                    int a = rng.Next(span), b = Math.Min(span, a + rng.Next(1, rng.Next(2) == 0 ? 5 : 400));
                    if (rng.Next(5) == 0)
                    {
                        map.Remove(a, b);
                        for (int k = a; k < b; k++) naive[k] = null;
                    }
                    else
                    {
                        int value = rng.Next(3);
                        map.Set(a, b, value);
                        for (int k = a; k < b; k++) naive[k] = value;
                    }
                }
                for (int k = 0; k < span; k++)
                {
                    bool has = map.TryGet(k, out int got);
                    Check.Equal(naive[k].HasValue, has, "trial " + trial + " key " + k);
                    if (has) Check.Equal(naive[k]!.Value, got, "trial " + trial + " key " + k);
                }
                // Minimal: no two adjacent segments share a value, and they are sorted and disjoint.
                var segs = map.Segments.ToList();
                for (int s = 1; s < segs.Count; s++)
                {
                    Check.True(segs[s - 1].Key.End.CompareTo(segs[s].Key.Start) <= 0, "sorted");
                    Check.False(segs[s - 1].Key.End == segs[s].Key.Start && segs[s - 1].Value == segs[s].Value, "coalesced");
                }
            }
        }

        public void RangeMap_ChangingWhileEnumeratingThrows()
        {
            var map = new RangeMap<int, int>();
            map.Set(0, 1, 1);
            map.Set(2, 3, 2);
            Check.Throws<InvalidOperationException>(() =>
            {
                foreach (var _ in map.Segments) map.Set(10, 11, 3);
            });
        }

        // ---- R15: a set comparer's exception is unwrapped ----

        public sealed class Touchy : IEqualityComparer<string>
        {
            public bool Armed;
            public bool Equals(string? x, string? y) => Armed ? throw new NotSupportedException("comparer") : x == y;
            public int GetHashCode(string s) => Armed ? throw new NotSupportedException("comparer") : s.Length;
        }

        public sealed class HoldSet
        {
            public HashSet<string>? S { get; set; }
        }

        public void Walkers_ASetComparersExceptionIsItself()
        {
            var touchy = new Touchy();
            var a = new HoldSet { S = new HashSet<string>(touchy) { "x", "y" } };
            var b = new HoldSet { S = new HashSet<string> { "x", "z" } };
            touchy.Armed = true;
            Check.Throws<NotSupportedException>(() => DeepEquals.Equals(a, b));
            Check.Throws<NotSupportedException>(() => PropertyDiff.Compare(a, b));
        }
    }
}
