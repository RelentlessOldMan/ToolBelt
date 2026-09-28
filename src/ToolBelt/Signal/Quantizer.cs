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
            if (!(max > min)) throw new ArgumentException("max must exceed min.", nameof(max));
            Bits = bits;
            _min = min;
            _max = max;
            _levels = 1 << bits;
            _step = (max - min) / _levels;
        }

        /// <summary>The integer code (0 .. 2ᴺ−1) for <paramref name="value"/>, clamped to range.</summary>
        public int Code(double value)
        {
            int code = (int)Math.Floor((value - _min) / _step);
            if (code < 0) code = 0;
            if (code > _levels - 1) code = _levels - 1;
            return code;
        }

        /// <summary>The reconstructed (quantized) value for <paramref name="value"/>: the center of its code bin.</summary>
        public double Quantize(double value) => _min + (Code(value) + 0.5) * _step;

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
