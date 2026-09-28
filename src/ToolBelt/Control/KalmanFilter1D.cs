// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Control
{
    /// <summary>
    /// A scalar (one-dimensional) Kalman filter over a random-walk model, for smoothing a noisy live reading
    /// or fusing estimates. Each <see cref="Update"/> performs a predict step (inflating the error covariance
    /// by the process noise) then a measurement update. Unusually verifiable for a filter: the gain
    /// converges to a closed-form steady-state value. Not thread-safe.
    /// </summary>
    public sealed class KalmanFilter1D
    {
        private readonly double _processNoise;      // Q
        private readonly double _measurementNoise;  // R

        public KalmanFilter1D(double processNoise, double measurementNoise,
            double initialEstimate = 0, double initialErrorCovariance = 1)
        {
            if (processNoise < 0) throw new ArgumentOutOfRangeException(nameof(processNoise), processNoise, "Process noise must be non-negative.");
            if (measurementNoise <= 0) throw new ArgumentOutOfRangeException(nameof(measurementNoise), measurementNoise, "Measurement noise must be positive.");
            _processNoise = processNoise;
            _measurementNoise = measurementNoise;
            Estimate = initialEstimate;
            ErrorCovariance = initialErrorCovariance;
        }

        /// <summary>The current best estimate of the state.</summary>
        public double Estimate { get; private set; }

        /// <summary>The current estimate error covariance (P).</summary>
        public double ErrorCovariance { get; private set; }

        /// <summary>The Kalman gain applied by the most recent <see cref="Update"/>.</summary>
        public double LastGain { get; private set; }

        /// <summary>Incorporates a new measurement and returns the updated estimate.</summary>
        public double Update(double measurement)
        {
            // Predict: covariance grows by the process noise.
            double predictedCovariance = ErrorCovariance + _processNoise;

            // Update: blend the prediction with the measurement by the Kalman gain.
            double gain = predictedCovariance / (predictedCovariance + _measurementNoise);
            Estimate += gain * (measurement - Estimate);
            ErrorCovariance = (1 - gain) * predictedCovariance;
            LastGain = gain;
            return Estimate;
        }
    }
}
