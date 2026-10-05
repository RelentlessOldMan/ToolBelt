using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Numerics;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>Reservoir / weighted / stratified sampling, sorted-list search, Pairwise and Unflatten.</summary>
    public sealed class SamplingSortedPairwiseTests
    {
        // ---------- reservoir ----------

        public void Reservoir_FillsThenStaysAtCapacity()
        {
            var r = new ReservoirSampler<int>(5, new DeterministicRandom(1));
            r.AddRange(new[] { 1, 2, 3 });
            Check.Equal("1,2,3", string.Join(",", r.Sample));
            r.AddRange(Enumerable.Range(4, 10000));
            Check.Equal(5, r.Sample.Count);
            Check.Equal(10003L, r.Count);
            Check.Equal(5, r.Sample.Distinct().Count());
        }

        public void Reservoir_IsUniform()
        {
            // Every one of 20 items should land in a size-5 sample with probability 1/4.
            var rng = new DeterministicRandom(2);
            var hits = new int[20];
            const int trials = 20000;
            for (int t = 0; t < trials; t++)
            {
                var r = new ReservoirSampler<int>(5, rng);
                r.AddRange(Enumerable.Range(0, 20));
                foreach (int v in r.Sample) hits[v]++;
            }
            var expected = Enumerable.Repeat(trials * 5 / 20.0, 20).ToArray();
            var chi = HypothesisTests.ChiSquareGoodnessOfFit(hits.Select(h => (double)h).ToArray(), expected);
            Check.True(chi.PValue > 0.001, $"inclusion counts not uniform: p={chi.PValue} [{string.Join(",", hits)}]");

            // A long stream exercises Algorithm L's skipping: early and late items must be equally likely.
            int early = 0, late = 0;
            for (int t = 0; t < 2000; t++)
            {
                var r = new ReservoirSampler<int>(10, rng);
                r.AddRange(Enumerable.Range(0, 5000));
                early += r.Sample.Count(v => v < 2500);
                late += r.Sample.Count(v => v >= 2500);
            }
            Check.Close(0.5, early / (double)(early + late), 0.02);
        }

        // ---------- weighted without replacement ----------

        public void Weighted_FirstDrawFollowsWeightsAndItemsAreDistinct()
        {
            var rng = new DeterministicRandom(3);
            string[] items = { "a", "b", "c", "d" };
            double[] weights = { 1, 2, 3, 0 };
            var first = new Dictionary<string, int>();
            for (int t = 0; t < 30000; t++)
            {
                var s = SamplingPlans.WeightedWithoutReplacement(items, weights, 2, rng);
                Check.Equal(2, s.Distinct().Count());
                Check.False(s.Contains("d"), "zero weight is never drawn");
                first[s[0]] = first.TryGetValue(s[0], out int n) ? n + 1 : 1;
            }
            Check.Close(1 / 6.0, first["a"] / 30000.0, 0.01);
            Check.Close(2 / 6.0, first["b"] / 30000.0, 0.01);
            Check.Close(3 / 6.0, first["c"] / 30000.0, 0.01);
        }

        public void Weighted_SecondDrawIsConditional()
        {
            // P(second = a | first = c) = 1/(1+2) for weights a=1, b=2, c=3.
            var rng = new DeterministicRandom(4);
            int firstC = 0, thenA = 0;
            for (int t = 0; t < 40000; t++)
            {
                var s = SamplingPlans.WeightedWithoutReplacement(new[] { "a", "b", "c" }, new double[] { 1, 2, 3 }, 2, rng);
                if (s[0] != "c") continue;
                firstC++;
                if (s[1] == "a") thenA++;
            }
            Check.Close(1 / 3.0, thenA / (double)firstC, 0.015);
        }

        public void Weighted_Validation()
        {
            var rng = new DeterministicRandom(5);
            Check.Throws<ArgumentException>(() => SamplingPlans.WeightedWithoutReplacement(new[] { 1, 2 }, new double[] { 1, 0 }, 2, rng));
            Check.Throws<ArgumentException>(() => SamplingPlans.WeightedWithoutReplacement(new[] { 1 }, new double[] { -1 }, 1, rng));
            Check.Throws<ArgumentException>(() => SamplingPlans.WeightedWithoutReplacement(new[] { 1 }, new double[] { double.NaN }, 1, rng));
            Check.Equal(0, SamplingPlans.WeightedWithoutReplacement(new[] { 1 }, new double[] { 1 }, 0, rng).Count);
        }

        // ---------- stratified ----------

        public void Stratified_PerGroupAndProportional()
        {
            var rng = new DeterministicRandom(6);
            var items = Enumerable.Range(0, 100).Select(i => (Group: i < 90 ? "common" : "rare", Id: i)).ToList();
            var per = SamplingPlans.Stratified(items, x => x.Group, 5, rng);
            Check.Equal(10, per.Count);
            Check.Equal(5, per.Count(x => x.Group == "rare"));
            Check.Equal(5, per.Distinct().Count(x => x.Group == "common"));

            var prop = SamplingPlans.StratifiedFraction(items, x => x.Group, 0.05, rng);
            Check.Equal(5, prop.Count(x => x.Group == "common"));                    // 90 × 5% ≈ 4.5 → 5
            Check.Equal(1, prop.Count(x => x.Group == "rare"));                      // 10 × 5% = 0.5 → at least 1
            Check.Throws<ArgumentOutOfRangeException>(() => SamplingPlans.StratifiedFraction(items, x => x.Group, 0, rng));
        }

        // ---------- sorted list ----------

        public void Bounds_MatchALinearScan()
        {
            var rng = new DeterministicRandom(7);
            for (int trial = 0; trial < 300; trial++)
            {
                var list = Enumerable.Range(0, rng.Next(0, 30)).Select(_ => rng.Next(0, 10)).OrderBy(v => v).ToList();
                int probe = rng.Next(-1, 12);
                IReadOnlyList<int> ro = list;
                int lower = list.FindIndex(v => v >= probe);
                int upper = list.FindIndex(v => v > probe);
                Check.Equal(lower < 0 ? list.Count : lower, ro.LowerBound(probe));
                Check.Equal(upper < 0 ? list.Count : upper, ro.UpperBound(probe));
                var (s, e) = ro.EqualRange(probe);
                Check.Equal(list.Count(v => v == probe), e - s);
                Check.Equal(list.Contains(probe), ro.ContainsSorted(probe));
            }
        }

        public void InsertRemoveAndRange()
        {
            var list = new List<int>();
            var rng = new DeterministicRandom(8);
            for (int i = 0; i < 200; i++) list.InsertSorted(rng.Next(0, 50));
            Check.True(((IReadOnlyList<int>)list).IsSorted());
            int count7 = list.Count(v => v == 7);
            Check.Equal(count7 > 0, list.RemoveSorted(7));
            Check.Equal(Math.Max(0, count7 - 1), list.Count(v => v == 7));
            Check.False(list.RemoveSorted(999));
            Check.Equal(list.Where(v => v >= 10 && v < 20).Count(), ((IReadOnlyList<int>)list).RangeSorted(10, 20).Count());

            var stable = new List<(int K, string Tag)>();
            var byKey = Comparer<(int K, string Tag)>.Create((a, b) => a.K.CompareTo(b.K));
            stable.InsertSorted((1, "first"), byKey);
            stable.InsertSorted((1, "second"), byKey);
            stable.InsertSorted((0, "zero"), byKey);
            Check.Equal("zero,first,second", string.Join(",", stable.Select(x => x.Tag)));
        }

        public void KeyProjectedSearchAndNearest()
        {
            var samples = new (double T, char V)[] { (0.0, 'a'), (1.0, 'b'), (2.5, 'c'), (4.0, 'd') };
            Check.Equal(2, samples.LowerBoundBy(2.0, s => s.T));
            Check.Equal(3, samples.UpperBoundBy(2.5, s => s.T));
            Check.Equal(1, samples.NearestBy(1.7, s => s.T));
            Check.Equal(2, samples.NearestBy(1.8, s => s.T));
            Check.Equal(1, samples.NearestBy(1.75, s => s.T));                        // tie → earlier
            Check.Equal(0, samples.NearestBy(-9, s => s.T));
            Check.Equal(3, samples.NearestBy(99, s => s.T));
            Check.Equal(-1, Array.Empty<(double, char)>().NearestBy(1, s => s.Item1));
        }

        // ---------- pairwise ----------

        public void Pairwise_AdjacentPairsSinglePass()
        {
            Check.Equal("(1, 2),(2, 4),(4, 7)", string.Join(",", new[] { 1, 2, 4, 7 }.Pairwise()));
            Check.Equal("1,2,3", string.Join(",", new[] { 1, 2, 4, 7 }.Pairwise((a, b) => b - a)));
            Check.Equal(0, new[] { 1 }.Pairwise().Count());
            int enumerations = 0;
            IEnumerable<int> Source() { enumerations++; yield return 1; yield return 2; yield return 3; }
            Check.Equal(2, Source().Pairwise().Count());
            Check.Equal(1, enumerations);
            Check.Throws<ArgumentNullException>(() => ((int[])null!).Pairwise());
        }

        // ---------- unflatten ----------

        private sealed class Role { public string Name { get; set; } = ""; public int Level { get; set; } }
        private sealed class User
        {
            public string Name { get; set; } = "";
            public List<string> Tags { get; set; } = new List<string>();
            public List<Role> Roles { get; set; } = new List<Role>();
            public Dictionary<string, int> Limits { get; set; } = new Dictionary<string, int>();
        }

        public void Unflatten_InvertsFlatten()
        {
            var u = new User
            {
                Name = "ada",
                Tags = { "x", "y" },
                Roles = { new Role { Name = "admin", Level = 3 } },
                Limits = { ["max"] = 9, ["min"] = 1 },
            };
            var flat = FlattenObject.Flatten(u);
            var tree = UnflattenObject.Unflatten(flat);
            Check.Equal("ada", tree["Name"]);
            var tags = (List<object?>)tree["Tags"]!;
            Check.Equal("x,y", string.Join(",", tags));
            var role = (Dictionary<string, object?>)((List<object?>)tree["Roles"]!)[0]!;
            Check.Equal(3, role["Level"]);
            var limits = (Dictionary<string, object?>)tree["Limits"]!;                // non-numeric brackets → dictionary
            Check.Equal(9, limits["max"]);
            // Re-flattening the tree gives the same leaves. Flatten writes dictionary keys as [key], so compare with the
            // original paths in all-bracket form ("Roles[0].Level" -> "[Roles][0][Level]").
            var again = FlattenObject.Flatten(tree);
            Check.Equal(flat.Count, again.Count);
            foreach (var kv in flat) Check.Equal(kv.Value, again[AllBrackets(kv.Key)], kv.Key);
        }

        private static string AllBrackets(string path)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string part in path.Split('.'))
            {
                int b = part.IndexOf('[');
                string name = b < 0 ? part : part.Substring(0, b);
                sb.Append('[').Append(name).Append(']');
                if (b >= 0) sb.Append(part.Substring(b));
            }
            return sb.ToString();
        }

        public void Unflatten_ShapesAndErrors()
        {
            var t = UnflattenObject.Unflatten(new Dictionary<string, object?>
            {
                ["a.b"] = 1,
                ["grid[1][0]"] = "r1c0",
                ["grid[0][0]"] = "r0c0",
                ["sparse[0]"] = 1,
                ["sparse[2]"] = 2,
            });
            Check.Equal(1, ((Dictionary<string, object?>)t["a"]!)["b"]);
            var grid = (List<object?>)t["grid"]!;
            Check.Equal("r0c0", ((List<object?>)grid[0]!)[0]);                     // index order, not insertion order
            Check.True(t["sparse"] is Dictionary<string, object?>, "non-dense indices stay a dictionary");
            Check.Equal(2, ((Dictionary<string, object?>)UnflattenObject.Unflatten(new Dictionary<string, object?> { ["x/y"] = 2 }, "/")["x"]!)["y"]);

            var conflict = Check.Throws<InvalidOperationException>(() =>
                UnflattenObject.Unflatten(new[] { new KeyValuePair<string, object?>("a", 1), new KeyValuePair<string, object?>("a.b", 2) }));
            Check.True(conflict.Message.Contains("'a'"), conflict.Message);
            Check.Throws<InvalidOperationException>(() =>
                UnflattenObject.Unflatten(new[] { new KeyValuePair<string, object?>("a.b", 1), new KeyValuePair<string, object?>("a", 2) }));
            foreach (string bad in new[] { "a..b", "a.", "a[0", "a]b", ".a" })
                Check.Throws<FormatException>(() => UnflattenObject.Unflatten(new[] { new KeyValuePair<string, object?>(bad, 1) }), bad);
        }
    }
}
