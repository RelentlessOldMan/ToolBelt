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
            if (!(processNoise >= 0) || double.IsInfinity(processNoise)) throw new ArgumentOutOfRangeException(nameof(processNoise), processNoise, "Process noise must be non-negative and finite.");
            if (!(measurementNoise > 0) || double.IsInfinity(measurementNoise)) throw new ArgumentOutOfRangeException(nameof(measurementNoise), measurementNoise, "Measurement noise must be positive and finite.");
            _processNoise = processNoise;
            _measurementNoise = measurementNoise;
            Reset(initialEstimate, initialErrorCovariance);
        }

        /// <summary>The current best estimate of the state.</summary>
        public double Estimate { get; private set; }

        /// <summary>The current estimate error covariance (P).</summary>
        public double ErrorCovariance { get; private set; }

        /// <summary>The Kalman gain applied by the most recent <see cref="Update"/>.</summary>
        public double LastGain { get; private set; }

        /// <summary>Incorporates a new measurement and returns the updated estimate. A non-finite measurement is rejected
        /// (state untouched) rather than poisoning the estimate for good.</summary>
        public double Update(double measurement)
        {
            if (double.IsNaN(measurement) || double.IsInfinity(measurement)) throw new ArgumentOutOfRangeException(nameof(measurement), measurement, "Measurement must be finite.");
            // Predict: covariance grows by the process noise.
            double predictedCovariance = ErrorCovariance + _processNoise;

            // Update: blend the prediction with the measurement by the Kalman gain.
            double gain = predictedCovariance / (predictedCovariance + _measurementNoise);
            Estimate += gain * (measurement - Estimate);
            ErrorCovariance = (1 - gain) * predictedCovariance;
            LastGain = gain;
            return Estimate;
        }

        /// <summary>Restarts from <paramref name="estimate"/> with error covariance <paramref name="errorCovariance"/>.</summary>
        public void Reset(double estimate, double errorCovariance)
        {
            if (double.IsNaN(estimate) || double.IsInfinity(estimate)) throw new ArgumentOutOfRangeException(nameof(estimate), estimate, "Estimate must be finite.");
            if (!(errorCovariance >= 0) || double.IsInfinity(errorCovariance)) throw new ArgumentOutOfRangeException(nameof(errorCovariance), errorCovariance, "Error covariance must be non-negative and finite.");
            Estimate = estimate;
            ErrorCovariance = errorCovariance;
        }
    }
}
