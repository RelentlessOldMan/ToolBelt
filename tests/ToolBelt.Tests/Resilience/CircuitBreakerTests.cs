using System;
using System.Threading.Tasks;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Resilience
{
    public sealed class CircuitBreakerTests
    {
        // A hand-cranked clock so every time-based transition is deterministic.
        private sealed class FakeClock
        {
            public DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public Func<DateTimeOffset> Func => () => Now;
            public void Advance(TimeSpan by) => Now += by;
        }

        private static Task<int> Fail() => throw new InvalidOperationException("boom");
        private static Task<int> Ok() => Task.FromResult(1);

        public async Task Opens_AfterThresholdConsecutiveFailures()
        {
            var clock = new FakeClock();
            var breaker = new CircuitBreaker(failureThreshold: 3, resetTimeout: TimeSpan.FromSeconds(30), clock.Func);

            for (int i = 0; i < 3; i++)
                await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync(_ => Fail()));

            Check.Equal(CircuitState.Open, breaker.State);
        }

        public async Task Open_ShortCircuits_WithoutInvokingOperation()
        {
            var clock = new FakeClock();
            var breaker = new CircuitBreaker(2, TimeSpan.FromSeconds(30), clock.Func);

            await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync(_ => Fail()));
            await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync(_ => Fail())); // opens

            bool invoked = false;
            await Check.ThrowsAsync<CircuitOpenException>(() => breaker.ExecuteAsync<int>(_ =>
            {
                invoked = true;
                return Ok();
            }));
            Check.False(invoked, "operation must not run while open");
        }

        public async Task Success_ResetsFailureCount()
        {
            var clock = new FakeClock();
            var breaker = new CircuitBreaker(3, TimeSpan.FromSeconds(30), clock.Func);

            await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync(_ => Fail()));
            await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync(_ => Fail()));
            await breaker.ExecuteAsync(_ => Ok()); // resets

            Check.Equal(0, breaker.ConsecutiveFailures);
            Check.Equal(CircuitState.Closed, breaker.State);
        }

        public async Task AfterTimeout_HalfOpen_Success_Closes()
        {
            var clock = new FakeClock();
            var breaker = new CircuitBreaker(1, TimeSpan.FromSeconds(30), clock.Func);

            await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync(_ => Fail())); // opens
            Check.Equal(CircuitState.Open, breaker.State);

            clock.Advance(TimeSpan.FromSeconds(31));
            Check.Equal(CircuitState.HalfOpen, breaker.State); // timeout elapsed

            int result = await breaker.ExecuteAsync(_ => Ok()); // trial succeeds → close
            Check.Equal(1, result);
            Check.Equal(CircuitState.Closed, breaker.State);
        }

        public async Task HalfOpen_TrialFailure_Reopens_AndRestartsTimer()
        {
            var clock = new FakeClock();
            var breaker = new CircuitBreaker(1, TimeSpan.FromSeconds(30), clock.Func);

            await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync(_ => Fail())); // opens
            clock.Advance(TimeSpan.FromSeconds(31));
            Check.Equal(CircuitState.HalfOpen, breaker.State);

            // Trial fails → back to Open, timer restarts from now.
            await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync(_ => Fail()));
            Check.Equal(CircuitState.Open, breaker.State);

            clock.Advance(TimeSpan.FromSeconds(29)); // not yet elapsed
            Check.Equal(CircuitState.Open, breaker.State);
            clock.Advance(TimeSpan.FromSeconds(2));  // now elapsed
            Check.Equal(CircuitState.HalfOpen, breaker.State);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new CircuitBreaker(0, TimeSpan.FromSeconds(1)));
            Check.Throws<ArgumentOutOfRangeException>(() => new CircuitBreaker(1, TimeSpan.FromSeconds(-1)));
        }
    }
}
