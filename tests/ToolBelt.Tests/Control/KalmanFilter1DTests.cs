using System;
using ToolBelt.Control;
using ToolBelt.Tests.Framework;
using ToolBelt.Numerics;

namespace ToolBelt.Tests.Control
{
    public sealed class KalmanFilter1DTests
    {
        public void ConvergesToConstantSignal()
        {
            var kf = new KalmanFilter1D(processNoise: 1e-4, measurementNoise: 0.1, initialEstimate: 0);
            double est = 0;
            for (int i = 0; i < 500; i++) est = kf.Update(10);
            Check.Close(10, est, 0.05); // tracks the constant truth
        }

        public void TracksNoisyMeanCloser()
        {
            var kf = new KalmanFilter1D(processNoise: 1e-5, measurementNoise: 1.0);
            var rng = new DeterministicRandom(45);
            double est = 0;
            for (int i = 0; i < 2000; i++)
                est = kf.Update(20 + rng.NextGaussian(0, 1)); // noisy measurements around 20
            Check.Close(20, est, 0.5);
        }

        public void ErrorCovarianceAndGainDecrease()
        {
            var kf = new KalmanFilter1D(processNoise: 1e-4, measurementNoise: 0.1, initialErrorCovariance: 1);
            double firstGain, lastGain;
            kf.Update(1); firstGain = kf.LastGain;
            for (int i = 0; i < 100; i++) kf.Update(1);
            lastGain = kf.LastGain;
            Check.True(lastGain < firstGain, "gain should settle downward");
            Check.True(kf.ErrorCovariance < 1, "covariance should shrink");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new KalmanFilter1D(-1, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new KalmanFilter1D(1, 0));
        }
    }
}
