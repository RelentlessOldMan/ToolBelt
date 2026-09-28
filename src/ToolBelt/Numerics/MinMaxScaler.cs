// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Min-max feature scaling: linearly rescales values so the data's minimum maps to a target low and
    /// its maximum to a target high. Constant data (min == max) maps entirely to the target low, avoiding
    /// a divide-by-zero. Unlike <see cref="Interpolation.Remap"/>, the input range is derived from the
    /// data itself.
    /// </summary>
    public static class MinMaxScaler
    {
        /// <summary>Scales <paramref name="data"/> into [0, 1].</summary>
        public static double[] ToUnit(IReadOnlyList<double> data) => ToRange(data, 0.0, 1.0);

        /// <summary>Scales <paramref name="data"/> into [<paramref name="newMin"/>, <paramref name="newMax"/>].</summary>
        public static double[] ToRange(IReadOnlyList<double> data, double newMin, double newMax)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Count == 0) throw new ArgumentException("Data must not be empty.", nameof(data));

            double dataMin = data[0], dataMax = data[0];
            for (int i = 1; i < data.Count; i++)
            {
                if (data[i] < dataMin) dataMin = data[i];
                if (data[i] > dataMax) dataMax = data[i];
            }

            var result = new double[data.Count];
            for (int i = 0; i < data.Count; i++)
                result[i] = Scale(data[i], dataMin, dataMax, newMin, newMax);
            return result;
        }

        /// <summary>Scales one value from [<paramref name="dataMin"/>, <paramref name="dataMax"/>] to the target range.</summary>
        public static double Scale(double value, double dataMin, double dataMax, double newMin, double newMax)
        {
            if (dataMax == dataMin)
                return newMin; // constant input: no spread to preserve
            return newMin + (value - dataMin) / (dataMax - dataMin) * (newMax - newMin);
        }
    }
}
