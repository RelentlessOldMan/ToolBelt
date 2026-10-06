// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ToolBelt.Signal
{
    /// <summary>
    /// One second-order IIR section (a "biquad"), normalised so a0 = 1:
    /// H(z) = (b0 + b1·z⁻¹ + b2·z⁻²) / (1 + a1·z⁻¹ + a2·z⁻²). The factories are the RBJ "Audio EQ Cookbook" designs —
    /// the quick way to get one low/high/band-pass, notch, all-pass, peaking or shelving stage. Immutable; filter with
    /// an <see cref="IirFilter"/> (which holds the state).
    /// </summary>
    public sealed class Biquad
    {
        public Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            if (!(IsFinite(b0) && IsFinite(b1) && IsFinite(b2) && IsFinite(a1) && IsFinite(a2)))
                throw new ArgumentException("Coefficients must be finite.");
            B0 = b0; B1 = b1; B2 = b2; A1 = a1; A2 = a2;
        }

        public double B0 { get; }
        public double B1 { get; }
        public double B2 { get; }
        public double A1 { get; }
        public double A2 { get; }

        /// <summary>Second-order low-pass; <paramref name="q"/> = 1/√2 is maximally flat (Butterworth). |H(f0)| = Q.</summary>
        public static Biquad LowPass(double sampleRate, double frequency, double q = 0.7071067811865476)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            return Normalize((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        /// <summary>Second-order high-pass; <paramref name="q"/> = 1/√2 is maximally flat.</summary>
        public static Biquad HighPass(double sampleRate, double frequency, double q = 0.7071067811865476)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            return Normalize((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        /// <summary>Band-pass with unity (0 dB) gain at the centre <paramref name="frequency"/>; bandwidth ≈ frequency / Q.</summary>
        public static Biquad BandPass(double sampleRate, double frequency, double q)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            return Normalize(alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha);
        }

        /// <summary>Notch (band-reject): zero gain exactly at <paramref name="frequency"/>, e.g. 50/60 Hz mains hum.</summary>
        public static Biquad Notch(double sampleRate, double frequency, double q)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            return Normalize(1, -2 * cos, 1, 1 + alpha, -2 * cos, 1 - alpha);
        }

        /// <summary>All-pass: unity gain at every frequency, phase turning through −180° at <paramref name="frequency"/>.</summary>
        public static Biquad AllPass(double sampleRate, double frequency, double q)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            return Normalize(1 - alpha, -2 * cos, 1 + alpha, 1 + alpha, -2 * cos, 1 - alpha);
        }

        /// <summary>Peaking EQ: <paramref name="gainDb"/> of boost (or cut) centred on <paramref name="frequency"/>, unity far away.</summary>
        public static Biquad Peaking(double sampleRate, double frequency, double q, double gainDb)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            double a = GainAmplitude(gainDb);
            return Normalize(1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a);
        }

        /// <summary>Low shelf: <paramref name="gainDb"/> at DC, unity at Nyquist, mid-gain at <paramref name="frequency"/>. Slope 1 is the steepest monotonic shelf.</summary>
        public static Biquad LowShelf(double sampleRate, double frequency, double gainDb, double slope = 1)
        {
            var (a, cos, k) = Shelf(sampleRate, frequency, gainDb, slope);
            return Normalize(
                a * ((a + 1) - (a - 1) * cos + k), 2 * a * ((a - 1) - (a + 1) * cos), a * ((a + 1) - (a - 1) * cos - k),
                (a + 1) + (a - 1) * cos + k, -2 * ((a - 1) + (a + 1) * cos), (a + 1) + (a - 1) * cos - k);
        }

        /// <summary>High shelf: <paramref name="gainDb"/> at Nyquist, unity at DC.</summary>
        public static Biquad HighShelf(double sampleRate, double frequency, double gainDb, double slope = 1)
        {
            var (a, cos, k) = Shelf(sampleRate, frequency, gainDb, slope);
            return Normalize(
                a * ((a + 1) + (a - 1) * cos + k), -2 * a * ((a - 1) + (a + 1) * cos), a * ((a + 1) + (a - 1) * cos - k),
                (a + 1) - (a - 1) * cos + k, 2 * ((a - 1) - (a + 1) * cos), (a + 1) - (a - 1) * cos - k);
        }

        /// <summary>The complex frequency response at <paramref name="frequency"/> Hz.</summary>
        public Complex Response(double frequency, double sampleRate)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            Complex z1 = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / sampleRate);   // z⁻¹
            Complex z2 = z1 * z1;
            return (B0 + B1 * z1 + B2 * z2) / (1 + A1 * z1 + A2 * z2);
        }

        /// <summary>Group delay in samples at <paramref name="frequency"/> Hz (see <see cref="IirFilter.GroupDelay"/>).</summary>
        public double GroupDelay(double frequency, double sampleRate)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            Complex z1 = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / sampleRate);
            Complex z2 = z1 * z1;
            // For P(ω) = Σ c_k e^(−jωk): −d arg P / dω = Re(Σ k·c_k e^(−jωk) / P).
            double num = ((B1 * z1 + 2 * B2 * z2) / (B0 + B1 * z1 + B2 * z2)).Real;
            double den = ((A1 * z1 + 2 * A2 * z2) / (1 + A1 * z1 + A2 * z2)).Real;
            return num - den;
        }

        public override string ToString() => FormattableString.Invariant($"b=[{B0}, {B1}, {B2}] a=[1, {A1}, {A2}]");

        private static (double Cos, double Alpha) Prewarp(double sampleRate, double frequency, double q)
        {
            CheckFrequency(sampleRate, frequency);
            if (!(q > 0) || double.IsInfinity(q)) throw new ArgumentOutOfRangeException(nameof(q), q, "Q must be positive and finite.");
            double w0 = 2 * Math.PI * frequency / sampleRate;
            return (Math.Cos(w0), Math.Sin(w0) / (2 * q));
        }

        private static (double A, double Cos, double K) Shelf(double sampleRate, double frequency, double gainDb, double slope)
        {
            CheckFrequency(sampleRate, frequency);
            double a = GainAmplitude(gainDb);
            if (!(slope > 0) || double.IsInfinity(slope)) throw new ArgumentOutOfRangeException(nameof(slope), slope, "Slope must be positive and finite.");
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double inner = (a + 1 / a) * (1 / slope - 1) + 2;
            if (inner < 0) throw new ArgumentOutOfRangeException(nameof(slope), slope, "Slope too steep for this gain (the shelf would not be monotonic).");
            double alpha = Math.Sin(w0) / 2 * Math.Sqrt(inner);
            return (a, Math.Cos(w0), 2 * Math.Sqrt(a) * alpha);
        }

        private static double GainAmplitude(double gainDb)
        {
            if (!IsFinite(gainDb)) throw new ArgumentOutOfRangeException(nameof(gainDb), gainDb, "Gain must be finite.");
            return Math.Pow(10, gainDb / 40);
        }

        internal static void CheckFrequency(double sampleRate, double frequency)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (!(frequency > 0 && frequency < sampleRate / 2)) throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be between 0 and Nyquist (sampleRate / 2), exclusive.");
        }

        private static Biquad Normalize(double b0, double b1, double b2, double a0, double a1, double a2)
            => new Biquad(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);

        internal static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }

    /// <summary>How a Bessel prototype is scaled (SciPy's <c>norm</c> argument).</summary>
    public enum BesselNormalization
    {
        /// <summary>Phase response at the cutoff is half its high-frequency value; asymptotes match a Butterworth of the same order (SciPy's default).</summary>
        Phase,
        /// <summary>The analog group delay is 1/(2π·cutoff) seconds at low frequency.</summary>
        Delay,
        /// <summary>−3 dB at the cutoff, like the Butterworth.</summary>
        Magnitude,
    }

    /// <summary>
    /// A stateful IIR filter built as a cascade of <see cref="Biquad"/> sections (second-order sections: far better
    /// conditioned than one high-order polynomial). The classic designs are all here, following the standard
    /// analog-prototype → band transform → pre-warped bilinear route (the same one as SciPy's <c>butter</c>,
    /// <c>cheby1</c>, <c>cheby2</c>, <c>ellip</c> and <c>bessel</c>, so designs agree to rounding):
    /// <list type="bullet">
    /// <item><b>Butterworth</b>: maximally flat passband, gentle roll-off. The safe default.</item>
    /// <item><b>Chebyshev I</b>: ripple in the passband buys a steeper transition.</item>
    /// <item><b>Chebyshev II</b> (inverse Chebyshev): flat passband, ripple in the stop band, which never rises above the stated attenuation.</item>
    /// <item><b>Elliptic</b> (Cauer): ripple in both bands; the steepest transition for a given order.</item>
    /// <item><b>Bessel</b>: the flattest group delay — pulses and steps keep their shape, with almost no overshoot — at the cost of the slowest roll-off.</item>
    /// </list>
    /// Use <see cref="Process"/> for streaming, <see cref="Filter"/> for a one-shot causal pass, and
    /// <see cref="FiltFilt"/> for zero-phase offline filtering. Not thread-safe.
    /// </summary>
    public sealed class IirFilter
    {
        private readonly Biquad[] _sections;
        private readonly double[] _z1, _z2;

        public IirFilter(IEnumerable<Biquad> sections)
        {
            if (sections is null) throw new ArgumentNullException(nameof(sections));
            _sections = sections.ToArray();
            if (_sections.Length == 0) throw new ArgumentException("At least one section is required.", nameof(sections));
            if (_sections.Any(s => s is null)) throw new ArgumentException("Sections must not be null.", nameof(sections));
            _z1 = new double[_sections.Length];
            _z2 = new double[_sections.Length];
        }

        public IirFilter(params Biquad[] sections) : this((IEnumerable<Biquad>)sections) { }

        public IReadOnlyList<Biquad> Sections => _sections;

        /// <summary>Filters one sample, carrying state from the previous call (direct form II transposed).</summary>
        public double Process(double sample)
        {
            double x = sample;
            for (int s = 0; s < _sections.Length; s++)
            {
                var q = _sections[s];
                double y = q.B0 * x + _z1[s];
                _z1[s] = q.B1 * x - q.A1 * y + _z2[s];
                _z2[s] = q.B2 * x - q.A2 * y;
                x = y;
            }
            return x;
        }

        /// <summary>Clears the streaming state (as if no samples had been seen).</summary>
        public void Reset()
        {
            Array.Clear(_z1, 0, _z1.Length);
            Array.Clear(_z2, 0, _z2.Length);
        }

        /// <summary>
        /// Clears the state to the steady state for a constant input <paramref name="value"/>, so a signal that starts
        /// far from zero doesn't produce a start-up transient.
        /// </summary>
        public void Reset(double value)
        {
            var (z1, z2) = SteadyState();
            for (int s = 0; s < _sections.Length; s++) { _z1[s] = z1[s] * value; _z2[s] = z2[s] * value; }
        }

        /// <summary>A causal pass over <paramref name="samples"/> from a zero state (SciPy <c>sosfilt</c>); the streaming state is untouched.</summary>
        public double[] Filter(IReadOnlyList<double> samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            var output = new double[samples.Count];
            for (int i = 0; i < output.Length; i++) output[i] = samples[i];
            Run(output, new double[_sections.Length], new double[_sections.Length]);
            return output;
        }

        /// <summary>
        /// Zero-phase filtering: forward, then backward, so the result has no delay and the magnitude response is applied
        /// twice (squared). Edges are handled like SciPy's <c>sosfiltfilt</c>: odd (point-reflected) extension by
        /// <paramref name="padLength"/> samples and steady-state initial conditions, so the output starts and ends without
        /// transients. Default pad = 3 × (2 × sections + 1, less trailing zero coefficients), as in SciPy; the signal must be
        /// longer than the pad.
        /// </summary>
        public double[] FiltFilt(IReadOnlyList<double> samples, int? padLength = null)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            int n = samples.Count;
            int pad = padLength ?? DefaultPadLength();
            if (pad < 0) throw new ArgumentOutOfRangeException(nameof(padLength), pad, "Pad length must be non-negative.");
            if (n <= pad) throw new ArgumentException($"The signal must be longer than the pad length ({pad}); pass a smaller padLength for short signals.", nameof(samples));

            var ext = new double[n + 2 * pad];
            for (int i = 0; i < pad; i++) ext[i] = 2 * samples[0] - samples[pad - i];
            for (int i = 0; i < n; i++) ext[pad + i] = samples[i];
            for (int i = 0; i < pad; i++) ext[pad + n + i] = 2 * samples[n - 1] - samples[n - 2 - i];

            var (zi1, zi2) = SteadyState();
            Run(ext, Scaled(zi1, ext[0]), Scaled(zi2, ext[0]));
            Array.Reverse(ext);
            Run(ext, Scaled(zi1, ext[0]), Scaled(zi2, ext[0]));
            Array.Reverse(ext);

            var result = new double[n];
            Array.Copy(ext, pad, result, 0, n);
            return result;
        }

        /// <summary>The complex frequency response of the whole cascade at <paramref name="frequency"/> Hz.</summary>
        public Complex Response(double frequency, double sampleRate)
        {
            Complex h = Complex.One;
            foreach (var s in _sections) h *= s.Response(frequency, sampleRate);
            return h;
        }

        /// <summary>|H| in decibels at <paramref name="frequency"/> Hz (−∞ at an exact zero).</summary>
        public double MagnitudeDb(double frequency, double sampleRate) => 20 * Math.Log10(Response(frequency, sampleRate).Magnitude);

        /// <summary>
        /// Group delay −dφ/dω at <paramref name="frequency"/> Hz, in samples (divide by the sample rate for seconds): how long
        /// the envelope of a narrow-band signal at that frequency is delayed. Exact (analytic), not a numerical derivative;
        /// undefined (non-finite) exactly on a zero of the response.
        /// </summary>
        public double GroupDelay(double frequency, double sampleRate)
        {
            double total = 0;
            foreach (var s in _sections) total += s.GroupDelay(frequency, sampleRate);
            return total;
        }

        // ---- Designs -------------------------------------------------------------------------------------------------
        // Every family has LowPass / HighPass / BandPass / BandStop; orders are those of the low-pass prototype (band
        // designs have twice as many poles).

        /// <summary>Butterworth low-pass: maximally flat passband, −3.01 dB at <paramref name="cutoff"/>, −20·order dB/decade beyond.</summary>
        public static IirFilter ButterworthLowPass(int order, double cutoff, double sampleRate)
            => Design(ButterworthPrototype(order), Band.LowPass, sampleRate, cutoff, 0);

        public static IirFilter ButterworthHighPass(int order, double cutoff, double sampleRate)
            => Design(ButterworthPrototype(order), Band.HighPass, sampleRate, cutoff, 0);

        /// <summary>Butterworth band-pass between <paramref name="low"/> and <paramref name="high"/> (−3 dB edges); the result has 2 × order poles.</summary>
        public static IirFilter ButterworthBandPass(int order, double low, double high, double sampleRate)
            => Design(ButterworthPrototype(order), Band.BandPass, sampleRate, low, high);

        public static IirFilter ButterworthBandStop(int order, double low, double high, double sampleRate)
            => Design(ButterworthPrototype(order), Band.BandStop, sampleRate, low, high);

        /// <summary>
        /// Chebyshev type I low-pass: passband ripple of <paramref name="rippleDb"/> (gain swings between 0 and −ripple dB),
        /// in exchange for a steeper transition than a Butterworth of the same order. The gain at <paramref name="cutoff"/> is −ripple dB.
        /// </summary>
        public static IirFilter Chebyshev1LowPass(int order, double rippleDb, double cutoff, double sampleRate)
            => Design(Chebyshev1Prototype(order, rippleDb), Band.LowPass, sampleRate, cutoff, 0);

        public static IirFilter Chebyshev1HighPass(int order, double rippleDb, double cutoff, double sampleRate)
            => Design(Chebyshev1Prototype(order, rippleDb), Band.HighPass, sampleRate, cutoff, 0);

        public static IirFilter Chebyshev1BandPass(int order, double rippleDb, double low, double high, double sampleRate)
            => Design(Chebyshev1Prototype(order, rippleDb), Band.BandPass, sampleRate, low, high);

        public static IirFilter Chebyshev1BandStop(int order, double rippleDb, double low, double high, double sampleRate)
            => Design(Chebyshev1Prototype(order, rippleDb), Band.BandStop, sampleRate, low, high);

        /// <summary>
        /// Chebyshev type II (inverse Chebyshev) low-pass: monotonic, ripple-free passband and a stop band that stays at
        /// least <paramref name="stopAttenuationDb"/> down. Note <paramref name="stopEdge"/> is where the stop band
        /// <em>begins</em> (gain first reaches −attenuation), not a −3 dB point — the passband ends somewhat below it.
        /// </summary>
        public static IirFilter Chebyshev2LowPass(int order, double stopAttenuationDb, double stopEdge, double sampleRate)
            => Design(Chebyshev2Prototype(order, stopAttenuationDb), Band.LowPass, sampleRate, stopEdge, 0);

        public static IirFilter Chebyshev2HighPass(int order, double stopAttenuationDb, double stopEdge, double sampleRate)
            => Design(Chebyshev2Prototype(order, stopAttenuationDb), Band.HighPass, sampleRate, stopEdge, 0);

        /// <summary>Chebyshev type II band-pass; <paramref name="low"/>/<paramref name="high"/> are the stop-band edges.</summary>
        public static IirFilter Chebyshev2BandPass(int order, double stopAttenuationDb, double low, double high, double sampleRate)
            => Design(Chebyshev2Prototype(order, stopAttenuationDb), Band.BandPass, sampleRate, low, high);

        /// <summary>Chebyshev type II band-stop; the rejection band (at least the stated attenuation) is <paramref name="low"/>..<paramref name="high"/>.</summary>
        public static IirFilter Chebyshev2BandStop(int order, double stopAttenuationDb, double low, double high, double sampleRate)
            => Design(Chebyshev2Prototype(order, stopAttenuationDb), Band.BandStop, sampleRate, low, high);

        /// <summary>
        /// Elliptic (Cauer) low-pass: <paramref name="passRippleDb"/> of passband ripple and at least
        /// <paramref name="stopAttenuationDb"/> of stop-band rejection, with the narrowest transition of any design of the
        /// same order. The gain at <paramref name="cutoff"/> (the passband edge) is −ripple dB. Expect ringing: its group
        /// delay peaks sharply near the edge.
        /// </summary>
        public static IirFilter EllipticLowPass(int order, double passRippleDb, double stopAttenuationDb, double cutoff, double sampleRate)
            => Design(EllipticPrototype(order, passRippleDb, stopAttenuationDb), Band.LowPass, sampleRate, cutoff, 0);

        public static IirFilter EllipticHighPass(int order, double passRippleDb, double stopAttenuationDb, double cutoff, double sampleRate)
            => Design(EllipticPrototype(order, passRippleDb, stopAttenuationDb), Band.HighPass, sampleRate, cutoff, 0);

        /// <summary>Elliptic band-pass; <paramref name="low"/>/<paramref name="high"/> are the passband edges.</summary>
        public static IirFilter EllipticBandPass(int order, double passRippleDb, double stopAttenuationDb, double low, double high, double sampleRate)
            => Design(EllipticPrototype(order, passRippleDb, stopAttenuationDb), Band.BandPass, sampleRate, low, high);

        public static IirFilter EllipticBandStop(int order, double passRippleDb, double stopAttenuationDb, double low, double high, double sampleRate)
            => Design(EllipticPrototype(order, passRippleDb, stopAttenuationDb), Band.BandStop, sampleRate, low, high);

        /// <summary>
        /// Bessel (Thomson) low-pass: maximally flat group delay, so the passband is delayed without dispersion and a step
        /// comes through with well under 1% overshoot. Roll-off is the gentlest of the classic families.
        /// <paramref name="normalization"/> picks what <paramref name="cutoff"/> means (SciPy's default: phase). The
        /// bilinear transform warps the flat delay somewhat as the cutoff approaches Nyquist; keep it well below.
        /// </summary>
        public static IirFilter BesselLowPass(int order, double cutoff, double sampleRate, BesselNormalization normalization = BesselNormalization.Phase)
            => Design(BesselPrototype(order, normalization), Band.LowPass, sampleRate, cutoff, 0);

        public static IirFilter BesselHighPass(int order, double cutoff, double sampleRate, BesselNormalization normalization = BesselNormalization.Phase)
            => Design(BesselPrototype(order, normalization), Band.HighPass, sampleRate, cutoff, 0);

        public static IirFilter BesselBandPass(int order, double low, double high, double sampleRate, BesselNormalization normalization = BesselNormalization.Phase)
            => Design(BesselPrototype(order, normalization), Band.BandPass, sampleRate, low, high);

        public static IirFilter BesselBandStop(int order, double low, double high, double sampleRate, BesselNormalization normalization = BesselNormalization.Phase)
            => Design(BesselPrototype(order, normalization), Band.BandStop, sampleRate, low, high);

        // ---- Internals -----------------------------------------------------------------------------------------------

        private enum Band { LowPass, HighPass, BandPass, BandStop }

        private void Run(double[] data, double[] z1, double[] z2)
        {
            for (int s = 0; s < _sections.Length; s++)
            {
                var q = _sections[s];
                double a = z1[s], b = z2[s];
                for (int i = 0; i < data.Length; i++)
                {
                    double x = data[i];
                    double y = q.B0 * x + a;
                    a = q.B1 * x - q.A1 * y + b;
                    b = q.B2 * x - q.A2 * y;
                    data[i] = y;
                }
            }
        }

        // Per-section state for a unit step held forever, scaled by the DC gain of the sections before it (SciPy sosfilt_zi).
        private (double[] Z1, double[] Z2) SteadyState()
        {
            var z1 = new double[_sections.Length];
            var z2 = new double[_sections.Length];
            double scale = 1;
            for (int s = 0; s < _sections.Length; s++)
            {
                var q = _sections[s];
                double denominator = 1 + q.A1 + q.A2;
                // A pole at z = 1 has no steady state; leave that section (and everything after it) starting from zero.
                if (Math.Abs(denominator) < 1e-14) break;
                double gain = (q.B0 + q.B1 + q.B2) / denominator;
                z2[s] = scale * (q.B2 - q.A2 * gain);
                z1[s] = scale * (q.B1 - q.A1 * gain) + z2[s];
                scale *= gain;
            }
            return (z1, z2);
        }

        private int DefaultPadLength()
        {
            int b2Zeros = _sections.Count(s => s.B2 == 0), a2Zeros = _sections.Count(s => s.A2 == 0);
            return 3 * (2 * _sections.Length + 1 - Math.Min(b2Zeros, a2Zeros));
        }

        private static double[] Scaled(double[] v, double k)
        {
            var r = new double[v.Length];
            for (int i = 0; i < v.Length; i++) r[i] = v[i] * k;
            return r;
        }

        private static void CheckOrder(int order, int max = 40)
        {
            if (order < 1 || order > max) throw new ArgumentOutOfRangeException(nameof(order), order, $"Order must be between 1 and {max}.");
        }

        private static (List<Complex> Z, List<Complex> P, double K) ButterworthPrototype(int order)
        {
            CheckOrder(order);
            var p = new List<Complex>();
            for (int m = -order + 1; m < order; m += 2)
                p.Add(-Complex.Exp(new Complex(0, Math.PI * m / (2.0 * order))));
            return (new List<Complex>(), p, 1);
        }

        private static (List<Complex> Z, List<Complex> P, double K) Chebyshev1Prototype(int order, double rippleDb)
        {
            CheckOrder(order);
            if (!(rippleDb > 0) || double.IsInfinity(rippleDb)) throw new ArgumentOutOfRangeException(nameof(rippleDb), rippleDb, "Ripple must be positive and finite (dB).");
            double eps = Math.Sqrt(Math.Pow(10, 0.1 * rippleDb) - 1);
            double mu = Asinh(1 / eps) / order;
            var p = new List<Complex>();
            for (int m = -order + 1; m < order; m += 2)
                p.Add(-Complex.Sinh(new Complex(mu, Math.PI * m / (2.0 * order))));
            Complex prod = Complex.One;
            foreach (var pole in p) prod *= -pole;
            double k = prod.Real;
            if (order % 2 == 0) k /= Math.Sqrt(1 + eps * eps);     // even orders start the ripple at −ripple dB
            return (new List<Complex>(), p, k);
        }

        private static (List<Complex> Z, List<Complex> P, double K) Chebyshev2Prototype(int order, double stopAttenuationDb)
        {
            CheckOrder(order);
            if (!(stopAttenuationDb > 0 && stopAttenuationDb <= 1000)) throw new ArgumentOutOfRangeException(nameof(stopAttenuationDb), stopAttenuationDb, "Stop-band attenuation must be in (0, 1000] dB.");
            double de = 1 / Math.Sqrt(Math.Pow(10, 0.1 * stopAttenuationDb) - 1);
            double mu = Asinh(1 / de) / order;
            var z = new List<Complex>();
            var p = new List<Complex>();
            for (int m = -order + 1; m < order; m += 2)
            {
                if (m != 0) z.Add(new Complex(0, 1 / Math.Sin(m * Math.PI / (2.0 * order))));    // zeros on the jω axis
                Complex b = -Complex.Exp(new Complex(0, Math.PI * m / (2.0 * order)));
                p.Add(1 / new Complex(Math.Sinh(mu) * b.Real, Math.Cosh(mu) * b.Imaginary));
            }
            double k = (Product(p.Select(x => -x)) / Product(z.Select(x => -x))).Real;
            return (z, p, k);
        }

        private static (List<Complex> Z, List<Complex> P, double K) EllipticPrototype(int order, double passRippleDb, double stopAttenuationDb)
        {
            CheckOrder(order, 25);
            if (!(passRippleDb > 0 && passRippleDb < 100)) throw new ArgumentOutOfRangeException(nameof(passRippleDb), passRippleDb, "Passband ripple must be in (0, 100) dB.");
            if (!(stopAttenuationDb > passRippleDb && stopAttenuationDb <= 1000)) throw new ArgumentOutOfRangeException(nameof(stopAttenuationDb), stopAttenuationDb, "Stop-band attenuation must exceed the passband ripple (and be at most 1000 dB).");
            double epsSq = Math.Pow(10, 0.1 * passRippleDb) - 1;
            if (order == 1)
            {
                double pole = -Math.Sqrt(1 / epsSq);
                return (new List<Complex>(), new List<Complex> { pole }, -pole);
            }
            double eps = Math.Sqrt(epsSq);
            double ck1Sq = epsSq / (Math.Pow(10, 0.1 * stopAttenuationDb) - 1);
            double m = EllipticDegree(order, ck1Sq);
            double capK = EllipK(m);

            var s = new List<double>();
            var c = new List<double>();
            var d = new List<double>();
            for (int j = 1 - order % 2; j < order; j += 2)
            {
                var (sn, cn, dn) = Jacobi(j * capK / order, m);
                s.Add(sn); c.Add(cn); d.Add(dn);
            }
            var z = new List<Complex>();
            foreach (double sn in s)
                if (Math.Abs(sn) > Epsilon) z.Add(new Complex(0, 1 / (Math.Sqrt(m) * sn)));
            z.AddRange(z.Select(Complex.Conjugate).ToList());

            double r = ArcJacobiSc1(1 / eps, ck1Sq);
            double v0 = capK * r / (order * EllipK(ck1Sq));
            var (sv, cv, dv) = Jacobi(v0, 1 - m);
            var p = new List<Complex>();
            for (int i = 0; i < s.Count; i++)
                p.Add(-new Complex(c[i] * d[i] * sv * cv, s[i] * dv) / (1 - Math.Pow(d[i] * sv, 2)));
            // Conjugate partners; an odd order's real pole (j = 0) has none.
            var partners = p.Where(x => order % 2 == 0 || Math.Abs(x.Imaginary) > Epsilon * x.Magnitude).Select(Complex.Conjugate).ToList();
            p.AddRange(partners);
            for (int i = 0; i < p.Count; i++) if (Math.Abs(p[i].Imaginary) <= Epsilon * p[i].Magnitude) p[i] = p[i].Real;

            double k = (Product(p.Select(x => -x)) / Product(z.Select(x => -x))).Real;
            if (order % 2 == 0) k /= Math.Sqrt(1 + epsSq);
            return (z, p, k);
        }

        private static (List<Complex> Z, List<Complex> P, double K) BesselPrototype(int order, BesselNormalization normalization)
        {
            CheckOrder(order, 25);
            if (!Enum.IsDefined(typeof(BesselNormalization), normalization)) throw new ArgumentOutOfRangeException(nameof(normalization), normalization, "Unknown normalization.");
            // Poles are the roots of the reverse Bessel polynomial θn(s) = Σ a_k s^k, a_k = (2n − k)! / (2^(n−k) k! (n − k)!).
            // Delay normalisation uses them as they are; phase normalisation divides by c = a_0^(1/n) (so the poles'
            // product is 1). The roots are badly conditioned — evaluating θn in double precision, by coefficients or by its
            // recurrence, leaves ~1e-4 error at order 25 — so rough Aberth roots are polished by Newton steps whose
            // residual is computed in double-double arithmetic from the exact integer coefficients.
            int n = order;
            var exact = BesselCoefficients(n);
            double c = Math.Exp(Math.Log((double)exact[0]) / n);
            var roots = BesselRoots(exact, c);
            for (int i = 0; i < roots.Length; i++) roots[i] /= c;                  // phase-normalised

            var p = new List<Complex>();
            foreach (var root in roots)
                if (root.Imaginary > 1e-9 * root.Magnitude) { p.Add(root); p.Add(Complex.Conjugate(root)); }
                else if (Math.Abs(root.Imaginary) <= 1e-9 * root.Magnitude) p.Add(root.Real);
            if (p.Count != n) throw new InvalidOperationException("Bessel root finding failed to separate the poles.");

            if (normalization != BesselNormalization.Phase)
            {
                for (int i = 0; i < n; i++) p[i] *= c;                              // back to the roots of θn: delay-normalised
                if (normalization == BesselNormalization.Magnitude)
                {
                    double w = MagnitudeCutoff(p);
                    for (int i = 0; i < n; i++) p[i] /= w;
                }
            }
            return (new List<Complex>(), p, Product(p.Select(x => -x)).Real);       // unity gain at DC
        }

        // Frequency where an all-pole prototype with unity DC gain is 3 dB down (its magnitude falls monotonically).
        private static double MagnitudeCutoff(List<Complex> poles)
        {
            double k = Product(poles.Select(x => -x)).Magnitude;
            double Gain(double w)
            {
                double g = k;
                foreach (var pole in poles) g /= (new Complex(0, w) - pole).Magnitude;
                return g;
            }
            double lo = 1e-6, hi = 1e6, target = Math.Sqrt(0.5);
            for (int i = 0; i < 200; i++)
            {
                double mid = Math.Sqrt(lo * hi);
                if (Gain(mid) > target) lo = mid; else hi = mid;
            }
            return Math.Sqrt(lo * hi);
        }

        private static System.Numerics.BigInteger[] BesselCoefficients(int n)
        {
            var fact = new System.Numerics.BigInteger[2 * n + 1];
            fact[0] = 1;
            for (int i = 1; i <= 2 * n; i++) fact[i] = fact[i - 1] * i;
            var a = new System.Numerics.BigInteger[n + 1];
            for (int k = 0; k <= n; k++) a[k] = fact[2 * n - k] / (System.Numerics.BigInteger.Pow(2, n - k) * fact[k] * fact[n - k]);
            return a;
        }

        // Roots of Σ a_k s^k: Aberth–Ehrlich iteration in double (starting on the circle |s| = c the roots cluster around),
        // then Newton polishing with the value evaluated in double-double.
        private static Complex[] BesselRoots(System.Numerics.BigInteger[] exact, double c)
        {
            int n = exact.Length - 1;
            var coeff = exact.Select(x => (double)x).ToArray();
            var hi = new double[n + 1];
            var lo = new double[n + 1];
            for (int k = 0; k <= n; k++)
            {
                hi[k] = coeff[k];
                lo[k] = (double)(exact[k] - new System.Numerics.BigInteger(hi[k]));
            }

            Complex Derivative(Complex x)
            {
                Complex pv = coeff[n], dv = Complex.Zero;
                for (int k = n - 1; k >= 0; k--) { dv = dv * x + pv; pv = pv * x + coeff[k]; }
                return dv;
            }

            var z = new Complex[n];
            for (int i = 0; i < n; i++) z[i] = Complex.FromPolarCoordinates(c, Math.PI / 2 + Math.PI * (2 * i + 1.5) / (2 * n));
            for (int iter = 0; iter < 500; iter++)
            {
                double worst = 0;
                for (int i = 0; i < n; i++)
                {
                    Complex pv = coeff[n];
                    for (int k = n - 1; k >= 0; k--) pv = pv * z[i] + coeff[k];
                    if (pv == Complex.Zero) continue;
                    Complex ratio = pv / Derivative(z[i]), sum = Complex.Zero;
                    for (int j = 0; j < n; j++) if (j != i) sum += 1 / (z[i] - z[j]);
                    Complex step = ratio / (1 - ratio * sum);
                    z[i] -= step;
                    worst = Math.Max(worst, step.Magnitude / z[i].Magnitude);
                }
                if (worst < 1e-12) break;
            }

            for (int i = 0; i < n; i++)
                for (int iter = 0; iter < 8; iter++)
                {
                    Complex step = HornerDoubleDouble(hi, lo, z[i]) / Derivative(z[i]);
                    z[i] -= step;
                    if (step.Magnitude <= 1e-17 * z[i].Magnitude) break;
                }
            return z;
        }

        // Σ (hi_k + lo_k) x^k with ~106-bit intermediate precision (Dekker/Knuth error-free transformations), rounded at the end.
        private static Complex HornerDoubleDouble(double[] hi, double[] lo, Complex x)
        {
            int n = hi.Length - 1;
            double rh = hi[n], rl = lo[n], ih = 0, il = 0;
            for (int k = n - 1; k >= 0; k--)
            {
                // (r + i·m)(xr + i·xi) + a_k
                var (ah, al) = MulDD(rh, rl, x.Real);
                var (bh, bl) = MulDD(ih, il, -x.Imaginary);
                var (ch, cl) = MulDD(rh, rl, x.Imaginary);
                var (dh, dl) = MulDD(ih, il, x.Real);
                var (re1h, re1l) = AddDD(ah, al, bh, bl);
                (rh, rl) = AddDD(re1h, re1l, hi[k], lo[k]);
                (ih, il) = AddDD(ch, cl, dh, dl);
            }
            return new Complex(rh + rl, ih + il);
        }

        private static (double Hi, double Lo) AddDD(double ah, double al, double bh, double bl)
        {
            double s = ah + bh, bb = s - ah, e = (ah - (s - bb)) + (bh - bb);
            e += al + bl;
            double r = s + e;
            return (r, e - (r - s));
        }

        private static (double Hi, double Lo) MulDD(double ah, double al, double b)
        {
            double p = ah * b;
            var (xh, xl) = Split(ah);
            var (yh, yl) = Split(b);
            double e = ((xh * yh - p) + xh * yl + xl * yh) + xl * yl;
            e += al * b;
            double r = p + e;
            return (r, e - (r - p));
        }

        private static (double Hi, double Lo) Split(double a)
        {
            double t = 134217729.0 * a;                                           // 2^27 + 1
            double h = t - (t - a);
            return (h, a - h);
        }

        // ---- Elliptic functions (ports of the SciPy/Cephes routines the ellip design uses) ----------------------------

        private const double Epsilon = 2e-16;

        // Complete elliptic integral of the first kind K(m), parameter m = k², via the arithmetic-geometric mean.
        private static double EllipK(double m) => Math.PI / (2 * Agm(1, Math.Sqrt(1 - m)));

        // K(1 − p), accurate when p is tiny (the complementary modulus is computed directly, not as 1 − (1 − p)).
        private static double EllipKm1(double p) => Math.PI / (2 * Agm(1, Math.Sqrt(p)));

        private static double Agm(double a, double b)
        {
            for (int i = 0; i < 100 && Math.Abs(a - b) > 1e-16 * a; i++)
            {
                double next = (a + b) / 2;
                b = Math.Sqrt(a * b);
                a = next;
            }
            return (a + b) / 2;
        }

        // Solves the degree equation: the elliptic modulus an order-n filter achieves for selectivity factor m1.
        private static double EllipticDegree(int n, double m1)
        {
            double q1 = Math.Exp(-Math.PI * EllipKm1(m1) / EllipK(m1));
            double q = Math.Pow(q1, 1.0 / n);
            double num = 0, den = 0;
            for (int i = 0; i <= 7; i++) num += Math.Pow(q, i * (i + 1));
            for (int i = 1; i <= 8; i++) den += Math.Pow(q, i * i);
            return 16 * q * Math.Pow(num / (1 + 2 * den), 4);
        }

        // Jacobi elliptic functions sn, cn, dn of u with parameter m (Cephes ellpj: descending Landen / AGM).
        private static (double Sn, double Cn, double Dn) Jacobi(double u, double m)
        {
            if (m < 1e-9)
            {
                double t = Math.Sin(u), b = Math.Cos(u), ai = 0.25 * m * (u - t * b);
                return (t - ai * b, b + ai * t, 1 - 0.5 * m * t * t);
            }
            if (m >= 0.9999999999)
            {
                double ai = 0.25 * (1 - m), b = Math.Cosh(u), t = Math.Tanh(u), phi0 = 1 / b, twon0 = b * Math.Sinh(u);
                double sn = t + ai * (twon0 - u) / (b * b);
                ai *= t * phi0;
                return (sn, phi0 - ai * (twon0 - u), phi0 + ai * (twon0 + u));
            }
            var a = new double[9];
            var c = new double[9];
            a[0] = 1;
            double bb = Math.Sqrt(1 - m);
            c[0] = Math.Sqrt(m);
            double twon = 1;
            int i = 0;
            while (Math.Abs(c[i] / a[i]) > 1.11e-16 && i < 8)
            {
                double ai = a[i];
                i++;
                c[i] = (ai - bb) / 2;
                double t = Math.Sqrt(ai * bb);
                a[i] = (ai + bb) / 2;
                bb = t;
                twon *= 2;
            }
            double phi = twon * a[i] * u, prev = phi;
            for (; i > 0; i--)
            {
                double t = c[i] * Math.Sin(phi) / a[i];
                prev = phi;
                phi = (Math.Asin(t) + phi) / 2;
            }
            return (Math.Sin(phi), Math.Cos(phi), Math.Cos(phi) / Math.Cos(phi - prev));
        }

        // Imaginary part of the inverse Jacobi sn at the purely imaginary argument j·w (SciPy _arc_jac_sc1), by Landen descent.
        private static double ArcJacobiSc1(double w, double m)
        {
            var ks = new List<double> { Math.Sqrt(m) };
            while (ks[ks.Count - 1] != 0 && ks.Count < 20)
            {
                double kn = ks[ks.Count - 1], kp = Math.Sqrt((1 - kn) * (1 + kn));
                ks.Add((1 - kp) / (1 + kp));
            }
            double capK = Math.PI / 2;
            for (int i = 1; i < ks.Count; i++) capK *= 1 + ks[i];
            Complex wn = new Complex(0, w);
            for (int i = 0; i + 1 < ks.Count; i++)
            {
                Complex x = ks[i] * wn;
                wn = 2 * wn / ((1 + ks[i + 1]) * (1 + Complex.Sqrt((1 - x) * (1 + x))));
            }
            return (capK * 2 / Math.PI * Complex.Asin(wn)).Imaginary;
        }

        private static IirFilter Design((List<Complex> Z, List<Complex> P, double K) prototype, Band band, double fs, double f1, double f2)
        {
            if (!(fs > 0) || double.IsInfinity(fs)) throw new ArgumentOutOfRangeException(nameof(fs), fs, "Sample rate must be positive and finite.");
            bool twoEdges = band == Band.BandPass || band == Band.BandStop;
            CheckEdge(f1, fs, twoEdges ? "low" : "cutoff");
            if (twoEdges)
            {
                CheckEdge(f2, fs, "high");
                if (!(f1 < f2)) throw new ArgumentException("The low edge must be below the high edge.");
            }

            var (z, p, k) = prototype;
            double w1 = 2 * fs * Math.Tan(Math.PI * f1 / fs);           // pre-warp so the digital edges land exactly
            double w2 = twoEdges ? 2 * fs * Math.Tan(Math.PI * f2 / fs) : 0;
            switch (band)
            {
                case Band.LowPass: (z, p, k) = ToLowPass(z, p, k, w1); break;
                case Band.HighPass: (z, p, k) = ToHighPass(z, p, k, w1); break;
                case Band.BandPass: (z, p, k) = ToBandPass(z, p, k, Math.Sqrt(w1 * w2), w2 - w1); break;
                default: (z, p, k) = ToBandStop(z, p, k, Math.Sqrt(w1 * w2), w2 - w1); break;
            }
            (z, p, k) = Bilinear(z, p, k, fs);
            return new IirFilter(ToSections(z, p, k));
        }

        private static void CheckEdge(double f, double fs, string name)
        {
            if (!(f > 0 && f < fs / 2)) throw new ArgumentOutOfRangeException(name, f, "Edge frequencies must be between 0 and Nyquist (sampleRate / 2), exclusive.");
        }

        private static (List<Complex>, List<Complex>, double) ToLowPass(List<Complex> z, List<Complex> p, double k, double wo)
            => (z.Select(x => x * wo).ToList(), p.Select(x => x * wo).ToList(), k * Math.Pow(wo, p.Count - z.Count));

        private static (List<Complex>, List<Complex>, double) ToHighPass(List<Complex> z, List<Complex> p, double k, double wo)
        {
            int degree = p.Count - z.Count;
            var zh = z.Select(x => wo / x).ToList();
            zh.AddRange(Enumerable.Repeat(Complex.Zero, degree));
            double kh = k * (Product(z.Select(x => -x)) / Product(p.Select(x => -x))).Real;
            return (zh, p.Select(x => wo / x).ToList(), kh);
        }

        private static (List<Complex>, List<Complex>, double) ToBandPass(List<Complex> z, List<Complex> p, double k, double wo, double bw)
        {
            int degree = p.Count - z.Count;
            var zb = SplitRoots(z.Select(x => x * bw / 2), wo);
            zb.AddRange(Enumerable.Repeat(Complex.Zero, degree));
            return (zb, SplitRoots(p.Select(x => x * bw / 2), wo), k * Math.Pow(bw, degree));
        }

        private static (List<Complex>, List<Complex>, double) ToBandStop(List<Complex> z, List<Complex> p, double k, double wo, double bw)
        {
            int degree = p.Count - z.Count;
            var zb = SplitRoots(z.Select(x => bw / 2 / x), wo);
            for (int i = 0; i < degree; i++) zb.Add(new Complex(0, wo));
            for (int i = 0; i < degree; i++) zb.Add(new Complex(0, -wo));
            double kb = k * (Product(z.Select(x => -x)) / Product(p.Select(x => -x))).Real;
            return (zb, SplitRoots(p.Select(x => bw / 2 / x), wo), kb);
        }

        // Each scaled root r becomes the pair r ± sqrt(r² − wo²) (the low-pass → band frequency substitution).
        private static List<Complex> SplitRoots(IEnumerable<Complex> scaled, double wo)
        {
            var list = scaled.ToList();
            var result = new List<Complex>(2 * list.Count);
            foreach (var r in list) result.Add(r + Complex.Sqrt(r * r - wo * wo));
            foreach (var r in list) result.Add(r - Complex.Sqrt(r * r - wo * wo));
            return result;
        }

        private static (List<Complex>, List<Complex>, double) Bilinear(List<Complex> z, List<Complex> p, double k, double fs)
        {
            double fs2 = 2 * fs;
            int degree = p.Count - z.Count;
            var zz = z.Select(x => (fs2 + x) / (fs2 - x)).ToList();
            zz.AddRange(Enumerable.Repeat(new Complex(-1, 0), degree));    // analog zeros at infinity land at Nyquist
            double kz = k * (Product(z.Select(x => fs2 - x)) / Product(p.Select(x => fs2 - x))).Real;
            return (zz, p.Select(x => (fs2 + x) / (fs2 - x)).ToList(), kz);
        }

        private static Complex Product(IEnumerable<Complex> values)
        {
            Complex r = Complex.One;
            foreach (var v in values) r *= v;
            return r;
        }

        // Groups conjugate (or real) pole pairs into sections, the poles nearest the unit circle last (they have the
        // highest gain, so placing them late keeps intermediate signals small), each matched with the nearest zero pair.
        private static List<Biquad> ToSections(List<Complex> zeros, List<Complex> poles, double gain)
        {
            var poleGroups = Pairs(poles);
            var zeroGroups = Pairs(zeros);
            while (zeroGroups.Count < poleGroups.Count) zeroGroups.Add(new Complex[0]);
            poleGroups.Sort((a, b) => a.Max(x => x.Magnitude).CompareTo(b.Max(x => x.Magnitude)));

            var sections = new Biquad[poleGroups.Count];
            for (int i = poleGroups.Count - 1; i >= 0; i--)          // closest-to-circle poles pick their zeros first
            {
                var pg = poleGroups[i];
                int best = 0;
                double bestDistance = double.PositiveInfinity;
                for (int j = 0; j < zeroGroups.Count; j++)
                {
                    if (zeroGroups[j].Length > pg.Length) continue;  // a one-pole section can't take two zeros
                    double d = zeroGroups[j].Length == 0 ? double.MaxValue : zeroGroups[j].Min(zr => (zr - pg[0]).Magnitude);
                    if (d < bestDistance) { bestDistance = d; best = j; }
                }
                var zg = zeroGroups[best];
                zeroGroups.RemoveAt(best);
                var (b0, b1, b2) = Quadratic(zg);
                var (_, a1, a2) = Quadratic(pg);
                sections[i] = new Biquad(b0, b1, b2, a1, a2);
            }
            var first = sections[0];
            sections[0] = new Biquad(first.B0 * gain, first.B1 * gain, first.B2 * gain, first.A1, first.A2);
            return sections.ToList();
        }

        private static List<Complex[]> Pairs(List<Complex> roots)
        {
            var groups = new List<Complex[]>();
            var reals = new List<double>();
            foreach (var r in roots)
            {
                if (Math.Abs(r.Imaginary) <= 1e-10 * Math.Max(1, r.Magnitude)) reals.Add(r.Real);
                else if (r.Imaginary > 0) groups.Add(new[] { r, Complex.Conjugate(r) });
            }
            reals.Sort();
            for (int i = 0; i < reals.Count; i += 2)
                groups.Add(i + 1 < reals.Count ? new Complex[] { reals[i], reals[i + 1] } : new Complex[] { reals[i] });
            return groups;
        }

        // (1 − r1·z⁻¹)(1 − r2·z⁻¹) = 1 − (r1 + r2)·z⁻¹ + r1·r2·z⁻², as real coefficients (c0 = 1).
        private static (double C0, double C1, double C2) Quadratic(Complex[] roots)
        {
            switch (roots.Length)
            {
                case 0: return (1, 0, 0);
                case 1: return (1, -roots[0].Real, 0);
                default: return (1, -(roots[0] + roots[1]).Real, (roots[0] * roots[1]).Real);
            }
        }

        private static double Asinh(double x) => Math.Log(x + Math.Sqrt(x * x + 1));
    }
}
