using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class PulseMeasurementsTests
    {
        // A trapezoidal pulse: low, 10-interval rise, high, 10-interval fall, low.
        private static double[] Trapezoid()
        {
            var x = new double[51];
            for (int i = 0; i <= 10; i++) x[i] = 0;
            for (int i = 10; i <= 20; i++) x[i] = (i - 10) / 10.0;   // rise 0->1
            for (int i = 20; i <= 30; i++) x[i] = 1;
            for (int i = 30; i <= 40; i++) x[i] = 1 - (i - 30) / 10.0; // fall 1->0
            for (int i = 40; i < 51; i++) x[i] = 0;
            return x;
        }

        public void StateLevels_AreMinMax()
        {
            var (lo, hi) = PulseMeasurements.StateLevels(Trapezoid());
            Check.Close(0.0, lo, 1e-12);
            Check.Close(1.0, hi, 1e-12);
        }

        public void RiseTime_Is80PercentOfRamp()
        {
            // Ramp spans 10 intervals; 10%-90% is 8 intervals. fs=2 -> 4 seconds.
            Check.Close(4.0, PulseMeasurements.RiseTime(Trapezoid(), sampleRate: 2.0), 1e-9);
        }

        public void FallTime_Is80PercentOfRamp()
        {
            Check.Close(4.0, PulseMeasurements.FallTime(Trapezoid(), sampleRate: 2.0), 1e-9);
        }

        public void PulseWidth_BetweenMidCrossings()
        {
            // 50% rising at index 15, 50% falling at index 35 -> 20 intervals / fs 2 = 10 s.
            Check.Close(10.0, PulseMeasurements.PulseWidth(Trapezoid(), sampleRate: 2.0), 1e-9);
        }

        public void DutyCycle_OfRectangularPulse()
        {
            var x = new double[100];
            for (int i = 30; i < 70; i++) x[i] = 1; // 40 of 100 samples high
            Check.Close(0.4, PulseMeasurements.DutyCycle(x), 1e-12);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => PulseMeasurements.StateLevels(null!));
            Check.Throws<ArgumentException>(() => PulseMeasurements.DutyCycle(Array.Empty<double>()));
            Check.Throws<ArgumentOutOfRangeException>(() => PulseMeasurements.RiseTime(Trapezoid(), 0));
        }
    }
}
