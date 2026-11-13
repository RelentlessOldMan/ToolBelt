// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Control
{
    /// <summary>
    /// A proportional-integral-derivative controller with output clamping, conditional-integration
    /// anti-windup, and derivative-on-measurement (to avoid the derivative "kick" when the setpoint
    /// changes). The time step is passed explicitly to <see cref="Update"/>, so behaviour is fully
    /// deterministic and testable against a simulated plant. Not thread-safe.
    /// </summary>
    public sealed class PidController
    {
        private readonly double _kp, _ki, _kd, _min, _max;
        private readonly bool _derivativeOnMeasurement;
        private double _integral, _lastError, _lastMeasurement;
        private double _lastSetpoint;   // the scaled retry rebuilds the last error from it: a stored error that overflowed stays ∞
        private bool _hasLast;

        public PidController(double kp, double ki, double kd,
            double outputMin = double.NegativeInfinity, double outputMax = double.PositiveInfinity,
            bool derivativeOnMeasurement = true)
        {
            CheckFinite(kp, nameof(kp)); CheckFinite(ki, nameof(ki)); CheckFinite(kd, nameof(kd));
            if (double.IsNaN(outputMin)) throw new ArgumentOutOfRangeException(nameof(outputMin), outputMin, "Output limit must not be NaN.");
            if (double.IsNaN(outputMax)) throw new ArgumentOutOfRangeException(nameof(outputMax), outputMax, "Output limit must not be NaN.");
            if (outputMin > outputMax) throw new ArgumentException("outputMin must not exceed outputMax.", nameof(outputMin));
            // Both limits at +∞ (or both at −∞) pinned every output to infinity.
            if (double.IsPositiveInfinity(outputMin)) throw new ArgumentOutOfRangeException(nameof(outputMin), outputMin, "The lower output limit must be below +∞.");
            if (double.IsNegativeInfinity(outputMax)) throw new ArgumentOutOfRangeException(nameof(outputMax), outputMax, "The upper output limit must be above −∞.");
            _kp = kp; _ki = ki; _kd = kd;
            _min = outputMin; _max = outputMax;
            _derivativeOnMeasurement = derivativeOnMeasurement;
        }

        /// <summary>The target value the controller drives the measurement toward. Must be finite.</summary>
        public double Setpoint
        {
            get => _setpoint;
            set { CheckFinite(value, nameof(value)); _setpoint = value; }
        }

        private double _setpoint;

        /// <summary>
        /// Computes the control output for the latest <paramref name="measurement"/> after <paramref name="dt"/> seconds.
        /// A non-finite measurement or time step is rejected (and leaves the state untouched) rather than poisoning the
        /// integral for good; skip a bad sensor reading at the call site.
        /// </summary>
        public double Update(double measurement, double dt)
        {
            if (!(dt > 0) || double.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "Time step must be positive and finite.");
            CheckFinite(measurement, nameof(measurement));

            double error = Setpoint - measurement;
            // A zero gain contributes nothing: 0 · ∞ (an overflowed error or change) made the output NaN.
            double proportional = _kp == 0 ? 0 : _kp * error;

            double derivative = 0;
            if (_hasLast && _kd != 0)
                derivative = _derivativeOnMeasurement
                    ? -_kd * (measurement - _lastMeasurement) / dt
                    : _kd * (error - _lastError) / dt;

            // Conditional integration: skip the integral step when it would drive further into saturation. The direction
            // is that of the integral's contribution, ki·error, so reverse-acting (negative) gains unwind too.
            double integral = _integral + error * dt;
            double tentative = proportional + _ki * integral + derivative;
            double push = _ki * error;
            bool windupHigh = tentative > _max && push > 0;
            bool windupLow = tentative < _min && push < 0;
            // Finite inputs can still overflow error·dt or the sum; an infinite integral (or one with ki = 0, where it is
            // unused, and 0·∞ made every later output NaN) was kept for good.
            if (!windupHigh && !windupLow && _ki != 0 && !double.IsInfinity(integral))
                _integral = integral;

            double output = proportional + _ki * _integral + derivative;
            // A term that overflowed (an error past double.MaxValue times a tiny gain, or ∞ − ∞ between terms) is summed
            // again in units of 2^-600, where finite inputs and gains below about 1e180 can't overflow.
            if (double.IsNaN(output) || double.IsInfinity(output))
            {
                double scaled = ScaledOutput(measurement, dt);
                if (!double.IsNaN(scaled)) output = scaled;
            }

            _lastError = error;
            _lastMeasurement = measurement;
            _lastSetpoint = Setpoint;
            _hasLast = true;

            return Clamp(output);
        }

        /// <summary>Clears the integral term and derivative history.</summary>
        public void Reset()
        {
            _integral = 0;
            _lastError = 0;
            _lastMeasurement = 0;
            _lastSetpoint = 0;
            _hasLast = false;
        }

        private double ScaledOutput(double measurement, double dt)
        {
            const double S = 2.4099198651028841e-181;                           // 2^-600
            double p = _kp == 0 ? 0 : _kp * (Setpoint * S - measurement * S);
            double d = 0;
            if (_hasLast && _kd != 0)
                d = _derivativeOnMeasurement
                    ? -_kd * (measurement * S - _lastMeasurement * S) / dt
                    : _kd * ((Setpoint * S - measurement * S) - (_lastSetpoint * S - _lastMeasurement * S)) / dt;
            double sum = p + _ki * S * _integral + d;
            return sum / S;
        }

        private static void CheckFinite(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentOutOfRangeException(name, v, "Value must be finite.");
        }

        private double Clamp(double v) => v < _min ? _min : (v > _max ? _max : v);
    }
}
