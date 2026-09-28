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
        private bool _hasLast;

        public PidController(double kp, double ki, double kd,
            double outputMin = double.NegativeInfinity, double outputMax = double.PositiveInfinity,
            bool derivativeOnMeasurement = true)
        {
            if (outputMin > outputMax) throw new ArgumentException("outputMin must not exceed outputMax.");
            _kp = kp; _ki = ki; _kd = kd;
            _min = outputMin; _max = outputMax;
            _derivativeOnMeasurement = derivativeOnMeasurement;
        }

        /// <summary>The target value the controller drives the measurement toward.</summary>
        public double Setpoint { get; set; }

        /// <summary>Computes the control output for the latest <paramref name="measurement"/> after <paramref name="dt"/> seconds.</summary>
        public double Update(double measurement, double dt)
        {
            if (dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt), dt, "Time step must be positive.");

            double error = Setpoint - measurement;
            double proportional = _kp * error;

            double derivative = 0;
            if (_hasLast)
                derivative = _derivativeOnMeasurement
                    ? -_kd * (measurement - _lastMeasurement) / dt
                    : _kd * (error - _lastError) / dt;

            // Conditional integration: skip the integral step when it would drive further into saturation.
            double tentative = proportional + _ki * (_integral + error * dt) + derivative;
            bool windupHigh = tentative > _max && error > 0;
            bool windupLow = tentative < _min && error < 0;
            if (!windupHigh && !windupLow)
                _integral += error * dt;

            double output = proportional + _ki * _integral + derivative;

            _lastError = error;
            _lastMeasurement = measurement;
            _hasLast = true;

            return Clamp(output);
        }

        /// <summary>Clears the integral term and derivative history.</summary>
        public void Reset()
        {
            _integral = 0;
            _lastError = 0;
            _lastMeasurement = 0;
            _hasLast = false;
        }

        private double Clamp(double v) => v < _min ? _min : (v > _max ? _max : v);
    }
}
