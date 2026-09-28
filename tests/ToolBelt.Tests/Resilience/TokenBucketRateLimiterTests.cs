using System;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Resilience
{
    public sealed class TokenBucketRateLimiterTests
    {
        private sealed class FakeClock
        {
            public DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public Func<DateTimeOffset> Func => () => Now;
            public void Advance(TimeSpan by) => Now += by;
        }

        public void StartsFull_ThenDeniesWhenEmpty()
        {
            var clock = new FakeClock();
            var limiter = new TokenBucketRateLimiter(capacity: 3, refillTokensPerSecond: 1, clock.Func);

            Check.True(limiter.TryAcquire());
            Check.True(limiter.TryAcquire());
            Check.True(limiter.TryAcquire());
            Check.False(limiter.TryAcquire()); // bucket empty, no time passed
        }

        public void RefillsOverTime()
        {
            var clock = new FakeClock();
            var limiter = new TokenBucketRateLimiter(capacity: 2, refillTokensPerSecond: 2, clock.Func);

            Check.True(limiter.TryAcquire(2)); // drain
            Check.False(limiter.TryAcquire());

            clock.Advance(TimeSpan.FromSeconds(0.5)); // 0.5s * 2/s = 1 token
            Check.True(limiter.TryAcquire());
            Check.False(limiter.TryAcquire());
        }

        public void RefillIsCappedAtCapacity()
        {
            var clock = new FakeClock();
            var limiter = new TokenBucketRateLimiter(capacity: 5, refillTokensPerSecond: 10, clock.Func);

            Check.True(limiter.TryAcquire(5)); // empty
            clock.Advance(TimeSpan.FromSeconds(100)); // would refill 1000, but caps at 5
            Check.Close(5.0, limiter.AvailableTokens, 1e-9);
            Check.True(limiter.TryAcquire(5));
            Check.False(limiter.TryAcquire());
        }

        public void MultiTokenAcquire_AllOrNothing()
        {
            var clock = new FakeClock();
            var limiter = new TokenBucketRateLimiter(capacity: 5, refillTokensPerSecond: 1, clock.Func);

            Check.True(limiter.TryAcquire(3));
            Check.False(limiter.TryAcquire(3)); // only 2 left → nothing consumed
            Check.Close(2.0, limiter.AvailableTokens, 1e-9);
            Check.True(limiter.TryAcquire(2));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiter(0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiter(1, 0));
            var ok = new TokenBucketRateLimiter(1, 1);
            Check.Throws<ArgumentOutOfRangeException>(() => ok.TryAcquire(0));
        }
    }
}
