// Exercises the ToolBelt micro-benchmark harness on a few hot-path utilities.
using System;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Collections;
using ToolBelt.Diagnostics;
using ToolBelt.Numerics;
using ToolBelt.Text;

internal static class Program
{
    private static void Main()
    {
        var payload = Encoding.ASCII.GetBytes(new string('x', 1024));
        var rng = new DeterministicRandom(1);
        var cache = new LruCache<int, int>(256);
        for (int i = 0; i < 256; i++) cache.Set(i, i);
        int key = 0;

        var results = Benchmark.Compare(new (string, Action)[]
        {
            ("Crc32 (1KiB)",        () => GC.KeepAlive(Crc32.Compute(payload))),
            ("Fnv1a (1KiB)",        () => GC.KeepAlive(Fnv1a.Hash64(payload))),
            ("Base58 enc (32B)",    () => GC.KeepAlive(Base58.Encode(payload.AsSpan(0, 32).ToArray()))),
            ("DetRandom.NextDouble",() => GC.KeepAlive(rng.NextDouble())),
            ("LruCache get",        () => { cache.TryGet(key = (key + 1) & 255, out _); }),
            ("Levenshtein(short)",  () => GC.KeepAlive(LevenshteinDistance.Distance("kitten", "sitting"))),
        }, iterations: 200_000, warmupIterations: 20_000);

        Console.WriteLine($"{"Benchmark",-24}{"mean ns",12}{"ops/sec",16}{"B/op",10}{"vs base",10}");
        Console.WriteLine(new string('-', 72));
        foreach (var r in results)
            Console.WriteLine($"{r.Name,-24}{r.MeanNanoseconds,12:F1}{r.OperationsPerSecond,16:N0}{r.AllocatedBytesPerOperation,10}{r.RelativeToBaseline,10:F2}");
    }
}
