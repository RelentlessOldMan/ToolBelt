// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Numerics;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Sample-rate conversion by linear interpolation, plus integer decimation with an anti-alias
    /// pre-average. Linear resampling is cheap and phase-linear — the right default when the signal is
    /// already comfortably oversampled; for aggressive downsampling, pre-filter first.
    /// </summary>
    public static class Resample
    {
        /// <summary>
        /// Resamples <paramref name="samples"/> to exactly <paramref name="newLength"/> points by linear
        /// interpolation, holding the endpoints fixed.
        /// </summary>
        public static double[] Linear(double[] samples, int newLength)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            // Past Array.MaxLength the result threw OutOfMemoryException ("array dimensions exceeded").
            if (newLength < 1 || newLength > 0x7FFFFFC7) throw new ArgumentOutOfRangeException(nameof(newLength), newLength, "New length must be 1 to Array.MaxLength (0x7FFFFFC7).");
            if (samples.Length == 0) throw new ArgumentException("Input must be non-empty.", nameof(samples));

            var result = new double[newLength];
            if (samples.Length == 1 || newLength == 1)
            {
                for (int i = 0; i < newLength; i++) result[i] = samples[0];
                return result;
            }

            // The position i·(L − 1)/(N − 1) in integers, whole part and remainder: i times a rounded scale missed input
            // points (3 to 99 points put output 49 a few ulps before input 1, interpolated, and NaN from a NaN input 0).
            for (int i = 0; i < newLength; i++)
            {
                long num = (long)i * (samples.Length - 1);
                int lo = (int)(num / (newLength - 1));
                // The last point is the last input: i·scale can round just below it ([0, 1] to 50 points ended at
                // 0.9999999999999999, and [NaN, 1] at NaN).
                if (lo >= samples.Length - 1 || i == newLength - 1) { result[i] = samples[samples.Length - 1]; continue; }
                double frac = (double)(num % (newLength - 1)) / (newLength - 1);
                // On an input point, take it as is: 0 × a NaN or ∞ neighbour is NaN.
                result[i] = frac == 0 ? samples[lo] : Lerp(samples[lo], samples[lo + 1], frac);
            }
            return result;
        }

        /// <summary>
        /// Resamples from <paramref name="sourceRate"/> to <paramref name="targetRate"/> (Hz) by linear interpolation. Output
        /// sample k is at time k/<paramref name="targetRate"/>, the same start as the input; the output covers the input's
        /// duration (length round(N·target/source), a half rounded up), and points past the last input sample hold its value.
        /// </summary>
        public static double[] ToRate(double[] samples, double sourceRate, double targetRate)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (!(sourceRate > 0) || double.IsInfinity(sourceRate)) throw new ArgumentOutOfRangeException(nameof(sourceRate), sourceRate, "Source rate must be positive and finite.");
            if (!(targetRate > 0) || double.IsInfinity(targetRate)) throw new ArgumentOutOfRangeException(nameof(targetRate), targetRate, "Target rate must be positive and finite.");
            if (samples.Length == 0) throw new ArgumentException("Input must be non-empty.", nameof(samples));
            // Halves went to even: 5 samples halved gave 2 and 7 gave 4.
            double length = Math.Round(samples.Length * (targetRate / sourceRate), MidpointRounding.AwayFromZero);
            // N·(t/s) rounded the ratio first, so a length near a half went the wrong way (1269 samples from 7.8 to 11.7 gave
            // 1904, though the binary rates put N·t/s just under 1903.5): the estimate is corrected against the exact halves
            // 2N·t = (2L ± 1)·s.
            if (length < 4294967296.0)
            {
                long l = (long)length, twoN = 2L * samples.Length;
                while (l > 0 && CompareProducts(targetRate, twoN, sourceRate, 2 * l - 1) < 0) l--;
                while (CompareProducts(targetRate, twoN, sourceRate, 2 * l + 1) >= 0) l++;
                length = l;
            }
            // Past Array.MaxLength (not int.MaxValue) the result threw OutOfMemoryException.
            if (!(length <= 0x7FFFFFC7)) throw new ArgumentOutOfRangeException(nameof(targetRate), targetRate, "The resampled signal would be too long for an array.");
            int newLength = Math.Max(1, (int)length);

            // Not Linear(samples, newLength): that pins both endpoints, which stretches the spacing away from the target rate.
            double step = sourceRate / targetRate;
            var result = new double[newLength];
            int last = samples.Length - 1;
            for (int i = 0; i < newLength; i++)
            {
                // i·source / target, rounded once: i times the rounded step put output 294 of 1 Hz to 49 Hz 6 ulps off input 6
                // (and NaN from a NaN input 5), though both are at 6 s. Where i·source overflows, the step is used.
                double scaled = i * sourceRate;
                double pos = double.IsInfinity(scaled) ? i * step : scaled / targetRate; // step = ∞ made 0·∞ = NaN, then an index out of range
                if (!(pos < last)) { result[i] = i == 0 ? samples[0] : samples[last]; continue; }
                int lo = (int)pos;
                double frac = pos - lo;
                result[i] = frac == 0 ? samples[lo] : Lerp(samples[lo], samples[lo + 1], frac);
            }
            return result;
        }

        private const double Down32 = 1.0 / 4294967296.0, Up32 = 4294967296.0;

        /// <summary>
        /// Downsamples by an integer <paramref name="factor"/>, averaging each block of
        /// <paramref name="factor"/> samples first to suppress aliasing. The result has
        /// <c>samples.Length / factor</c> samples: a trailing partial block (the last <c>Length % factor</c> samples) is
        /// dropped, not averaged short (SciPy's <c>decimate</c> keeps ⌈n/q⌉ samples).
        /// </summary>
        public static double[] Decimate(double[] samples, int factor)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be at least 1.");
            if (factor == 1) return (double[])samples.Clone();

            int outLen = samples.Length / factor;
            var result = new double[outLen];
            for (int i = 0; i < outLen; i++)
            {
                double sum = 0;
                for (int j = 0; j < factor; j++) sum += samples[i * factor + j];
                // The sum overflowed for representable samples; ∞ then plus a −∞ sample is NaN, so NaN is retried too. The
                // retry is the same sum scaled by 2^-32 (exact, and a block of 2^31 maxima fits): summing x/factor rounded
                // every term, so three double.MaxValue samples gave ∞ and constant blocks drifted by ulps.
                if (double.IsInfinity(sum) || double.IsNaN(sum))
                {
                    sum = 0;
                    for (int j = 0; j < factor; j++) sum += samples[i * factor + j] * Down32;
                    result[i] = sum / factor * Up32;
                }
                else result[i] = sum / factor;
            }
            return result;
        }

        // The difference form returns a constant signal exactly (a·(1 − t) + b·t rippled by an ulp); the weighted form is
        // the fallback where b − a overflows. Equal ends are returned as they are (two +∞ samples gave ∞ − ∞ = NaN between
        // them). Subnormal ends rounded d·t and then the sum (Linear([ε, 2ε], 3) had ε for 1.5ε, so 2ε): they are
        // interpolated scaled up by 2^600 (exact) and scaled back, one rounding.
        private static double Lerp(double a, double b, double t)
        {
            if (a == b) return a;
            if (Math.Abs(a) < Small && Math.Abs(b) < Small) return Lerp(a * Up, b * Up, t) * Down;
            double d = b - a;
            return double.IsInfinity(d) ? a * (1 - t) + b * t : a + d * t;
        }

        private static readonly double Up = Math.Pow(2, 600), Down = Math.Pow(2, -600), Small = Math.Pow(2, -400);
        // The sign of x·a − y·b, exactly (x, y finite; a, b ≥ 0).
        private static int CompareProducts(double x, long a, double y, long b)
        {
            var (mx, ex) = Decompose(x);
            var (my, ey) = Decompose(y);
            int e = Math.Min(ex, ey);
            return (new BigInteger(mx) * a << (ex - e)).CompareTo(new BigInteger(my) * b << (ey - e));
        }

        // x as an integer mantissa times a power of two (zero has mantissa 0).
        private static (long Mantissa, int Exponent) Decompose(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            return (bits < 0 ? -man : man, exp - 1075);
        }
    }
}
