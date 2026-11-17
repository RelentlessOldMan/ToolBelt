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
            string? bad = !IsFinite(b0) ? nameof(b0) : !IsFinite(b1) ? nameof(b1) : !IsFinite(b2) ? nameof(b2)
                : !IsFinite(a1) ? nameof(a1) : !IsFinite(a2) ? nameof(a2) : null;
            if (bad != null) throw new ArgumentException("Coefficients must be finite.", bad);
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
            // (1 − cos)/2 from the same rounded cos as the poles keeps the DC gain exactly 1; it cancelled to 0 only where
            // the poles were already on the unit circle, which Normalize rejects.
            return Normalize((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha, sampleRate, frequency, q);
        }

        /// <summary>Second-order high-pass; <paramref name="q"/> = 1/√2 is maximally flat.</summary>
        public static Biquad HighPass(double sampleRate, double frequency, double q = 0.7071067811865476)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            return Normalize((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha, sampleRate, frequency, q);
        }

        /// <summary>Band-pass with unity (0 dB) gain at the centre <paramref name="frequency"/>; bandwidth ≈ frequency / Q.</summary>
        public static Biquad BandPass(double sampleRate, double frequency, double q)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            return Normalize(alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha, sampleRate, frequency, q);
        }

        /// <summary>Notch (band-reject): zero gain exactly at <paramref name="frequency"/>, e.g. 50/60 Hz mains hum.</summary>
        public static Biquad Notch(double sampleRate, double frequency, double q)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            return Normalize(1, -2 * cos, 1, 1 + alpha, -2 * cos, 1 - alpha, sampleRate, frequency, q);
        }

        /// <summary>All-pass: unity gain at every frequency, phase turning through −180° at <paramref name="frequency"/>.</summary>
        public static Biquad AllPass(double sampleRate, double frequency, double q)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            return Normalize(1 - alpha, -2 * cos, 1 + alpha, 1 + alpha, -2 * cos, 1 - alpha, sampleRate, frequency, q);
        }

        /// <summary>Peaking EQ: <paramref name="gainDb"/> of boost (or cut) centred on <paramref name="frequency"/>, unity far away.</summary>
        public static Biquad Peaking(double sampleRate, double frequency, double q, double gainDb)
        {
            var (cos, alpha) = Prewarp(sampleRate, frequency, q);
            double a = GainAmplitude(gainDb);
            // A tiny Q with an extreme gain overflowed here and came out of the constructor as a plain ArgumentException.
            if (double.IsInfinity(alpha * a) || double.IsInfinity(alpha / a))
                throw new ArgumentOutOfRangeException(nameof(q), q, "Q is too small for this gain.");
            var s = Normalize(1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a);
            return CheckGain(s, sampleRate, frequency, nameof(q), q, gainDb, () => Peaking(sampleRate, frequency, q, 0));
        }

        /// <summary>Low shelf: <paramref name="gainDb"/> at DC, unity at Nyquist, mid-gain at <paramref name="frequency"/>. Slope 1 is the steepest monotonic shelf.</summary>
        public static Biquad LowShelf(double sampleRate, double frequency, double gainDb, double slope = 1)
        {
            var (a, cos, k) = Shelf(sampleRate, frequency, gainDb, slope);
            var s = Normalize(
                a * ((a + 1) - (a - 1) * cos + k), 2 * a * ((a - 1) - (a + 1) * cos), a * ((a + 1) - (a - 1) * cos - k),
                (a + 1) + (a - 1) * cos + k, -2 * ((a - 1) + (a + 1) * cos), (a + 1) + (a - 1) * cos - k);
            return CheckGain(s, sampleRate, frequency, nameof(slope), slope, gainDb, () => LowShelf(sampleRate, frequency, 0, slope));
        }

        /// <summary>High shelf: <paramref name="gainDb"/> at Nyquist, unity at DC.</summary>
        public static Biquad HighShelf(double sampleRate, double frequency, double gainDb, double slope = 1)
        {
            var (a, cos, k) = Shelf(sampleRate, frequency, gainDb, slope);
            var s = Normalize(
                a * ((a + 1) + (a - 1) * cos + k), -2 * a * ((a - 1) + (a + 1) * cos), a * ((a + 1) + (a - 1) * cos - k),
                (a + 1) - (a - 1) * cos + k, 2 * ((a - 1) - (a + 1) * cos), (a + 1) - (a - 1) * cos - k);
            return CheckGain(s, sampleRate, frequency, nameof(slope), slope, gainDb, () => HighShelf(sampleRate, frequency, 0, slope));
        }

        /// <summary>The complex frequency response at <paramref name="frequency"/> Hz.</summary>
        public Complex Response(double frequency, double sampleRate)
        {
            CheckResponseArguments(frequency, sampleRate);
            Complex z1 = Complex.FromPolarCoordinates(1, -2 * Math.PI * Cycles(frequency, sampleRate));   // z⁻¹
            Complex z2 = z1 * z1;
            return (B0 + B1 * z1 + B2 * z2) / (1 + A1 * z1 + A2 * z2);
        }

        /// <summary>Group delay in samples at <paramref name="frequency"/> Hz (see <see cref="IirFilter.GroupDelay"/>).</summary>
        public double GroupDelay(double frequency, double sampleRate)
        {
            CheckResponseArguments(frequency, sampleRate);
            double x = Cycles(frequency, sampleRate);
            return PolynomialDelay(B0, B1, B2, x) - PolynomialDelay(1, A1, A2, x);
        }

        // −d arg P / dω for P = c0 + c1·z⁻¹ + c2·z⁻² at ω = 2πx: Re(Σ k·c_k e^(−jωk) / P). Next to a root on the unit circle
        // both sums cancel (a high-pass's delay near DC came out as −8e15 samples), so there it is summed per root:
        // P = c0·(1 − r1·z⁻¹)(1 − r2·z⁻¹), each factor contributing Re(−r / (e^(jω) − r)), with e^(jω) − r taken exactly.
        private static double PolynomialDelay(double c0, double c1, double c2, double x)
        {
            Complex z1 = Complex.FromPolarCoordinates(1, -2 * Math.PI * x), z2 = z1 * z1;
            Complex p = c0 + c1 * z1 + c2 * z2;
            double direct = ((c1 * z1 + 2 * c2 * z2) / p).Real;
            double size = Math.Abs(c0) + Math.Abs(c1) + Math.Abs(c2);
            if (!(p.Magnitude <= 1e-6 * size) || c0 == 0) return direct;
            Complex disc = Complex.Sqrt(new Complex(c1 * c1 - 4 * c0 * c2, 0));
            if (disc.Real * c1 < 0) disc = -disc;
            Complex q = -(c1 + disc) / 2;
            if (!IsFinite(q.Real) || !IsFinite(q.Imaginary) || q == Complex.Zero) return direct;
            Complex r1 = q / c0, r2 = c2 / q;
            // The sign of x only conjugates both terms (a real root, or swaps a conjugate pair), so the real part holds.
            double factored = (-r1 / OffsetOnCircle(x, r1)).Real + (-r2 / OffsetOnCircle(x, r2)).Real;
            return IsFinite(factored) ? factored : direct;
        }

        public override string ToString() => FormattableString.Invariant($"b=[{B0}, {B1}, {B2}] a=[1, {A1}, {A2}]");

        /// <summary>
        /// |numerator| at <paramref name="frequency"/>: B0 + B1·z⁻¹ + B2·z⁻² directly, or, when that sum cancels (next to a
        /// zero on the unit circle, as at Nyquist for a low-pass), from its roots as |B0|·|z − r1|·|z − r2|.
        /// </summary>
        internal double NumeratorMagnitude(double frequency, double sampleRate)
        {
            CheckResponseArguments(frequency, sampleRate);
            double w = -2 * Math.PI * Cycles(frequency, sampleRate);
            Complex z1 = Complex.FromPolarCoordinates(1, w), z2 = z1 * z1;
            double direct = (B0 + B1 * z1 + B2 * z2).Magnitude;
            double size = Math.Abs(B0) + Math.Abs(B1) + Math.Abs(B2);
            if (!(direct <= 1e-6 * size) || B0 == 0) return direct;
            // Roots of B0·z² + B1·z + B2 (|z⁻²·N(z)| = |N| on the circle), the stable way: q = −(B1 + sign(B1)·√disc)/2.
            Complex disc = Complex.Sqrt(new Complex(B1 * B1 - 4 * B0 * B2, 0));
            if (disc.Real * B1 < 0) disc = -disc;
            Complex q = -(B1 + disc) / 2;
            if (!IsFinite(q.Real) || !IsFinite(q.Imaginary) || q == Complex.Zero) return direct;
            Complex r1 = q / B0, r2 = B2 / q;
            double factored = Math.Abs(B0) * DistanceOnCircle(Cycles(frequency, sampleRate), r1) * DistanceOnCircle(Cycles(frequency, sampleRate), r2);
            return IsFinite(factored) ? factored : direct;
        }

        // |e^(j2πx) − r| for a root r of a real polynomial (so the sign of x does not matter). The point is taken relative
        // to the nearer of z = 1 and z = −1, through sin of the exact offset: e^(jω) itself rounds ω by about 4e-16, which
        // next to a zero at Nyquist was a relative error of 4e-16 / (2π·offset) per zero (0.03 dB at an offset of 1e-12
        // for 40 zeros).
        private static double DistanceOnCircle(double x, Complex r)
        {
            Complex d = OffsetOnCircle(x, r);
            return Hypot(d.Real, d.Imaginary);
        }

        // e^(j2π|x|) − r, with the point taken exactly as in DistanceOnCircle.
        private static Complex OffsetOnCircle(double x, Complex r)
        {
            double a = Math.Abs(x - Math.Round(x));                 // in [0, 0.5], exact
            double re, im;
            if (a <= 0.25)
            {
                double s = Math.Sin(Math.PI * a);                   // cos ω = 1 − 2 sin²(ω/2)
                re = (1 - r.Real) - 2 * s * s;
                im = Math.Sin(2 * Math.PI * a) - r.Imaginary;
            }
            else
            {
                double b = 0.5 - a, s = Math.Sin(Math.PI * b);      // ω = π − 2πb: cos ω = −1 + 2 sin²(πb)
                re = -(1 + r.Real) + 2 * s * s;
                im = Math.Sin(2 * Math.PI * b) - r.Imaginary;
            }
            return new Complex(re, im);
        }

        private static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            if (a < b) { double t = a; a = b; b = t; }
            if (a == 0) return 0;
            double t2 = b / a;
            return a * Math.Sqrt(1 + t2 * t2);
        }

        /// <summary>|denominator| 1 + A1·z⁻¹ + A2·z⁻² at <paramref name="frequency"/>.</summary>
        internal double DenominatorMagnitude(double frequency, double sampleRate)
        {
            Complex z1 = Complex.FromPolarCoordinates(1, -2 * Math.PI * Cycles(frequency, sampleRate));
            return (1 + A1 * z1 + A2 * z1 * z1).Magnitude;
        }

        // The frequency in cycles per sample, reduced to [−½, ½] first (IEEERemainder is exact): the plain ratio lost the
        // phase for a frequency far past the sample rate (2^60 Hz at 1 Hz did not give the DC response) and overflowed to
        // NaN for 1e300 Hz at 1e-10 Hz.
        private static double Cycles(double frequency, double sampleRate) => Math.IEEERemainder(frequency, sampleRate) / sampleRate;

        // A NaN or infinite frequency made every response NaN instead of failing; any finite one is fine (it aliases).
        private static void CheckResponseArguments(double frequency, double sampleRate)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (!IsFinite(frequency)) throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be finite.");
        }

        private static (double Cos, double Alpha) Prewarp(double sampleRate, double frequency, double q)
        {
            CheckFrequency(sampleRate, frequency);
            if (!(q > 0) || double.IsInfinity(q)) throw new ArgumentOutOfRangeException(nameof(q), q, "Q must be positive and finite.");
            double w0 = 2 * Math.PI * (frequency / sampleRate);   // the ratio first: π·f overflowed past fs ≈ 5e307
            double alpha = Math.Sin(w0) / (2 * q);
            if (double.IsInfinity(alpha)) throw new ArgumentOutOfRangeException(nameof(q), q, "Q is too small to represent.");
            return (Math.Cos(w0), alpha);
        }

        private static (double A, double Cos, double K) Shelf(double sampleRate, double frequency, double gainDb, double slope)
        {
            CheckFrequency(sampleRate, frequency);
            double a = GainAmplitude(gainDb);
            if (!(slope > 0) || double.IsInfinity(slope)) throw new ArgumentOutOfRangeException(nameof(slope), slope, "Slope must be positive and finite.");
            double w0 = 2 * Math.PI * (frequency / sampleRate);   // the ratio first: π·f overflowed past fs ≈ 5e307
            double inner = (a + 1 / a) * (1 / slope - 1) + 2;
            if (double.IsInfinity(inner)) throw new ArgumentOutOfRangeException(nameof(slope), slope, "Slope is too small to represent.");
            // At exactly 0 the poles sit on the unit circle; that fell through to the gain check, which blamed gainDb.
            if (!(inner > 0)) throw new ArgumentOutOfRangeException(nameof(slope), slope, "Slope too steep for this gain (the shelf's poles would reach the unit circle).");
            double alpha = Math.Sin(w0) / 2 * Math.Sqrt(inner);
            return (a, Math.Cos(w0), 2 * Math.Sqrt(a) * alpha);
        }

        private static double GainAmplitude(double gainDb)
        {
            if (!(Math.Abs(gainDb) <= 1000)) throw new ArgumentOutOfRangeException(nameof(gainDb), gainDb, "Gain must be within ±1000 dB.");
            return Math.Pow(10, gainDb / 40);
        }

        internal static void CheckFrequency(double sampleRate, double frequency)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (!(frequency > 0 && frequency < sampleRate / 2)) throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be between 0 and Nyquist (sampleRate / 2), exclusive.");
        }

        // A frequency near 0 or Nyquist (or an extreme gain) put a pole within rounding of the unit circle: the quantised
        // section had a pole at z = 1 (LowPass(1e9, 1): DC gain ∞, output 0) or a DC gain 33% off (3 Hz at 1 GHz).
        private static Biquad Normalize(double b0, double b1, double b2, double a0, double a1, double a2, double sampleRate, double frequency, double q)
        {
            var s = Normalize(b0, b1, b2, a0, a1, a2);
            if (!s.IsWellConditioned()) throw TooClose(sampleRate, frequency, nameof(q), q);
            return s;
        }

        private static Biquad Normalize(double b0, double b1, double b2, double a0, double a1, double a2)
            => new Biquad(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);

        // An extreme gain (±600 dB at 1 kHz of 48 kHz) pushes the poles onto the circle on its own: |H| came out NaN or
        // 0 at DC. The gain is named when the same filter at 0 dB holds.
        private static Biquad CheckGain(Biquad s, double sampleRate, double frequency, string shapeName, double shape, double gainDb, Func<Biquad> unity)
        {
            if (s.IsWellConditioned()) return s;
            if (gainDb != 0)
            {
                unity();                                                          // throws on the frequency if that's the cause
                throw new ArgumentOutOfRangeException(nameof(gainDb), gainDb, "The gain is too extreme for this frequency to hold in double precision: a pole lands on or within rounding of the unit circle.");
            }
            throw TooClose(sampleRate, frequency, shapeName, shape);
        }

        // An extreme Q (1e12 at 1 kHz of 48 kHz) or slope puts the poles on the circle at any frequency: it blamed the
        // frequency. The frequency is named only when a Butterworth section there fails too.
        private static ArgumentOutOfRangeException TooClose(double sampleRate, double frequency, string shapeName, double shape)
        {
            double w0 = 2 * Math.PI * (frequency / sampleRate), cos = Math.Cos(w0), alpha = Math.Sin(w0) / Math.Sqrt(2);
            var reference = Normalize((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
            if (reference.IsWellConditioned())
                return new ArgumentOutOfRangeException(shapeName, shape, $"The {(shapeName == "q" ? "Q" : shapeName)} is too extreme for this frequency to hold in double precision: a pole lands on or within rounding of the unit circle.");
            return new ArgumentOutOfRangeException(nameof(frequency), frequency, "The frequency is too close to 0 or Nyquist (for this Q and gain) to hold in double precision: a pole lands on or within rounding of the unit circle.");
        }

        // The poles are inside the unit circle, and rounding the coefficients moves the response at DC and Nyquist (and
        // the pole radius) by at most about 1e-4 relative.
        internal bool IsWellConditioned()
        {
            const double Eps = 2.220446049250313e-16, MaxRelativeError = 1e-4;
            if (!(Math.Abs(A2) < 1 && Math.Abs(A1) < 1 + A2)) return false;
            double size = 1 + Math.Abs(A1) + Math.Abs(A2);
            return Eps * size <= MaxRelativeError * (1 + A1 + A2) && Eps * size <= MaxRelativeError * (1 - A1 + A2)
                && Eps <= MaxRelativeError * (1 - Math.Abs(A2));
        }

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
    /// <item><b>Bessel</b>: the flattest group delay — pulses and steps keep their shape, with little overshoot (about 1% at low orders) — at the cost of the slowest roll-off.</item>
    /// </list>
    /// Use <see cref="Process"/> for streaming, <see cref="Filter"/> for a one-shot causal pass, and
    /// <see cref="FiltFilt"/> for zero-phase offline filtering. Not thread-safe.
    /// </summary>
    public sealed class IirFilter
    {
        private readonly Biquad[] _sections;
        private readonly double[] _z1, _z2;
        private readonly double[] _o1, _o2;    // the state before the current sample, to retry it rescaled
        private double _peak;           // the largest |input| since the last reset, for the subnormal flush
        // The streaming state is held in units of 2^_shift (inputs scaled by _down = 2^-_shift, outputs by _up): a finite
        // input whose state would overflow (a constant 1e308 through a low-pass, where A1·y passes double.MaxValue) left
        // the state infinite or NaN for good. It grows by 64 at a time when that happens; Reset() returns it to 0.
        private int _shift;
        private double _down = 1, _up = 1;
        private const int ShiftStep = 64, MaxShift = 960;
        private const double LowerBelow = 1.0e269;     // under 2^895: lowered by 2^64, the state is still 2^64 from overflow

        // A subnormal state is flushed to 0 once the signal reaches this size, where the change (under 2.3e-308 per sample)
        // is far below one rounding of the signal: a decaying tail in subnormal arithmetic ran 20 times slower. A signal
        // that is itself tiny keeps its subnormal state, so it filters exactly as before.
        private const double MinNormal = 2.2250738585072014E-308, FlushAbove = 2.0041683600089728E-292;     // 2^-1022, 2^-969

        public IirFilter(IEnumerable<Biquad> sections)
        {
            if (sections is null) throw new ArgumentNullException(nameof(sections));
            _sections = sections.ToArray();
            if (_sections.Length == 0) throw new ArgumentException("At least one section is required.", nameof(sections));
            if (_sections.Any(s => s is null)) throw new ArgumentException("Sections must not be null.", nameof(sections));
            _z1 = new double[_sections.Length];
            _z2 = new double[_sections.Length];
            _o1 = new double[_sections.Length];
            _o2 = new double[_sections.Length];
        }

        public IirFilter(params Biquad[] sections) : this((IEnumerable<Biquad>)sections) { }

        public IReadOnlyList<Biquad> Sections => Array.AsReadOnly(_sections);

        /// <summary>Filters one sample, carrying state from the previous call (direct form II transposed). A NaN or infinite
        /// input stays in the state (every later output is NaN) until <see cref="Reset()"/>, as with any recursive filter. A
        /// finite input never overflows the state (it is rescaled by a power of two when it would); an output whose true
        /// value is past double.MaxValue comes out infinite.</summary>
        public double Process(double sample)
        {
            if (Math.Abs(sample) > _peak) _peak = Math.Abs(sample);
            while (true)
            {
                double x = sample * _down;
                bool flush = _peak * _down >= FlushAbove;
                int s = 0;
                for (; s < _sections.Length; s++)
                {
                    var q = _sections[s];
                    double y = q.B0 * x + _z1[s];
                    if (flush && Math.Abs(y) < MinNormal) y = 0;
                    double a = q.B1 * x - q.A1 * y + _z2[s], b = q.B2 * x - q.A2 * y;
                    if (flush)
                    {
                        if (Math.Abs(a) < MinNormal) a = 0;
                        if (Math.Abs(b) < MinNormal) b = 0;
                    }
                    // A finite sample whose state overflows here: undo this sample, rescale and run it again.
                    if (!(Biquad.IsFinite(y) && Biquad.IsFinite(a) && Biquad.IsFinite(b)) && Biquad.IsFinite(x) && _shift < MaxShift
                        && Biquad.IsFinite(_z1[s]) && Biquad.IsFinite(_z2[s]))
                        break;
                    _o1[s] = _z1[s];
                    _o2[s] = _z2[s];
                    _z1[s] = a;
                    _z2[s] = b;
                    x = y;
                }
                if (s == _sections.Length)
                {
                    double output = x * _up;
                    // Back down once the state is small again: a shift that only grew kept flushing (to 0) the low bits of
                    // every later small signal after one burst near double.MaxValue, until Reset.
                    if (_shift > 0 && Math.Abs(sample * _down) < LowerBelow && Math.Abs(x) < LowerBelow)
                    {
                        bool small = true;
                        for (int r = 0; r < _sections.Length && small; r++)
                            small = Math.Abs(_z1[r]) < LowerBelow && Math.Abs(_z2[r]) < LowerBelow;
                        if (small) SetShift(_shift - ShiftStep);
                    }
                    return output;
                }
                for (int r = 0; r < s; r++) { _z1[r] = _o1[r]; _z2[r] = _o2[r]; }
                SetShift(_shift + ShiftStep);
            }
        }

        // Moves the streaming state to units of 2^shift (an exact power-of-two rescale, but for subnormal parts).
        private void SetShift(int shift)
        {
            double factor = Pow2(_shift - shift);
            for (int s = 0; s < _sections.Length; s++) { _z1[s] *= factor; _z2[s] *= factor; }
            _shift = shift;
            _down = Pow2(-shift);
            _up = Pow2(shift);
        }

        private static double Pow2(int e) => BitConverter.Int64BitsToDouble((long)(1023 + e) << 52);   // |e| ≤ 1022

        /// <summary>Clears the streaming state (as if no samples had been seen).</summary>
        public void Reset()
        {
            Array.Clear(_z1, 0, _z1.Length);
            Array.Clear(_z2, 0, _z2.Length);
            _peak = 0;
            _shift = 0;
            _down = _up = 1;
        }

        /// <summary>
        /// Clears the state to the steady state for a constant input <paramref name="value"/>, so a signal that starts
        /// far from zero doesn't produce a start-up transient.
        /// </summary>
        public void Reset(double value)
        {
            if (!Biquad.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be finite.");
            for (int shift = 0; ; shift += ShiftStep)
            {
                var (z1, z2) = SteadyState(value * Pow2(-shift));
                bool finite = true;
                for (int s = 0; s < _sections.Length; s++)
                {
                    _z1[s] = z1[s];
                    _z2[s] = z2[s];
                    finite &= Biquad.IsFinite(_z1[s]) && Biquad.IsFinite(_z2[s]);
                }
                if (finite || shift >= MaxShift)
                {
                    _shift = shift;
                    _down = Pow2(-shift);
                    _up = Pow2(shift);
                    break;
                }
            }
            _peak = Math.Abs(value);
        }

        /// <summary>A causal pass over <paramref name="samples"/> from a zero state (SciPy <c>sosfilt</c>); the streaming state is untouched.</summary>
        public double[] Filter(IReadOnlyList<double> samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            var output = new double[samples.Count];
            for (int i = 0; i < output.Length; i++) output[i] = samples[i];
            Run(output, new double[_sections.Length], new double[_sections.Length]);
            if (AllFinite(output) || !AllFinite(samples)) return output;
            // A finite input overflowed: stream it through a fresh copy, which rescales only from the sample that needs it
            // and back once the state is small again. One rescaled pass over the whole buffer lost the low bits of every
            // small output, before the overflow too.
            var fresh = new IirFilter(_sections);
            for (int i = 0; i < output.Length; i++) output[i] = fresh.Process(samples[i]);
            return output;
        }

        // Runs a linear pass at scale 1 and, when finite samples gave a non-finite result (an intermediate value, the
        // state or FiltFilt's extension past double.MaxValue), again at 2^-64, 2^-128, ... with the result scaled back:
        // an output whose true value is past double.MaxValue then comes out infinite, and the rest exact.
        private static double[] Rescaled(IReadOnlyList<double> samples, Func<double, double[]> pass)
        {
            double[] result = pass(1);
            if (AllFinite(result) || !AllFinite(samples)) return result;
            // Only the outputs that came out non-finite are taken from a rescaled pass: an overflow never yields a finite
            // value, so a finite one is exact, and redoing all of them at 2^-64 lost the low bits of small outputs (and
            // made a causal pass depend on later samples).
            for (int shift = ShiftStep; ; shift += ShiftStep)
            {
                double[] scaled = pass(Pow2(-shift));
                double up = Pow2(shift);
                bool done = true;
                for (int i = 0; i < result.Length; i++)
                {
                    if (Biquad.IsFinite(result[i])) continue;
                    if (Biquad.IsFinite(scaled[i]) || shift >= MaxShift) result[i] = scaled[i] * up;
                    if (!Biquad.IsFinite(scaled[i])) done = false;
                }
                if (done || shift >= MaxShift) return result;
            }
        }

        private static bool AllFinite(IReadOnlyList<double> values)
        {
            for (int i = 0; i < values.Count; i++) if (!Biquad.IsFinite(values[i])) return false;
            return true;
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
            if (n == 0 && pad == 0) return new double[0];         // was IndexOutOfRangeException reading ext[0]
            if (n <= pad) throw new ArgumentException($"The signal must be longer than the pad length ({pad}); pass a smaller padLength for short signals.", nameof(samples));

            // n + 2·pad wrapped in int (an OverflowException in the checked build) for a long signal with a long pad.
            if ((long)n + 2L * pad > 0x7FFFFFC7)
                throw new ArgumentOutOfRangeException(nameof(padLength), pad, "The padded signal (length + 2 × pad) is longer than the largest array.");
            double[] Pass(IReadOnlyList<double> x, double scale)
            {
                var ext = new double[n + 2 * pad];
                // x0 + (x0 − x[k]), not 2·x0 − x[k]: 2·x0 overflowed for samples near double.MaxValue (a constant 1e308 gave NaN).
                double first = x[0] * scale, last = x[n - 1] * scale;
                for (int i = 0; i < pad; i++) ext[i] = first + (first - x[pad - i] * scale);
                for (int i = 0; i < n; i++) ext[pad + i] = x[i] * scale;
                for (int i = 0; i < pad; i++) ext[pad + n + i] = last + (last - x[n - 2 - i] * scale);

                var (f1, f2) = SteadyState(ext[0]);
                Run(ext, f1, f2);
                Array.Reverse(ext);
                var (r1, r2) = SteadyState(ext[0]);
                Run(ext, r1, r2);
                Array.Reverse(ext);

                var result = new double[n];
                Array.Copy(ext, pad, result, 0, n);
                return result;
            }

            double[] direct = Pass(samples, 1);
            if (AllFinite(direct) || !AllFinite(samples)) return direct;
            // A finite signal overflowed. The backward pass carries the overflow to every sample, so rescaling the whole
            // signal lost the low bits (or all) of outputs far from the burst. FiltFilt is linear in the samples (so are
            // the extension and the initial state), so the small samples are filtered unscaled, only the large ones are
            // rescaled, and the two are added.
            var small = new double[n];
            var large = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(samples[i]) < LowerBelow) small[i] = samples[i];
                else large[i] = samples[i];
            }
            double[] smallOut = Pass(small, 1);
            if (!AllFinite(smallOut)) return Rescaled(samples, scale => Pass(samples, scale));
            double[] largeOut = Rescaled(large, scale => Pass(large, scale));
            for (int i = 0; i < n; i++) smallOut[i] += largeOut[i];
            return smallOut;
        }

        /// <summary>The complex frequency response of the whole cascade at <paramref name="frequency"/> Hz.</summary>
        public Complex Response(double frequency, double sampleRate)
        {
            // Kept as h·2^e: the product of the sections overflowed (∞ − ∞ = NaN parts) or underflowed where |H| fits.
            Complex h = Complex.One;
            int e = 0;
            foreach (var s in _sections)
            {
                Complex r = s.Response(frequency, sampleRate);
                int er = Exponent(Math.Max(Math.Abs(r.Real), Math.Abs(r.Imaginary)));
                h *= new Complex(ScaleB(r.Real, -er), ScaleB(r.Imaginary, -er));
                e += er;
                int eh = Exponent(Math.Max(Math.Abs(h.Real), Math.Abs(h.Imaginary)));
                h = new Complex(ScaleB(h.Real, -eh), ScaleB(h.Imaginary, -eh));
                e += eh;
            }
            return new Complex(ScaleB(h.Real, e), ScaleB(h.Imaginary, e));
        }

        /// <summary>|H| in decibels at <paramref name="frequency"/> Hz (−∞ at an exact zero).</summary>
        /// <remarks>Summed per section, with each numerator taken from its roots where the direct sum cancels: deep in
        /// the stop band of a high-order design |H| underflowed (−8000 dB came out as −∞) and the numerator next to its
        /// zeros at Nyquist cancelled completely.</remarks>
        public double MagnitudeDb(double frequency, double sampleRate)
        {
            double db = 0;
            foreach (var s in _sections)
                db += 20 * (Math.Log10(s.NumeratorMagnitude(frequency, sampleRate)) - Math.Log10(s.DenominatorMagnitude(frequency, sampleRate)));
            return db;
        }

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
            => Design(Chebyshev1Prototype(order, rippleDb), Band.LowPass, sampleRate, cutoff, 0, poleSpec: nameof(rippleDb));

        public static IirFilter Chebyshev1HighPass(int order, double rippleDb, double cutoff, double sampleRate)
            => Design(Chebyshev1Prototype(order, rippleDb), Band.HighPass, sampleRate, cutoff, 0, poleSpec: nameof(rippleDb));

        public static IirFilter Chebyshev1BandPass(int order, double rippleDb, double low, double high, double sampleRate)
            => Design(Chebyshev1Prototype(order, rippleDb), Band.BandPass, sampleRate, low, high, poleSpec: nameof(rippleDb));

        public static IirFilter Chebyshev1BandStop(int order, double rippleDb, double low, double high, double sampleRate)
            => Design(Chebyshev1Prototype(order, rippleDb), Band.BandStop, sampleRate, low, high, poleSpec: nameof(rippleDb));

        /// <summary>
        /// Chebyshev type II (inverse Chebyshev) low-pass: monotonic, ripple-free passband and a stop band that stays at
        /// least <paramref name="stopAttenuationDb"/> down. Note <paramref name="stopEdge"/> is where the stop band
        /// <em>begins</em> (gain first reaches −attenuation), not a −3 dB point — the passband ends somewhat below it.
        /// </summary>
        public static IirFilter Chebyshev2LowPass(int order, double stopAttenuationDb, double stopEdge, double sampleRate)
            => Design(Chebyshev2Prototype(order, stopAttenuationDb), Band.LowPass, sampleRate, stopEdge, 0, edgeName: nameof(stopEdge), poleSpec: nameof(stopAttenuationDb), zeroSpec: nameof(stopAttenuationDb));

        public static IirFilter Chebyshev2HighPass(int order, double stopAttenuationDb, double stopEdge, double sampleRate)
            => Design(Chebyshev2Prototype(order, stopAttenuationDb), Band.HighPass, sampleRate, stopEdge, 0, edgeName: nameof(stopEdge), poleSpec: nameof(stopAttenuationDb), zeroSpec: nameof(stopAttenuationDb));

        /// <summary>Chebyshev type II band-pass; <paramref name="low"/>/<paramref name="high"/> are the stop-band edges.</summary>
        public static IirFilter Chebyshev2BandPass(int order, double stopAttenuationDb, double low, double high, double sampleRate)
            => Design(Chebyshev2Prototype(order, stopAttenuationDb), Band.BandPass, sampleRate, low, high, poleSpec: nameof(stopAttenuationDb), zeroSpec: nameof(stopAttenuationDb));

        /// <summary>Chebyshev type II band-stop; the rejection band (at least the stated attenuation) is <paramref name="low"/>..<paramref name="high"/>.</summary>
        public static IirFilter Chebyshev2BandStop(int order, double stopAttenuationDb, double low, double high, double sampleRate)
            => Design(Chebyshev2Prototype(order, stopAttenuationDb), Band.BandStop, sampleRate, low, high, poleSpec: nameof(stopAttenuationDb), zeroSpec: nameof(stopAttenuationDb));

        /// <summary>
        /// Elliptic (Cauer) low-pass: <paramref name="passRippleDb"/> of passband ripple and at least
        /// <paramref name="stopAttenuationDb"/> of stop-band rejection, with the narrowest transition of any design of the
        /// same order. The gain at <paramref name="cutoff"/> (the passband edge) is −ripple dB. Expect ringing: its group
        /// delay peaks sharply near the edge.
        /// </summary>
        public static IirFilter EllipticLowPass(int order, double passRippleDb, double stopAttenuationDb, double cutoff, double sampleRate)
            => Design(EllipticPrototype(order, passRippleDb, stopAttenuationDb), Band.LowPass, sampleRate, cutoff, 0, poleSpec: nameof(passRippleDb), zeroSpec: nameof(stopAttenuationDb));

        public static IirFilter EllipticHighPass(int order, double passRippleDb, double stopAttenuationDb, double cutoff, double sampleRate)
            => Design(EllipticPrototype(order, passRippleDb, stopAttenuationDb), Band.HighPass, sampleRate, cutoff, 0, poleSpec: nameof(passRippleDb), zeroSpec: nameof(stopAttenuationDb));

        /// <summary>Elliptic band-pass; <paramref name="low"/>/<paramref name="high"/> are the passband edges.</summary>
        public static IirFilter EllipticBandPass(int order, double passRippleDb, double stopAttenuationDb, double low, double high, double sampleRate)
            => Design(EllipticPrototype(order, passRippleDb, stopAttenuationDb), Band.BandPass, sampleRate, low, high, poleSpec: nameof(passRippleDb), zeroSpec: nameof(stopAttenuationDb));

        public static IirFilter EllipticBandStop(int order, double passRippleDb, double stopAttenuationDb, double low, double high, double sampleRate)
            => Design(EllipticPrototype(order, passRippleDb, stopAttenuationDb), Band.BandStop, sampleRate, low, high, poleSpec: nameof(passRippleDb), zeroSpec: nameof(stopAttenuationDb));

        /// <summary>
        /// Bessel (Thomson) low-pass: maximally flat group delay, so the passband is delayed without dispersion and a step
        /// comes through with little overshoot: about 1% at orders 4 to 5 with the cutoff at or below 0.1·fs, more at higher
        /// orders and cutoffs (about 5% at order 8, 0.2·fs). Roll-off is the gentlest of the classic families.
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

        // ---- Order estimation ----------------------------------------------------------------------------------------
        // SciPy's buttord / cheb1ord / cheb2ord / ellipord: from a specification — passband edge(s) where the gain may
        // drop by at most passRippleDb, stop-band edge(s) where it must be down at least stopAttenuationDb — the lowest
        // order that meets it and the edge frequencies to hand to the matching design method. The band is implied by
        // the edges: pass below stop is a low-pass, pass above stop a high-pass; a stop band outside the passband is a
        // band-pass, one inside it a band-stop. The order is returned even when it exceeds what the designs accept.
        //
        // One deliberate difference: for band-stop specs SciPy searches numerically (fminbound) for passband edges that
        // balance the two transitions. The optimum has a closed form — tighten whichever passband edge restores geometric
        // symmetry about the stop band, pass_low·pass_high = stop_low·stop_high — so we use it. Orders agree with SciPy
        // for any sensible spec (the search's tolerance can cost it an order on very narrow ones); edges differ by ~1e-5.

        /// <summary>
        /// Minimum Butterworth order for a low- or high-pass specification, and the −3 dB cutoff to design it with
        /// (<see cref="ButterworthLowPass"/> / <see cref="ButterworthHighPass"/>), chosen so the gain at
        /// <paramref name="passEdge"/> is exactly −<paramref name="passRippleDb"/> dB.
        /// </summary>
        /// <example><code>
        /// var (order, cutoff) = IirFilter.ButterworthOrder(passEdge: 1000, stopEdge: 1500, passRippleDb: 1, stopAttenuationDb: 40, sampleRate: 8000);
        /// var filter = IirFilter.ButterworthLowPass(order, cutoff, 8000);
        /// </code></example>
        public static (int Order, double Cutoff) ButterworthOrder(double passEdge, double stopEdge, double passRippleDb, double stopAttenuationDb, double sampleRate)
        {
            var spec = new OrderSpec(passEdge, stopEdge, passRippleDb, stopAttenuationDb, sampleRate);
            int n = ButterworthOrderFor(spec);
            double w0 = ButterworthW0(spec, n);
            return (n, spec.ToHz(spec.Band == Band.LowPass ? w0 * spec.P0 : spec.P0 / w0));
        }

        /// <summary>
        /// Minimum Butterworth order for a band-pass or band-stop specification, and the −3 dB edges to design it with
        /// (<see cref="ButterworthBandPass"/> / <see cref="ButterworthBandStop"/>).
        /// </summary>
        public static (int Order, double Low, double High) ButterworthOrder(double passLow, double passHigh, double stopLow, double stopHigh, double passRippleDb, double stopAttenuationDb, double sampleRate)
        {
            var spec = new OrderSpec(passLow, passHigh, stopLow, stopHigh, passRippleDb, stopAttenuationDb, sampleRate);
            int n = ButterworthOrderFor(spec);
            double w0 = ButterworthW0(spec, n), d = spec.P1 - spec.P0, prod = spec.P0 * spec.P1;
            double a, b;
            if (spec.Band == Band.BandPass)
            {
                a = Math.Abs(-w0 * d / 2 + Math.Sqrt(w0 * w0 / 4 * d * d + prod));
                b = Math.Abs(w0 * d / 2 + Math.Sqrt(w0 * w0 / 4 * d * d + prod));
            }
            else
            {
                double discr = Math.Sqrt(d * d + 4 * w0 * w0 * prod);
                a = Math.Abs((d + discr) / (2 * w0));
                b = Math.Abs((d - discr) / (2 * w0));
            }
            return (n, spec.ToHz(Math.Min(a, b)), spec.ToHz(Math.Max(a, b)));
        }

        /// <summary>
        /// Minimum Chebyshev type I order for a low- or high-pass specification. The cutoff to design with
        /// (<see cref="Chebyshev1LowPass"/>, ripple = <paramref name="passRippleDb"/>) is the passband edge itself.
        /// </summary>
        public static (int Order, double Cutoff) Chebyshev1Order(double passEdge, double stopEdge, double passRippleDb, double stopAttenuationDb, double sampleRate)
        {
            var spec = new OrderSpec(passEdge, stopEdge, passRippleDb, stopAttenuationDb, sampleRate);
            return (ChebyshevOrderFor(spec), spec.ToHz(spec.P0));
        }

        /// <summary>Minimum Chebyshev type I order for a band-pass or band-stop specification, and the passband edges to design with.</summary>
        public static (int Order, double Low, double High) Chebyshev1Order(double passLow, double passHigh, double stopLow, double stopHigh, double passRippleDb, double stopAttenuationDb, double sampleRate)
        {
            var spec = new OrderSpec(passLow, passHigh, stopLow, stopHigh, passRippleDb, stopAttenuationDb, sampleRate);
            return (ChebyshevOrderFor(spec), spec.ToHz(spec.P0), spec.ToHz(spec.P1));
        }

        /// <summary>
        /// Minimum Chebyshev type II order for a low- or high-pass specification, and the stop-band edge to design it
        /// with (<see cref="Chebyshev2LowPass"/>, attenuation = <paramref name="stopAttenuationDb"/>): placed so the gain
        /// at <paramref name="passEdge"/> is exactly −<paramref name="passRippleDb"/> dB, leaving any slack in the stop band.
        /// </summary>
        public static (int Order, double StopEdge) Chebyshev2Order(double passEdge, double stopEdge, double passRippleDb, double stopAttenuationDb, double sampleRate)
        {
            var spec = new OrderSpec(passEdge, stopEdge, passRippleDb, stopAttenuationDb, sampleRate);
            int n = ChebyshevOrderFor(spec);
            double f = Chebyshev2Scale(spec, n);
            return (n, spec.ToHz(spec.Band == Band.LowPass ? spec.P0 / f : spec.P0 * f));
        }

        /// <summary>Minimum Chebyshev type II order for a band-pass or band-stop specification, and the stop-band edges to design with.</summary>
        public static (int Order, double Low, double High) Chebyshev2Order(double passLow, double passHigh, double stopLow, double stopHigh, double passRippleDb, double stopAttenuationDb, double sampleRate)
        {
            var spec = new OrderSpec(passLow, passHigh, stopLow, stopHigh, passRippleDb, stopAttenuationDb, sampleRate);
            int n = ChebyshevOrderFor(spec);
            double f = Chebyshev2Scale(spec, n), d = spec.P1 - spec.P0, prod = spec.P0 * spec.P1;
            double a = spec.Band == Band.BandStop
                ? -f / 2 * d + Math.Sqrt(f * f * d * d / 4 + prod)
                : -d / (2 * f) + Math.Sqrt(d * d / (4 * f * f) + prod);
            double b = prod / a;
            return (n, spec.ToHz(Math.Min(a, b)), spec.ToHz(Math.Max(a, b)));
        }

        /// <summary>
        /// Minimum elliptic order for a low- or high-pass specification. The cutoff to design with
        /// (<see cref="EllipticLowPass"/>, same ripple and attenuation) is the passband edge itself.
        /// </summary>
        /// <example><code>
        /// var (order, cutoff) = IirFilter.EllipticOrder(passEdge: 1000, stopEdge: 1100, passRippleDb: 0.5, stopAttenuationDb: 60, sampleRate: 8000);
        /// var filter = IirFilter.EllipticLowPass(order, 0.5, 60, cutoff, 8000);
        /// </code></example>
        public static (int Order, double Cutoff) EllipticOrder(double passEdge, double stopEdge, double passRippleDb, double stopAttenuationDb, double sampleRate)
        {
            var spec = new OrderSpec(passEdge, stopEdge, passRippleDb, stopAttenuationDb, sampleRate);
            return (EllipticOrderFor(spec), spec.ToHz(spec.P0));
        }

        /// <summary>Minimum elliptic order for a band-pass or band-stop specification, and the passband edges to design with.</summary>
        public static (int Order, double Low, double High) EllipticOrder(double passLow, double passHigh, double stopLow, double stopHigh, double passRippleDb, double stopAttenuationDb, double sampleRate)
        {
            var spec = new OrderSpec(passLow, passHigh, stopLow, stopHigh, passRippleDb, stopAttenuationDb, sampleRate);
            return (EllipticOrderFor(spec), spec.ToHz(spec.P0), spec.ToHz(spec.P1));
        }

        // A specification pre-warped to the analog prototype (ω = tan(π f / fs)), with the band-stop passband already
        // tightened to geometric symmetry; Selectivity is the stop/pass frequency ratio of the equivalent low-pass.
        private sealed class OrderSpec
        {
            public Band Band;
            public double P0, P1, S0, S1, Selectivity, Discrimination, LogDiscrimination, PassRippleDb, StopAttenuationDb;
            private readonly double _fs;

            public OrderSpec(double passEdge, double stopEdge, double passRippleDb, double stopAttenuationDb, double fs)
            {
                _fs = CheckSpec(passRippleDb, stopAttenuationDb, fs);
                CheckEdge(passEdge, fs, nameof(passEdge));
                CheckEdge(stopEdge, fs, nameof(stopEdge));
                if (passEdge == stopEdge) throw new ArgumentException("The pass and stop edges must differ.", nameof(stopEdge));
                Band = passEdge < stopEdge ? Band.LowPass : Band.HighPass;
                P0 = Warp(passEdge); S0 = Warp(stopEdge);
                Selectivity = Band == Band.LowPass ? S0 / P0 : P0 / S0;
                Finish(passRippleDb, stopAttenuationDb);
            }

            public OrderSpec(double passLow, double passHigh, double stopLow, double stopHigh, double passRippleDb, double stopAttenuationDb, double fs)
            {
                _fs = CheckSpec(passRippleDb, stopAttenuationDb, fs);
                CheckEdge(passLow, fs, nameof(passLow));
                CheckEdge(passHigh, fs, nameof(passHigh));
                CheckEdge(stopLow, fs, nameof(stopLow));
                CheckEdge(stopHigh, fs, nameof(stopHigh));
                if (stopLow < passLow && passLow < passHigh && passHigh < stopHigh) Band = Band.BandPass;
                else if (passLow < stopLow && stopLow < stopHigh && stopHigh < passHigh) Band = Band.BandStop;
                else throw new ArgumentException("Band edges must nest: stopLow < passLow < passHigh < stopHigh (band-pass) or passLow < stopLow < stopHigh < passHigh (band-stop).", nameof(stopLow));
                P0 = Warp(passLow); P1 = Warp(passHigh); S0 = Warp(stopLow); S1 = Warp(stopHigh);
                if (Band == Band.BandStop)
                {
                    if (P0 * P1 < S0 * S1) P0 = S0 * S1 / P1;
                    else P1 = S0 * S1 / P0;
                }
                double Ratio(double s) => Math.Abs((s * s - P0 * P1) / (s * (P1 - P0)));
                double lower = Ratio(S0), upper = Ratio(S1);
                Selectivity = Math.Min(Band == Band.BandPass ? lower : 1 / lower, Band == Band.BandPass ? upper : 1 / upper);
                Finish(passRippleDb, stopAttenuationDb);
            }

            // Ratios first: π·f and atan(w)·fs overflowed for a sample rate near MaxValue, and rounded a subnormal one.
            public double ToHz(double w) => Math.Atan(w) / Math.PI * _fs;

            private double Warp(double f) => Math.Tan(Math.PI * (f / _fs));

            private void Finish(double passRippleDb, double stopAttenuationDb)
            {
                PassRippleDb = passRippleDb;
                StopAttenuationDb = stopAttenuationDb;
                // (10^(As/10) − 1) / (10^(Ap/10) − 1): the squared ratio of stop-band to passband deviation.
                Discrimination = Pow10m1(stopAttenuationDb / 10) / Pow10m1(passRippleDb / 10);
                // The ratio overflows for a tiny ripple (1e-300 dB), which was reported as "transition band too narrow".
                LogDiscrimination = Math.Log(Pow10m1(stopAttenuationDb / 10)) - Math.Log(Pow10m1(passRippleDb / 10));
            }

            // acosh(√D), from the logarithm when D overflows.
            public double AcoshRootDiscrimination
                => Discrimination <= 1e300 ? Acosh(Math.Sqrt(Discrimination)) : Math.Log(2) + LogDiscrimination / 2;

            private static double CheckSpec(double passRippleDb, double stopAttenuationDb, double fs)
            {
                if (!(fs > 0) || double.IsInfinity(fs)) throw new ArgumentOutOfRangeException("sampleRate", fs, "Sample rate must be positive and finite.");
                if (!(passRippleDb > 0) || double.IsInfinity(passRippleDb)) throw new ArgumentOutOfRangeException(nameof(passRippleDb), passRippleDb, "Passband ripple must be positive and finite.");
                if (Pow10m1(passRippleDb / 10) == 0) throw new ArgumentOutOfRangeException(nameof(passRippleDb), passRippleDb, "Passband ripple is too small to represent.");
                if (!(stopAttenuationDb > passRippleDb && stopAttenuationDb <= 1000))
                    throw new ArgumentOutOfRangeException(nameof(stopAttenuationDb), stopAttenuationDb, "Stop-band attenuation must be greater than the passband ripple and at most 1000 dB.");
                return fs;
            }
        }

        // A selectivity rounded just below 1 (band edges a few ulps apart) made the logarithm negative, and the order 1:
        // no order meets such a spec, as the Chebyshev and elliptic estimates (acosh of it is NaN) already said.
        private static int ButterworthOrderFor(OrderSpec spec)
            => Ceiling(spec.Selectivity > 1 ? spec.LogDiscrimination / (2 * Math.Log(spec.Selectivity)) : double.PositiveInfinity);

        // Prototype frequency at which an order-n Butterworth is exactly −passRipple dB.
        private static double ButterworthW0(OrderSpec spec, int n) => Math.Pow(Pow10m1(spec.PassRippleDb / 10), -1.0 / (2 * n));

        private static int ChebyshevOrderFor(OrderSpec spec) => Ceiling(spec.AcoshRootDiscrimination / Acosh(spec.Selectivity));

        // Chebyshev II prototype frequency (stop edge = 1) at which the gain is exactly −passRipple dB.
        private static double Chebyshev2Scale(OrderSpec spec, int n) => 1 / Math.Cosh(spec.AcoshRootDiscrimination / n);

        private static int EllipticOrderFor(OrderSpec spec)
        {
            double m0 = 1 / (spec.Selectivity * spec.Selectivity), m1 = 1 / spec.Discrimination;
            // K(1 − m1) = ln(4/√m1) + O(m1 ln m1) once m1 = 1/D underflows.
            double km1 = spec.Discrimination <= 1e300 ? EllipKm1(m1) : Math.Log(4) + spec.LogDiscrimination / 2;
            return Ceiling(EllipK(m0) * km1 / (EllipKm1(m0) * EllipK(m1)));
        }

        private static int Ceiling(double n)
        {
            if (!(n <= int.MaxValue)) throw new ArgumentException("The transition band is too narrow for any practical order.", "stopAttenuationDb");
            return Math.Max(1, (int)Math.Ceiling(n));
        }

        // 10^x − 1 without cancellation for small x (Kahan's expm1 trick).
        private static double Pow10m1(double x)
        {
            double y = x * Math.Log(10), u = Math.Exp(y);
            if (u == 1) return y;
            return double.IsInfinity(u) ? u : (u - 1) * y / Math.Log(u);
        }

        // acosh(x) for x ≥ 1 = log1p((x − 1) + √((x − 1)(x + 1))): log(x + √(x² − 1)) lost digits near 1 and overflowed
        // past 1e154.
        private static double Acosh(double x)
        {
            if (x > 1e150) return Math.Log(x) + Math.Log(2);
            double d = x - 1;
            return Log1p(d + Math.Sqrt(d * (x + 1)));
        }

        private static double Log1p(double x)
        {
            double u = 1 + x;
            return u == 1 ? x : Math.Log(u) * x / (u - 1);
        }

        // ---- Internals -----------------------------------------------------------------------------------------------

        private enum Band { LowPass, HighPass, BandPass, BandStop }

        private void Run(double[] data, double[] z1, double[] z2)
        {
            // See FlushAbove. The flush starts at the first sample that reaches it, as in Process: the peak of the whole
            // buffer turned it on from sample 0, so a large sample later changed the outputs before it (not causal).
            // A NaN never reaches it.
            int flushFrom = data.Length;
            for (int i = 0; i < data.Length; i++) if (Math.Abs(data[i]) >= FlushAbove) { flushFrom = i; break; }
            for (int s = 0; s < _sections.Length; s++)
            {
                var q = _sections[s];
                double a = z1[s], b = z2[s];
                for (int i = 0; i < data.Length; i++)
                {
                    double x = data[i];
                    bool flush = i >= flushFrom;
                    double y = q.B0 * x + a;
                    if (flush && Math.Abs(y) < MinNormal) y = 0;
                    a = q.B1 * x - q.A1 * y + b;
                    b = q.B2 * x - q.A2 * y;
                    if (flush)
                    {
                        if (Math.Abs(a) < MinNormal) a = 0;
                        if (Math.Abs(b) < MinNormal) b = 0;
                    }
                    data[i] = y;
                }
            }
        }

        // Per-section state for the input value held forever, scaled by the DC gain of the sections before it (SciPy
        // sosfilt_zi times the value). The running scale is kept as m·2^e: the product of the gains overflowed (∞ · 0
        // made the state NaN) or underflowed to 0 where the state itself fits.
        private (double[] Z1, double[] Z2) SteadyState(double value)
        {
            var z1 = new double[_sections.Length];
            var z2 = new double[_sections.Length];
            int e = Exponent(Math.Abs(value));
            double m = ScaleB(value, -e);
            for (int s = 0; s < _sections.Length; s++)
            {
                var q = _sections[s];
                double denominator = 1 + q.A1 + q.A2, numerator = q.B0 + q.B1 + q.B2;
                // A pole at z = 1 has no steady state; leave that section (and everything after it) starting from zero.
                // Only an exact zero counts: an absolute threshold (1e-14) caught cutoffs near 1e-8·fs whose poles are all
                // inside the circle (design rejects |p| ≥ 1), and a high-pass's DC gain there is exactly 0.
                double gain = numerator == 0 ? 0 : numerator / denominator;
                if (double.IsNaN(gain) || double.IsInfinity(gain)) break;
                double m2 = m * (q.B2 - q.A2 * gain);
                double m1 = m * (q.B1 - q.A1 * gain) + m2;
                z2[s] = ScaleB(m2, e);
                z1[s] = ScaleB(m1, e);
                int eg = Exponent(Math.Abs(gain));
                m *= ScaleB(gain, -eg);
                int em = Exponent(Math.Abs(m));
                m = ScaleB(m, -em);
                e += eg + em;
            }
            return (z1, z2);
        }

        // The binary exponent of a finite non-zero x (0 for zero or non-finite), so x·2^-e lies in [1, 2).
        private static int Exponent(double x)
        {
            if (x == 0 || double.IsNaN(x) || double.IsInfinity(x)) return 0;
            int bias = 0;
            if (x < MinNormal) { x *= 1.8014398509481984E16; bias = 54; }            // 2^54
            return (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF) - 1023 - bias;
        }

        // x·2^n for any n, in steps of at most 2^±1000 so no intermediate power overflows.
        private static double ScaleB(double x, int n)
        {
            while (n > 1000 && x != 0 && !double.IsInfinity(x)) { x *= Pow2(1000); n -= 1000; }
            while (n < -1000 && x != 0 && !double.IsInfinity(x)) { x *= Pow2(-1000); n += 1000; }
            return x * Pow2(Math.Max(-1022, Math.Min(1023, n)));
        }

        private int DefaultPadLength()
        {
            int b2Zeros = _sections.Count(s => s.B2 == 0), a2Zeros = _sections.Count(s => s.A2 == 0);
            // In long: past about 358 million sections 3·(2S + 1) went negative, blamed on a padLength never passed.
            return (int)Math.Min(int.MaxValue, 3L * (2L * _sections.Length + 1 - Math.Min(b2Zeros, a2Zeros)));
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
            if (!(rippleDb > 0 && rippleDb < 100)) throw new ArgumentOutOfRangeException(nameof(rippleDb), rippleDb, "Ripple must be in (0, 100) dB.");
            double eps = Math.Sqrt(Pow10m1(0.1 * rippleDb));
            if (eps == 0) throw new ArgumentOutOfRangeException(nameof(rippleDb), rippleDb, "Ripple is too small to represent.");
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
            double de = 1 / Math.Sqrt(Pow10m1(0.1 * stopAttenuationDb));
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
            double epsSq = Pow10m1(0.1 * passRippleDb);
            if (epsSq == 0) throw new ArgumentOutOfRangeException(nameof(passRippleDb), passRippleDb, "Passband ripple is too small to represent.");
            if (order == 1)
            {
                double pole = -Math.Sqrt(1 / epsSq);
                return (new List<Complex>(), new List<Complex> { pole }, -pole);
            }
            double eps = Math.Sqrt(epsSq);
            double stopSq = Pow10m1(0.1 * stopAttenuationDb), ck1Sq = epsSq / stopSq;
            // 1 − ck1², exactly as (10^(rs/10) − 10^(rp/10)) / (10^(rs/10) − 1) when the attenuation is close to the ripple.
            double ck1c = ck1Sq < 0.5 ? 1 - ck1Sq : (1 + epsSq) * Pow10m1(0.1 * (stopAttenuationDb - passRippleDb)) / stopSq;
            var (m, mc) = EllipticDegree(order, ck1Sq, ck1c);
            double capK = EllipKm1(mc);

            var s = new List<double>();
            var c = new List<double>();
            var d = new List<double>();
            for (int j = 1 - order % 2; j < order; j += 2)
            {
                // Past K/2 the functions come from v = K − u (sn(u) = cn(v)/dn(v), cn(u) = √mc·sn(v)/dn(v), dn(u) = √mc/dn(v)):
                // sn(u) itself is near 1 there, and its distance from 1 (which places the zeros and poles next to the
                // band edge) was 4% off for a parameter near 1, so order 13 with 3 dB ripple and 10 dB attenuation dipped to
                // −3.08 dB in the passband.
                double sn, cn, dn;
                if (2 * j <= order) (sn, cn, dn) = Jacobi(j * capK / order, m, mc);
                else
                {
                    var (sv0, cv0, dv0) = Jacobi((order - j) * capK / order, m, mc);
                    double rootMc = Math.Sqrt(mc);
                    (sn, cn, dn) = (cv0 / dv0, rootMc * sv0 / dv0, rootMc / dv0);
                }
                s.Add(sn); c.Add(cn); d.Add(dn);
            }
            var z = new List<Complex>();
            foreach (double sn in s)
                if (Math.Abs(sn) > Epsilon) z.Add(new Complex(0, 1 / (Math.Sqrt(m) * sn)));
            z.AddRange(z.Select(Complex.Conjugate).ToList());

            double r = ArcJacobiSc1(1 / eps, ck1Sq, ck1c);
            double v0 = capK * r / (order * EllipKm1(ck1c));
            var (sv, cv, dv) = Jacobi(v0, mc, m);
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

        // Solves the degree equation: the elliptic parameter m an order-n filter achieves for the parameter m1 (complement
        // m1c = 1 − m1), returned with its complement 1 − m. K'(m)/K(m) = K'(m1)/(n·K(m1)); m = (θ2/θ3)⁴ of the nome
        // e^(−π K'/K). That series was summed to 8 terms at any nome, so at high order or high selectivity (nome near 1;
        // 0.9 gave m = 0.99949 for 1) the design missed its passband spec, and 1 − m lost what was left. Below a ratio of
        // 1 the complementary nome e^(−π K/K') gives 1 − m instead; either nome is then at most e^(−π) ≈ 0.043.
        private static (double M, double Mc) EllipticDegree(int n, double m1, double m1c)
        {
            double ratio = EllipKm1(m1) / (n * EllipKm1(m1c));                  // K'(m)/K(m)
            if (ratio >= 1)
            {
                double m = ThetaRatio(Math.Exp(-Math.PI * ratio));
                return (m, 1 - m);
            }
            double mc = ThetaRatio(Math.Exp(-Math.PI / ratio));
            return (1 - mc, mc);
        }

        // 16q·(Σ q^(i(i+1)) / (1 + 2Σ q^(i²)))⁴ = (θ2(q)/θ3(q))⁴, for a nome q ≤ e^(−π), where 8 terms are exact.
        private static double ThetaRatio(double q)
        {
            double num = 0, den = 0;
            for (int i = 0; i <= 7; i++) num += Math.Pow(q, i * (i + 1));
            for (int i = 1; i <= 8; i++) den += Math.Pow(q, i * i);
            return 16 * q * Math.Pow(num / (1 + 2 * den), 4);
        }

        // Jacobi elliptic functions sn, cn, dn of u with parameter m and its complement mc = 1 − m (Cephes ellpj:
        // descending Landen / AGM). The complement is passed, not formed, so a parameter near 1 keeps its digits.
        private static (double Sn, double Cn, double Dn) Jacobi(double u, double m, double mc)
        {
            if (m < 1e-9)
            {
                double t = Math.Sin(u), b = Math.Cos(u), ai = 0.25 * m * (u - t * b);
                return (t - ai * b, b + ai * t, 1 - 0.5 * m * t * t);
            }
            if (mc <= 1e-10)
            {
                double ai = 0.25 * mc, b = Math.Cosh(u), t = Math.Tanh(u), phi0 = 1 / b, twon0 = b * Math.Sinh(u);
                double sn = t + ai * (twon0 - u) / (b * b);
                ai *= t * phi0;
                return (sn, phi0 - ai * (twon0 - u), phi0 + ai * (twon0 + u));
            }
            var a = new double[9];
            var c = new double[9];
            a[0] = 1;
            double bb = Math.Sqrt(mc);
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

        // Imaginary part of the inverse Jacobi sn at the purely imaginary argument j·w (SciPy _arc_jac_sc1), by Landen
        // descent. The complementary modulus k' starts from mc and follows k'ₙ₊₁ = 2√k'ₙ / (1 + k'ₙ): forming √(1 − k²)
        // lost it for k near 1 (attenuation close to the ripple).
        private static double ArcJacobiSc1(double w, double m, double mc)
        {
            var ks = new List<double> { Math.Sqrt(m) };
            double kp = Math.Sqrt(mc);
            while (ks[ks.Count - 1] != 0 && ks.Count < 20)
            {
                ks.Add((1 - kp) / (1 + kp));
                kp = Math.Min(1, 2 * Math.Sqrt(kp) / (1 + kp));
            }
            double capK = Math.PI / 2;
            for (int i = 1; i < ks.Count; i++) capK *= 1 + ks[i];
            // The argument stays purely imaginary, j·y, so the descent runs on y in real arithmetic and asin(j·y) = j·asinh(y).
            // .NET Framework's Complex.Asin was 9e-8 off at j·1e5, which moved small-ripple elliptic designs by up to 7e-3.
            double y = w;
            for (int i = 0; i + 1 < ks.Count; i++)
            {
                double x = Math.Abs(ks[i] * y);                                  // (1 − jx)(1 + jx) = 1 + x²
                double root = x > 1e150 ? x : Math.Sqrt(1 + x * x);
                y = 2 * y / ((1 + ks[i + 1]) * (1 + root));
            }
            return capK * 2 / Math.PI * Asinh(y);
        }

        private static IirFilter Design((List<Complex> Z, List<Complex> P, double K) prototype, Band band, double sampleRate,
            double f1, double f2, string edgeName = "cutoff", string? poleSpec = null, string? zeroSpec = null, bool reference = false)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            bool twoEdges = band == Band.BandPass || band == Band.BandStop;
            CheckEdge(f1, sampleRate, twoEdges ? "low" : edgeName);
            if (twoEdges)
            {
                CheckEdge(f2, sampleRate, "high");
                if (!(f1 < f2)) throw new ArgumentException("The low edge must be below the high edge.", "high");
            }

            // Work in normalised frequency (a sample rate of 2, as SciPy does): the z-plane result is identical, but the
            // analog roots stay O(1) instead of scaling with the sample rate, whose products over 2N roots overflow.
            const double fs = 2;
            var (z, p, k) = prototype;
            // An extreme ripple or attenuation (1e-300 dB) puts prototype roots near 0, near ∞ or past it. The design then
            // failed as an edge too close to 0 or Nyquist, naming a mid-band cutoff, or as a degenerate design naming no
            // parameter. When it fails, this names the specification instead.
            string? spec = ExtremeRoot(p) ? poleSpec : ExtremeRoot(z) ? zeroSpec : null;
            Complex logK = Complex.Log(k);                              // the gain is carried as a logarithm for the same reason
            // Pre-warp so the digital edges land exactly. The ratio comes first: π·f overflowed for a sample rate near
            // MaxValue, and π·f/fs rounded at a subnormal one (a1 = 4e-4 for −1e-16 at fs = 2024·ε).
            double w1 = 2 * fs * Math.Tan(Math.PI * (f1 / sampleRate));
            double w2 = twoEdges ? 2 * fs * Math.Tan(Math.PI * (f2 / sampleRate)) : 0;
            switch (band)
            {
                case Band.LowPass: (z, p, logK) = ToLowPass(z, p, logK, w1); break;
                case Band.HighPass: (z, p, logK) = ToHighPass(z, p, logK, w1); break;
                case Band.BandPass: (z, p, logK) = ToBandPass(z, p, logK, Math.Sqrt(w1 * w2), w2 - w1); break;
                default: (z, p, logK) = ToBandStop(z, p, logK, Math.Sqrt(w1 * w2), w2 - w1); break;
            }
            (z, p, logK) = Bilinear(z, p, logK, fs);
            if (spec != null && !(AllFinite(z) && AllFinite(p)))
                throw new ArgumentOutOfRangeException(spec, "The design is numerically degenerate (a root is not finite); relax the specification.");
            // A pole on or outside the circle (an edge within rounding of 0 or Nyquist) was an ArgumentException naming no
            // parameter. A pole inside can still round onto it: ButterworthLowPass(2, 1, 1e9) had a section pole at exactly
            // z = 1 (DC gain ∞; a constant 3 came out as 3e-17).
            foreach (var x in p)
                if (!(x.Magnitude < 1)) throw spec != null ? SpecTooExtreme(spec) : SpecOrEdge(EdgeTooClose(x.Real < 0, twoEdges, edgeName, f1, f2));
            // A zero that is not finite reached the grouping, whose ArgumentException named no parameter.
            if (!AllFinite(z))
                throw new ArgumentOutOfRangeException(spec ?? edgeName, "The design is numerically degenerate (a root is not finite); relax the specification.");
            var sections = ToSections(z, p, logK);
            foreach (var s in sections)
                if (!s.IsWellConditioned()) throw spec != null ? SpecTooExtreme(spec) : SpecOrEdge(EdgeTooClose(1 - s.A1 + s.A2 < 1 + s.A1 + s.A2, twoEdges, edgeName, f1, f2));
            return new IirFilter(sections);

            // A very selective elliptic or Chebyshev design (order 15 with 3 dB ripple and 10 dB attenuation) has prototype
            // poles within 1e-11 of the jω axis, which no edge can hold: that was blamed on a mid-band cutoff "too close
            // to 0". When the same prototype fails at the middle of the band too, the specification is named.
            ArgumentOutOfRangeException SpecOrEdge(ArgumentOutOfRangeException edge)
            {
                if (reference || poleSpec == null) return edge;
                try { Design(prototype, Band.LowPass, 2, 0.5, 0, poleSpec: poleSpec, zeroSpec: zeroSpec, reference: true); }
                catch (ArgumentOutOfRangeException) { return SpecTooExtreme(zeroSpec ?? poleSpec); }
                return edge;
            }
        }

        private static bool ExtremeRoot(List<Complex> roots)
        {
            foreach (var r in roots)
            {
                double m = r.Magnitude;
                if (!(m < 1e30) || (m != 0 && m < 1e-30)) return true;   // also NaN; a prototype's roots are O(1) at sane specs
            }
            return false;
        }

        private static bool AllFinite(List<Complex> roots)
        {
            foreach (var r in roots)
                if (double.IsNaN(r.Real) || double.IsInfinity(r.Real) || double.IsNaN(r.Imaginary) || double.IsInfinity(r.Imaginary)) return false;
            return true;
        }

        private static ArgumentOutOfRangeException SpecTooExtreme(string spec)
            => new ArgumentOutOfRangeException(spec, "The ripple or attenuation is too extreme for this order in double precision: the poles land on or within rounding of the unit circle.");

        private static ArgumentOutOfRangeException EdgeTooClose(bool nearNyquist, bool twoEdges, string edgeName, double f1, double f2)
        {
            string name = twoEdges ? (nearNyquist ? "high" : "low") : edgeName;
            return new ArgumentOutOfRangeException(name, nearNyquist && twoEdges ? f2 : f1,
                $"The edge is too close to {(nearNyquist ? "Nyquist" : "0")} for this sample rate and order: in double precision a pole lands on or within rounding of the unit circle.");
        }

        private static void CheckEdge(double f, double fs, string name)
        {
            if (!(f > 0 && f < fs / 2)) throw new ArgumentOutOfRangeException(name, f, "Edge frequencies must be between 0 and Nyquist (sampleRate / 2), exclusive.");
        }

        private static (List<Complex>, List<Complex>, Complex) ToLowPass(List<Complex> z, List<Complex> p, Complex logK, double wo)
            => (z.Select(x => x * wo).ToList(), p.Select(x => x * wo).ToList(), logK + (p.Count - z.Count) * Math.Log(wo));

        private static (List<Complex>, List<Complex>, Complex) ToHighPass(List<Complex> z, List<Complex> p, Complex logK, double wo)
        {
            int degree = p.Count - z.Count;
            var zh = z.Select(x => wo / x).ToList();
            zh.AddRange(Enumerable.Repeat(Complex.Zero, degree));
            return (zh, p.Select(x => wo / x).ToList(), logK + LogProduct(z.Select(x => -x)) - LogProduct(p.Select(x => -x)));
        }

        private static (List<Complex>, List<Complex>, Complex) ToBandPass(List<Complex> z, List<Complex> p, Complex logK, double wo, double bw)
        {
            int degree = p.Count - z.Count;
            var zb = SplitRoots(z.Select(x => x * bw / 2), wo);
            zb.AddRange(Enumerable.Repeat(Complex.Zero, degree));
            return (zb, SplitRoots(p.Select(x => x * bw / 2), wo), logK + degree * Math.Log(bw));
        }

        private static (List<Complex>, List<Complex>, Complex) ToBandStop(List<Complex> z, List<Complex> p, Complex logK, double wo, double bw)
        {
            int degree = p.Count - z.Count;
            var zb = SplitRoots(z.Select(x => bw / 2 / x), wo);
            for (int i = 0; i < degree; i++) zb.Add(new Complex(0, wo));
            for (int i = 0; i < degree; i++) zb.Add(new Complex(0, -wo));
            return (zb, SplitRoots(p.Select(x => bw / 2 / x), wo), logK + LogProduct(z.Select(x => -x)) - LogProduct(p.Select(x => -x)));
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

        private static (List<Complex>, List<Complex>, Complex) Bilinear(List<Complex> z, List<Complex> p, Complex logK, double fs)
        {
            double fs2 = 2 * fs;
            int degree = p.Count - z.Count;
            var zz = z.Select(x => (fs2 + x) / (fs2 - x)).ToList();
            zz.AddRange(Enumerable.Repeat(new Complex(-1, 0), degree));    // analog zeros at infinity land at Nyquist
            return (zz, p.Select(x => (fs2 + x) / (fs2 - x)).ToList(), logK + LogProduct(z.Select(x => fs2 - x)) - LogProduct(p.Select(x => fs2 - x)));
        }

        private static Complex Product(IEnumerable<Complex> values)
        {
            Complex r = Complex.One;
            foreach (var v in values) r *= v;
            return r;
        }

        private static Complex LogProduct(IEnumerable<Complex> values)
        {
            Complex r = Complex.Zero;
            foreach (var v in values) r += Complex.Log(v);
            return r;
        }

        // Groups conjugate (or real) pole pairs into sections, each matched with the nearest zero pair (the poles nearest
        // the unit circle choose first), then orders and scales the sections so every intermediate signal stays near the
        // final output's level.
        private static List<Biquad> ToSections(List<Complex> zeros, List<Complex> poles, Complex logGain)
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
            return OrderAndScale(sections, logGain) ?? PlaceGain(sections, logGain);
        }

        // Ordering by pole radius alone let intermediate signals swing by many orders of magnitude: a band-stop
        // Butterworth of order 36 came out 3e4 times its own output in error, a Chebyshev I of order 30 3e13 times. Each
        // step picks the section that keeps the partial response's shape closest to its share (i/n) of the final one
        // over the passband (where the response is within 60 dB of its peak), and each partial product is scaled so its
        // peak equals the final peak, the last section taking the rest of the gain. Null if the response can't be
        // evaluated (no finite peak, or a scale that over- or underflows).
        private static List<Biquad>? OrderAndScale(Biquad[] sections, Complex logGain)
        {
            int n = sections.Length;
            if (n < 2) return null;
            var points = new List<double>(513 + 257 + 257 + 9 * n);
            for (int i = 0; i < 513; i++) points.Add(Math.PI * i / 512);
            for (int i = 0; i < 257; i++) points.Add(Math.PI * Math.Pow(10, -12.0 * i / 256));     // down to 1e-12·π
            // and up to π − 1e-12·π, and around each resonance: a narrow peak (a Chebyshev high-pass with its edge near
            // Nyquist) fell between the fixed points, leaving a partial product 24 dB above the output there.
            for (int i = 0; i < 257; i++) points.Add(Math.PI - Math.PI * Math.Pow(10, -12.0 * i / 256));
            foreach (var sec in sections)
            {
                double r2 = sec.A2, c = r2 > 0 ? -sec.A1 / (2 * Math.Sqrt(r2)) : 2;
                if (!(c > -1 && c < 1)) continue;                    // real poles: no resonance off 0 and π
                double theta = Math.Acos(c), width = Math.Max(1 - Math.Sqrt(r2), 1e-12);
                foreach (double k in new[] { 0, 0.25, 0.5, 1, 2 })
                    foreach (double w in new[] { theta - k * width, theta + k * width })
                        if (w > 0 && w < Math.PI) points.Add(w);
            }
            var grid = points.ToArray();
            int m = grid.Length;
            var lm = new double[n][];
            var total = new double[m];
            for (int j = 0; j < n; j++)
            {
                lm[j] = new double[m];
                for (int g = 0; g < m; g++) { lm[j][g] = LogMagnitude(sections[j], grid[g]); total[g] += lm[j][g]; }
            }
            double peak = double.NegativeInfinity;
            for (int g = 0; g < m; g++) if (total[g] > peak) peak = total[g];
            if (double.IsNaN(peak) || double.IsInfinity(peak)) return null;
            var mask = new List<int>();
            for (int g = 0; g < m; g++) if (total[g] >= peak - Math.Log(1000)) mask.Add(g);

            var remaining = Enumerable.Range(0, n).ToList();
            var order = new List<int>();
            var partial = new double[m];
            for (int i = 1; i <= n; i++)
            {
                int best = remaining[0];
                double bestSpread = double.PositiveInfinity;
                foreach (int j in remaining)
                {
                    double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                    foreach (int g in mask)
                    {
                        double d = partial[g] + lm[j][g] - (double)i / n * total[g];
                        if (d < lo) lo = d;
                        if (d > hi) hi = d;
                    }
                    if (hi - lo < bestSpread - 1e-12) { bestSpread = hi - lo; best = j; }
                }
                order.Add(best);
                remaining.Remove(best);
                for (int g = 0; g < m; g++) partial[g] += lm[best][g];
            }

            // Scale (as logarithms): the partial products' peaks all sit at the response's peak, peak + log|K|.
            double sign = Math.Cos(logGain.Imaginary) < 0 ? -1 : 1;
            var result = new List<Biquad>(n);
            Array.Clear(partial, 0, m);
            double applied = 0;
            for (int idx = 0; idx < n; idx++)
            {
                int j = order[idx];
                double partialPeak = double.NegativeInfinity;
                for (int g = 0; g < m; g++) { partial[g] += lm[j][g]; if (partial[g] > partialPeak) partialPeak = partial[g]; }
                // The final peak is peak + log|K|; aiming the partial products at the unscaled peak left every
                // intermediate signal about 1/|K| times the output (1e140 for a 40th-order low-pass at 1e-4·fs), so a
                // large input overflowed between sections.
                double lg = idx < n - 1 ? peak + logGain.Real - partialPeak : logGain.Real - applied;
                if (idx < n - 1) for (int g = 0; g < m; g++) partial[g] += lg;
                applied += lg;
                double gain = Math.Exp(lg);
                if (gain == 0 || double.IsInfinity(gain) || double.IsNaN(gain)) return null;
                if (idx == n - 1) gain *= sign;
                var s = sections[j];
                double b0 = s.B0 * gain, b1 = s.B1 * gain, b2 = s.B2 * gain;
                if (!(Biquad.IsFinite(b0) && Biquad.IsFinite(b1) && Biquad.IsFinite(b2))) return null;
                result.Add(new Biquad(b0, b1, b2, s.A1, s.A2));
            }
            return result;
        }

        // ln|H(e^jw)| of one section, from its (unscaled) coefficients.
        private static double LogMagnitude(Biquad s, double w)
        {
            var e1 = new Complex(Math.Cos(w), -Math.Sin(w));
            var e2 = e1 * e1;
            Complex num = s.B0 + s.B1 * e1 + s.B2 * e2, den = 1 + s.A1 * e1 + s.A2 * e2;
            return Math.Log(num.Magnitude) - Math.Log(den.Magnitude);
        }

        private static List<Biquad> PlaceGain(Biquad[] sections, Complex logGain)
        {
            // The gain is real (conjugate roots), so its log's imaginary part is 0 or ±π. It all goes on the first section,
            // as SciPy does, unless that would leave a comfortable range; then it is spread evenly over the sections.
            double sign = Math.Cos(logGain.Imaginary) < 0 ? -1 : 1;
            double log10Gain = logGain.Real / Math.Log(10);
            int spread = Math.Abs(log10Gain) <= 150 ? 1 : sections.Length;
            double each = Math.Pow(10, log10Gain / spread);
            for (int i = 0; i < spread; i++)
            {
                var section = sections[i];
                double g = i == 0 ? sign * each : each;
                sections[i] = new Biquad(section.B0 * g, section.B1 * g, section.B2 * g, section.A1, section.A2);
            }
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
            if (roots.Any(r => !Biquad.IsFinite(r.Real) || !Biquad.IsFinite(r.Imaginary)))
                throw new InvalidOperationException("A root is not finite.");   // Design rejects these before grouping
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

        // asinh(x) = log1p(|x| + x²/(1 + √(1 + x²))), signed: log(x + √(x² + 1)) lost digits for small x and overflowed past
        // 1e154 (Chebyshev I with a 1e-300 dB ripple took Asinh(2e150)).
        private static double Asinh(double x)
        {
            double a = Math.Abs(x);
            double r = a > 1e150 ? Math.Log(a) + Math.Log(2) : Log1p(a + a * a / (1 + Math.Sqrt(1 + a * a)));
            return x < 0 ? -r : r;
        }
    }
}
