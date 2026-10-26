// ToolBelt drop-in — self-contained except for Guards/Guard.cs (copy that file too).
using System;
using ToolBelt.Guards;

namespace ToolBelt.Resilience
{
    /// <summary>
    /// A token-bucket rate limiter. The bucket holds up to <c>capacity</c> tokens and refills
    /// continuously at <c>refillTokensPerSecond</c>. Each <see cref="TryAcquire"/> refills for the time
    /// elapsed since the last call, then consumes tokens if enough are available. Time is read through an
    /// injectable clock so behavior is deterministic in tests; if that clock steps back, refilling resumes from the
    /// new time rather than after the clock passes the old one. Not thread-safe.
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
            // Written so NaN fails too.
            if (!(capacity > 0) || double.IsInfinity(capacity))
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive and finite.");
            if (!(refillTokensPerSecond > 0) || double.IsInfinity(refillTokensPerSecond))
                throw new ArgumentOutOfRangeException(nameof(refillTokensPerSecond), refillTokensPerSecond, "Refill rate must be positive and finite.");

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

        /// <summary>
        /// Attempts to consume <paramref name="count"/> tokens. Returns false (consuming nothing) if too few are available.
        /// A count above the capacity throws <see cref="ArgumentOutOfRangeException"/>: the bucket can never hold that many.
        /// </summary>
        public bool TryAcquire(int count = 1)
        {
            Guard.Positive(count);
            if (count > _capacity)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Count exceeds the bucket's capacity; it could never be acquired.");
            Refill(_clock());

            // A tolerance: refills summed in small steps fall short by rounding (ten 100 ms steps at 1/s left 0.9999999999999999),
            // which refused a token that was due and under-granted by 10% when polled every 10 ms.
            if (_tokens >= count - count * 1e-9)
            {
                _tokens = Math.Max(0, _tokens - count);
                return true;
            }
            return false;
        }

        private void Refill(DateTimeOffset now)
        {
            double elapsedSeconds = (now - _lastRefill).TotalSeconds;
            if (elapsedSeconds > 0)
                _tokens = Math.Min(_capacity, _tokens + elapsedSeconds * _refillPerSecond);
            // Moved back too when the clock stepped back; kept, nothing refilled until it passed the old time again.
            if (elapsedSeconds != 0) _lastRefill = now;
        }
    }
}
