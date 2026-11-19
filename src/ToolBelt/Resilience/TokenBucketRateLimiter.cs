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

        // The tokens held at _lastRefill; the refill since is added on each read, not stored. Stored at every poll, a refill
        // too small to change a large count was lost with its time: 1e16 tokens polled each millisecond never refilled.
        private double _tokens;
        private DateTimeOffset _lastRefill;
        private DateTimeOffset _seen;   // the latest clock reading, to tell a clock stepped back

        /// <param name="capacity">Maximum tokens the bucket holds (also the initial fill).</param>
        /// <param name="refillTokensPerSecond">Tokens added per second, up to the capacity.</param>
        /// <param name="clock">Time source; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
        public TokenBucketRateLimiter(double capacity, double refillTokensPerSecond, Func<DateTimeOffset>? clock = null)
        {
            // Written so NaN fails too.
            // At least one token: below it no count (an int of 1 or more) could ever be acquired, and every TryAcquire threw.
            if (!(capacity >= 1) || double.IsInfinity(capacity))
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1 and finite.");
            if (!(refillTokensPerSecond > 0) || double.IsInfinity(refillTokensPerSecond))
                throw new ArgumentOutOfRangeException(nameof(refillTokensPerSecond), refillTokensPerSecond, "Refill rate must be positive and finite.");

            _capacity = capacity;
            _refillPerSecond = refillTokensPerSecond;
            _clock = clock ?? (static () => DateTimeOffset.UtcNow);
            _tokens = capacity;      // start full
            _lastRefill = _seen = _clock();
        }

        /// <summary>Tokens currently available (after accounting for elapsed refill). Does not consume.</summary>
        public double AvailableTokens
        {
            get
            {
                return Refill(_clock());
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
            DateTimeOffset now = _clock();
            double tokens = Refill(now);

            // A tolerance: refills summed in small steps fall short by rounding (ten 100 ms steps at 1/s left 0.9999999999999999),
            // which refused a token that was due and under-granted by 10% when polled every 10 ms. Capped at a millionth of a
            // token: in proportion to the count, int.MaxValue was granted with two tokens short.
            if (tokens >= count - Math.Min(count, 1000) * 1e-9)
            {
                _tokens = Math.Max(0, tokens - count);
                _lastRefill = now;
                return true;
            }
            return false;
        }

        // The tokens held now. The base moves only when the bucket fills (time past that adds nothing) or the clock steps
        // back from the latest time seen: the refill up to that time is kept, and it restarts from the new time.
        private double Refill(DateTimeOffset now)
        {
            if (now < _seen)
            {
                _tokens = Math.Min(_capacity, _tokens + (_seen - _lastRefill).TotalSeconds * _refillPerSecond);
                _lastRefill = now;
            }
            _seen = now;
            double tokens = Math.Min(_capacity, _tokens + (now - _lastRefill).TotalSeconds * _refillPerSecond);
            if (tokens >= _capacity)
            {
                _tokens = _capacity;
                _lastRefill = now;
            }
            return tokens;
        }
    }
}
