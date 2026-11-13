// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// A uniform mid-rise quantizer / ideal-ADC model: maps a continuous voltage to one of 2ᴺ codes and
    /// back. Useful for modelling bit-depth reduction and for sanity-checking measured SNR against the
    /// textbook <c>6.02·N + 1.76 dB</c> figure.
    /// </summary>
    public sealed class Quantizer
    {
        private readonly double _min;
        private readonly double _max;
        private readonly int _levels;
        private readonly double _step;

        /// <summary>Number of bits of resolution.</summary>
        public int Bits { get; }

        /// <summary>The quantization step (LSB) size in input units.</summary>
        public double Step => _step;

        /// <summary>Builds a quantizer of <paramref name="bits"/> bits spanning [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public Quantizer(int bits, double min = -1.0, double max = 1.0)
        {
            if (bits < 1 || bits > 30) throw new ArgumentOutOfRangeException(nameof(bits), bits, "Bits must be in 1..30.");
            if (double.IsNaN(min) || double.IsInfinity(min)) throw new ArgumentOutOfRangeException(nameof(min), min, "The range must be finite.");
            if (double.IsNaN(max) || double.IsInfinity(max)) throw new ArgumentOutOfRangeException(nameof(max), max, "The range must be finite.");
            if (!(max > min)) throw new ArgumentException("max must exceed min.", nameof(max));
            double step = (max - min) / (1 << bits);
            // ±1e308 overflowed max − min and the range was refused, although its step is representable: the halves are
            // divided separately then. A step that is still ∞, or 0 for a denormal range, can't be used.
            if (double.IsInfinity(step)) step = max / (1 << bits) - min / (1 << bits);
            if (double.IsInfinity(step) || !(step > 0)) throw new ArgumentOutOfRangeException(nameof(max), max, "The range is too wide or too narrow for this many bits.");
            Bits = bits;
            _min = min;
            _max = max;
            _levels = 1 << bits;
            _step = step;
        }

        /// <summary>The integer code (0 .. 2ᴺ−1) for <paramref name="value"/>, clamped to range (±∞ included). NaN has no code.</summary>
        public int Code(double value)
        {
            if (double.IsNaN(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "NaN has no quantizer code.");
            // Clamp before the cast: an out-of-range double → int conversion is undefined (int.MinValue on x64 before .NET 9).
            double offset = value - _min;
            // value − min overflows for a range near ±double.MaxValue; halved, it can't (an infinite value stays infinite).
            double code = Math.Floor(double.IsInfinity(offset) && !double.IsInfinity(value) ? (value / 2 - _min / 2) / (_step / 2) : offset / _step);
            if (!(code > 0)) return 0;
            if (code >= _levels - 1) return _levels - 1;
            return (int)code;
        }

        /// <summary>The reconstructed (quantized) value for <paramref name="value"/>: the center of its code bin.</summary>
        public double Quantize(double value)
        {
            double center = _min + (Code(value) + 0.5) * _step;
            // (code + ½)·step passes double.MaxValue for a range near ±double.MaxValue; halved, it can't.
            return double.IsInfinity(center) ? 2 * (_min / 2 + (Code(value) + 0.5) * (_step / 2)) : center;
        }

        /// <summary>Quantizes an entire signal.</summary>
        public double[] Quantize(double[] samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            var result = new double[samples.Length];
            for (int i = 0; i < samples.Length; i++) result[i] = Quantize(samples[i]);
            return result;
        }

        /// <summary>The ideal SNR of an N-bit quantizer for a full-scale sine: <c>6.02·N + 1.76 dB</c>.</summary>
        public static double IdealSineSnrDb(int bits)
        {
            if (bits < 1) throw new ArgumentOutOfRangeException(nameof(bits), bits, "Bits must be positive.");
            return 6.02 * bits + 1.76;
        }
    }
}
