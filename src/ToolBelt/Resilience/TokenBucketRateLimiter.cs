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
        // What _tokens could not hold: above 2^53 a double does not count whole tokens, so taking one left the bucket as
        // full as before and the limiter granted everything. The held count is _tokens + _tokensLow, kept exact.
        private double _tokensLow;
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
        /// A count above the capacity (by more than the rounding tolerance a grant allows) throws <see cref="ArgumentOutOfRangeException"/>: the bucket can never hold that many.
        /// </summary>
        public bool TryAcquire(int count = 1)
        {
            Guard.Positive(count);
            // With the grant's tolerance: a capacity of 2.9999999995 refused 3, which a full bucket would grant.
            if (Needed(count) > _capacity)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Count exceeds the bucket's capacity; it could never be acquired.");
            DateTimeOffset now = _clock();
            double tokens = Refill(now);

            // A tolerance: refills summed in small steps fall short by rounding (ten 100 ms steps at 1/s left 0.9999999999999999),
            // which refused a token that was due and under-granted by 10% when polled every 10 ms. Capped at a millionth of a
            // token: in proportion to the count, int.MaxValue was granted with two tokens short.
            if (tokens >= Needed(count))
            {
                double high = _tokens, low = _tokensLow;
                Add(ref high, ref low, (now - _lastRefill).TotalSeconds * _refillPerSecond);
                if (high > _capacity || (high == _capacity && low > 0)) { high = _capacity; low = 0; }
                Add(ref high, ref low, -count);
                if (high + low < 0) { high = 0; low = 0; } // the tolerance may grant a hair more than held
                _tokens = high;
                _tokensLow = low;
                _lastRefill = now;
                return true;
            }
            return false;
        }

        // high + low += x, with what high cannot hold carried in low (Knuth's two-sum), so the pair stays exact.
        private static void Add(ref double high, ref double low, double x)
        {
            double s = high + x, bp = s - high;
            if (double.IsInfinity(s)) { high = s; low = 0; return; } // a refill past double.MaxValue fills the bucket
            double e = (high - (s - bp)) + (x - bp) + low;
            high = s + e;
            low = e - (high - s);
        }

        private static double Needed(int count) => count - Math.Min(count, 1000) * 1e-9;

        // The tokens held now. The base moves only when the bucket fills (time past that adds nothing) or the clock steps
        // back from the latest time seen: the refill up to that time is kept, and it restarts from the new time.
        private double Refill(DateTimeOffset now)
        {
            if (now < _seen)
            {
                Add(ref _tokens, ref _tokensLow, (_seen - _lastRefill).TotalSeconds * _refillPerSecond);
                if (_tokens > _capacity || (_tokens == _capacity && _tokensLow > 0)) { _tokens = _capacity; _tokensLow = 0; }
                _lastRefill = now;
            }
            _seen = now;
            double high = _tokens, low = _tokensLow;
            Add(ref high, ref low, (now - _lastRefill).TotalSeconds * _refillPerSecond);
            // Full by the exact count: the rounded one read a bucket a few tokens short of 1e17 as full, and refilled them.
            double tokens = Math.Min(_capacity, high + low);
            if (high > _capacity || (high == _capacity && low >= 0))
            {
                _tokens = _capacity;
                _tokensLow = 0;
                _lastRefill = now;
            }
            return tokens;
        }
    }
}
