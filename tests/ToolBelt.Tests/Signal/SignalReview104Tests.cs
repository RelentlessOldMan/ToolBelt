using System;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 104 (signal analysis, sixth pass): each test failed before its fix.
    public class SignalReview104Tests
    {
        private const double Eps = double.Epsilon;
        private static readonly double Up = Math.Pow(2, 600), Down = Math.Pow(2, -600);

        private static void Same(double[] want, double[] got)
        {
            Check.Equal(want.Length, got.Length);
            for (int i = 0; i < want.Length; i++) Check.Equal(want[i], got[i], "i=" + i);
        }

        // ---------- Convolution ----------

        // Each subnormal product was rounded on its own: 0.5ε products summed to 0 everywhere, not [0, ε, 2ε, ε, 0].
        public void ConvolutionOfSubnormalsIsRoundedOnce()
        {
            double a = Math.Pow(2, -537), b = Math.Pow(2, -538);
            var c = Convolution.Convolve(new[] { a, a, a }, new[] { b, b, b });
            Same(new[] { 0, Eps, 2 * Eps, Eps, 0 }, c);
            var x = Convolution.CrossCorrelate(new[] { a, a, a }, new[] { b, b, b });
            Same(new[] { 0, Eps, 2 * Eps, Eps, 0 }, x);
        }

        // One ∞ anywhere turned the overflow rescue off for every element: an element of 1e200·1e200 − 1e200·1e200 was NaN.
        public void OneNonFiniteSampleLeavesOtherElementsRescued()
        {
            var c = Convolution.Convolve(new[] { double.PositiveInfinity, 0, 0, 1e200, 1e200 }, new[] { 1e200, -1e200 });
            Check.Equal(0.0, c[4]);
            Check.True(double.IsPositiveInfinity(c[0]), c[0].ToString());
            var x = Convolution.CrossCorrelate(new[] { double.NaN, 0, 0, 1e200, 1e200 }, new[] { -1e200, 1e200 });
            Check.Equal(0.0, x[4]);
        }

        // ---------- Savitzky-Golay ----------

        // Polynomials up to the order pass through unchanged, subnormal ones too: 3ε came back as 4ε at the edges.
        public void SubnormalPolynomialsPassThroughSavitzkyGolay()
        {
            var flat = new double[9];
            for (int i = 0; i < 9; i++) flat[i] = 3 * Eps;
            var s = SavitzkyGolay.Smooth(flat, 5, 2);
            for (int i = 0; i < 9; i++) Check.Equal(3 * Eps, s[i], "i=" + i);
            var ramp = new double[9];
            for (int i = 0; i < 9; i++) ramp[i] = i * Eps;
            var r = SavitzkyGolay.Smooth(ramp, 5, 1);
            for (int i = 0; i < 9; i++) Check.Equal(i * Eps, r[i], "i=" + i);
        }

        // ---------- Peak interpolation ----------

        // The vertex is scale-free; for subnormal values 0.5·(left − right) rounded away (offset 0 for 1/6).
        public void ParabolicVertexOfSubnormalsMatchesTheScaledShape()
        {
            foreach (var (l, c, r) in new[] { (1.0, 2.0, 0.0), (3.0, 4.0, 0.0), (1.0, 3.0, 2.0) })
            {
                var tiny = PeakInterpolation.Parabolic(l * Eps, c * Eps, r * Eps);
                var unit = PeakInterpolation.Parabolic(l, c, r);
                Check.True(Math.Abs(tiny.Offset - unit.Offset) < 1e-15, $"{l},{c},{r}: {tiny.Offset} vs {unit.Offset}");
            }
        }

        // ---------- Fft ----------

        // The inverse rounded a subnormal result twice (at the scale-back, then by 1/N): bin 3 was (−7, −10)ε for (−6.6, −10.53)ε.
        public void InverseOfASubnormalSpectrumIsRoundedOnce()
        {
            var x = new[] { new Complex(-19, -10), new Complex(-7, 20), new Complex(7, 6), new Complex(-9, 5), new Complex(8, 8), new Complex(18, -17), new Complex(-14, -5), new Complex(12, -14) };
            var tiny = new Complex[8];
            for (int i = 0; i < 8; i++) tiny[i] = x[i] * Eps;
            var y = Fft.Inverse(tiny);
            Check.Equal(-7 * Eps, y[3].Real);
            Check.Equal(-11 * Eps, y[3].Imaginary);

            var rng = new Random(104);
            for (int trial = 0; trial < 500; trial++)
            {
                int n = 1 << rng.Next(1, 5);
                var spec = new Complex[n];
                var big = new Complex[n];
                for (int i = 0; i < n; i++)
                {
                    spec[i] = new Complex(rng.Next(-40, 41) * Eps, rng.Next(-40, 41) * Eps);
                    big[i] = spec[i] * Up;
                }
                var got = Fft.Inverse(spec);
                var want = Fft.Inverse(big);
                for (int i = 0; i < n; i++)
                {
                    Check.Equal(want[i].Real * Down, got[i].Real, $"trial {trial} i={i}");
                    Check.Equal(want[i].Imaginary * Down, got[i].Imaginary, $"trial {trial} i={i}");
                }
            }
        }

        // ---------- Pulse measurements ----------

        // A rising crossing whose time rounded up to the next integer started the falling search a segment late: a runt at
        // index 1000 measured 599.5 samples wide (the next pulse's fall) for 4e-14.
        public void PulseWidthDoesNotSkipTheSegmentAfterARoundedCrossing()
        {
            var s = new double[2000];
            s[1000] = 0.5 + 1e-14;
            for (int i = 1500; i < 1600; i++) s[i] = 1;
            double width = PulseMeasurements.PulseWidth(s, 1);
            Check.True(width >= 0 && width < 1e-10, width.ToString("R"));
        }

        // ---------- Hampel ----------

        // With nσ = 0 any point off the median is an outlier; a MAD of double.MaxValue made the threshold 0·∞ = NaN, so none were.
        public void HampelWithZeroSigmasFlagsHugeWindows()
        {
            double m = double.MaxValue;
            var data = new[] { -m, m, -m, m, 0 };
            Check.Equal(5, Hampel.CountOutliers(data, 1, 0));
            var unit = Hampel.Filter(new[] { -1.0, 1, -1, 1, 0 }, 1, 0);
            var huge = Hampel.Filter(data, 1, 0);
            for (int i = 0; i < 5; i++) Check.Equal(unit[i] * m, huge[i], "i=" + i);
        }

        // 1.4826·MAD overflowed to ∞ for a representable MAD, so real outliers near ±1.5e308 were kept.
        public void HampelThresholdDoesNotOverflow()
        {
            var data = new[] { -1.5e308, -1.5e308, 0, 1.5e308, 1.5e308 };
            Check.Equal(4, Hampel.CountOutliers(data, 4, 0.5));
            Check.Equal(4, Hampel.CountOutliers(new[] { -1.5, -1.5, 0, 1.5, 1.5 }, 4, 0.5));
        }

        // ---------- Resample ----------

        // d·t rounded, then the sum: halfway between ε and 2ε came out ε, not 2ε (1.5ε rounded to even).
        public void LinearInterpolationOfSubnormalsIsRoundedOnce()
        {
            Same(new[] { Eps, 2 * Eps, 2 * Eps }, Resample.Linear(new[] { Eps, 2 * Eps }, 3));
            Same(new[] { Eps, 2 * Eps, 2 * Eps, 2 * Eps, 3 * Eps, 3 * Eps }, Resample.ToRate(new[] { Eps, 2 * Eps, 3 * Eps }, 1, 2));
        }

        // Two equal infinities gave ∞ − ∞ = NaN between them.
        public void LinearInterpolationBetweenEqualInfinitiesIsInfinite()
        {
            var r = Resample.Linear(new[] { double.PositiveInfinity, double.PositiveInfinity }, 3);
            foreach (double v in r) Check.True(double.IsPositiveInfinity(v), v.ToString());
            var t = Resample.ToRate(new[] { double.PositiveInfinity, double.PositiveInfinity, 1 }, 2, 4);
            Check.True(double.IsPositiveInfinity(t[1]), t[1].ToString());
        }

        // ---------- Time delay ----------

        // Lags 0 and 1 tie at exactly 1; the FFT's rounding gave lag 1 at 1.0000000000000002 against the nearest-zero rule.
        public void CrossCorrelationTiesGoToTheLagNearestZero()
        {
            var (lag, value) = TimeDelayEstimate.CrossCorrelationPeak(new[] { -1.0 }, new[] { -1.0, -1, 1, 2, 1, 2, -1 });
            Check.Equal(0, lag);
            Check.Equal(1.0, value);
        }

        // Graded against a direct correlation with the same tie rule over random small-integer signals.
        public void CrossCorrelationMatchesADirectReference()
        {
            var rng = new Random(1041);
            for (int trial = 0; trial < 1500; trial++)
            {
                var a = new double[rng.Next(1, 9)];
                var b = new double[rng.Next(1, 12)];
                for (int i = 0; i < a.Length; i++) a[i] = rng.Next(-3, 4);
                for (int i = 0; i < b.Length; i++) b[i] = rng.Next(-3, 4);
                int bestLag = 0;
                double best = double.NegativeInfinity;
                for (int k = -(a.Length - 1); k <= b.Length - 1; k++)
                {
                    double sum = 0;
                    for (int l = 0; l < a.Length; l++) if (l + k >= 0 && l + k < b.Length) sum += b[l + k] * a[l];
                    if (sum > best || (sum == best && Math.Abs(k) < Math.Abs(bestLag))) { best = sum; bestLag = k; }
                }
                var (lag, value) = TimeDelayEstimate.CrossCorrelationPeak(a, b);
                Check.Equal(bestLag, lag, $"trial {trial}");
                Check.Equal(best, value, $"trial {trial}");
            }
        }

        // ---------- Envelope follower ----------

        // Just above the series cutoff 1 − e^(−x) lost five digits: the weight was 6.4e-13 off (about 3000 ulps).
        public void EnvelopeCoefficientIsAccurateAboveTheSeriesCutoff()
        {
            double x = 1.0000001e-5;
            var f = new EnvelopeFollower(1, 1 / x, 1 / x);
            double g = f.Next(1);
            double want = 0, term = 1;
            for (int k = 1; k <= 30; k++) { term *= -x / k; want -= term; }
            Check.True(Math.Abs(g - want) <= 2e-16 * want * 4, $"{g:R} vs {want:R}");
        }

        public void EnvelopeAndQuantizerArraysNameTheirArgument()
        {
            var e = Check.Throws<ArgumentOutOfRangeException>(() => EnvelopeFollower.Process(new[] { 1, double.NaN }, 1000, 0.01, 0.1));
            Check.Equal("samples", e.ParamName);
            var q = Check.Throws<ArgumentOutOfRangeException>(() => new Quantizer(8).Quantize(new[] { 0.1, double.NaN }));
            Check.Equal("samples", q.ParamName);
        }

        // ---------- Cycles, group delay, Goertzel ----------

        // In seconds the high time and period both overflowed at 1e-306 Hz, so the duty cycle was ∞/∞ = NaN.
        public void DutyCycleAtATinySampleRateIsTheRatio()
        {
            var sq = new double[4000];
            for (int i = 0; i < sq.Length; i++) sq[i] = i % 400 < 200 ? 1 : 0;
            var stats = CycleMeasurements.Measure(sq, 1e-306);
            var unit = CycleMeasurements.Measure(sq, 1);
            Check.Equal(unit.MeanDutyCycle, stats.MeanDutyCycle);
            Check.Equal(unit.Cycles[0].DutyCycle, stats.Cycles[0].DutyCycle);
        }

        // 2π·Δf rounded on the subnormal grid (2π·ε is 6ε), so a pure delay came out 4.7% off.
        public void GroupDelayWithSubnormalSpacingIsTheDelay()
        {
            var f = new[] { 0, Eps, 2 * Eps, 3 * Eps };
            var phase = new double[4];
            for (int i = 0; i < 4; i++) phase[i] = -(2 * Math.PI) * (f[i] * 1e300);
            var tau = PhaseUnwrap.GroupDelay(phase, f);
            foreach (double t in tau) Check.True(Math.Abs(t / 1e300 - 1) < 1e-12, t.ToString("R"));
        }

        // f/fs overflowed to ∞ and cos(∞) made the result NaN; the tone is the alias f mod fs.
        public void GoertzelAtAFrequencyBeyondTheRatioRangeIsFinite()
        {
            var s = new[] { 1.0, 2, 3, 4 };
            var (mag, phase) = Goertzel.Estimate(s, 1e300, 1e-10);
            var alias = Goertzel.Estimate(s, 1e300 % 1e-10, 1e-10);
            Check.False(double.IsNaN(mag) || double.IsNaN(phase), $"{mag} {phase}");
            Check.True(Math.Abs(mag - alias.Magnitude) <= 1e-12 * Math.Max(1, alias.Magnitude), $"{mag:R} vs {alias.Magnitude:R}");
        }
    }
}
