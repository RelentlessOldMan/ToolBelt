// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Control
{
    /// <summary>
    /// Limits how fast a value may change: the output follows the target but moves at most <c>maxRise</c> units per
    /// second upward and <c>maxFall</c> downward. Use it to ramp setpoints (no step kicks into a controller), soften
    /// actuator commands, or reject impossible jumps in a reading. The first <see cref="Update"/> (or
    /// <see cref="Reset(double)"/>) sets the starting value. Not thread-safe.
    /// </summary>
    public sealed class SlewRateLimiter
    {
        private readonly double _maxRise, _maxFall;

        /// <param name="maxRisePerSecond">Largest upward change per second (+∞ for unlimited).</param>
        /// <param name="maxFallPerSecond">Largest downward change per second, as a positive number; defaults to the rise rate.</param>
        public SlewRateLimiter(double maxRisePerSecond, double? maxFallPerSecond = null)
        {
            double fall = maxFallPerSecond ?? maxRisePerSecond;
            if (!(maxRisePerSecond > 0)) throw new ArgumentOutOfRangeException(nameof(maxRisePerSecond), maxRisePerSecond, "Rate must be positive (+∞ for unlimited).");
            if (!(fall > 0)) throw new ArgumentOutOfRangeException(nameof(maxFallPerSecond), fall, "Rate must be positive (+∞ for unlimited).");
            _maxRise = maxRisePerSecond;
            _maxFall = fall;
        }

        /// <summary>The current (limited) output.</summary>
        public double Value { get; private set; }

        /// <summary>False until the first <see cref="Update"/> or <see cref="Reset(double)"/>.</summary>
        public bool HasValue { get; private set; }

        /// <summary>True when the last update had to clip the change.</summary>
        public bool IsLimiting { get; private set; }

        /// <summary>Moves toward <paramref name="target"/> by at most rate × <paramref name="dt"/> and returns the new output.</summary>
        public double Update(double target, double dt)
        {
            if (double.IsNaN(target) || double.IsInfinity(target)) throw new ArgumentOutOfRangeException(nameof(target), target, "Target must be finite.");
            if (!(dt >= 0) || double.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "Time step must be non-negative and finite.");
            if (!HasValue)
            {
                Reset(target);
                return Value;
            }
            double delta = target - Value;
            // An unlimited (∞) rate stays unlimited even for dt = 0, where ∞ × 0 would be NaN.
            double up = double.IsPositiveInfinity(_maxRise) ? _maxRise : _maxRise * dt;
            double down = double.IsPositiveInfinity(_maxFall) ? _maxFall : _maxFall * dt;
            // A finite rate whose step overflowed (both ∞ compared as no limit, so the output jumped to the target) is
            // compared and taken in halves. Only that side: halving every step (as an unlimited rise rate once forced) rounded
            // a subnormal step, so a fall of 3ε went 4ε. An overflowed gap needs no halves: it is past every finite step.
            bool halvesUp = double.IsInfinity(up) && !double.IsPositiveInfinity(_maxRise);
            bool halvesDown = double.IsInfinity(down) && !double.IsPositiveInfinity(_maxFall);
            double halfDelta = target * 0.5 - Value * 0.5;
            double halfUp = _maxRise * 0.5 * dt, halfDown = _maxFall * 0.5 * dt;
            bool rising = halvesUp ? halfDelta > halfUp : delta > up;
            bool falling = !rising && (halvesDown ? halfDelta < -halfDown : delta < -down);
            IsLimiting = rising || falling;
            Value = rising ? (halvesUp ? Value + halfUp + halfUp : Value + up)
                : falling ? (halvesDown ? Value - halfDown - halfDown : Value - down)
                : target;
            return Value;
        }

        /// <summary>Jumps straight to <paramref name="value"/>.</summary>
        public void Reset(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be finite.");
            Value = value;
            HasValue = true;
            IsLimiting = false;
        }

        /// <summary>Forgets the value; the next update starts from its target.</summary>
        public void Reset()
        {
            Value = 0;
            HasValue = false;
            IsLimiting = false;
        }
    }
}
