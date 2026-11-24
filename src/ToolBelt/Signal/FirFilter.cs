// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ToolBelt.Signal
{
    /// <summary>The taper applied to an ideal (sinc) FIR impulse response; trades transition width for stop-band rejection.</summary>
    public enum FirWindow
    {
        /// <summary>No taper: narrowest transition, only ~21 dB of rejection.</summary>
        Rectangular,
        /// <summary>~44 dB rejection.</summary>
        Hann,
        /// <summary>~53 dB rejection; the usual default.</summary>
        Hamming,
        /// <summary>~74 dB rejection, wider transition.</summary>
        Blackman,
        /// <summary>Adjustable via β; pick β and the tap count for a target attenuation with <see cref="FirFilter.KaiserParameters"/>.</summary>
        Kaiser,
    }

    /// <summary>
    /// A finite-impulse-response filter, designed by the window method (ideal sinc response × taper), plus a streaming
    /// processor. FIR filters are always stable and have exactly linear phase when the taps are symmetric (as every
    /// design here is): every frequency is delayed by the same <see cref="DelaySamples"/>, so waveform shape is
    /// preserved. Designs match SciPy's <c>firwin</c> (taps normalised to unit gain at the centre of the first passband).
    /// Not thread-safe.
    /// </summary>
    public sealed class FirFilter
    {
        private readonly double[] _taps;
        private readonly double[] _history;
        private int _head;

        public FirFilter(IReadOnlyList<double> taps)
        {
            if (taps is null) throw new ArgumentNullException(nameof(taps));
            if (taps.Count == 0) throw new ArgumentException("At least one tap is required.", nameof(taps));
            _taps = new double[taps.Count];
            for (int i = 0; i < _taps.Length; i++)
            {
                if (double.IsNaN(taps[i]) || double.IsInfinity(taps[i])) throw new ArgumentException("Taps must be finite.", nameof(taps));
                _taps[i] = taps[i];
            }
            _history = new double[_taps.Length];
        }

        public IReadOnlyList<double> Taps => Array.AsReadOnly(_taps);

        /// <summary>The group delay of a linear-phase design, (taps − 1) / 2 samples.</summary>
        public double DelaySamples => (_taps.Length - 1) / 2.0;

        /// <summary>Filters one sample, carrying the last taps − 1 inputs from previous calls.</summary>
        public double Process(double sample)
        {
            _history[_head] = sample;
            double sum = 0;
            int idx = _head;
            for (int k = 0; k < _taps.Length; k++)
            {
                sum += _taps[k] * _history[idx];
                idx = idx == 0 ? _history.Length - 1 : idx - 1;
            }
            // The running sum of finite terms can pass double.MaxValue while the output fits (a constant 1.7e308 through
            // a low-pass whose positive taps sum to 1.15): summed again in units of a power of two.
            if (!IsFinite(sum) && IsFinite(sample))
            {
                bool finite = true;
                foreach (double h in _history) finite &= IsFinite(h);
                if (finite)
                    for (int shift = ShiftStep; shift <= MaxShift; shift += ShiftStep)
                    {
                        double down = Pow2(-shift), scaled = 0;
                        idx = _head;
                        for (int k = 0; k < _taps.Length; k++)
                        {
                            scaled += _taps[k] * (_history[idx] * down);
                            idx = idx == 0 ? _history.Length - 1 : idx - 1;
                        }
                        sum = scaled * Pow2(shift);
                        if (IsFinite(scaled)) break;
                    }
            }
            _head = _head + 1 == _history.Length ? 0 : _head + 1;
            return sum;
        }

        private const int ShiftStep = 64, MaxShift = 960;

        private static double Pow2(int e) => BitConverter.Int64BitsToDouble((long)(1023 + e) << 52);   // |e| ≤ 1022

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // Runs a pass at scale 1 and, when finite samples gave a non-finite result, again at 2^-64, 2^-128, ... with the
        // result scaled back, so only an output whose true value is past double.MaxValue comes out infinite.
        private static double[] Rescaled(IReadOnlyList<double> samples, Func<double, double[]> pass)
        {
            double[] result = pass(1);
            if (AllFinite(result)) return result;
            // Outputs a non-finite sample reaches are non-finite at every scale; pass(0) marks them (a finite sample
            // times 0 is 0, NaN or ∞ times 0 is NaN), and the rest are rescued. Returning whenever any sample was
            // non-finite left an overflow away from a NaN as ∞.
            double[]? reached = AllFinite(samples) ? null : pass(0);
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
                    if (IsFinite(result[i]) || (reached != null && !IsFinite(reached[i]))) continue;
                    if (IsFinite(scaled[i]) || shift >= MaxShift) result[i] = scaled[i] * up;
                    if (!IsFinite(scaled[i])) done = false;
                }
                if (done || shift >= MaxShift) return result;
            }
        }

        private static bool AllFinite(IReadOnlyList<double> values)
        {
            for (int i = 0; i < values.Count; i++) if (!IsFinite(values[i])) return false;
            return true;
        }

        public void Reset()
        {
            Array.Clear(_history, 0, _history.Length);
            _head = 0;
        }

        /// <summary>A causal pass from a zero history (SciPy <c>lfilter(taps, 1, x)</c>): output[i] depends on inputs up to i, delayed by <see cref="DelaySamples"/>.</summary>
        public double[] Filter(IReadOnlyList<double> samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            return Rescaled(samples, scale =>
            {
                var output = new double[samples.Count];
                for (int i = 0; i < output.Length; i++)
                {
                    double sum = 0;
                    int kMax = Math.Min(i, _taps.Length - 1);
                    for (int k = 0; k <= kMax; k++) sum += _taps[k] * (samples[i - k] * scale);
                    output[i] = sum;
                }
                return output;
            });
        }

        /// <summary>
        /// The filtered signal shifted back by the delay so it lines up with the input (convolution "same" mode, zero
        /// beyond the ends). Needs an odd tap count, whose delay is a whole number of samples.
        /// </summary>
        public double[] FilterAligned(IReadOnlyList<double> samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (_taps.Length % 2 == 0) throw new InvalidOperationException("Aligned filtering needs an odd number of taps (an even count delays by a half sample).");
            int n = samples.Count, delay = _taps.Length / 2;
            return Rescaled(samples, scale =>
            {
                var output = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double sum = 0;
                    long centre = (long)i + delay;          // i + delay wrapped in int past int.MaxValue
                    int kLo = (int)Math.Max(0, centre - (n - 1)), kHi = (int)Math.Min(_taps.Length - 1, centre);
                    for (int k = kLo; k <= kHi; k++) sum += _taps[k] * (samples[(int)(centre - k)] * scale);
                    output[i] = sum;
                }
                return output;
            });
        }

        public Complex Response(double frequency, double sampleRate)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (double.IsNaN(frequency) || double.IsInfinity(frequency)) throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be finite.");
            // Reduced to [−fs/2, fs/2] first (exactly): the ratio alone lost the phase far past fs, and overflowed to NaN.
            double w = -2 * Math.PI * (Math.IEEERemainder(frequency, sampleRate) / sampleRate);
            double re = 0, im = 0;
            for (int k = 0; k < _taps.Length; k++) { re += _taps[k] * Math.Cos(w * k); im += _taps[k] * Math.Sin(w * k); }
            return new Complex(re, im);
        }

        public double MagnitudeDb(double frequency, double sampleRate) => 20 * Math.Log10(Response(frequency, sampleRate).Magnitude);

        // ---- Designs -------------------------------------------------------------------------------------------------

        /// <summary>Windowed-sinc low-pass with its −6 dB point at <paramref name="cutoff"/> Hz.</summary>
        public static FirFilter LowPass(int taps, double cutoff, double sampleRate, FirWindow window = FirWindow.Hamming, double kaiserBeta = 8.6)
            => Design(taps, sampleRate, window, kaiserBeta, passZero: true, (cutoff, nameof(cutoff)));

        /// <summary>Windowed-sinc high-pass. Needs an odd tap count (an even-length symmetric filter has a zero at Nyquist).</summary>
        public static FirFilter HighPass(int taps, double cutoff, double sampleRate, FirWindow window = FirWindow.Hamming, double kaiserBeta = 8.6)
            => Design(taps, sampleRate, window, kaiserBeta, passZero: false, (cutoff, nameof(cutoff)));

        public static FirFilter BandPass(int taps, double low, double high, double sampleRate, FirWindow window = FirWindow.Hamming, double kaiserBeta = 8.6)
            => Design(taps, sampleRate, window, kaiserBeta, passZero: false, (low, nameof(low)), (high, nameof(high)));

        /// <summary>Windowed-sinc band-stop. Needs an odd tap count.</summary>
        public static FirFilter BandStop(int taps, double low, double high, double sampleRate, FirWindow window = FirWindow.Hamming, double kaiserBeta = 8.6)
            => Design(taps, sampleRate, window, kaiserBeta, passZero: true, (low, nameof(low)), (high, nameof(high)));

        /// <summary>
        /// Kaiser's estimate of the tap count and β for <paramref name="attenuationDb"/> of stop-band rejection with a
        /// transition band <paramref name="transitionWidth"/> Hz wide (SciPy <c>kaiserord</c>). Use with <see cref="FirWindow.Kaiser"/>.
        /// </summary>
        public static (int Taps, double Beta) KaiserParameters(double attenuationDb, double transitionWidth, double sampleRate)
        {
            // 8 itself is allowed, as kaiserord allows it (it was refused).
            if (!(attenuationDb >= 8 && attenuationDb < 1000)) throw new ArgumentOutOfRangeException(nameof(attenuationDb), attenuationDb, "Attenuation must be at least 8 dB and below 1000 dB.");
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            // The ratio, not sampleRate / 2, as Design does: halving a subnormal sample rate rounded it (16 taps at fs =
            // 1.5e-323 where the same ratio at fs = 3 gives 12).
            if (!(transitionWidth > 0 && transitionWidth / sampleRate < 0.5)) throw new ArgumentOutOfRangeException(nameof(transitionWidth), transitionWidth, "Transition width must be between 0 and Nyquist, exclusive.");
            double a = attenuationDb;
            double beta = a > 50 ? 0.1102 * (a - 8.7) : a > 21 ? 0.5842 * Math.Pow(a - 21, 0.4) + 0.07886 * (a - 21) : 0;
            double width = 2 * (transitionWidth / sampleRate);                      // fraction of Nyquist
            double taps = Math.Ceiling((a - 7.95) / 2.285 / (Math.PI * width) + 1);
            if (!(taps <= MaxTaps)) throw new ArgumentOutOfRangeException(nameof(transitionWidth), transitionWidth, "The transition band is too narrow: the filter would need more taps than the largest array holds.");
            return ((int)taps, beta);
        }

        private static FirFilter Design(int taps, double sampleRate, FirWindow window, double kaiserBeta, bool passZero, params (double Hz, string Name)[] edges)
        {
            if (taps < 1) throw new ArgumentOutOfRangeException(nameof(taps), taps, "At least one tap is required.");
            if (taps > MaxTaps) throw new ArgumentOutOfRangeException(nameof(taps), taps, "More taps than the largest array holds.");
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (window == FirWindow.Kaiser && !(kaiserBeta >= 0 && kaiserBeta < 700)) throw new ArgumentOutOfRangeException(nameof(kaiserBeta), kaiserBeta, "Kaiser beta must be in [0, 700).");
            if (!Enum.IsDefined(typeof(FirWindow), window)) throw new ArgumentOutOfRangeException(nameof(window), window, "Unknown window.");
            // Edges as fractions of Nyquist, 2·(f/fs): f/(fs/2) rounded fs/2 at a subnormal sample rate.
            var normalized = new double[edges.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                var (hz, name) = edges[i];
                if (!(hz > 0 && hz / sampleRate < 0.5)) throw new ArgumentOutOfRangeException(name, hz, "Edge frequencies must be between 0 and Nyquist (sampleRate / 2), exclusive.");
                if (i > 0 && !(hz > edges[i - 1].Hz)) throw new ArgumentException("The low edge must be below the high edge.", name);
                normalized[i] = 2 * (hz / sampleRate);
                // A ratio that underflowed to 0 passed the check above: a low-pass then blamed taps ("no gain"), and a
                // band or high-pass edge was taken as 0. Two edges that round to one ratio make an empty band.
                if (normalized[i] == 0) throw new ArgumentOutOfRangeException(name, hz, "Edge frequency is too small relative to the sample rate to represent.");
                if (i > 0 && !(normalized[i] > normalized[i - 1])) throw new ArgumentException("The edges are too close together to represent at this sample rate.", name);
            }

            // Band edges in units of Nyquist, as (left, right) pairs: pass-zero designs start a band at 0, and a design
            // whose last band reaches Nyquist ends at 1.
            var bands = new List<double>();
            if (passZero) bands.Add(0);
            bands.AddRange(normalized);
            bool passNyquist = bands.Count % 2 == 1;
            if (passNyquist)
            {
                if (taps % 2 == 0) throw new ArgumentException("A filter that passes Nyquist (high-pass, band-stop) needs an odd number of taps.", nameof(taps));
                bands.Add(1);
            }

            // The band terms are taken relative to the highest edge (the normalisation below removes the factor): for a
            // subnormal cutoff, c·sinc(c·m) itself was subnormal and kept only a few digits (taps 60% off at 2e-323).
            double top = bands[bands.Count - 1];
            var h = NewTaps(taps);
            double alpha = (taps - 1) / 2.0;
            for (int n = 0; n < taps; n++)
            {
                double m = n - alpha, sum = 0;
                for (int b = 0; b < bands.Count; b += 2) sum += BandTerm(bands[b], bands[b + 1], top, m);
                h[n] = sum * Window(window, kaiserBeta, n, taps);
            }
            // The window's cosines round differently at n and taps − 1 − n; mirroring makes the taps exactly symmetric, so
            // the phase is exactly linear as documented.
            for (int n = 0; n < taps / 2; n++) h[taps - 1 - n] = h[n];

            // Unit gain at the centre of the first passband (DC for pass-zero, Nyquist for a high-pass).
            double left = bands[0], right = bands[1];
            // The centre's cosines are reduced exactly too: by Nyquist they sit next to zeros, and π·m·(left + right)/2
            // rounded left the gain at the band's centre 1e-8 off 1.
            double scaleLo = left == 0 ? 0 : right == 1 ? 1 : left, scaleHi = left == 0 ? 0 : right == 1 ? 1 : right;
            double s = 0;
            for (int n = 0; n < taps; n++) s += h[n] * CosPiHalfSum(n - alpha, scaleLo, scaleHi);
            if (!(Math.Abs(s) > 0) || double.IsInfinity(s))
                throw new ArgumentException("The window leaves no gain to normalise (all taps are zero): use more taps or another window.", nameof(taps));
            for (int n = 0; n < taps; n++) h[n] /= s;
            return new FirFilter(h);
        }

        private const int MaxTaps = 0x7FFFFFC7;     // the largest double[] (Array.MaxLength); past it the allocation was an OutOfMemoryException

        private static double[] NewTaps(int taps)
        {
#if NET8_0_OR_GREATER
            return new double[taps];
#else
            try { return new double[taps]; }
            catch (OutOfMemoryException) when (taps > 268435448)
            {
                throw new ArgumentOutOfRangeException(nameof(taps), taps,
                    "The taps need an array over 2 GB, which .NET Framework allows only with gcAllowVeryLargeObjects (or memory ran out).");
            }
#endif
        }

        // hi·sinc(hi·m) − lo·sinc(lo·m), over top. As that difference a narrow band lost its digits to cancellation (a
        // 1e-9-wide band's taps were 6e-8 off, a band by Nyquist 1.6e-4): it is taken in product form,
        // (hi − lo)·cos(π·m·(lo + hi)/2)·sinc(m·(hi − lo)/2), with nothing left to cancel.
        private static double BandTerm(double lo, double hi, double top, double m)
        {
            if (lo == 0) return hi / top * Sinc(hi * m);
            double width = hi - lo;
            if (m == 0) return width / top;
            return width / top * CosPiHalfSum(m, lo, hi) * Sinc(m * width * 0.5);
        }

        // cos(π·m·(lo + hi)/2) with the argument reduced exactly: by Nyquist the cosine sits next to a zero, where π·m·s
        // rounded (and s = (lo + hi)/2 rounded) carried most of the small remainder that decides it. m·lo and m·hi are
        // each split into an integer, an exact fraction and an exact product error (Dekker), and the fractions are
        // brought within a quarter turn, so only a small remainder meets the cosine.
        private static double CosPiHalfSum(double m, double lo, double hi)
        {
            double pLo = m * lo, pHi = m * hi;
            double kLo = Math.Round(pLo), kHi = Math.Round(pHi);
            double dLo = pLo - kLo, dHi = pHi - kHi;             // exact, each in [-1/2, 1/2]
            double j = Math.Round(dLo + dHi);                    // -1, 0 or 1
            double t = ((dLo - 0.5 * j) + (dHi - 0.5 * j)) + (ProductError(m, lo, pLo) + ProductError(m, hi, pHi));
            double x = Math.PI * 0.5 * t;                        // m·(lo + hi) = kLo + kHi + j + t, |t| about 1/2 or less
            switch ((int)(((long)kLo + (long)kHi + (long)j) & 3))
            {
                case 0: return Math.Cos(x);
                case 1: return -Math.Sin(x);
                case 2: return -Math.Cos(x);
                default: return Math.Sin(x);
            }
        }

        // The rounding error of p = a·b (Dekker's TwoProduct, by Veltkamp splitting): a·b = p + error exactly, barring
        // underflow. Used on both targets (netstandard2.0 has no fused multiply-add).
        private static double ProductError(double a, double b, double p)
        {
            const double Split = 134217729;   // 2^27 + 1
            double c = Split * a, aHi = c - (c - a), aLo = a - aHi;
            double d = Split * b, bHi = d - (d - b), bLo = b - bHi;
            return ((aHi * bHi - p) + aHi * bLo + aLo * bHi) + aLo * bLo;
        }

        private static double Sinc(double x)
        {
            if (x == 0) return 1;
            double px = Math.PI * x;
            return Math.Sin(px) / px;
        }

        // Symmetric windows (the ends both touch the taper), as filter design wants.
        private static double Window(FirWindow window, double beta, int n, int length)
        {
            if (length == 1) return 1;
            double r = 2 * Math.PI * n / (length - 1);
            switch (window)
            {
                case FirWindow.Rectangular: return 1;
                case FirWindow.Hann: return 0.5 - 0.5 * Math.Cos(r);
                case FirWindow.Hamming: return 0.54 - 0.46 * Math.Cos(r);
                case FirWindow.Blackman: return 0.42 - 0.5 * Math.Cos(r) + 0.08 * Math.Cos(2 * r);
                default:
                    double t = 2.0 * n / (length - 1) - 1;
                    return BesselI0(beta * Math.Sqrt(Math.Max(0, 1 - t * t))) / BesselI0(beta);
            }
        }

        // Modified Bessel function of the first kind, order 0: Σ ((x/2)^k / k!)², converging for every finite x.
        private static double BesselI0(double x)
        {
            double sum = 1, term = 1, half = x / 2;
            for (int k = 1; k < 500; k++)
            {
                term *= half / k;
                double t2 = term * term;
                sum += t2;
                if (t2 < sum * 1e-17) break;
            }
            return sum;
        }
    }
}
