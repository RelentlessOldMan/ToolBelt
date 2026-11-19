using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Resilience;
using ToolBelt.Runtime;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    // Review round 147 (concurrency, ninth pass): the first two failed on the code before their fixes.
    public sealed class ConcurrencyReview147Tests
    {
        // ---------- TokenBucketRateLimiter: a capacity below one token ----------

        // Accepted, but no count of 1 or more fits under it: every TryAcquire threw ArgumentOutOfRangeException("count").
        public void TokenBucket_ACapacityBelowOneIsRefused()
        {
            Check.Equal("capacity", Check.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiter(0.5, 1)).ParamName);
            Check.Equal("capacity", Check.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiter(0.999999, 1)).ParamName);
            // Guard: one token, and a fractional capacity above it, still work.
            Check.True(new TokenBucketRateLimiter(1, 1).TryAcquire());
            Check.True(new TokenBucketRateLimiter(1.5, 1).TryAcquire());
        }

        // ---------- StartupTiming.Report: a clock that steps back ----------

        // The step printed as "(+   -5.0 ms)".
        public void StartupTiming_ANegativeStepHasOneSign()
        {
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var readings = new[] { start, start.AddMilliseconds(10), start.AddMilliseconds(5) };
            int next = 0;
            var timing = new StartupTiming(() => readings[next++]);
            timing.Mark("a");
            timing.Mark("b");
            string[] lines = timing.Report().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Check.Equal("    10.0 ms (+   10.0 ms)  a", lines[0]);
            Check.Equal("     5.0 ms (-    5.0 ms)  b", lines[1]);
        }

        // ---------- ConcurrentPipeline: work the callback starts, under SuppressFlow ----------

        // The documented way out for work the callback starts: without the callback's context it waits for space like any
        // producer, so a Block producer started from the callback on a full pipeline loses nothing.
        public void Pipeline_AProducerStartedWithoutTheFlowWaitsForSpace()
        {
            var seen = new ConcurrentQueue<int>();
            Task? producer = null;
            using var started = new ManualResetEventSlim();
            ConcurrentPipeline<int>? pipeline = null;
            using var owned = pipeline = new ConcurrentPipeline<int>(async (item, ct) =>
            {
                if (item == 0)
                {
                    using (ExecutionContext.SuppressFlow())
                        producer = Task.Run(() => { for (int i = 1; i <= 5; i++) Check.True(pipeline!.TryEnqueue(i)); });
                    started.Set();
                    await Task.Delay(200, ct);
                }
                seen.Enqueue(item);
            }, 2, PipelineOverflowPolicy.Block);
            pipeline.TryEnqueue(0);
            Check.True(started.Wait(5000), "the callback started the producer");
            Check.True(producer!.Wait(10000), "the producer finished");
            Check.True(pipeline.CompleteAsync().Wait(10000), "the pipeline drained");
            Check.Equal("0,1,2,3,4,5", string.Join(",", seen));
        }
    }
}
