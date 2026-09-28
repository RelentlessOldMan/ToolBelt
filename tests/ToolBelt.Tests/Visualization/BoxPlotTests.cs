using System;
using System.Collections.Generic;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class BoxPlotTests
    {
        public void Compute_QuartilesAndWhiskers()
        {
            var s = BoxPlot.Compute(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            Check.Close(3.0, s.Q1, 1e-12);   // rank 0.25*8 = 2 -> sorted[2]
            Check.Close(5.0, s.Median, 1e-12);
            Check.Close(7.0, s.Q3, 1e-12);
            Check.Close(4.0, s.Iqr, 1e-12);
            Check.Close(1.0, s.Min, 1e-12);
            Check.Close(9.0, s.Max, 1e-12);
            Check.Close(1.0, s.LowerWhisker, 1e-12); // no outliers -> whiskers reach the extremes
            Check.Close(9.0, s.UpperWhisker, 1e-12);
            Check.Equal(0, s.Outliers.Length);
        }

        public void Compute_DetectsOutlier()
        {
            var s = BoxPlot.Compute(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 100 });
            Check.Equal(1, s.Outliers.Length);
            Check.Close(100.0, s.Outliers[0], 1e-9);
            Check.Close(9.0, s.UpperWhisker, 1e-9); // whisker stops at the last in-fence value
        }

        public void Compute_InterpolatesQuartiles()
        {
            // 10 points -> fractional ranks exercise the R-7 interpolation.
            var s = BoxPlot.Compute(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            Check.Close(3.25, s.Q1, 1e-9);   // rank 0.25*9 = 2.25
            Check.Close(5.5, s.Median, 1e-9);
            Check.Close(7.75, s.Q3, 1e-9);
        }

        public void Compute_SingleValue()
        {
            var s = BoxPlot.Compute(new double[] { 42 });
            Check.Close(42.0, s.Q1, 1e-12);
            Check.Close(42.0, s.Median, 1e-12);
            Check.Close(42.0, s.Q3, 1e-12);
            Check.Equal(0, s.Outliers.Length);
        }

        public void Render_ProducesImageWithMedianColor()
        {
            var img = BoxPlot.Render(120, 100,
                new List<IReadOnlyList<double>> { new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 } },
                new BoxPlotOptions { MedianColor = Rgba.Red, DrawFrame = false });
            Check.Equal(120, img.Width);
            Check.Equal(100, img.Height);

            bool foundMedian = false;
            for (int y = 0; y < img.Height && !foundMedian; y++)
                for (int x = 0; x < img.Width; x++)
                    if (img.GetPixel(x, y) == Rgba.Red) { foundMedian = true; break; }
            Check.True(foundMedian, "median line should be drawn");
        }

        public void Render_MultipleDatasetsSideBySide()
        {
            var img = BoxPlot.Render(200, 100, new List<IReadOnlyList<double>>
            {
                new double[] { 1, 2, 3, 4, 5 },
                new double[] { 10, 11, 12, 13, 14 },
            }, new BoxPlotOptions { BoxColor = Rgba.Blue, DrawFrame = false });

            // Two boxes -> blue pixels on both the left and right halves.
            bool leftHalf = false, rightHalf = false;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if (img.GetPixel(x, y) == Rgba.Blue)
                    {
                        if (x < img.Width / 2) leftHalf = true; else rightHalf = true;
                    }
            Check.True(leftHalf && rightHalf, "a box should render in each half");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => BoxPlot.Compute(null!));
            Check.Throws<ArgumentException>(() => BoxPlot.Compute(Array.Empty<double>()));
            Check.Throws<ArgumentException>(() => BoxPlot.Render(50, 50, new List<IReadOnlyList<double>>()));
        }
    }
}
