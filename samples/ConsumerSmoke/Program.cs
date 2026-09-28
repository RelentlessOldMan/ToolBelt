// Fresh external consumer of ToolBelt — uses ONLY the public API, the way a downstream app would.
// Prints each check; exits 0 if every expectation holds, 1 otherwise.
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ToolBelt.Binary;
using ToolBelt.Collections;
using ToolBelt.Enums;
using ToolBelt.Functional;
using ToolBelt.Configuration;
using ToolBelt.Control;
using ToolBelt.Graphs;
using ToolBelt.Guards;
using ToolBelt.Identifiers;
using ToolBelt.Intervals;
using ToolBelt.IO;
using ToolBelt.Net;
using ToolBelt.Numerics;
using ToolBelt.Objects;
using ToolBelt.Quality;
using ToolBelt.Resilience;
using ToolBelt.Signal;
using ToolBelt.Text;
using ToolBelt.Threading;
using ToolBelt.Time;
using System.Collections.Generic;
using System.Net;

internal static class Program
{
    private static int _failures;

    private static async Task<int> Main()
    {
        // ---- Pit-of-success path: the obvious usage should be the correct usage. ----

        Expect("Base32 round-trips", Base32.Decode(Base32.Encode(Encoding.ASCII.GetBytes("hello")))
            .SequenceEqual(Encoding.ASCII.GetBytes("hello")));
        Expect("Base58 round-trips", Base58.Decode(Base58.Encode(new byte[] { 0, 1, 2, 250 }))
            .SequenceEqual(new byte[] { 0, 1, 2, 250 }));
        Expect("Crc32 known vector", Crc32.Compute(Encoding.ASCII.GetBytes("123456789")) == 0xCBF43926u);
        Expect("Bits.PopCount", Bits.PopCount(0xFFu) == 8);

        Expect("CaseConverter", CaseConverter.ToSnakeCase("HttpServerId") == "http_server_id");
        Expect("Slug", Slug.Slugify("Héllo, World!") == "hello-world");
        Expect("JaroWinkler", JaroWinkler.Similarity("MARTHA", "MARHTA") > 0.96);

        var cache = new LruCache<int, string>(2);
        cache.Set(1, "a"); cache.Set(2, "b"); cache.Set(3, "c"); // evicts 1
        Expect("LruCache eviction", !cache.ContainsKey(1) && cache.ContainsKey(3));

        var batched = new[] { 1, 2, 3, 4, 5 }.Batch(2).Select(b => b.Count).ToArray();
        Expect("Batch", batched.SequenceEqual(new[] { 2, 2, 1 }));

        Expect("Percentile median", Percentile.Median(new double[] { 1, 2, 3, 4 }) == 2.5);
        Expect("ByteSize format", ByteSize.Format(1536) == "1.50 KiB");
        Expect("HumanDuration round-trip", HumanDuration.Parse(HumanDuration.Format(TimeSpan.FromSeconds(90))) == TimeSpan.FromSeconds(90));

        var range = new DateRange(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                                  new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        Expect("DateRange contains", range.Contains(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)));

        var cron = new CronSchedule("0 9 * * 1-5");
        var next = cron.GetNextOccurrence(new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero));
        Expect("Cron weekday 9am", next.Hour == 9 && next.DayOfWeek >= DayOfWeek.Monday && next.DayOfWeek <= DayOfWeek.Friday);

        // Deterministic ids via an injected clock — a consumer can make output reproducible.
        var fixedNow = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var ulid = new UlidGenerator(() => fixedNow, new Random(1));
        string u1 = ulid.NewUlid(), u2 = ulid.NewUlid();
        Expect("Ulid monotonic", string.CompareOrdinal(u2, u1) > 0 && u1.Length == 26);

        var flags = new EnumMap<DayOfWeek, bool>(false);
        flags[DayOfWeek.Monday] = true;
        Expect("EnumMap", flags[DayOfWeek.Monday] && !flags[DayOfWeek.Tuesday]);

        // Functional types read naturally.
        Result<int> parsed = int.TryParse("42", out var n) ? Result.Success(n) : Result.Failure<int>("bad");
        Expect("Result", parsed.IsSuccess && parsed.Value == 42);
        Expect("Option", Option.Some(5).Map(x => x * 2).GetValueOrDefault(-1) == 10);

