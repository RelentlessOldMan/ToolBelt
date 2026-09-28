using System;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Resilience
{
    public sealed class ThrottleTests
    {
        private sealed class FakeClock
        {
            public DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public Func<DateTimeOffset> Func => () => Now;
            public void Advance(TimeSpan by) => Now += by;
        }

        public void FirstAcquire_Succeeds()
        {
            var clock = new FakeClock();
            var throttle = new Throttle(TimeSpan.FromSeconds(1), clock.Func);
            Check.True(throttle.TryAcquire());
        }

        public void SecondImmediateAcquire_Fails()
        {
            var clock = new FakeClock();
            var throttle = new Throttle(TimeSpan.FromSeconds(1), clock.Func);
            Check.True(throttle.TryAcquire());
            Check.False(throttle.TryAcquire());
        }

        public void SucceedsAgainAfterInterval()
        {
            var clock = new FakeClock();
            var throttle = new Throttle(TimeSpan.FromSeconds(1), clock.Func);

            Check.True(throttle.TryAcquire());
            clock.Advance(TimeSpan.FromMilliseconds(999));
            Check.False(throttle.TryAcquire());
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Check.True(throttle.TryAcquire()); // exactly at the interval boundary
        }

        public void TimeUntilNext_Decreases()
        {
            var clock = new FakeClock();
            var throttle = new Throttle(TimeSpan.FromSeconds(10), clock.Func);

            Check.Equal(TimeSpan.Zero, throttle.TimeUntilNext); // nothing acquired yet
            throttle.TryAcquire();
            Check.Equal(TimeSpan.FromSeconds(10), throttle.TimeUntilNext);
            clock.Advance(TimeSpan.FromSeconds(4));
            Check.Equal(TimeSpan.FromSeconds(6), throttle.TimeUntilNext);
            clock.Advance(TimeSpan.FromSeconds(6));
            Check.Equal(TimeSpan.Zero, throttle.TimeUntilNext);
        }

        public void NegativeInterval_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new Throttle(TimeSpan.FromSeconds(-1)));
        }

        public void ZeroInterval_AlwaysAllows()
        {
            var clock = new FakeClock();
            var throttle = new Throttle(TimeSpan.Zero, clock.Func);
            Check.True(throttle.TryAcquire());
            Check.True(throttle.TryAcquire()); // no wait required
        }
    }
}
