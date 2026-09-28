using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class QuantizerTests
    {
        public void StepAndCodes()
        {
            var q = new Quantizer(2, -1, 1);   // 4 levels over [-1,1], step 0.5
            Check.Close(0.5, q.Step, 1e-12);
            Check.Equal(0, q.Code(-1.0));
            Check.Equal(3, q.Code(0.99));
            Check.Equal(3, q.Code(5.0));       // clamps high
            Check.Equal(0, q.Code(-5.0));      // clamps low
        }

        public void ReconstructsBinCenters()
        {
            var q = new Quantizer(2, -1, 1);   // bin centers at -0.75,-0.25,0.25,0.75
            Check.Close(-0.75, q.Quantize(-1.0), 1e-12);
            Check.Close(-0.25, q.Quantize(-0.3), 1e-12);
            Check.Close(0.25, q.Quantize(0.1), 1e-12);
            Check.Close(0.75, q.Quantize(0.99), 1e-12);
        }

        public void QuantizationErrorBoundedByHalfLsb()
        {
            var rng = new Random(17);
            var q = new Quantizer(8, -1, 1);
            for (int i = 0; i < 1000; i++)
            {
                double x = rng.NextDouble() * 2 - 1;
                double e = Math.Abs(q.Quantize(x) - x);
                Check.True(e <= q.Step / 2 + 1e-12, $"error {e} exceeds half-LSB");
            }
        }

        public void IdealSnr_ClosedForm()
        {
            Check.Close(49.92, Quantizer.IdealSineSnrDb(8), 1e-9);   // 6.02*8 + 1.76
            Check.Close(98.08, Quantizer.IdealSineSnrDb(16), 1e-9);
        }

        // Differential: the measured SNR of a full-scale sine tracks 6.02N+1.76 dB within ~1 dB.
        public void MeasuredSineSnr_MatchesIdeal()
        {
            const int bits = 10;
            const int n = 1 << 14;
            var q = new Quantizer(bits, -1, 1);
            double signalPower = 0, noisePower = 0;
            for (int i = 0; i < n; i++)
            {
                double x = 0.999 * Math.Sin(2 * Math.PI * 97 * i / n); // near full-scale, non-bin-aligned
                double qx = q.Quantize(x);
                signalPower += x * x;
                double e = qx - x;
                noisePower += e * e;
            }
            double measuredSnr = 10 * Math.Log10(signalPower / noisePower);
            Check.Close(Quantizer.IdealSineSnrDb(bits), measuredSnr, 1.5);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new Quantizer(0));
            Check.Throws<ArgumentOutOfRangeException>(() => new Quantizer(40));
            Check.Throws<ArgumentException>(() => new Quantizer(4, 1, 1));
            Check.Throws<ArgumentNullException>(() => new Quantizer(4).Quantize((double[])null!));
        }
    }
}
