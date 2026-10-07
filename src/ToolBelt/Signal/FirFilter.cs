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
            _head = _head + 1 == _history.Length ? 0 : _head + 1;
            return sum;
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
            var output = new double[samples.Count];
            for (int i = 0; i < output.Length; i++)
            {
                double sum = 0;
                int kMax = Math.Min(i, _taps.Length - 1);
                for (int k = 0; k <= kMax; k++) sum += _taps[k] * samples[i - k];
                output[i] = sum;
            }
            return output;
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
            var output = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                int centre = i + delay;
                int kLo = Math.Max(0, centre - (n - 1)), kHi = Math.Min(_taps.Length - 1, centre);
                for (int k = kLo; k <= kHi; k++) sum += _taps[k] * samples[centre - k];
                output[i] = sum;
            }
            return output;
        }

        public Complex Response(double frequency, double sampleRate)
        {
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            double w = -2 * Math.PI * frequency / sampleRate;
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
            if (!(attenuationDb > 8 && attenuationDb < 1000)) throw new ArgumentOutOfRangeException(nameof(attenuationDb), attenuationDb, "Attenuation must be between 8 and 1000 dB.");
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (!(transitionWidth > 0 && transitionWidth < sampleRate / 2)) throw new ArgumentOutOfRangeException(nameof(transitionWidth), transitionWidth, "Transition width must be between 0 and Nyquist, exclusive.");
            double a = attenuationDb;
            double beta = a > 50 ? 0.1102 * (a - 8.7) : a > 21 ? 0.5842 * Math.Pow(a - 21, 0.4) + 0.07886 * (a - 21) : 0;
            double width = transitionWidth / (sampleRate / 2);                      // fraction of Nyquist
            double taps = Math.Ceiling((a - 7.95) / 2.285 / (Math.PI * width) + 1);
            if (!(taps <= int.MaxValue)) throw new ArgumentOutOfRangeException(nameof(transitionWidth), transitionWidth, "The transition band is too narrow: the filter would need more than int.MaxValue taps.");
            return ((int)taps, beta);
        }

        private static FirFilter Design(int taps, double sampleRate, FirWindow window, double kaiserBeta, bool passZero, params (double Hz, string Name)[] edges)
        {
            if (taps < 1) throw new ArgumentOutOfRangeException(nameof(taps), taps, "At least one tap is required.");
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (window == FirWindow.Kaiser && !(kaiserBeta >= 0 && kaiserBeta < 700)) throw new ArgumentOutOfRangeException(nameof(kaiserBeta), kaiserBeta, "Kaiser beta must be in [0, 700).");
            if (!Enum.IsDefined(typeof(FirWindow), window)) throw new ArgumentOutOfRangeException(nameof(window), window, "Unknown window.");
            double nyquist = sampleRate / 2;
            var normalized = new double[edges.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                var (hz, name) = edges[i];
                if (!(hz > 0 && hz < nyquist)) throw new ArgumentOutOfRangeException(name, hz, "Edge frequencies must be between 0 and Nyquist (sampleRate / 2), exclusive.");
                if (i > 0 && !(hz > edges[i - 1].Hz)) throw new ArgumentException("The low edge must be below the high edge.");
                normalized[i] = hz / nyquist;
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

            var h = new double[taps];
            double alpha = (taps - 1) / 2.0;
            for (int n = 0; n < taps; n++)
            {
                double m = n - alpha, sum = 0;
                for (int b = 0; b < bands.Count; b += 2) sum += bands[b + 1] * Sinc(bands[b + 1] * m) - bands[b] * Sinc(bands[b] * m);
                h[n] = sum * Window(window, kaiserBeta, n, taps);
            }

            // Unit gain at the centre of the first passband (DC for pass-zero, Nyquist for a high-pass).
            double left = bands[0], right = bands[1];
            double scaleFrequency = left == 0 ? 0 : right == 1 ? 1 : (left + right) / 2;
            double s = 0;
            for (int n = 0; n < taps; n++) s += h[n] * Math.Cos(Math.PI * (n - alpha) * scaleFrequency);
            if (!(Math.Abs(s) > 0) || double.IsInfinity(s))
                throw new ArgumentException("The window leaves no gain to normalise (all taps are zero): use more taps or another window.", nameof(taps));
            for (int n = 0; n < taps; n++) h[n] /= s;
            return new FirFilter(h);
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
