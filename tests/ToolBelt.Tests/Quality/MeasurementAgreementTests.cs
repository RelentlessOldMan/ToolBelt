using System;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Quality
{
    public sealed class MeasurementAgreementTests
    {
        public void IdenticalMethodsAgreePerfectly()
        {
            var a = new double[] { 1, 2, 3, 4, 5 };
            var r = MeasurementAgreement.BlandAltman(a, a);
            Check.Close(0, r.Bias, 1e-9);
            Check.Close(0, r.StdDevOfDifferences, 1e-9);
            Check.Close(0, r.LowerLimit, 1e-9);
            Check.Close(0, r.UpperLimit, 1e-9);
        }

        public void ConstantOffsetIsPureBias()
        {
            var a = new double[] { 10, 20, 30, 40 };
            var b = new double[] { 8, 18, 28, 38 }; // uniformly 2 lower
            var r = MeasurementAgreement.BlandAltman(a, b);
            Check.Close(2, r.Bias, 1e-9);
            Check.Close(0, r.StdDevOfDifferences, 1e-9); // no spread in the differences
        }

        public void LimitsAreBiasPlusMinusZSd()
        {
            var a = new double[] { 1, 2, 3, 4, 5, 6 };
            var b = new double[] { 1, 3, 3, 5, 5, 8 }; // varying differences
            var r = MeasurementAgreement.BlandAltman(a, b, z: 1.96);
            Check.Close(r.Bias - 1.96 * r.StdDevOfDifferences, r.LowerLimit, 1e-9);
            Check.Close(r.Bias + 1.96 * r.StdDevOfDifferences, r.UpperLimit, 1e-9);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => MeasurementAgreement.BlandAltman(new double[] { 1, 2 }, new double[] { 1 }));
            Check.Throws<ArgumentException>(() => MeasurementAgreement.BlandAltman(new double[] { 1 }, new double[] { 1 }));
            Check.Throws<ArgumentNullException>(() => MeasurementAgreement.BlandAltman(null!, new double[] { 1, 2 }));
        }
    }
}
