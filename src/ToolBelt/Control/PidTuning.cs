// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Control
{
    /// <summary>
    /// A first-order-plus-dead-time process model, K·e^(−θs) / (τs + 1): after a delay θ the output moves
    /// exponentially (time constant τ) toward K × the input change. Most thermal, flow and level loops are described
    /// well enough by it to tune a PID controller.
    /// </summary>
    public readonly struct FopdtModel
    {
        public FopdtModel(double gain, double timeConstant, double deadTime)
        {
            if (gain == 0 || double.IsNaN(gain) || double.IsInfinity(gain)) throw new ArgumentOutOfRangeException(nameof(gain), gain, "Gain must be non-zero and finite.");
            if (!(timeConstant > 0) || double.IsInfinity(timeConstant)) throw new ArgumentOutOfRangeException(nameof(timeConstant), timeConstant, "Time constant must be positive and finite.");
            if (!(deadTime >= 0) || double.IsInfinity(deadTime)) throw new ArgumentOutOfRangeException(nameof(deadTime), deadTime, "Dead time must be non-negative and finite.");
            Gain = gain;
            TimeConstant = timeConstant;
            DeadTime = deadTime;
        }

        /// <summary>Steady-state output change per unit input change (K).</summary>
        public double Gain { get; }

        /// <summary>τ, in the time unit of the data it was fitted from.</summary>
        public double TimeConstant { get; }

        /// <summary>θ, the delay before the output starts to move.</summary>
        public double DeadTime { get; }

        /// <summary>The model's output change at <paramref name="time"/> after an input step of <paramref name="inputStep"/>.</summary>
        public double StepResponse(double time, double inputStep = 1)
            => time <= DeadTime ? 0 : Gain * inputStep * (1 - Math.Exp(-(time - DeadTime) / TimeConstant));

        /// <summary>
        /// Fits a model to an open-loop step test with Smith's two-point method: the times the response reaches 28.3% and
        /// 63.2% of its total change pin down τ and θ exactly for a true FOPDT process. <paramref name="times"/>[0] is the
        /// instant the input stepped by <paramref name="inputStep"/>; the final value defaults to the last sample (record
        /// until the output has settled, or pass the steady-state value).
        /// </summary>
        public static FopdtModel FitStep(IReadOnlyList<double> times, IReadOnlyList<double> values, double inputStep,
            double? initialValue = null, double? finalValue = null)
        {
            if (times is null) throw new ArgumentNullException(nameof(times));
            if (values is null) throw new ArgumentNullException(nameof(values));
            int n = values.Count;
            if (times.Count != n) throw new ArgumentException("Times and values must have the same length.", nameof(times));
            if (n < 3) throw new ArgumentException("At least three samples are required.", nameof(values));
            if (inputStep == 0 || double.IsNaN(inputStep) || double.IsInfinity(inputStep)) throw new ArgumentOutOfRangeException(nameof(inputStep), inputStep, "The input step must be non-zero and finite.");
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i]) || double.IsNaN(times[i]) || double.IsInfinity(times[i]))
                    throw new ArgumentException($"Sample {i} is not finite.", nameof(values));
                if (i > 0 && !(times[i] > times[i - 1])) throw new ArgumentException("Times must be strictly increasing.", nameof(times));
            }
            if (initialValue is double iv && (double.IsNaN(iv) || double.IsInfinity(iv))) throw new ArgumentOutOfRangeException(nameof(initialValue), iv, "The initial value must be finite.");
            if (finalValue is double fv && (double.IsNaN(fv) || double.IsInfinity(fv))) throw new ArgumentOutOfRangeException(nameof(finalValue), fv, "The final value must be finite.");
            double y0 = initialValue ?? values[0], yf = finalValue ?? values[n - 1];
            if (yf == y0) throw new ArgumentException("The output did not change: nothing to fit.", nameof(values));
            // Finite values can still be more than double.MaxValue apart; the fit then works on halved values.
            double scale = double.IsInfinity(yf - y0) ? 0.5 : 1, change = yf * scale - y0 * scale;

            // For y = 1 − e^(−(t−θ)/τ) these levels are reached at exactly θ + τ/3 and θ + τ.
            double t1 = Crossing(times, values, y0, change, scale, 1 - Math.Exp(-1.0 / 3));
            double t2 = Crossing(times, values, y0, change, scale, 1 - Math.Exp(-1.0));
            if (double.IsNaN(t1) || double.IsNaN(t2)) throw new ArgumentException("The response never reaches 63.2% of its final change; record for longer.", nameof(values));
            double tau = 1.5 * (t2 - t1);
            if (!(tau > 0)) throw new ArgumentException("The response rises too abruptly to fit (63.2% is reached in the same sample as 28.3%); sample faster.", nameof(times));
            double theta = Math.Max(0, t2 - tau);
            double gain = change / inputStep / scale;
            if (double.IsInfinity(gain))
                throw new ArgumentOutOfRangeException(nameof(inputStep), inputStep, "The input step is too small for this output change: the gain overflows.");
            // An underflowed gain was reported as the model's "gain", a parameter the caller never passed.
            if (gain == 0)
                throw new ArgumentOutOfRangeException(nameof(inputStep), inputStep, "The input step is too large for this output change: the gain underflows to 0.");
            return new FopdtModel(gain, tau, theta);
        }

        private static double Crossing(IReadOnlyList<double> t, IReadOnlyList<double> y, double y0, double change, double scale, double level)
        {
            double U(int i)
            {
                double d = y[i] * scale - y0 * scale;                             // overflowed for a representable change
                return double.IsInfinity(d) ? y[i] * scale / change - y0 * scale / change : d / change;
            }
            for (int i = 1; i < y.Count; i++)
                if (U(i) >= level)
                {
                    double a = U(i - 1), b = U(i);
                    double frac = b == a ? 0 : (level - a) / (b - a);
                    return t[i - 1] + Math.Max(0, Math.Min(1, frac)) * (t[i] - t[i - 1]) - t[0];
                }
            return double.NaN;
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "K={0:G5}, τ={1:G5}, θ={2:G5}", Gain, TimeConstant, DeadTime);
    }

    /// <summary>
    /// PID gains in parallel form, u = Kp·e + Ki·∫e dt + Kd·de/dt — the form <c>PidController</c> takes — alongside the
    /// equivalent standard-form times (Kp·(e + (1/Ti)∫e + Td·de/dt)). A PI design has Kd = 0.
    /// </summary>
    public readonly struct PidGains
    {
        public PidGains(double kp, double integralTime, double derivativeTime)
        {
            if (double.IsNaN(kp) || double.IsInfinity(kp)) throw new ArgumentOutOfRangeException(nameof(kp), kp, "Kp must be finite.");
            if (!(integralTime > 0)) throw new ArgumentOutOfRangeException(nameof(integralTime), integralTime, "Integral time must be positive (+∞ for no integral action).");
            if (!(derivativeTime >= 0) || double.IsInfinity(derivativeTime)) throw new ArgumentOutOfRangeException(nameof(derivativeTime), derivativeTime, "Derivative time must be non-negative and finite.");
            // Ki = Kp/Ti and Kd = Kp·Td overflowed to ∞ (which PidController rejects) or underflowed to 0 (silently no
            // integral or derivative action).
            if (!double.IsPositiveInfinity(integralTime) && kp != 0)
            {
                double ki = kp / integralTime;
                if (double.IsInfinity(ki) || ki == 0) throw new ArgumentOutOfRangeException(nameof(integralTime), integralTime, "Ki = Kp / Ti is not representable for this Kp.");
            }
            if (derivativeTime != 0 && kp != 0)
            {
                double kd = kp * derivativeTime;
                if (double.IsInfinity(kd) || kd == 0) throw new ArgumentOutOfRangeException(nameof(derivativeTime), derivativeTime, "Kd = Kp · Td is not representable for this Kp.");
            }
            Kp = kp;
            IntegralTime = integralTime;
            DerivativeTime = derivativeTime;
        }

        public double Kp { get; }

        /// <summary>Ti (reset time); +∞ for no integral action.</summary>
        public double IntegralTime { get; }

        /// <summary>Td; 0 for no derivative action.</summary>
        public double DerivativeTime { get; }

        // IntegralTime is 0 only in default(PidGains), which has no gains at all.
        public double Ki => double.IsPositiveInfinity(IntegralTime) || IntegralTime == 0 ? 0 : Kp / IntegralTime;

        public double Kd => Kp * DerivativeTime;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Kp={0:G5}, Ki={1:G5}, Kd={2:G5} (Ti={3:G5}, Td={4:G5})", Kp, Ki, Kd, IntegralTime, DerivativeTime);
    }

    /// <summary>
    /// Classic PID tuning rules from a <see cref="FopdtModel"/> (fit one from a step test with <see cref="FopdtModel.FitStep"/>)
    /// or from a closed-loop ultimate-gain experiment. Rough guide: SIMC and lambda are robust, smooth starting points
    /// with a single knob for speed; Ziegler–Nichols and Cohen–Coon are aggressive (quarter-amplitude decay) and usually
    /// want detuning. Every rule is a starting point for a real loop, not a guarantee.
    /// </summary>
    public static class PidTuning
    {
        /// <summary>
        /// Skogestad's SIMC PI rule: Kp = τ / (K(τc + θ)), Ti = min(τ, 4(τc + θ)). The closed-loop time constant τc trades
        /// speed for robustness; the default τc = θ gives a good balance (about 2–10% overshoot on setpoint changes).
        /// </summary>
        public static PidGains Simc(FopdtModel model, double? closedLoopTimeConstant = null)
        {
            double tc = Knob(closedLoopTimeConstant ?? model.DeadTime, model, nameof(closedLoopTimeConstant));
            double sum = tc + model.DeadTime;
            return Gains(Ratio(model, tc), Math.Min(model.TimeConstant, double.IsInfinity(sum) ? sum : 4 * sum), 0);
        }

        /// <summary>Lambda (IMC) PI tuning: Kp = τ / (K(λ + θ)), Ti = τ. λ is the desired closed-loop time constant (default τ: no faster than open loop).</summary>
        public static PidGains Lambda(FopdtModel model, double? lambda = null)
        {
            double l = Knob(lambda ?? model.TimeConstant, model, nameof(lambda));
            return Gains(Ratio(model, l), model.TimeConstant, 0);
        }

        /// <summary>Ziegler–Nichols open-loop (reaction curve): PID Kp = 1.2τ/(Kθ), Ti = 2θ, Td = θ/2; PI Kp = 0.9τ/(Kθ), Ti = 3.33θ. Needs θ &gt; 0.</summary>
        public static PidGains ZieglerNichols(FopdtModel model, bool includeDerivative = true)
        {
            double theta = RequireDeadTime(model), ratio = model.TimeConstant / (model.Gain * theta);
            return includeDerivative ? Gains(1.2 * ratio, 2 * theta, 0.5 * theta) : Gains(0.9 * ratio, theta / 0.3, 0);
        }

        /// <summary>Cohen–Coon: like Ziegler–Nichols but corrected for larger dead-time ratios. Needs θ &gt; 0.</summary>
        public static PidGains CohenCoon(FopdtModel model, bool includeDerivative = true)
        {
            double theta = RequireDeadTime(model), tau = model.TimeConstant, r = theta / tau, k = model.Gain;
            if (includeDerivative)
                return Gains(tau / (k * theta) * (4.0 / 3 + r / 4), theta * (32 + 6 * r) / (13 + 8 * r), 4 * theta / (11 + 2 * r));
            return Gains(tau / (k * theta) * (0.9 + r / 12), theta * (30 + 3 * r) / (9 + 20 * r), 0);
        }

        /// <summary>
        /// Ziegler–Nichols closed-loop rule from the ultimate gain <paramref name="ultimateGain"/> (the proportional gain at
        /// which the loop oscillates steadily) and that oscillation's period: PID 0.6Ku, Ti = Pu/2, Td = Pu/8; PI 0.45Ku, Ti = Pu/1.2.
        /// </summary>
        public static PidGains ZieglerNicholsUltimate(double ultimateGain, double ultimatePeriod, bool includeDerivative = true)
        {
            if (ultimateGain == 0 || double.IsNaN(ultimateGain) || double.IsInfinity(ultimateGain)) throw new ArgumentOutOfRangeException(nameof(ultimateGain), ultimateGain, "Ultimate gain must be non-zero and finite.");
            if (!(ultimatePeriod > 0) || double.IsInfinity(ultimatePeriod)) throw new ArgumentOutOfRangeException(nameof(ultimatePeriod), ultimatePeriod, "Ultimate period must be positive and finite.");
            double kp = includeDerivative ? 0.6 * ultimateGain : 0.45 * ultimateGain;
            if (kp == 0) throw new ArgumentOutOfRangeException(nameof(ultimateGain), ultimateGain, "The ultimate gain is too small: Kp underflows to 0.");
            double ti = includeDerivative ? ultimatePeriod / 2 : ultimatePeriod / 1.2, td = includeDerivative ? ultimatePeriod / 8 : 0;
            // Pu/2 or Pu/8 underflowed (reported as "integralTime", which the caller never passed, or silently no
            // derivative), or Ki or Kd overflowed.
            if (!Representable(kp, ti, td) || ti == 0 || (includeDerivative && td == 0))
                throw new ArgumentOutOfRangeException(nameof(ultimatePeriod), ultimatePeriod, "The ultimate period is too extreme for this gain: the tuned gains are not representable.");
            return new PidGains(kp, ti, td);
        }

        // τ / (K(λ + θ)). λ + θ overflowed to ∞ (silently Kp = 0), so an infinite sum is halved; and when K(λ + θ)
        // itself overflows or underflows, the divisions are taken one at a time.
        private static double Ratio(FopdtModel model, double lambda)
        {
            double tau = model.TimeConstant, k = model.Gain, sum = lambda + model.DeadTime, h = 1;
            if (double.IsInfinity(sum)) { sum = lambda / 2 + model.DeadTime / 2; h = 2; }
            double r = tau / (k * sum);
            if (!Usable(r)) r = tau / k / sum;
            if (!Usable(r)) r = tau / sum / k;
            return r / h;
        }

        private static bool Usable(double v) => v != 0 && !double.IsInfinity(v) && !double.IsNaN(v);

        private static bool Representable(double kp, double integralTime, double derivativeTime)
        {
            double ki = kp / integralTime, kd = kp * derivativeTime;
            return !double.IsInfinity(ki) && !double.IsInfinity(kd) && (ki != 0 || double.IsPositiveInfinity(integralTime))
                && (kd != 0 || derivativeTime == 0);
        }

        // An extreme model overflowed a gain (reported as a "kp" the caller never passed) or the integral time (∞, which
        // silently meant "no integral action").
        private static PidGains Gains(double kp, double integralTime, double derivativeTime)
        {
            // Kp = 0 can only be an underflow (the model gain is non-zero); Ki and Kd overflowed or underflowed too.
            if (double.IsNaN(kp) || double.IsInfinity(kp) || kp == 0 || !(integralTime > 0) || double.IsInfinity(integralTime)
                || double.IsNaN(derivativeTime) || double.IsInfinity(derivativeTime) || !Representable(kp, integralTime, derivativeTime))
                throw new ArgumentOutOfRangeException("model", "The model's values are too extreme: the tuned gains are not representable.");
            return new PidGains(kp, integralTime, derivativeTime);
        }

        private static double Knob(double value, FopdtModel model, string name)
        {
            if (model.TimeConstant == 0) throw new ArgumentException("The model is uninitialised (default struct).", nameof(model));
            if (!(value >= 0) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, value, "Must be non-negative and finite.");
            if (value + model.DeadTime == 0) throw new ArgumentOutOfRangeException(name, value, "With no dead time the closed-loop time constant must be positive.");
            return value;
        }

        private static double RequireDeadTime(FopdtModel model)
        {
            if (model.TimeConstant == 0) throw new ArgumentException("The model is uninitialised (default struct).", nameof(model));
            if (!(model.DeadTime > 0)) throw new ArgumentException("This rule divides by the dead time; it needs a model with θ > 0 (use Simc or Lambda otherwise).", nameof(model));
            return model.DeadTime;
        }
    }
}
