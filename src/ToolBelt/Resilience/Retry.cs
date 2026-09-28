// ToolBelt drop-in — self-contained except for Guards/Guard.cs (copy that file too).
using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Guards;

namespace ToolBelt.Resilience
{
    /// <summary>How the delay between retry attempts grows.</summary>
    public enum BackoffStrategy
    {
        /// <summary>The same delay before every retry.</summary>
        Constant,
        /// <summary>Delay grows linearly: base, 2·base, 3·base, ...</summary>
        Linear,
        /// <summary>Delay doubles each attempt: base, 2·base, 4·base, ... (capped at <see cref="RetryPolicy.MaxDelay"/>).</summary>
        Exponential,
    }

    /// <summary>Configuration for <see cref="Retry"/>. All time-based behavior is driven through an injectable delay delegate so tests are deterministic.</summary>
    public sealed class RetryPolicy
    {
        /// <summary>Total attempts, including the first. Must be at least 1. A value of 1 means "no retries".</summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>The base delay used by the backoff strategy.</summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>Upper bound on any single computed delay (applies to <see cref="BackoffStrategy.Exponential"/> in particular).</summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>The backoff shape between attempts.</summary>
        public BackoffStrategy Backoff { get; set; } = BackoffStrategy.Exponential;

        /// <summary>Decides whether a thrown exception is retryable. Default: retry any exception.</summary>
        public Func<Exception, bool> ShouldRetry { get; set; } = static _ => true;

        /// <summary>
        /// The delay implementation. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
        /// Tests replace this with a delegate that records the requested delay and returns immediately, so
        /// no wall-clock time passes.
        /// </summary>
        public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } =
            static (delay, ct) => Task.Delay(delay, ct);

        /// <summary>Computes the delay before the retry that follows a failed attempt (1-based attempt number).</summary>
        public TimeSpan DelayForAttempt(int attempt)
        {
            Guard.Positive(attempt);
            double baseMs = BaseDelay.TotalMilliseconds;
            double ms = Backoff switch
            {
                BackoffStrategy.Constant => baseMs,
                BackoffStrategy.Linear => baseMs * attempt,
                BackoffStrategy.Exponential => baseMs * Math.Pow(2, attempt - 1),
                _ => baseMs,
            };

            // Return MaxDelay directly when the computed delay meets/exceeds the cap or is non-finite
            // (exponential growth overflows double to Infinity). Reconstructing via FromMilliseconds at
            // the extreme could itself overflow the TimeSpan range.
            double capMs = MaxDelay.TotalMilliseconds;
            if (double.IsNaN(ms) || ms >= capMs)
                return MaxDelay;
            return TimeSpan.FromMilliseconds(ms);
        }
    }

    /// <summary>
    /// Retries a delegate according to a <see cref="RetryPolicy"/>. When every attempt fails, the final
    /// exception is rethrown with its original stack trace preserved.
    /// </summary>
    public static class Retry
    {
        /// <summary>Runs an async operation with retries, returning its result.</summary>
        public static async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            RetryPolicy policy,
            CancellationToken cancellationToken = default)
        {
            Guard.NotNull(operation);
            Guard.NotNull(policy);
            Guard.Positive(policy.MaxAttempts, nameof(policy) + "." + nameof(policy.MaxAttempts));

            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await operation(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < policy.MaxAttempts && policy.ShouldRetry(ex))
                {
                    var delay = policy.DelayForAttempt(attempt);
                    await policy.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Runs an async operation with retries (no return value).</summary>
        public static Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            RetryPolicy policy,
            CancellationToken cancellationToken = default)
        {
            Guard.NotNull(operation);
            return ExecuteAsync<bool>(async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            }, policy, cancellationToken);
        }
    }
}
