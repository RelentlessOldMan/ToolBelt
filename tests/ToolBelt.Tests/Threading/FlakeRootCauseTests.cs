using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Logging;
using ToolBelt.Process;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    /// <summary>Regressions from round 46 (root causes of flaky tests): each test failed before its fix.</summary>
    public sealed class FlakeRootCauseTests
    {
        private sealed class DelegateSink : ILogSink
        {
            private readonly Action<LogEvent> _emit;
            public DelegateSink(Action<LogEvent> emit) => _emit = emit;
            public void Emit(LogEvent logEvent) => _emit(logEvent);
            public void Dispose() { }
        }

        // The consumer resumed on the thread pool after each wait, so producers blocked for space on pool threads starved
        // it: with the pool capped at 2 threads, 2 blocking producers moved 0 of 200 items in 60 s (probe; the cap is
        // process-wide, so the test checks the cause instead).
        public void Pipeline_ConsumerRunsOffTheThreadPool()
        {
            int poolThreads = 0, items = 0;
            var pipe = new ConcurrentPipeline<int>(async (i, ct) =>
            {
                if (Thread.CurrentThread.IsThreadPoolThread) Interlocked.Increment(ref poolThreads);
                if (i % 2 == 0) await Task.Yield();       // an async consumer still works (its continuations may use the pool)
                Interlocked.Increment(ref items);
            }, capacity: 1);
            for (int i = 0; i < 50; i++) pipe.TryEnqueue(i);
            Check.True(pipe.CompleteAsync().Wait(TimeSpan.FromSeconds(20)));
            pipe.Dispose();
            Check.Equal(50, items);
            // Odd items run synchronously on the loop's thread; even ones start there before yielding.
            Check.Equal(0, poolThreads);
        }

        public void AsyncLogSink_ConsumerRunsOffTheThreadPool()
        {
            int poolThreads = 0, written = 0;
            var sink = new AsyncLogSink(new DelegateSink(e =>
            {
                if (Thread.CurrentThread.IsThreadPoolThread) Interlocked.Increment(ref poolThreads);
                Interlocked.Increment(ref written);
            }), capacity: 1);
            var producers = new Task[4];
            for (int p = 0; p < producers.Length; p++)
                producers[p] = Task.Run(() => { for (int i = 0; i < 25; i++) sink.Emit(new LogEvent(DateTimeOffset.UnixEpoch, LogLevel.Info, "c", "m", null)); });
            Check.True(Task.WaitAll(producers, TimeSpan.FromSeconds(20)));
            sink.Dispose();
            Check.Equal(100, written);
            Check.Equal(0, poolThreads);
        }

        // The output was read with BeginOutputReadLine, on the thread pool: two Runs on a pool capped at two threads both
        // timed out (30 s) with 0 and 4 KB of 660 KB captured (probe), the "flaky" HandlesLargeOutputWithoutDeadlock.
        public void ProcessRunner_ReadsOutputOffTheThreadPool()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            int poolThreads = 0, lines = 0;
            void Line(string _)
            {
                if (Thread.CurrentThread.IsThreadPoolThread) Interlocked.Increment(ref poolThreads);
                Interlocked.Increment(ref lines);
            }
            var r = ProcessRunner.Run("cmd.exe", "/c (for /L %i in (1,1,200) do @echo out) & (for /L %i in (1,1,200) do @echo err 1>&2)",
                new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(30), OnOutputLine = Line, OnErrorLine = Line, StandardInput = "unread" });
            Check.False(r.TimedOut);
            Check.Equal(400, lines);
            Check.Equal(0, poolThreads);
        }
    }
}
