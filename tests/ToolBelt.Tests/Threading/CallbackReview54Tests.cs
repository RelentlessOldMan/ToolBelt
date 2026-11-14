using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Collections;
using ToolBelt.Diagnostics;
using ToolBelt.Graphs;
using ToolBelt.IO;
using ToolBelt.Logging;
using ToolBelt.Process;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;
using ToolBelt.Time;
#if !NETFRAMEWORK
using ToolBelt.Configuration;
#endif

namespace ToolBelt.Tests.Threading
{
    /// <summary>
    /// Regressions from review round 54 (cross-cutting: callbacks that throw). A comparer, clock, delay, handler, token
    /// callback or error sink that throws must not kill a loop, corrupt a collection, lose data, crash the process on a
    /// background thread, or replace the caller's own exception. Each test failed before its fix.
    /// </summary>
    public sealed class CallbackReview54Tests
    {
        private static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static async Task<bool> Within(Func<bool> condition, int milliseconds = 5000)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > milliseconds) return false;
                await Task.Delay(10);
            }
            return true;
        }

        // A comparer that throws on its Nth comparison from when it is armed.
        private sealed class TrippingComparer : IComparer<int>
        {
            public int TripAfter = -1;
            public int Compare(int x, int y)
            {
                if (TripAfter == 0) throw new FormatException("comparer");
                if (TripAfter > 0) TripAfter--;
                return x.CompareTo(y);
            }
        }

        // An equality comparer whose Nth hash from when it is armed throws, or that throws for one value.
        private sealed class TrippingEquality : IEqualityComparer<string>
        {
            public int TripAfter = -1;
            public string? Poison;
            public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);
            public int GetHashCode(string s)
            {
                if (s == Poison) throw new FormatException("hash");
                if (TripAfter == 0) throw new FormatException("hash");
                if (TripAfter > 0) TripAfter--;
                return StringComparer.Ordinal.GetHashCode(s);
            }
        }

        // --- Token callbacks that throw when cancelled ---

        public async Task CronScheduler_ClockThatThrowsOnce_DoesNotKillTheLoop()
        {
            var errors = new List<(ScheduledJob? Job, Exception Error)>();
            int calls = 0, armed = 0;
            DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 0, 0, 30, TimeSpan.Zero);
            DateTimeOffset Clock()
            {
                Interlocked.Increment(ref calls);
                if (Interlocked.CompareExchange(ref armed, 2, 1) == 1) throw new FormatException("clock");
                return now;
            }
            using var s = new CronScheduler((j, e) => { lock (errors) errors.Add((j, e)); }, Clock, (t, ct) => Task.Delay(10, ct), TimeZoneInfo.Utc);
            s.Add("job", "* * * * *", () => { });
            s.Start();
            Volatile.Write(ref armed, 1);
            Check.True(await Within(() => Volatile.Read(ref armed) == 2), "the clock threw");
            int after = Volatile.Read(ref calls);
            Check.True(await Within(() => Volatile.Read(ref calls) > after + 3), "the loop keeps reading the clock");
            lock (errors)
                Check.True(errors.Any(e => e.Job is null && e.Error is FormatException), "reported with a null job");
        }

        public async Task CronScheduler_DelayThatThrows_DoesNotKillTheLoop()
        {
            var errors = new List<Exception>();
            int delays = 0;
            Task Delay(TimeSpan t, CancellationToken ct)
            {
                if (Interlocked.Increment(ref delays) == 1) throw new FormatException("delay");
                return Task.Delay(10, ct);
            }
            using var s = new CronScheduler((j, e) => { lock (errors) errors.Add(e); }, () => new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), Delay, TimeZoneInfo.Utc);
            s.Start();
            Check.True(await Within(() => Volatile.Read(ref delays) > 3), "the loop keeps waiting");
            lock (errors) Check.True(errors.Any(e => e is FormatException));
        }

        public async Task CronScheduler_Dispose_ThrowingTokenCallback_IsReportedNotThrown()
        {
            var errors = new List<(ScheduledJob? Job, Exception Error)>();
            DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 0, 0, 30, TimeSpan.Zero);
            var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var s = new CronScheduler((j, e) => { lock (errors) errors.Add((j, e)); }, () => now,
                (t, ct) => Task.Delay(Timeout.Infinite, ct), TimeZoneInfo.Utc);
            var job = s.Add("job", "* * * * *", async ct =>
            {
                ct.Register(() => throw new FormatException("callback"));
                registered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
            });
            s.Start();
            now = job.NextRun!.Value;
            s.RunDue();
            Check.True(await Task.WhenAny(registered.Task, Task.Delay(5000)) == registered.Task, "job started");
            s.Dispose();
            lock (errors)
                Check.True(errors.Any(e => e.Job is null && e.Error is AggregateException a && a.InnerExceptions.Any(i => i is FormatException)));
        }

        public async Task PeriodicWorker_Dispose_ThrowingTokenCallback_IsReportedNotThrown()
        {
            var errors = new List<Exception>();
            var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var w = new PeriodicWorker(async ct =>
            {
                ct.Register(() => throw new FormatException("callback"));
                registered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
            }, TimeSpan.Zero, e => { lock (errors) errors.Add(e); });
            w.Start();
            Check.True(await Task.WhenAny(registered.Task, Task.Delay(5000)) == registered.Task);
            w.Dispose();
            lock (errors) Check.True(errors.Any(e => e is AggregateException a && a.InnerExceptions.Any(i => i is FormatException)));
        }

        public async Task ConcurrentPipeline_Dispose_ThrowingTokenCallback_IsReportedNotThrown()
        {
            var errors = new List<Exception>();
            var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var p = new ConcurrentPipeline<int>(async (item, ct) =>
            {
                ct.Register(() => throw new FormatException("callback"));
                registered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
            }, 4, onError: e => { lock (errors) errors.Add(e); });
            Check.True(p.TryEnqueue(1));
            Check.True(await Task.WhenAny(registered.Task, Task.Delay(5000)) == registered.Task);
            p.Dispose();
            lock (errors) Check.True(errors.Any(e => e is AggregateException a && a.InnerExceptions.Any(i => i is FormatException)));
        }

        // The timeout's delay could beat CancelAfter's timer; disposing the source then dropped the timer, and the abandoned
        // probe's token was never cancelled. Many probes, so some lose the race on the old code.
        public async Task HealthCheck_TimedOutProbes_AllHaveTheirTokensCancelled()
        {
            const int probes = 200;
            var cancelled = new TaskCompletionSource<bool>[probes];
            var check = new HealthCheck();
            for (int i = 0; i < probes; i++)
            {
                var mine = cancelled[i] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                check.Add("p" + i, async ct =>
                {
                    ct.Register(() => mine.TrySetResult(true));
                    await Task.Delay(Timeout.Infinite);   // ignores the token: the race enforces the timeout
                    return new ProbeResult(HealthStatus.Healthy);
                }, TimeSpan.FromMilliseconds(1 + i % 20));
            }
            HealthReport report = await check.RunAsync();
            Check.True(report.Probes.All(p => p.TimedOut));
            Task all = Task.WhenAll(cancelled.Select(c => c.Task));
            await Task.WhenAny(all, Task.Delay(5000));
            Check.Equal(probes, cancelled.Count(c => c.Task.IsCompleted), "tokens cancelled");
        }

        public async Task HealthCheck_TimedOutProbe_WhoseTokenCallbackThrows_IsReported()
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var check = new HealthCheck().Add("slow", async ct =>
            {
                ct.Register(() => { cancelled.TrySetResult(true); throw new FormatException("callback"); });
                await Task.Delay(Timeout.Infinite);
                return new ProbeResult(HealthStatus.Healthy);
            }, TimeSpan.FromMilliseconds(50));
            HealthReport report = await check.RunAsync();
            Check.True(report.Probes[0].TimedOut);
            Check.True(await Task.WhenAny(cancelled.Task, Task.Delay(5000)) == cancelled.Task, "the abandoned probe's token was cancelled");
            await Task.Delay(300);   // on the old code CancelAfter's timer thread died of the callback's exception, ending the process
        }

        public async Task ParallelUtils_FailFast_ThrowingTokenCallback_IsCollected()
        {
            var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? caught = null;
            try
            {
                await ParallelUtils.ForEachAsync(new[] { 0, 1 }, 2, async (i, ct) =>
                {
                    if (i == 0)
                    {
                        ct.Register(() => throw new InvalidTimeZoneException("callback"));
                        registered.TrySetResult(true);
                        await Task.Delay(Timeout.Infinite, ct);
                    }
                    else
                    {
                        await registered.Task;
                        throw new FormatException("body");
                    }
                });
            }
            catch (Exception ex) { caught = ex; }
            var all = caught is AggregateException a ? a.InnerExceptions.ToList() : new List<Exception> { caught! };
            Check.True(all.Any(e => e is FormatException), "the body's failure");
            Check.True(all.Any(e => e is InvalidTimeZoneException), "the callback's failure");
        }

        public async Task ParallelUtils_SourceThrows_ThrowingTokenCallback_KeepsTheSourcesException()
        {
            var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int finished = 0;
            IEnumerable<int> Source()
            {
                yield return 0;
                registered.Task.Wait();
                throw new FormatException("source");
            }
            // The callback's failure is the body's, kept with the source's since round 72; the source's comes first.
            var agg = await Check.ThrowsAsync<AggregateException>(() => ParallelUtils.ForEachAsync(Source(), 2, async (i, ct) =>
            {
                try
                {
                    ct.Register(() => throw new InvalidTimeZoneException("callback"));
                    registered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct);
                }
                finally { Interlocked.Exchange(ref finished, 1); }
            }));
            Check.True(agg.InnerExceptions[0] is FormatException { Message: "source" }, "the source's exception was not first");
            Check.True(agg.InnerExceptions.Any(e => e is InvalidTimeZoneException), "the callback's failure was dropped");
            Check.Equal(1, Volatile.Read(ref finished), "the body was waited for");
        }

        public async Task TaskRace_LosersThrowingTokenCallback_DoesNotReplaceTheWinner()
        {
            var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int result = await TaskRace.FirstToComplete(new Func<CancellationToken, Task<int>>[]
            {
                async ct => { await registered.Task; return 1; },
                async ct =>
                {
                    ct.Register(() => throw new FormatException("callback"));
                    registered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct);
                    return 2;
                },
            });
            Check.Equal(1, result);
        }

        public async Task TaskRace_Hedged_LosersThrowingTokenCallback_DoesNotReplaceTheWinner()
        {
            var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int result = await TaskRace.Hedged(async (n, ct) =>
            {
                if (n == 0)
                {
                    ct.Register(() => throw new FormatException("callback"));
                    registered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct);
                    return 0;
                }
                await registered.Task;
                return 7;
            }, TimeSpan.FromMilliseconds(1));
            Check.Equal(7, result);
        }

        // --- Comparers and hashes that throw ---

        public void BinaryHeap_ThrowingComparer_LeavesTheHeapUnchanged()
        {
            var rng = new Random(54);
            for (int trial = 0; trial < 200; trial++)
            {
                var cmp = new TrippingComparer();
                int[] items = Enumerable.Range(0, rng.Next(1, 30)).Select(_ => rng.Next(20)).ToArray();
                var heap = new BinaryHeap<int>(items, cmp);
                bool push = rng.Next(2) == 0;
                cmp.TripAfter = rng.Next(0, 8);
                try
                {
                    if (push) heap.Push(rng.Next(-5, 25));
                    else heap.Pop();
                    continue;   // finished before the comparer tripped: nothing to check
                }
                catch (FormatException) { }
                cmp.TripAfter = -1;
                Check.Equal(items.Length, heap.Count, "count, trial " + trial);
                var popped = new List<int>();
                while (heap.Count > 0) popped.Add(heap.Pop());
                Check.True(popped.SequenceEqual(items.OrderBy(x => x)), "contents and order, trial " + trial);
            }
        }

        public void TopN_ThrowingComparer_LeavesTheContentsUnchanged()
        {
            var rng = new Random(541);
            for (int trial = 0; trial < 200; trial++)
            {
                var cmp = new TrippingComparer();
                var top = new TopN<int>(5, cmp);
                for (int i = 0; i < rng.Next(1, 12); i++) top.Add(rng.Next(50));
                int[] before = top.ToArray();
                cmp.TripAfter = rng.Next(0, 6);
                try { top.Add(rng.Next(50)); continue; }
                catch (FormatException) { }
                cmp.TripAfter = -1;
                Check.True(top.ToArray().SequenceEqual(before), "trial " + trial);
                Check.Equal(before.Length, top.Count);
            }
        }

        public void TopN_ToArray_ThrowingComparer_ThrowsItUnwrapped()
        {
            var cmp = new TrippingComparer();
            var top = new TopN<int>(5, cmp);
            foreach (int x in new[] { 3, 1, 4, 1, 5, 9 }) top.Add(x);
            cmp.TripAfter = 0;
            var ex = Check.Throws<FormatException>(() => top.ToArray());
            Check.Equal("comparer", ex.Message);
        }

        public void TopN_ToArray_IsLargestFirst()
        {
            var rng = new Random(542);
            for (int trial = 0; trial < 100; trial++)
            {
                var top = new TopN<int>(rng.Next(1, 10));
                var all = Enumerable.Range(0, rng.Next(0, 30)).Select(_ => rng.Next(15)).ToList();
                foreach (int x in all) top.Add(x);
                Check.True(top.ToArray().SequenceEqual(all.OrderByDescending(x => x).Take(top.Capacity)), "trial " + trial);
            }
        }

        public void IndexedPriorityQueue_ThrowingComparer_LeavesTheQueueUnchanged()
        {
            var rng = new Random(543);
            for (int trial = 0; trial < 300; trial++)
            {
                var cmp = new TrippingComparer();
                var q = new IndexedPriorityQueue<string, int>(cmp);
                var model = new Dictionary<string, int>();
                for (int i = 0; i < rng.Next(1, 20); i++)
                {
                    int p = rng.Next(30);
                    q.Enqueue("k" + i, p);
                    model["k" + i] = p;
                }
                int op = rng.Next(3);
                cmp.TripAfter = rng.Next(0, 8);
                try
                {
                    if (op == 0) q.Enqueue("new", rng.Next(30));
                    else if (op == 1) q.UpdatePriority("k" + rng.Next(model.Count), rng.Next(-5, 35));
                    else q.TryDequeueMin(out _, out _);
                    continue;
                }
                catch (FormatException) { }
                cmp.TripAfter = -1;
                Check.Equal(model.Count, q.Count, "count, trial " + trial);
                foreach (var kv in model)
                {
                    Check.True(q.TryGetPriority(kv.Key, out int p), "contains " + kv.Key + ", trial " + trial);
                    Check.Equal(kv.Value, p);
                }
                var drained = new List<int>();
                while (q.TryDequeueMin(out string key, out int pr)) { drained.Add(pr); Check.Equal(model[key], pr); }
                Check.True(drained.SequenceEqual(model.Values.OrderBy(x => x)), "order, trial " + trial);
            }
        }

        private sealed class ThrowingObjectComparer : IComparer<object?>
        {
            public int Compare(object? x, object? y) => throw new FormatException("comparer");
        }

        public void DataTableLite_OrderBy_ThrowingComparer_ThrowsItUnwrapped()
        {
            var t = new DataTableLite(new[] { "a" }).AddRow(2).AddRow(1).AddRow(3);
            var ex = Check.Throws<FormatException>(() => t.OrderBy("a", new ThrowingObjectComparer()));
            Check.Equal("comparer", ex.Message);
        }

        public void DataTableLite_OrderBy_IsStable()
        {
            var rng = new Random(544);
            for (int trial = 0; trial < 50; trial++)
            {
                var t = new DataTableLite(new[] { "k", "i" });
                var rows = Enumerable.Range(0, rng.Next(0, 40)).Select(i => (K: rng.Next(4), I: i)).ToList();
                foreach (var r in rows) t.AddRow(r.K, r.I);
                var sorted = t.OrderBy("k");
                var expected = rows.OrderBy(r => r.K).Select(r => r.I).ToList();
                var actual = Enumerable.Range(0, expected.Count).Select(i => (int)sorted[i, "i"]!).ToList();
                Check.True(actual.SequenceEqual(expected), "trial " + trial);
                var desc = t.OrderByDescending("k");
                var expectedDesc = rows.OrderByDescending(r => r.K).Select(r => r.I).ToList();
                var actualDesc = Enumerable.Range(0, expectedDesc.Count).Select(i => (int)desc[i, "i"]!).ToList();
                Check.True(actualDesc.SequenceEqual(expectedDesc), "descending, trial " + trial);
            }
        }

        public void Graph_AddEdge_ThrowingHash_AddsNothing()
        {
            var g = new Graph<string>(directed: false, comparer: new TrippingEquality { Poison = "bad" });
            Check.Throws<FormatException>(() => g.AddEdge("a", "bad"));
            Check.Equal(0, g.VertexCount);
            Check.False(g.ContainsVertex("a"));
            Check.Equal(0, g.EdgeCount);
        }

        // A key whose Nth hash from when it is armed throws.
        private sealed class TripKey
        {
            public static int TripAfter = -1;
            public readonly string Name;
            public TripKey(string name) => Name = name;
            public override bool Equals(object? obj) => obj is TripKey k && k.Name == Name;
            public override int GetHashCode()
            {
                if (TripAfter == 0) { TripAfter = -1; throw new FormatException("hash"); }
                if (TripAfter > 0) TripAfter--;
                return StringComparer.Ordinal.GetHashCode(Name);
            }
            public override string ToString() => Name;
        }

        public void OrderedSet_Remove_ThrowingHash_LeavesSetConsistent()
        {
            var set = new OrderedSet<TripKey>();
            set.Add(new TripKey("a")); set.Add(new TripKey("b")); set.Add(new TripKey("c"));
            TripKey.TripAfter = 1;   // the lookup hashes, the removal throws
            try { Check.Throws<FormatException>(() => set.Remove(new TripKey("b"))); }
            finally { TripKey.TripAfter = -1; }
            Check.Equal(set.Count, set.Count());
            Check.Equal("a,b,c", string.Join(",", set));
        }

        public void OrderedDictionary_Remove_ThrowingHash_LeavesDictionaryConsistent()
        {
            var map = new OrderedDictionary<TripKey, int>();
            map.Add(new TripKey("a"), 1); map.Add(new TripKey("b"), 2); map.Add(new TripKey("c"), 3);
            TripKey.TripAfter = 1;
            try { Check.Throws<FormatException>(() => map.Remove(new TripKey("b"))); }
            finally { TripKey.TripAfter = -1; }
            Check.Equal(map.Count, map.Count());
            Check.Equal("a,b,c", string.Join(",", map.Keys));
        }

        // --- Predicates and clocks that throw ---

        public void FileChangeCoalescer_TakeQuiet_ThrowingIsReady_TakesNothing()
        {
            var c = new FileChangeCoalescer(ignoreCase: false);
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            c.Add(new FileChange(FileChangeKind.Changed, Path.Combine(Path.GetTempPath(), "one.txt")), t0);
            c.Add(new FileChange(FileChangeKind.Changed, Path.Combine(Path.GetTempPath(), "two.txt")), t0);
            int calls = 0;
            Check.Throws<FormatException>(() => c.TakeQuiet(t0.AddSeconds(10), TimeSpan.FromSeconds(1), _ =>
            {
                if (++calls == 2) throw new FormatException("isReady");
                return true;
            }));
            Check.Equal(2, c.TakeAll().Count);
        }

        public async Task Retry_ThrowingShouldRetry_PropagatesThePredicatesException()
        {
            var policy = new RetryPolicy { MaxAttempts = 3, BaseDelay = TimeSpan.Zero, ShouldRetry = _ => throw new FormatException("predicate") };
            int attempts = 0;
            var ex = await Check.ThrowsAsync<FormatException>(() => Retry.ExecuteAsync<int>(ct => { attempts++; throw new IOException("op"); }, policy));
            Check.Equal("predicate", ex.Message);
            Check.Equal(1, attempts);
        }

        public async Task CircuitBreaker_ThrowingClockOnFailure_KeepsTheOperationsExceptionAndCountsIt()
        {
            bool fail = false;
            var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var breaker = new CircuitBreaker(1, TimeSpan.FromMinutes(1), () =>
            {
                if (fail) { fail = false; throw new FormatException("clock"); }
                return t;
            });
            var ex = await Check.ThrowsAsync<IOException>(() => breaker.ExecuteAsync<int>(ct =>
            {
                fail = true;   // the clock throws when the failure is recorded
                throw new IOException("op");
            }));
            Check.Equal("op", ex.Message);
            await Check.ThrowsAsync<CircuitOpenException>(() => breaker.ExecuteAsync(ct => Task.FromResult(1)));
        }

        private sealed class ListSink : ILogSink
        {
            public readonly List<LogEvent> Events = new List<LogEvent>();
            public int ThrowCount;
            public void Emit(LogEvent e)
            {
                Events.Add(e);
                if (ThrowCount > 0) { ThrowCount--; throw new IOException("sink"); }
            }
            public void Dispose() { }
        }

        public void RateLimitedSink_InnerThatThrows_StillStartsTheWindow()
        {
            var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var inner = new ListSink { ThrowCount = 1 };
            var sink = new RateLimitedSink(inner, TimeSpan.FromMinutes(1), () => t);
            var e = new LogEvent(t, LogLevel.Error, "c", "disk full", null);
            Check.Throws<IOException>(() => sink.Emit(e));
            sink.Emit(e);
            sink.Emit(e);
            Check.Equal(1, inner.Events.Count, "the repeats are suppressed");
        }

        public void RateLimitedSink_SummaryAndEventBothFail_ReportsBoth()
        {
            var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var inner = new ListSink();
            var sink = new RateLimitedSink(inner, TimeSpan.FromMinutes(1), () => t);
            var e = new LogEvent(t, LogLevel.Error, "c", "one", null);
            sink.Emit(e);
            sink.Emit(e);   // suppressed: a summary is due
            inner.ThrowCount = 2;
            var ex = Check.Throws<AggregateException>(() => sink.Emit(new LogEvent(t, LogLevel.Error, "c", "two", null)));
            Check.Equal(2, ex.InnerExceptions.Count);
        }

        // --- Handlers and error sinks that throw ---

        public async Task FileWatcher_HandlerThrowingInsufficientMemory_IsReportedNotFatal()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-r54-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var errors = new List<Exception>();
                using var w = new FileWatcher(dir, new FileWatcherOptions { Debounce = TimeSpan.FromMilliseconds(20) });
                w.Changed += (_, __) => throw new InsufficientMemoryException("handler");
                w.Error += (_, e) => { lock (errors) errors.Add(e.GetException()); };
                w.Start();
                File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
                Check.True(await Within(() => { lock (errors) return errors.Any(e => e is InsufficientMemoryException); }), "reported via Error");
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        private sealed class BreakingWriter : StringWriter
        {
            public int WritesLeft = 1;
            public override void Write(string? value)
            {
                if (WritesLeft-- <= 0) throw new IOException("pipe closed");
                base.Write(value);
            }
        }

        public async Task ConsoleSpinner_BrokenWriter_DoesNotFaultTheLoop()
        {
            var writer = new BreakingWriter();
            var spinner = new ConsoleSpinner("working", writer, interactive: true, interval: TimeSpan.FromMilliseconds(5));
            var loop = (Task?)typeof(ConsoleSpinner).GetField("_loop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(spinner);
            Check.NotNull(loop);
            Check.True(await Task.WhenAny(loop!, Task.Delay(5000)) == loop, "the loop ends when the output breaks");
            Check.False(loop!.IsFaulted, "no unobserved fault");
            spinner.Dispose();
        }

        public void ProcessRunner_NoOutputCallbacksAfterRunReturns()
        {
            if (!Windows) return;
            int afterReturn = 0;
            bool returned = false;
            bool first = true;
            var options = new ProcessRunOptions
            {
                Timeout = TimeSpan.FromMilliseconds(300),
                OnOutputLine = line =>
                {
                    if (Volatile.Read(ref returned)) Interlocked.Increment(ref afterReturn);
                    if (first) { first = false; Thread.Sleep(2500); }   // still in the callback when Run gives up
                },
            };
            var result = ProcessRunner.Run("cmd.exe", "/c \"echo a& echo b& echo c& ping -n 30 127.0.0.1 >nul\"", options);
            Volatile.Write(ref returned, true);
            Check.True(result.TimedOut);
            Thread.Sleep(3000);
            Check.Equal(0, Volatile.Read(ref afterReturn));
        }

        public async Task FireAndForget_ThrowingOnError_LeavesNoUnobservedFault()
        {
            string marker = "r54-" + Guid.NewGuid().ToString("N");
            int seen = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            {
                if (e.Exception.InnerExceptions.Any(i => i.Message == marker)) Interlocked.Increment(ref seen);
                e.SetObserved();
            };
            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                var ran = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Fire(ran, marker);
                await ran.Task;
                await Task.Delay(200);
                for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                Check.Equal(0, Volatile.Read(ref seen));
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }

        // Separate so that no local keeps the continuation reachable.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Fire(TaskCompletionSource<bool> ran, string marker)
            => Task.FromException(new IOException("work")).FireAndForget(_ => { ran.TrySetResult(true); throw new FormatException(marker); });

#if !NETFRAMEWORK
        public sealed class Prefs
        {
            public int A { get; set; }
            public List<string> Names { get; set; } = new List<string>();
        }

        public void SettingsStore_Update_ThrowingChange_RestoresTheValue()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-r54-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = SettingsStore<Prefs>.LoadFrom(Path.Combine(dir, "s.json"));
                store.Update(p => { p.A = 1; p.Names.Add("x"); });
                Check.Throws<FormatException>(() => store.Update(p => { p.A = 2; p.Names.Add("y"); throw new FormatException("change"); }));
                Check.Equal(1, store.Value.A);
                Check.Equal("x", string.Join(",", store.Value.Names));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        public sealed class OomCollection : ICollection<string>
        {
            private readonly List<string> _items = new List<string>();
            public bool ThrowOnAdd;   // only the owner's instance: the serializer builds the loaded value in one of its own
            public int Count => _items.Count;
            public bool IsReadOnly => false;
            public void Add(string item)
            {
                if (ThrowOnAdd) throw new OutOfMemoryException("collection");
                _items.Add(item);
            }
            public void Clear() => _items.Clear();
            public bool Contains(string item) => _items.Contains(item);
            public void CopyTo(string[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
            public bool Remove(string item) => _items.Remove(item);
            public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public sealed class WithOomCollection
        {
            public OomCollection Items { get; } = new OomCollection { ThrowOnAdd = true };
        }

        public void SettingsStore_GetOnlyCollectionOutOfMemory_IsNotTakenForACorruptFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-r54-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "s.json");
                File.WriteAllText(path, "{ \"Items\": [\"a\"] }");
                var ex = Check.Throws<OutOfMemoryException>(() => SettingsStore<WithOomCollection>.LoadFrom(path));
                Check.Equal("collection", ex.Message);
                Check.True(File.Exists(path), "the file was not moved aside");
            }
            finally { Directory.Delete(dir, true); }
        }
#endif
    }
}
