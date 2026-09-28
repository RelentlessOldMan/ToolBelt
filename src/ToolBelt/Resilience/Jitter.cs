// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Resilience
{
    /// <summary>How much randomness to fold into a backoff delay.</summary>
    public enum JitterMode
    {
        /// <summary>No jitter — the delay is returned unchanged.</summary>
        None,
        /// <summary>Full jitter: a uniform random delay in <c>[0, delay]</c> (AWS "full jitter").</summary>
        Full,
        /// <summary>Equal jitter: half the delay plus a uniform random half in <c>[delay/2, delay]</c>.</summary>
        Equal,
    }

    /// <summary>
    /// Adds randomized jitter to a backoff delay to spread out retries and avoid thundering-herd
    /// synchronization. The randomness source is injected as a function returning a value in [0, 1), so
    /// results are deterministic in tests. Pairs with <see cref="RetryPolicy"/>.
    /// </summary>
    public static class Jitter
    {
        /// <summary>Applies <paramref name="mode"/> to <paramref name="delay"/> using <paramref name="random"/> (values in [0, 1)).</summary>
        public static TimeSpan Apply(TimeSpan delay, JitterMode mode, Func<double> random)
        {
            if (delay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must not be negative.");
            if (mode == JitterMode.None)
                return delay;
            if (random is null)
                throw new ArgumentNullException(nameof(random));

            double r = random();
            if (r < 0 || r >= 1)
                throw new ArgumentOutOfRangeException(nameof(random), r, "Random source must return a value in [0, 1).");

            double ms = delay.TotalMilliseconds;
            double result = mode switch
            {
                JitterMode.Full => ms * r,
                JitterMode.Equal => ms * 0.5 + ms * 0.5 * r,
                _ => ms,
            };
            return TimeSpan.FromMilliseconds(result);
        }
    }
}
