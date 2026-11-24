using System;
using System.Numerics;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    /// <summary>Round 215 review regressions (filters and control, thirteenth pass).</summary>
    public sealed class FiltersControlReview215Tests
    {
        // A finite double as an exact integer count of 2^-1074.
        private static BigInteger Exact(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            BigInteger m = exp == 0 ? mant : mant | (1L << 52);
            BigInteger v = m << Math.Max(exp - 1, 0);
            return bits < 0 ? -v : v;
        }

        // ---------- FirFilter ----------

        // Fixed-point reals: a BigInteger counting 2^-256, with every double in range exact.
        private const int Bits = 256;
        private static readonly BigInteger One = BigInteger.One << Bits;

        private static BigInteger Fixed(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            if (exp == 0) return BigInteger.Zero;   // not used below
            BigInteger m = (bits & 0xFFFFFFFFFFFFFL) | (1L << 52);
            int shift = exp - 1075 + Bits;
            BigInteger v = shift >= 0 ? m << shift : m >> -shift;
            return bits < 0 ? -v : v;
        }

        private static BigInteger Mul(BigInteger a, BigInteger b) => (a * b) >> Bits;
        private static BigInteger Div(BigInteger a, BigInteger b) => (a << Bits) / b;
        private static double ToDouble(BigInteger a) => (double)(a >> (Bits - 120)) / Math.Pow(2, 120);

        private static BigInteger AtanInverse(int x)
        {
            BigInteger sum = 0, term = One / x, x2 = (BigInteger)x * x;
            for (int k = 0; term != 0; k++)
            {
                sum += (k % 2 == 0 ? term : -term) / (2 * k + 1);
                term /= x2;
            }
            return sum;
        }

        private static readonly BigInteger FixedPi = 16 * AtanInverse(5) - 4 * AtanInverse(239);   // Machin

        private static BigInteger Sin(BigInteger x)
        {
            BigInteger twoPi = 2 * FixedPi;
            BigInteger k = BigInteger.Divide(x + FixedPi, twoPi);
            if (x + FixedPi < 0) k -= 1;
            x -= k * twoPi;                                   // now in [-π, π)
            BigInteger term = x, sum = x, x2 = Mul(x, x);
            for (int n = 1; term != 0; n++)
            {
                term = -Mul(term, x2) / ((2 * n) * (2 * n + 1));
                sum += term;
            }
            return sum;
        }

        private static BigInteger Cos(BigInteger x) => Sin(x + FixedPi / 2);

        // The windowed (Hamming) band-pass taps formed in 256-bit fixed point from the same double edges:
        // (sin(π·hi·m) − sin(π·lo·m)) / (π·m), normalised to unit gain at the band centre.
        private static double[] ReferenceBandPass(int taps, double low, double high, double sampleRate)
        {
            BigInteger lo = Fixed(2 * (low / sampleRate)), hi = Fixed(2 * (high / sampleRate));
            double alpha = (taps - 1) / 2.0;
            var h = new BigInteger[taps];
            for (int n = 0; n < taps; n++)
            {
                BigInteger m = Fixed(n - alpha);
                BigInteger v = m.IsZero ? hi - lo : Div(Sin(Mul(Mul(FixedPi, hi), m)) - Sin(Mul(Mul(FixedPi, lo), m)), Mul(FixedPi, m));
                double w = taps == 1 ? 1 : 0.54 - 0.46 * Math.Cos(2 * Math.PI * n / (taps - 1));
                h[n] = Mul(v, Fixed(w));
            }
            BigInteger centre = (lo + hi) / 2, gain = 0;
            for (int n = 0; n < taps; n++) gain += Mul(h[n], Cos(Mul(Mul(FixedPi, Fixed(n - alpha)), centre)));
            var result = new double[taps];
            for (int n = 0; n < taps; n++) result[n] = ToDouble(Div(h[n], gain));
            return result;
        }

        // Each band-pass tap was the difference of two sinc terms, which cancel for a narrow band: a 1e-9-wide band was
        // 6e-8 off and a band by Nyquist 1.6e-4. Graded against the same design in 256-bit fixed point over random narrow bands.
        public void FirBandPass_NarrowBand_KeepsItsDigits()
        {
            var rng = new Random(2150);
            for (int trial = 0; trial < 300; trial++)
            {
                int taps = rng.Next(2, 120);
                double width = Math.Pow(10, -rng.Next(3, 11));
                double low = trial % 3 == 0 ? 0.5 - width * (1 + rng.NextDouble()) : 0.05 + rng.NextDouble() * 0.4;
                double high = low + width * (0.5 + rng.NextDouble());
                if (!(high < 0.5)) continue;
                double[] expected = ReferenceBandPass(taps, low, high, 1);
                var got = FirFilter.BandPass(taps, low, high, 1).Taps;
                double max = 0;
                foreach (double e in expected) max = Math.Max(max, Math.Abs(e));
                for (int n = 0; n < taps; n++)
                    Check.True(Math.Abs(got[n] - expected[n]) <= 1e-12 * max,
                        "taps " + taps + " band " + low.ToString("R") + ".." + high.ToString("R") + " tap " + n + ": " + got[n].ToString("R") + " vs " + expected[n].ToString("R"));
            }
        }

        // ---------- SlewRateLimiter ----------

        // The gap was rounded before it was compared: a true gap just past the step that rounds to exactly the step fired
        // neither test, so the output jumped to the target and IsLimiting read false. Graded against the exact gap.
        public void SlewRateLimiter_GapRoundingToTheStep_IsStillLimited()
        {
            var s = new SlewRateLimiter(1);
            s.Reset(2);
            Check.Equal(0.0, s.Update(-1e-16, 2));
            Check.True(s.IsLimiting);
            var big = new SlewRateLimiter(1, 1e17);
            big.Reset(1e17);
            Check.Equal(0.0, big.Update(-4, 1));
            var huge = new SlewRateLimiter(1, 1e300);
            huge.Reset(1e300);
            Check.Equal(0.0, huge.Update(-7e283, 1));

            var rng = new Random(215);
            for (int trial = 0; trial < 200000; trial++)
            {
                double value = Math.Pow(10, rng.NextDouble() * 40 - 20) * (rng.Next(2) == 0 ? 1 : -1);
                double rise = Math.Pow(10, rng.NextDouble() * 40 - 20), fall = rng.Next(2) == 0 ? rise : Math.Pow(10, rng.NextDouble() * 40 - 20);
                double dt = Math.Pow(2, rng.Next(-4, 5));
                double up = rise * dt, down = fall * dt;
                // A target whose gap from the value is the step, plus or minus a little, so the rounded gap ties often.
                double step = rng.Next(2) == 0 ? up : -down;
                double tiny = Math.Abs(step) * Math.Pow(2, -rng.Next(40, 60)) * (rng.Next(2) == 0 ? 1 : -1);
                double target = value + step + (rng.Next(3) == 0 ? 0 : tiny);
                if (rng.Next(4) == 0) target = value + step * (1 + tiny / Math.Abs(step));
                var limiter = new SlewRateLimiter(rise, fall);
                limiter.Reset(value);
                double got = limiter.Update(target, dt);
                BigInteger gap = Exact(target) - Exact(value);
                bool rising = gap > Exact(up), falling = gap < -Exact(down);
                double expected = rising ? value + up : falling ? value - down : target;
                string at = "value " + value.ToString("R") + " target " + target.ToString("R") + " rise " + rise.ToString("R") + " fall " + fall.ToString("R") + " dt " + dt;
                Check.Equal(expected, got, at);
                Check.Equal(rising || falling, limiter.IsLimiting, at);
            }
        }
    }
}
