using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Resilience
{
    public sealed class RetryTests
    {
        // A recording delay: never sleeps, just captures what would have been waited.
        private static (RetryPolicy policy, List<TimeSpan> delays) PolicyWithRecordedDelays(RetryPolicy policy)
        {
            var delays = new List<TimeSpan>();
            policy.DelayAsync = (d, ct) => { delays.Add(d); return Task.CompletedTask; };
            return (policy, delays);
        }

        public async Task Succeeds_FirstTry_NoDelays()
        {
            var (policy, delays) = PolicyWithRecordedDelays(new RetryPolicy { MaxAttempts = 3 });
            int calls = 0;

            int result = await Retry.ExecuteAsync(_ => { calls++; return Task.FromResult(42); }, policy);

            Check.Equal(42, result);
            Check.Equal(1, calls);
            Check.Equal(0, delays.Count);
        }

        public async Task Retries_UntilSuccess()
        {
            var (policy, delays) = PolicyWithRecordedDelays(new RetryPolicy { MaxAttempts = 5 });
            int calls = 0;

            int result = await Retry.ExecuteAsync(_ =>
            {
                calls++;
                if (calls < 3) throw new InvalidOperationException("transient");
                return Task.FromResult(7);
            }, policy);

            Check.Equal(7, result);
            Check.Equal(3, calls);
            Check.Equal(2, delays.Count); // delayed before the 2 retries
        }

        public async Task Exhausts_And_Rethrows_LastException()
        {
            var (policy, delays) = PolicyWithRecordedDelays(new RetryPolicy { MaxAttempts = 3 });
            int calls = 0;

            var ex = await AssertThrowsAsync<InvalidOperationException>(() =>
                Retry.ExecuteAsync<int>(_ =>
                {
                    calls++;
                    throw new InvalidOperationException("attempt " + calls);
                }, policy));

            Check.Equal(3, calls);
            Check.Equal("attempt 3", ex.Message); // last failure surfaces
            Check.Equal(2, delays.Count);         // one delay before each of the 2 retries
        }

        public async Task NonRetryableException_ShortCircuits()
        {
            var (policy, delays) = PolicyWithRecordedDelays(new RetryPolicy { MaxAttempts = 5 });
            policy.ShouldRetry = ex => ex is TimeoutException; // only timeouts retry
            int calls = 0;

            await AssertThrowsAsync<InvalidOperationException>(() =>
                Retry.ExecuteAsync<int>(_ =>
                {
                    calls++;
                    throw new InvalidOperationException("fatal");
                }, policy));

            Check.Equal(1, calls);        // not retried
            Check.Equal(0, delays.Count);
        }

        public async Task ExponentialBackoff_DoublesAndCaps()
        {
            var (policy, delays) = PolicyWithRecordedDelays(new RetryPolicy
            {
                MaxAttempts = 6,
                BaseDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromMilliseconds(500),
                Backoff = BackoffStrategy.Exponential,
            });

            await AssertThrowsAsync<Exception>(() =>
                Retry.ExecuteAsync<int>(_ => throw new Exception("x"), policy));

            // base=100 → 100, 200, 400, 800→capped 500, 1600→capped 500
            var expected = new[] { 100.0, 200, 400, 500, 500 };
            Check.Equal(expected.Length, delays.Count);
            for (int i = 0; i < expected.Length; i++)
                Check.Close(expected[i], delays[i].TotalMilliseconds, 1e-6, $"delay {i}");
        }

        public async Task LinearBackoff_GrowsLinearly()
        {
            var (policy, delays) = PolicyWithRecordedDelays(new RetryPolicy
            {
                MaxAttempts = 4,
                BaseDelay = TimeSpan.FromMilliseconds(50),
                Backoff = BackoffStrategy.Linear,
            });

            await AssertThrowsAsync<Exception>(() =>
                Retry.ExecuteAsync<int>(_ => throw new Exception("x"), policy));

            var expected = new[] { 50.0, 100, 150 };
            Check.Equal(expected.Length, delays.Count);
            for (int i = 0; i < expected.Length; i++)
                Check.Close(expected[i], delays[i].TotalMilliseconds, 1e-6, $"delay {i}");
        }

        public async Task Cancellation_StopsRetrying()
        {
            var (policy, _) = PolicyWithRecordedDelays(new RetryPolicy { MaxAttempts = 10 });
            using var cts = new CancellationTokenSource();

            await AssertThrowsAsync<OperationCanceledException>(() =>
                Retry.ExecuteAsync<int>(_ =>
                {
                    cts.Cancel();
                    throw new InvalidOperationException("transient");
                }, policy, cts.Token));
        }

        public void DelayForAttempt_UnboundedMaxDelay_DoesNotOverflow()
        {
            var policy = new RetryPolicy
            {
                BaseDelay = TimeSpan.FromDays(1),
                MaxDelay = TimeSpan.MaxValue,
                Backoff = BackoffStrategy.Exponential,
            };
            // 2^59 days of milliseconds vastly exceeds the cap; must clamp to MaxDelay, not throw.
            Check.Equal(TimeSpan.MaxValue, policy.DelayForAttempt(60));
        }

        public async Task NonGenericOverload_RunsAndRetries()
        {
            // The result-less ExecuteAsync(Func<CancellationToken, Task>, ...) overload wraps the generic one;
            // a lambda returning plain Task binds here. Verify it actually retries and reports completion.
            var (policy, delays) = PolicyWithRecordedDelays(new RetryPolicy { MaxAttempts = 3 });
            int calls = 0;

            await Retry.ExecuteAsync(_ =>
            {
                calls++;
                if (calls < 2) throw new InvalidOperationException("transient");
                return Task.CompletedTask;
            }, policy);

            Check.Equal(2, calls);        // failed once, then succeeded
            Check.Equal(1, delays.Count); // one delay before the single retry
        }

        public async Task NonGenericOverload_NullOperation_Throws()
        {
            var policy = new RetryPolicy();
            await AssertThrowsAsync<ArgumentNullException>(
                () => Retry.ExecuteAsync((Func<CancellationToken, Task>)null!, policy));
        }

        // Local async-throws helper (Check.Throws is sync-only).
        private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException ex)
            {
                return ex;
            }
            throw new CheckFailedException($"expected {typeof(TException).Name}, but nothing was thrown.");
        }
    }
}
