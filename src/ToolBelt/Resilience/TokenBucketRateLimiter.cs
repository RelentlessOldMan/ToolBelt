// ToolBelt drop-in — self-contained except for Guards/Guard.cs (copy that file too).
using System;
using ToolBelt.Guards;

namespace ToolBelt.Resilience
{
    /// <summary>
    /// A token-bucket rate limiter. The bucket holds up to <c>capacity</c> tokens and refills
    /// continuously at <c>refillTokensPerSecond</c>. Each <see cref="TryAcquire"/> refills for the time
    /// elapsed since the last call, then consumes tokens if enough are available. Time is read through an
    /// injectable clock so behavior is deterministic in tests. Not thread-safe.
    /// </summary>
    public sealed class TokenBucketRateLimiter
    {
        private readonly double _capacity;
        private readonly double _refillPerSecond;
        private readonly Func<DateTimeOffset> _clock;

        private double _tokens;
        private DateTimeOffset _lastRefill;

        /// <param name="capacity">Maximum tokens the bucket holds (also the initial fill).</param>
        /// <param name="refillTokensPerSecond">Tokens added per second, up to the capacity.</param>
        /// <param name="clock">Time source; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
        public TokenBucketRateLimiter(double capacity, double refillTokensPerSecond, Func<DateTimeOffset>? clock = null)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            if (refillTokensPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(refillTokensPerSecond), refillTokensPerSecond, "Refill rate must be positive.");

            _capacity = capacity;
            _refillPerSecond = refillTokensPerSecond;
            _clock = clock ?? (static () => DateTimeOffset.UtcNow);
            _tokens = capacity;      // start full
            _lastRefill = _clock();
        }

        /// <summary>Tokens currently available (after accounting for elapsed refill). Does not consume.</summary>
        public double AvailableTokens
        {
            get
            {
                Refill(_clock());
                return _tokens;
            }
        }

        /// <summary>Attempts to consume <paramref name="count"/> tokens. Returns false (consuming nothing) if too few are available.</summary>
        public bool TryAcquire(int count = 1)
        {
            Guard.Positive(count);
            Refill(_clock());

            if (_tokens >= count)
            {
                _tokens -= count;
                return true;
            }
            return false;
        }

        private void Refill(DateTimeOffset now)
        {
            double elapsedSeconds = (now - _lastRefill).TotalSeconds;
            if (elapsedSeconds > 0)
            {
                _tokens = Math.Min(_capacity, _tokens + elapsedSeconds * _refillPerSecond);
                _lastRefill = now;
            }
        }
    }
}
