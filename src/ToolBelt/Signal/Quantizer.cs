// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Numerics;

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
        // A range so narrow that its step is subnormal (0 to 100ε at 4 bits: 6ε for 6.25ε) rounded the step and every code
        // computed from it; min, max and step are kept scaled up by 2^600 (exact) then, and values are scaled to match.
        private readonly double _scale;

        /// <summary>Number of bits of resolution.</summary>
        public int Bits { get; }

        /// <summary>The quantization step (LSB) size in input units (rounded, if it is subnormal).</summary>
        public double Step => _step / _scale;

        /// <summary>Builds a quantizer of <paramref name="bits"/> bits spanning [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public Quantizer(int bits, double min = -1.0, double max = 1.0)
        {
            if (bits < 1 || bits > 30) throw new ArgumentOutOfRangeException(nameof(bits), bits, "Bits must be in 1..30.");
            if (double.IsNaN(min) || double.IsInfinity(min)) throw new ArgumentOutOfRangeException(nameof(min), min, "The range must be finite.");
            if (double.IsNaN(max) || double.IsInfinity(max)) throw new ArgumentOutOfRangeException(nameof(max), max, "The range must be finite.");
            if (!(max > min)) throw new ArgumentException("max must exceed min.", nameof(max));
            _scale = max - min < 3.054936363499605e-151 ? Math.Pow(2, 600) : 1;                     // 2^-500
            min *= _scale;
            max *= _scale;
            double step = (max - min) / (1 << bits);
            // ±1e308 overflowed max − min and the range was refused, although its step is representable: the halves are
            // divided separately then. A step that is still ∞ can't be used.
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
            value *= _scale;
            double offset = value - _min;
            // value − min overflows for a range near ±double.MaxValue; halved, it can't (an infinite value stays infinite).
            double ratio = double.IsInfinity(offset) && !double.IsInfinity(value) ? (value / 2 - _min / 2) / (_step / 2) : offset / _step;
            double code = Math.Floor(ratio);
            if (!(code > 0)) code = 0;
            if (code >= _levels - 1) code = _levels - 1;
            int c = (int)code;
            // The quotient is rounded, so a value an ulp below a bin edge could take the next code (Code(0.25⁻) at 3 bits
            // over [−1, 1] was 5). Its error is under 1e-6 of a step even at 30 bits; near an edge the code is settled exactly.
            double frac = ratio - code;
            if (!double.IsInfinity(value) && !(frac > 1e-5 && frac < 1 - 1e-5))
            {
                BigInteger v = Exact(value), lo = Exact(_min), range = Exact(_max) - lo;
                while (c < _levels - 1 && (v - lo) * _levels >= range * (c + 1)) c++;
                while (c > 0 && (v - lo) * _levels < range * c) c--;
            }
            return c;
        }

        // x·2^1074 as an integer: exact for every finite double.
        private static BigInteger Exact(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            BigInteger m = exponent == 0 ? mantissa : (BigInteger)(mantissa | 1L << 52) << (exponent - 1);
            return bits < 0 ? -m : m;
        }

        /// <summary>The reconstructed (quantized) value for <paramref name="value"/>: the center of its code bin.</summary>
        public double Quantize(double value)
        {
            int code = Code(value);
            double center = _min + (code + 0.5) * _step;
            // (code + ½)·step passes double.MaxValue for a range near ±double.MaxValue; halved, it can't.
            if (double.IsInfinity(center)) center = 2 * (_min / 2 + (code + 0.5) * (_step / 2));
            // Rounding the scaled center back down can step past the range (0 to 122ε gave 124ε for the top code).
            return Math.Min(_max / _scale, Math.Max(_min / _scale, center / _scale));
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
