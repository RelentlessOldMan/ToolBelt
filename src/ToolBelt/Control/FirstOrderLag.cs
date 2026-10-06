// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Control
{
    /// <summary>
    /// A first-order low-pass defined by a time constant τ rather than a per-sample factor, so it behaves the same at any
    /// (even irregular) update rate: each <see cref="Update"/> moves the output a fraction 1 − e^(−dt/τ) of the way to the
    /// input, the exact response of an RC filter to an input held over the step. The output reaches 63.2% of a step
    /// after τ seconds; the −3 dB frequency is 1/(2πτ). The first sample initialises the output. Not thread-safe.
    /// </summary>
    public sealed class FirstOrderLag
    {
        public FirstOrderLag(double timeConstant)
        {
            if (!(timeConstant >= 0) || double.IsInfinity(timeConstant)) throw new ArgumentOutOfRangeException(nameof(timeConstant), timeConstant, "Time constant must be non-negative and finite (0 = pass-through).");
            TimeConstant = timeConstant;
        }

        /// <summary>A lag with its −3 dB point at <paramref name="cutoffHz"/>.</summary>
        public static FirstOrderLag FromCutoff(double cutoffHz)
        {
            if (!(cutoffHz > 0) || double.IsInfinity(cutoffHz)) throw new ArgumentOutOfRangeException(nameof(cutoffHz), cutoffHz, "Cutoff must be positive and finite.");
            return new FirstOrderLag(1 / (2 * Math.PI * cutoffHz));
        }

        public double TimeConstant { get; }

        public double Value { get; private set; }

        public bool HasValue { get; private set; }

        /// <summary>Advances by <paramref name="dt"/> seconds toward <paramref name="input"/> and returns the output.</summary>
        public double Update(double input, double dt)
        {
            if (double.IsNaN(input) || double.IsInfinity(input)) throw new ArgumentOutOfRangeException(nameof(input), input, "Input must be finite.");
            if (!(dt >= 0) || double.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "Time step must be non-negative and finite.");
            if (!HasValue)
            {
                Value = input;
                HasValue = true;
                return Value;
            }
            double alpha = TimeConstant == 0 ? 1 : -Expm1(-dt / TimeConstant);   // 1 − e^(−dt/τ), accurate for tiny dt
            Value += alpha * (input - Value);
            return Value;
        }

        public void Reset(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be finite.");
            Value = value;
            HasValue = true;
        }

        public void Reset()
        {
            Value = 0;
            HasValue = false;
        }

        // e^x − 1 without cancellation for small |x| (Math has no expm1, on either target).
        private static double Expm1(double x)
        {
            if (Math.Abs(x) < 1e-5) return x + x * x / 2 + x * x * x / 6;
            return Math.Exp(x) - 1;
        }
    }
}
