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
            double y0 = initialValue ?? values[0], yf = finalValue ?? values[n - 1], change = yf - y0;
            if (change == 0 || double.IsNaN(change) || double.IsInfinity(change)) throw new ArgumentException("The output did not change: nothing to fit.");

            // For y = 1 − e^(−(t−θ)/τ) these levels are reached at exactly θ + τ/3 and θ + τ.
            double t1 = Crossing(times, values, y0, change, 1 - Math.Exp(-1.0 / 3));
            double t2 = Crossing(times, values, y0, change, 1 - Math.Exp(-1.0));
            if (double.IsNaN(t1) || double.IsNaN(t2)) throw new ArgumentException("The response never reaches 63.2% of its final change; record for longer.");
            double tau = 1.5 * (t2 - t1);
            if (!(tau > 0)) throw new ArgumentException("The response rises too abruptly to fit (63.2% is reached in the same sample as 28.3%); sample faster.");
            double theta = Math.Max(0, t2 - tau);
            return new FopdtModel(change / inputStep, tau, theta);
        }

        private static double Crossing(IReadOnlyList<double> t, IReadOnlyList<double> y, double y0, double change, double level)
        {
            double U(int i) => (y[i] - y0) / change;
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
            Kp = kp;
            IntegralTime = integralTime;
            DerivativeTime = derivativeTime;
        }

        public double Kp { get; }

        /// <summary>Ti (reset time); +∞ for no integral action.</summary>
        public double IntegralTime { get; }

        /// <summary>Td; 0 for no derivative action.</summary>
        public double DerivativeTime { get; }

        public double Ki => double.IsPositiveInfinity(IntegralTime) ? 0 : Kp / IntegralTime;

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
            double kp = model.TimeConstant / (model.Gain * (tc + model.DeadTime));
            return new PidGains(kp, Math.Min(model.TimeConstant, 4 * (tc + model.DeadTime)), 0);
        }

        /// <summary>Lambda (IMC) PI tuning: Kp = τ / (K(λ + θ)), Ti = τ. λ is the desired closed-loop time constant (default τ: no faster than open loop).</summary>
        public static PidGains Lambda(FopdtModel model, double? lambda = null)
        {
            double l = Knob(lambda ?? model.TimeConstant, model, nameof(lambda));
            return new PidGains(model.TimeConstant / (model.Gain * (l + model.DeadTime)), model.TimeConstant, 0);
        }

        /// <summary>Ziegler–Nichols open-loop (reaction curve): PID Kp = 1.2τ/(Kθ), Ti = 2θ, Td = θ/2; PI Kp = 0.9τ/(Kθ), Ti = 3.33θ. Needs θ &gt; 0.</summary>
        public static PidGains ZieglerNichols(FopdtModel model, bool includeDerivative = true)
        {
            double theta = RequireDeadTime(model), ratio = model.TimeConstant / (model.Gain * theta);
            return includeDerivative ? new PidGains(1.2 * ratio, 2 * theta, 0.5 * theta) : new PidGains(0.9 * ratio, theta / 0.3, 0);
        }

        /// <summary>Cohen–Coon: like Ziegler–Nichols but corrected for larger dead-time ratios. Needs θ &gt; 0.</summary>
        public static PidGains CohenCoon(FopdtModel model, bool includeDerivative = true)
        {
            double theta = RequireDeadTime(model), tau = model.TimeConstant, r = theta / tau, k = model.Gain;
            if (includeDerivative)
                return new PidGains(tau / (k * theta) * (4.0 / 3 + r / 4), theta * (32 + 6 * r) / (13 + 8 * r), 4 * theta / (11 + 2 * r));
            return new PidGains(tau / (k * theta) * (0.9 + r / 12), theta * (30 + 3 * r) / (9 + 20 * r), 0);
        }

        /// <summary>
        /// Ziegler–Nichols closed-loop rule from the ultimate gain <paramref name="ultimateGain"/> (the proportional gain at
        /// which the loop oscillates steadily) and that oscillation's period: PID 0.6Ku, Ti = Pu/2, Td = Pu/8; PI 0.45Ku, Ti = Pu/1.2.
        /// </summary>
        public static PidGains ZieglerNicholsUltimate(double ultimateGain, double ultimatePeriod, bool includeDerivative = true)
        {
            if (ultimateGain == 0 || double.IsNaN(ultimateGain) || double.IsInfinity(ultimateGain)) throw new ArgumentOutOfRangeException(nameof(ultimateGain), ultimateGain, "Ultimate gain must be non-zero and finite.");
            if (!(ultimatePeriod > 0) || double.IsInfinity(ultimatePeriod)) throw new ArgumentOutOfRangeException(nameof(ultimatePeriod), ultimatePeriod, "Ultimate period must be positive and finite.");
            return includeDerivative
                ? new PidGains(0.6 * ultimateGain, ultimatePeriod / 2, ultimatePeriod / 8)
                : new PidGains(0.45 * ultimateGain, ultimatePeriod / 1.2, 0);
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
