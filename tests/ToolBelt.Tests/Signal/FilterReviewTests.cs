using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 1 (filters & control).
    public sealed class FilterReviewTests
    {
        public void Iir_HighOrderAndExtremeRateDesignsStayFiniteAndCorrect()
        {
            // The analog stage used physical units (ω ~ 2·fs), so the gain products over 2N roots overflowed or
            // underflowed at orders the API accepts. Each case: the filter, a passband frequency and a stop frequency.
            const double day = 86400;
            var cases = new List<(IirFilter Filter, double Fs, double Pass, double Stop)>
            {
                (IirFilter.ButterworthLowPass(40, 1e8, 1e9), 1e9, 1e7, 3e8),
                (IirFilter.ButterworthBandPass(20, 1e8, 2e8, 1e9), 1e9, 1.4e8, 3e8),
                (IirFilter.ButterworthBandPass(25, 1e5, 2e5, 1e6), 1e6, 1.4e5, 3e5),
                (IirFilter.ButterworthBandPass(40, 4800, 9600, 48000), 48000, 6800, 2000),
                (IirFilter.ButterworthBandPass(40, 1000, 1010, 48000), 48000, 1005, 2000),
                (IirFilter.EllipticBandPass(25, 0.5, 80, 1e6 / 10, 1e6 / 5, 1e6), 1e6, 1.4e5, 3e5),
                (IirFilter.Chebyshev1BandStop(22, 1, 1e6, 2e6, 1e7), 1e7, 1e5, 1.4e6),
                (IirFilter.Chebyshev2BandPass(22, 60, 1e6, 2e6, 1e7), 1e7, 1.4e6, 1e5),
                (IirFilter.ButterworthBandStop(36, 0.05 / day, 0.3 / day, 1.0 / day), 1.0 / day, 0.01 / day, 0.12 / day),
                (IirFilter.Chebyshev2BandStop(33, 120, 50, 499.5, 1000), 1000, 10, 200),
            };
            foreach (var (filter, fs, pass, stop) in cases)
            {
                foreach (var s in filter.Sections)
                    Check.True(double.IsFinite(s.B0) && double.IsFinite(s.B1) && double.IsFinite(s.B2), "finite coefficients");
                Check.Close(0, filter.MagnitudeDb(pass, fs), 1.5, $"passband gain at {pass} (fs {fs})");
                Check.True(filter.MagnitudeDb(stop, fs) < -40, $"stop band at {stop} (fs {fs}): {filter.MagnitudeDb(stop, fs)} dB");
            }
        }

        public void Iir_TinyRippleDesignsDoNotLoseRoots()
        {
            // 10^(r/10) − 1 cancelled to 0, the poles became NaN, and section grouping silently dropped them.
            var cheby = IirFilter.Chebyshev1LowPass(6, 4e-16, 1000, 48000);
            Check.Close(0, cheby.MagnitudeDb(100, 48000), 1e-6);
            var ellip = IirFilter.EllipticLowPass(6, 4e-16, 60, 1000, 48000);
            Check.Close(0, ellip.MagnitudeDb(100, 48000), 1e-6);
            var cheby2 = IirFilter.Chebyshev2LowPass(4, 1e-17, 1000, 48000);
            foreach (var s in cheby2.Sections) Check.True(Math.Abs(s.A2) < 1, "stable poles");
        }

        public void Iir_RejectsDegenerateRippleAndGain()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.Chebyshev1LowPass(4, 500, 1000, 48000));
            Check.Throws<ArgumentOutOfRangeException>(() => Biquad.Peaking(48000, 1000, 1, 1e5));
            Check.Throws<ArgumentOutOfRangeException>(() => Biquad.LowShelf(48000, 1000, 7000));
            Check.Throws<ArgumentOutOfRangeException>(() => Biquad.LowPass(48000, 1000, 1e-320));
            Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthOrder(1000, 2000, 1, 4000, 48000));
            var f = IirFilter.ButterworthLowPass(2, 100, 1000);
            Check.Throws<ArgumentOutOfRangeException>(() => f.Reset(double.NaN));
        }

        public void Iir_ArgumentErrorsNameThePublicParameter()
        {
            Check.Equal("sampleRate", Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthLowPass(2, 100, -1)).ParamName);
            Check.Equal("stopEdge", Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.Chebyshev2LowPass(2, 40, 900, 1000)).ParamName);
            Check.Equal("sampleRate", Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.LowPass(11, 100, 0)).ParamName);
            Check.Equal("cutoff", Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.LowPass(11, 600, 1000)).ParamName);
            Check.Equal("high", Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.BandPass(11, 100, 600, 1000)).ParamName);
            Check.Equal("kaiserBeta", Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.LowPass(11, 100, 1000, FirWindow.Kaiser, -1)).ParamName);
        }

        public void Filters_DoNotExposeMutableInternals()
        {
            var iir = IirFilter.ButterworthLowPass(4, 100, 1000);
            Check.False(iir.Sections is Biquad[], "Sections must not be the backing array");
            var fir = FirFilter.LowPass(11, 100, 1000);
            Check.False(fir.Taps is double[], "Taps must not be the backing array");
        }

        public void Fir_KaiserParametersRejectsImpossibleTapCounts()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.KaiserParameters(60, 1e-6, 48000));
            Check.Throws<ArgumentOutOfRangeException>(() => FirFilter.KaiserParameters(60, 1e-7, 1000));
            var (taps, _) = FirFilter.KaiserParameters(60, 1, 48000);
            Check.True(taps > 0);
        }

        public void Fir_ZeroSumWindowGivesAClearError()
        {
            var e = Check.Throws<ArgumentException>(() => FirFilter.LowPass(2, 0.0365, 1, FirWindow.Hann));
            Check.True(e.Message.Contains("window"), e.Message);
        }
    }
}
