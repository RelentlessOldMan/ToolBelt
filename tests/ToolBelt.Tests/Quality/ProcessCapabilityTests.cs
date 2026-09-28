using System;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Quality
{
    public sealed class ProcessCapabilityTests
    {
        // {-1, 0, 1} has mean 0 and sample stddev exactly 1, so capability is easy to reason about.
        private static readonly double[] Sample = { -1, 0, 1 };

        public void CenteredSixSigmaProcess()
        {
            var r = ProcessCapability.Compute(Sample, -3, 3, SigmaEstimator.Overall);
            Check.Close(0, r.Mean, 1e-9);
            Check.Close(1, r.Sigma, 1e-9);
            Check.Close(1, r.Cp, 1e-9);           // spec width 6 / (6·1)
            Check.Close(1, r.Cpk, 1e-9);          // centered
            Check.Close(3, r.SigmaLevel, 1e-9);   // Z = 3·Cpk
            Check.Close(2700, r.PpmDefective, 20);// ~2700 ppm for a ±3σ process
        }

        public void OffCenterReducesCpk()
        {
            var r = ProcessCapability.Compute(Sample, -3, 2, SigmaEstimator.Overall);
            Check.Close(5.0 / 6.0, r.Cp, 1e-9);   // spec width 5
            Check.Close(2.0 / 3.0, r.Cpk, 1e-9);  // nearer limit is USL at +2
        }

        public void WithinSubgroupEstimatorDiffers()
        {
            // Moving ranges are {1, 1} -> MRbar 1 -> sigma = 1/1.128.
            var r = ProcessCapability.Compute(Sample, -3, 3, SigmaEstimator.WithinSubgroup);
            Check.Close(1 / 1.128, r.Sigma, 1e-9);
            Check.Close(6.0 / (6 * (1 / 1.128)), r.Cp, 1e-9);
        }

        public void ZeroVariation_Throws()
        {
            Check.Throws<ArgumentException>(() => ProcessCapability.Compute(new double[] { 5, 5, 5 }, 0, 10));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => ProcessCapability.Compute(Sample, 3, 3));   // lsl>=usl
            Check.Throws<ArgumentException>(() => ProcessCapability.Compute(new double[] { 1 }, 0, 2));
            Check.Throws<ArgumentNullException>(() => ProcessCapability.Compute(null!, 0, 1));
        }
    }
}