        // Async utilities: retry with an injected (instant) delay so the consumer's test isn't slow.
        int attempts = 0;
        var policy = new RetryPolicy { MaxAttempts = 5, DelayAsync = (_, __) => Task.CompletedTask };
        int result = await Retry.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 3) throw new InvalidOperationException("transient");
            return Task.FromResult(99);
        }, policy);
        Expect("Retry succeeds after retries", result == 99 && attempts == 3);

        using (var gate = new AsyncLock())
        {
            using (await gate.LockAsync()) { /* critical section */ }
            Expect("AsyncLock reacquirable", gate.TryLock() is IDisposable d && Dispose(d));
        }

        // ---- Misuse: plausible mistakes should fail clearly and safely (§103, §161). ----

        ExpectThrows<ArgumentNullException>("Guard.NotNull(null)", () => Guard.NotNull((string?)null));
        ExpectThrows<FormatException>("Base32 bad char", () => Base32.Decode("!!!!"));
        ExpectThrows<InvalidOperationException>("Result.Value on failure", () => { var _ = Result<int>.Failure("nope").Value; });
        ExpectThrows<FormatException>("Cron bad expression", () => new CronSchedule("not a cron"));
        Expect("ByteSize.TryParse garbage -> false", !ByteSize.TryParse("banana", out _));
        Expect("GlobMatcher", GlobMatcher.IsMatch("readme.txt", "*.txt"));

        // Double-dispose must be safe (idempotent), a classic consumer mistake.
        var tmp = new TtlCache<string, int>(TimeSpan.FromMinutes(1));
        tmp.Set("k", 1);
        Expect("TtlCache hit", tmp.TryGet("k", out var v) && v == 1);

        // ---- New divisions (added since the last review) ----

        // Graphs — build on the shipped BinaryHeap/DisjointSet.
        var g = new Graph<string>(directed: true);
        g.AddEdge("a", "b", 1); g.AddEdge("b", "c", 2); g.AddEdge("a", "c", 5);
        Expect("Graph Dijkstra", ShortestPath.Dijkstra(g, "a", "c").Distance == 3);
        Expect("Graph topo sort acyclic", TopologicalSort.Sort(g).IsAcyclic);

        // Intervals.
        var tree = new IntervalTree<int, string>(new[]
        {
            new KeyValuePair<Interval<int>, string>(new Interval<int>(0, 10), "x"),
            new KeyValuePair<Interval<int>, string>(new Interval<int>(5, 15), "y"),
        });
        Expect("IntervalTree point query", tree.Query(7).Count == 2);
        var rset = new RangeSet<int>(new[] { new Interval<int>(0, 5) })
            .Union(new RangeSet<int>(new[] { new Interval<int>(5, 10) }));
        Expect("RangeSet union coalesces", rset.Intervals.Count == 1);

        // Objects.
        Expect("DeepEquals", DeepEquals.Equals(new[] { 1, 2, 3 }, new[] { 1, 2, 3 }));
        Expect("PropertyPath get", (int)PropertyPath.Get(new { Inner = new { N = 42 } }, "Inner.N")! == 42);
        Expect("TypeUtils friendly name", TypeUtils.FriendlyName(typeof(List<int>)) == "List<int>");

        // Net.
        var cidr = CidrRange.Parse("10.0.0.0/8");
        Expect("CidrRange contains", cidr.Contains(IPAddress.Parse("10.1.2.3")));
        Expect("IpUtils private", IpUtils.IsPrivate(IPAddress.Parse("192.168.1.1")));

        // Signal.
        Expect("Convolution", Convolution.Convolve(new double[] { 1, 1 }, new double[] { 1, 1 })
            .SequenceEqual(new double[] { 1, 2, 1 }));
        Expect("MedianFilter removes spike",
            MedianFilter.Apply(new double[] { 1, 1, 100, 1, 1 }, 3).All(x => x == 1));

        // New Numerics.
        Expect("Integration", Math.Abs(Integration.AdaptiveSimpson(Math.Sin, 0, Math.PI) - 2) < 1e-9);
        Expect("RootFinding Brent", Math.Abs(RootFinding.Brent(x => x * x - 2, 0, 2).Root - Math.Sqrt(2)) < 1e-9);
        Expect("EngineeringNotation round-trip", Math.Abs(EngineeringNotation.Parse(EngineeringNotation.Format(1500)) - 1500) < 1e-6);
        var det1 = new DeterministicRandom(42);
        var det2 = new DeterministicRandom(42);
        Expect("DeterministicRandom reproducible", det1.NextUInt64() == det2.NextUInt64());

        // New Threading.
        var counter = new AtomicLong();
        System.Threading.Tasks.Parallel.For(0, 10000, _ => counter.Increment());
        Expect("AtomicLong parallel", counter.Value == 10000);
        Expect("TaskExtensions timeout", await Task.FromResult(7).WithTimeout(TimeSpan.FromSeconds(5)) == 7);

        // New IO.
        Expect("FixedWidth round-trip",
            FixedWidth.Parse(FixedWidth.Format(new[] { "ab", "cd" }, new[] { 4, 4 }), new[] { 4, 4 })
                .SequenceEqual(new[] { "ab", "cd" }));

        // Config: layer sources, then bind to a typed object.
        var layers = new ConfigLayers()
            .Add("defaults", new Dictionary<string, string?> { ["Port"] = "80" })
            .Add("override", new Dictionary<string, string?> { ["Port"] = "8080" });
        Expect("ConfigLayers provenance", layers.SourceOf("Port") == "override");
        Expect("ConfigBinder", ConfigBinder.Bind<SmokeOptions>(layers.Resolve()).Port == 8080);

        // Numerics additions.
        Expect("UnitConvert", Math.Abs(UnitConvert.Convert(1, "km", "m") - 1000) < 1e-9);
        Expect("Polynomial fit", Math.Abs(Polynomial.Fit(new double[] { 0, 1, 2 }, new double[] { 0, 1, 4 }, 2).Evaluate(3) - 9) < 1e-6);
        Expect("LinearAlgebra solve", LinearAlgebra.Solve(new double[,] { { 2, 0 }, { 0, 4 } }, new double[] { 4, 8 }).SequenceEqual(new double[] { 2, 2 }));
        Expect("Trend increasing", Trend.MannKendall(new double[] { 1, 2, 3, 4 }).Tau == 1.0);
        Expect("LookupTable", Math.Abs(new LookupTable(new double[] { 0, 10 }, new double[] { 0, 100 }).Interpolate(5) - 50) < 1e-9);

        // Control: PID drives a simple plant toward the setpoint.
        var pid = new PidController(2, 1, 0.1) { Setpoint = 5 };
        double plant = 0;
        for (int i = 0; i < 3000; i++) { double u = pid.Update(plant, 0.01); plant += 0.01 * (-plant + u); }
        Expect("PidController converges", Math.Abs(plant - 5) < 0.1);

        // Quality.
        Expect("ProcessCapability", Math.Abs(ProcessCapability.Compute(new double[] { -1, 0, 1 }, -3, 3).Cp - 1) < 1e-9);

        // ---- Misuse of the new surface: should fail clearly ----
        ExpectThrows<KeyNotFoundException>("Dijkstra missing vertex", () => ShortestPath.Dijkstra(g, "a", "zzz"));
        ExpectThrows<InvalidOperationException>("Dijkstra negative weight",
            () => { var bad = new Graph<int>(); bad.AddEdge(1, 2, -1); ShortestPath.Dijkstra(bad, 1, 2); });
        ExpectThrows<ArgumentException>("Interval start>end", () => new Interval<int>(5, 1));
        ExpectThrows<FormatException>("Cidr bad text", () => CidrRange.Parse("not-cidr"));
        ExpectThrows<ArgumentException>("PropertyPath bad segment", () => PropertyPath.Get(new { A = 1 }, "Nope"));
        ExpectThrows<ArgumentException>("UnitConvert cross-category", () => UnitConvert.Convert(1, "kg", "m"));
        ExpectThrows<ConfigBindingException>("ConfigBinder reports errors",
            () => ConfigBinder.Bind<SmokeOptions>(new Dictionary<string, string?> { ["Port"] = "notanumber" }));

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("CONSUMER SMOKE: all checks passed.");
            return 0;
        }
        Console.WriteLine($"CONSUMER SMOKE: {_failures} check(s) FAILED.");
        return 1;
    }

    public sealed class SmokeOptions { public int Port { get; set; } }

    private static bool Dispose(IDisposable d) { d.Dispose(); return true; }

    private static void Expect(string label, bool ok)
    {
        Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {label}");
        if (!ok) _failures++;
    }

    private static void ExpectThrows<TException>(string label, Action action) where TException : Exception
    {
        try
        {
            action();
            Console.WriteLine($"  [FAIL] {label} — expected {typeof(TException).Name}, nothing thrown");
            _failures++;
        }
        catch (TException)
        {
            Console.WriteLine($"  [OK] {label} — threw {typeof(TException).Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] {label} — expected {typeof(TException).Name}, got {ex.GetType().Name}");
            _failures++;
        }
    }
}
