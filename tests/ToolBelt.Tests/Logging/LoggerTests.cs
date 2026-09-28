using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    public sealed class LoggerTests
    {
        public void FansOutToAllSinks()
        {
            var a = new List<LogEvent>();
            var b = new List<LogEvent>();
            using var logger = new Logger(minimumLevel: LogLevel.Trace);
            logger.AddSink(new DelegateSink(a.Add)).AddSink(new DelegateSink(b.Add));

            logger.Info("hello");
            Check.Equal(1, a.Count);
            Check.Equal(1, b.Count);
            Check.Equal("hello", a[0].Message);
        }

        public void FiltersBelowMinimumLevel()
        {
            var captured = new List<LogEvent>();
            using var logger = new Logger(minimumLevel: LogLevel.Warning);
            logger.AddSink(new DelegateSink(captured.Add));

            logger.Debug("dropped");
            logger.Info("dropped");
            logger.Warning("kept");
            logger.Error("kept");
            Check.Equal(2, captured.Count);
            Check.Equal(LogLevel.Warning, captured[0].Level);
        }

        public void CategoryAndExceptionAndInjectedClock()
        {
            var fixedTime = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
            var captured = new List<LogEvent>();
            using var logger = new Logger("net", LogLevel.Trace, () => fixedTime);
            logger.AddSink(new DelegateSink(captured.Add));

            var ex = new InvalidOperationException("boom");
            logger.Error("failed", ex);
            Check.Equal("net", captured[0].Category);
            Check.Equal(fixedTime, captured[0].Timestamp);
            Check.True(ReferenceEquals(ex, captured[0].Exception));
        }

        public void ForCategorySharesSinks()
        {
            var captured = new List<LogEvent>();
            using var root = new Logger("root", LogLevel.Trace);
            root.AddSink(new DelegateSink(captured.Add));
            Logger child = root.ForCategory("child");
            child.Info("from child");
            Check.Equal(1, captured.Count);
            Check.Equal("child", captured[0].Category);
        }

        public void FailingSinkDoesNotBreakOthers()
        {
            var captured = new List<LogEvent>();
            using var logger = new Logger(minimumLevel: LogLevel.Trace);
            logger.AddSink(new DelegateSink(_ => throw new Exception("bad sink")));
            logger.AddSink(new DelegateSink(captured.Add));
            logger.Info("still delivered");
            Check.Equal(1, captured.Count);
        }

        public void ThreadSafeFanOut()
        {
            int count = 0;
            using var logger = new Logger(minimumLevel: LogLevel.Trace);
            logger.AddSink(new DelegateSink(_ => System.Threading.Interlocked.Increment(ref count)));
            Parallel.For(0, 10000, _ => logger.Info("x"));
            Check.Equal(10000, count);
        }

        public void DisposeDisposesOwnedSinks()
        {
            bool disposed = false;
            var logger = new Logger();
            logger.AddSink(new DisposeProbe(() => disposed = true));
            logger.Dispose();
            Check.True(disposed);
        }

        private sealed class DisposeProbe : ILogSink
        {
            private readonly Action _onDispose;
            public DisposeProbe(Action onDispose) => _onDispose = onDispose;
            public void Emit(LogEvent logEvent) { }
            public void Dispose() => _onDispose();
        }
    }
}
