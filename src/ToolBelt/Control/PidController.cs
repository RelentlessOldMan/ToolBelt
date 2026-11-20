// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Control
{
    /// <summary>
    /// A proportional-integral-derivative controller with output clamping, conditional-integration
    /// anti-windup, and derivative-on-measurement (to avoid the derivative "kick" when the setpoint
    /// changes). The time step is passed explicitly to <see cref="Update"/>, so behaviour is fully
    /// deterministic and testable against a simulated plant. Not thread-safe.
    /// Anti-windup judges the whole tentative output: a step whose proportional or derivative part alone takes the output
    /// past a limit (with derivative on error, the kick of a setpoint step) integrates only up to that limit, or not at
    /// all when the output is already past it.
    /// </summary>
    public sealed class PidController
    {
        private readonly double _kp, _ki, _kd, _min, _max;
        private readonly bool _derivativeOnMeasurement;
        private double _integral, _lastError, _lastMeasurement;
        // The integral is held in units of 2^1200 (it is _integral / Scale / Scale) once it outgrows double: with a small Ki
        // and wide limits an integral past double.MaxValue was never taken, so the output stayed at 0 where it is 2e10.
        // Units of 2^600 still ran out past about 4e488 (Ki 1e-300, error 1e300 over dt 1e200 stayed at 0, not 1e200);
        // 2^1200 holds every integral whose term Ki·∫ is finite for a finite Ki.
        private bool _big;
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

            // Conditional integration: the integral step stops at the saturation limit. The direction is that of the
            // integral's contribution, ki·error, so reverse-acting (negative) gains unwind too. Skipping the whole step
            // instead stalled the loop short of the limit whenever one step would cross it (an I-only controller with
            // error·ki·dt past the limit never moved).
            // An error past double.MaxValue (setpoint and measurement far apart) made error·dt ∞ even where the step fits,
            // so the integral never moved: the step is formed from the halved difference.
            double step = double.IsInfinity(error) ? (Setpoint * 0.5 - measurement * 0.5) * dt * 2 : error * dt;
            double integral = _big ? double.PositiveInfinity : _integral + step;
            bool big = false;
            if (double.IsInfinity(integral) && _ki != 0)
            {
                // In units of 2^1200; for dt < 1 the product with dt comes first, which cannot overflow.
                double e = Setpoint * Scale - measurement * Scale;
                double stepBig = dt >= 1 ? e * (dt * Scale) : e * dt * Scale;
                integral = Shrink((_big ? _integral : _integral * Scale * Scale) + stepBig, out big);
            }
            double tentative = proportional + Term(integral, big) + derivative;
            // P and D overflowing with opposite signs made this NaN, so neither windup test fired and the integral took
            // the whole step past the limit; an overflowed term made it ∞ where the true output is inside the limits.
            if (double.IsNaN(tentative) || double.IsInfinity(tentative))
            {
                double scaled = ScaledOutput(measurement, dt, integral, big);
                tentative = double.IsNaN(scaled) ? DominantOutput(measurement, dt, integral, big) : scaled;
            }
            // By signs: ki·error underflowed to 0 at tiny scales (ki 1e-200), so neither test fired and the integral wound up.
            int push = Math.Sign(_ki) * Math.Sign(error);
            bool windupHigh = tentative > _max && push > 0;
            bool windupLow = tentative < _min && push < 0;
            // Finite inputs can still overflow error·dt or the sum; an infinite integral (or one with ki = 0, where it is
            // unused, and 0·∞ made every later output NaN) was kept for good.
            if (windupHigh || windupLow)
            {
                // The integral that puts the output exactly at the limit, taken only if it moves with the step (when the
                // output was already past the limit, it stays). This also covers an error·dt that overflowed.
                double limit = windupHigh ? _max : _min;
                double atLimit = (limit - proportional - derivative) / _ki;
                bool atBig = false;
                // With P or D overflowed the difference is formed in units of 2^-600 (∞ there was always discarded); an
                // integral past double.MaxValue is kept in units of 2^1200. The scaling comes before the division by a
                // small Ki, where the quotient overflowed
                // (a tiny Ki is scaled up instead, as gap·2^-600 underflowed).
                if (double.IsNaN(atLimit) || double.IsInfinity(atLimit))
                {
                    double gap = limit * Scale - ScaledProportional(measurement) - ScaledDerivative(measurement, dt);
                    atLimit = Shrink(Math.Abs(_ki) < 1e-100 ? gap / (_ki / Scale) : gap / _ki * Scale, out atBig);
                }
                // By signs: the product underflowed (5e-201 × 1e-200), so an I-only loop at tiny scales never moved.
                double moved = atBig || _big
                    ? (atBig ? atLimit : atLimit * Scale * Scale) - (_big ? _integral : _integral * Scale * Scale)
                    : atLimit - _integral;
                if (!double.IsNaN(atLimit) && !double.IsInfinity(atLimit) && Math.Sign(moved) * Math.Sign(error) > 0)
                {
                    _integral = atLimit;
                    _big = atBig;
                }
            }
            else if (_ki != 0 && !double.IsInfinity(integral) && !double.IsNaN(integral))
            {
                _integral = integral;
                _big = big;
            }

            double output = proportional + Term(_integral, _big) + derivative;
            // A term that overflowed (an error past double.MaxValue times a tiny gain, or ∞ − ∞ between terms) is summed
            // again in units of 2^-600, where finite inputs and gains below about 1e180 can't overflow.
            if (double.IsNaN(output) || double.IsInfinity(output))
            {
                double scaled = ScaledOutput(measurement, dt, _integral, _big);
                output = double.IsNaN(scaled) ? DominantOutput(measurement, dt, _integral, _big) : scaled;
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
            _big = false;
            _lastError = 0;
            _lastMeasurement = 0;
            _lastSetpoint = 0;
            _hasLast = false;
        }

        private const double Scale = 2.4099198651028841e-181;                    // 2^-600

        // An integral given in units of 2^1200 (big) back in ordinary units where it fits: the big form is kept only while
        // it must be. The input is in units of 2^1200.
        private static double Shrink(double integralBig, out bool big)
        {
            double plain = integralBig / Scale / Scale;
            big = double.IsInfinity(plain) || Math.Abs(plain) > 1e300;
            return big ? integralBig : plain;
        }

        // A tiny Ki is scaled up first: Ki·integral underflowed where the term is finite.
        private double Term(double integral, bool big) =>
            !big ? _ki * integral : Math.Abs(_ki) < 1e-100 ? _ki / Scale * integral / Scale : _ki * integral / Scale / Scale;

        private double ScaledOutput(double measurement, double dt, double integral, bool big) =>
            (ScaledProportional(measurement) + ScaledIntegral(integral, big) + ScaledDerivative(measurement, dt)) / Scale;

        // Ki·integral in units of 2^-600.
        private double ScaledIntegral(double integral, bool big) =>
            !big ? _ki * Scale * integral : Math.Abs(_ki) < 1e-100 ? _ki / Scale * integral : _ki * integral / Scale;

        // Terms that overflow with opposite signs even in units of 2^-600 (kp 1e200 against a derivative over a subnormal
        // dt) summed to NaN, which the clamp passed through. Their magnitudes are compared as logarithms: the largest term
        // decides the sign of the (overflowing) output. Exactly equal magnitudes give 0.
        private double DominantOutput(double measurement, double dt, double integral, bool big)
        {
            const double Ln2 = 0.69314718055994531;
            const double S = Scale;
            double best = double.NegativeInfinity;
            int sign = 0;
            void Add(int s, double logMagnitude)
            {
                if (s == 0 || double.IsNaN(logMagnitude)) return;
                if (logMagnitude > best) { best = logMagnitude; sign = s; }
                else if (logMagnitude == best && s != sign) sign = 0;
            }
            double p = Setpoint * 0.5 - measurement * 0.5;
            Add(Math.Sign(_kp) * Math.Sign(p), Math.Log(Math.Abs(_kp)) + Math.Log(Math.Abs(p)) + Ln2);
            Add(Math.Sign(_ki) * Math.Sign(integral), Math.Log(Math.Abs(_ki)) + Math.Log(Math.Abs(integral)) - (big ? 2 * Math.Log(S) : 0));
            if (_hasLast && _kd != 0)
            {
                double d = _derivativeOnMeasurement
                    ? -(measurement * S - _lastMeasurement * S)
                    : (Setpoint * S - measurement * S) - (_lastSetpoint * S - _lastMeasurement * S);
                Add(Math.Sign(_kd) * Math.Sign(d), Math.Log(Math.Abs(_kd)) + Math.Log(Math.Abs(d)) - Math.Log(S) - Math.Log(dt));
            }
            return sign == 0 ? 0 : sign * double.PositiveInfinity;
        }

        private double ScaledProportional(double measurement) =>
            _kp == 0 ? 0 : _kp * (Setpoint * Scale - measurement * Scale);

        private double ScaledDerivative(double measurement, double dt)
        {
            const double S = Scale;
            if (!_hasLast || _kd == 0) return 0;
            return _derivativeOnMeasurement
                ? -_kd * (measurement * S - _lastMeasurement * S) / dt
                : _kd * ((Setpoint * S - measurement * S) - (_lastSetpoint * S - _lastMeasurement * S)) / dt;
        }

        private static void CheckFinite(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentOutOfRangeException(name, v, "Value must be finite.");
        }

        private double Clamp(double v) => v < _min ? _min : (v > _max ? _max : v);
    }
}
