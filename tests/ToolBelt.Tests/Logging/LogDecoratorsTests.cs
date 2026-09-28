using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    public sealed class LogDecoratorsTests
    {
        private static LogEvent Ev(string message, LogLevel level = LogLevel.Info, string category = "cat")
            => new LogEvent(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero), level, category, message, null);

        public void FilterSinkPredicate()
        {
            var got = new List<LogEvent>();
            using var sink = new FilterSink(new DelegateSink(got.Add), e => e.Message.Contains("keep"));
            sink.Emit(Ev("keep me"));
            sink.Emit(Ev("drop me"));
            Check.Equal(1, got.Count);
            Check.Equal("keep me", got[0].Message);
        }

        public void FilterSinkMinimumLevel()
        {
            var got = new List<LogEvent>();
            using var sink = FilterSink.MinimumLevel(new DelegateSink(got.Add), LogLevel.Warning);
            sink.Emit(Ev("info", LogLevel.Info));
            sink.Emit(Ev("warn", LogLevel.Warning));
            sink.Emit(Ev("error", LogLevel.Error));
            Check.Equal(2, got.Count);
        }

        public void RouterSinkRoutesToAllMatching()
        {
            var errors = new List<LogEvent>();
            var all = new List<LogEvent>();
            using var router = new RouterSink()
                .AddLevelRoute(LogLevel.Error, new DelegateSink(errors.Add))
                .AddRoute(_ => true, new DelegateSink(all.Add));

            router.Emit(Ev("info", LogLevel.Info));
            router.Emit(Ev("boom", LogLevel.Error));

            Check.Equal(2, all.Count);      // catch-all sees both
            Check.Equal(1, errors.Count);   // error route sees only the error
            Check.Equal("boom", errors[0].Message);
        }

        public void RouterSinkDisposesOwnedSinks()
        {
            bool disposed = false;
            var router = new RouterSink(ownsSinks: true);
            router.AddRoute(_ => true, new ProbeSink(() => disposed = true));
            router.Dispose();
            Check.True(disposed);
        }

        public void RateLimitedSuppressesFloodAndSummarizes()
        {
            var got = new List<LogEvent>();
            var now = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
            using var sink = new RateLimitedSink(new DelegateSink(got.Add), TimeSpan.FromSeconds(10), () => now);

            sink.Emit(Ev("flood"));                 // forwarded
            for (int i = 0; i < 4; i++) sink.Emit(Ev("flood")); // suppressed (same window)
            Check.Equal(1, got.Count);

            now = now.AddSeconds(11);               // window elapsed
            sink.Emit(Ev("flood"));                 // -> summary + forward
            Check.Equal(3, got.Count);
            Check.True(got[1].Message.Contains("repeated 4 times"), got[1].Message);
            Check.Equal("flood", got[2].Message);
        }

        public void RateLimitedPassesDistinctMessages()
        {
            var got = new List<LogEvent>();
            var now = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
            using var sink = new RateLimitedSink(new DelegateSink(got.Add), TimeSpan.FromSeconds(10), () => now);
            sink.Emit(Ev("a"));
            sink.Emit(Ev("b"));
            sink.Emit(Ev("c"));
            Check.True(got.Select(e => e.Message).SequenceEqual(new[] { "a", "b", "c" }));
        }

        public void RateLimitedFlushesOnDispose()
        {
            var got = new List<LogEvent>();
            var now = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
            var sink = new RateLimitedSink(new DelegateSink(got.Add), TimeSpan.FromSeconds(10), () => now);
            sink.Emit(Ev("x"));                 // forwarded
            sink.Emit(Ev("x")); sink.Emit(Ev("x")); // suppressed (2)
            sink.Dispose();                      // flushes the summary
            Check.Equal(2, got.Count);
            Check.True(got[1].Message.Contains("repeated 2 times"), got[1].Message);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => new FilterSink(null!, _ => true));
            Check.Throws<ArgumentNullException>(() => new FilterSink(new DelegateSink(_ => { }), null!));
            Check.Throws<ArgumentOutOfRangeException>(() => new RateLimitedSink(new DelegateSink(_ => { }), TimeSpan.Zero));
        }

        private sealed class ProbeSink : ILogSink
        {
            private readonly Action _onDispose;
            public ProbeSink(Action onDispose) => _onDispose = onDispose;
            public void Emit(LogEvent logEvent) { }
            public void Dispose() => _onDispose();
        }
    }
}
