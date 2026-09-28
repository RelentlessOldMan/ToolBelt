// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Resilience
{
    /// <summary>
    /// A leading-edge throttle: <see cref="TryAcquire"/> succeeds at most once per <c>interval</c>. The
    /// first call always succeeds; subsequent calls succeed only once the interval has elapsed since the
    /// last success. Time is read through an injectable clock so behavior is deterministic in tests. Not
    /// thread-safe.
    /// </summary>
    public sealed class Throttle
    {
        private readonly TimeSpan _interval;
        private readonly Func<DateTimeOffset> _clock;
        private DateTimeOffset? _lastAcquired;

        public Throttle(TimeSpan interval, Func<DateTimeOffset>? clock = null)
        {
            if (interval < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must not be negative.");
            _interval = interval;
            _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        }

        /// <summary>Attempts to pass the throttle. Returns true (and records "now") if the interval has elapsed.</summary>
        public bool TryAcquire()
        {
            var now = _clock();
            if (_lastAcquired is null || now - _lastAcquired.Value >= _interval)
            {
                _lastAcquired = now;
                return true;
            }
            return false;
        }

        /// <summary>How long until the next acquire would succeed (zero if it would succeed now).</summary>
        public TimeSpan TimeUntilNext
        {
            get
            {
                if (_lastAcquired is null)
                    return TimeSpan.Zero;
                var elapsed = _clock() - _lastAcquired.Value;
                var remaining = _interval - elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }
}
