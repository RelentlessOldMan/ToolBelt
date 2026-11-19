using System;
using ToolBelt.Control;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    /// <summary>Round 150 review regressions (filters and control, ninth pass).</summary>
    public sealed class FiltersControlReview150Tests
    {
        // ---------- PidController ----------

        // P (1e200 × 1e300) and D (over a subnormal dt) overflowed with opposite signs even in units of 2^-600, so the
        // output was ∞ − ∞ = NaN, which the clamp passed through. The derivative dominates: the output is the low limit.
        public void Pid_OppositeOverflowingTermsNeverGiveNaN()
        {
            var p = new PidController(1e200, 0, 1, -1, 1);
            p.Update(-1e300, 1);
            Check.Equal(-1.0, p.Update(-9e299, 5e-324));

            var rng = new Random(150);
            for (int trial = 0; trial < 3000; trial++)
            {
                double G() => Math.Pow(10, rng.Next(-300, 301)) * (rng.Next(2) == 0 ? -1 : 1);
                double V() => (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(-300, 309));
                double lim = Math.Pow(10, rng.Next(-10, 300));
                var c = new PidController(G(), rng.Next(3) == 0 ? 0 : G(), G(), -lim, lim) { Setpoint = V() };
                for (int k = 0; k < 4; k++)
                {
                    double dt = rng.Next(3) == 0 ? double.Epsilon * rng.Next(1, 10) : Math.Pow(10, rng.Next(-300, 10));
                    double u = c.Update(V(), dt);
                    Check.True(u >= -lim && u <= lim, $"trial {trial} step {k}: {u} outside ±{lim}");
                }
            }
        }

        // With a small Ki and wide (or no) limits, an integral past double.MaxValue was never taken: error·dt = 2e310
        // left it at 0, so the output stayed 0 where Ki·∫e dt = 2e10. Graded against the exact sums.
        public void Pid_AnIntegralPastMaxValueIsKept()
        {
            var p = new PidController(0, 1e-300, 0) { Setpoint = 1e300 };
            Check.Close(2e10, p.Update(-1e300, 1e10), 1e-12 * 2e10);
            Check.Close(4e10, p.Update(-1e300, 1e10), 1e-12 * 4e10);
            Check.Close(2e10, p.Update(3e300, 1e10), 1e-12 * 2e10);
            Check.Close(0, p.Update(3e300, 1e10), 1e-3);
            Check.Close(-2e10, p.Update(3e300, 1e10), 1e-12 * 2e10);

            var q = new PidController(0, 1e-300, 0, -1e20, 1e20) { Setpoint = 1e300 };
            for (int i = 1; i <= 3; i++) Check.Close(2e15 * i, q.Update(-1e300, 1e15), 1e-12 * 2e15 * i);

            // Saturated, it stops at the limit (∫ = 1e320) and unwinds from there.
            var r = new PidController(0, 1e-300, 0, -1e20, 1e20) { Setpoint = 1e300 };
            Check.Equal(1e20, r.Update(-1e300, 1e30));
            Check.Equal(1e20, r.Update(-1e300, 1e30));
            Check.Close(8e19, r.Update(3e300, 1e19), 1e-12 * 8e19);
        }

        // ---------- PidTuning ----------

        // θ/2 (and the Cohen–Coon Td) underflowed to 0 for a subnormal dead time, so a "PID" silently had no derivative;
        // the ultimate-gain rule refused the same case.
        public void PidTuning_ADerivativeTimeThatUnderflowsIsRefused()
        {
            var model = new FopdtModel(1e308, double.Epsilon, double.Epsilon);
            Check.Equal("model", Check.Throws<ArgumentOutOfRangeException>(() => PidTuning.ZieglerNichols(model)).ParamName);
            Check.Equal("model", Check.Throws<ArgumentOutOfRangeException>(() => PidTuning.CohenCoon(model)).ParamName);
            Check.True(PidTuning.ZieglerNichols(new FopdtModel(2, 3, 0.5)).DerivativeTime > 0);
            Check.True(PidTuning.CohenCoon(new FopdtModel(2, 3, 0.5)).DerivativeTime > 0);
        }

        // ---------- Order estimators ----------

        private static double W(double f, double fs) => Math.Tan(Math.PI * (f / fs));

        // The small root −x + √(x² + prod) cancelled to 0 for a low edge far below the high one: pass 1e-17 to 0.3 gave a
        // low edge of exactly 0. Graded against the band transform the edges must satisfy: W(lo)·W(hi) = W(pl)·W(ph), and
        // for Butterworth band-pass W(hi) − W(lo) = w0·(W(ph) − W(pl)).
        public void Order_ALowEdgeFarBelowTheHighOneDoesNotCancel()
        {
            foreach (double pl in new[] { 1e-3, 1e-6, 1e-9, 1e-12, 1e-15, 1e-17, 1e-18, 1e-25 })
            {
                double fs = 1, ph = 0.3, sl = pl / 2, sh = 0.4, prod = W(pl, fs) * W(ph, fs);
                var b = IirFilter.ButterworthOrder(pl, ph, sl, sh, 1, 40, fs);
                Check.True(b.Low > 0 && b.Low < b.High, $"butter bp {pl}: {b}");
                Check.Close(1, W(b.Low, fs) * W(b.High, fs) / prod, 1e-12);
                double w0 = Math.Pow(Math.Pow(10, 0.1) - 1, -1.0 / (2 * b.Order));
                Check.Close(1, (W(b.High, fs) - W(b.Low, fs)) / (w0 * (W(ph, fs) - W(pl, fs))), 1e-12);

                var c = IirFilter.Chebyshev2Order(pl, ph, sl, sh, 1, 40, fs);
                Check.True(c.Low > 0 && c.Low < c.High, $"cheby2 bp {pl}: {c}");
                Check.Close(1, W(c.Low, fs) * W(c.High, fs) / prod, 1e-12);

                var bs = IirFilter.ButterworthOrder(pl, 0.45, pl * 2, 0.3, 1, 40, fs);
                Check.True(bs.Low > 0 && bs.Low < bs.High, $"butter bs {pl}: {bs}");
                var cs = IirFilter.Chebyshev2Order(pl, 0.45, pl * 2, 0.3, 1, 40, fs);
                Check.True(cs.Low > 0 && cs.Low < cs.High, $"cheby2 bs {pl}: {cs}");
            }
        }

        // At an extreme ripple the edge to design with rounded to exactly Nyquist (a tiny ripple just under the
        // attenuation) or the band's two edges to one frequency (999 dB against 1000), and neither can be designed with.
        public void Order_AnEdgeThatRoundsToNyquistOrOntoTheOtherEdgeIsRefused()
        {
            double rp = 1e-300, rs = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(rp) + 2);   // two ulps up
            Check.Equal("passRippleDb", Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthOrder(0.1, 0.2, rp, rs, 1)).ParamName);
            Check.Equal("passRippleDb", Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthOrder(0.2, 0.3, 0.1, 0.4, rp, rs, 1)).ParamName);
            Check.Equal("passRippleDb", Check.Throws<ArgumentOutOfRangeException>(() => IirFilter.ButterworthOrder(0.2, 0.3, 0.1, 0.4, 999, 1000, 1)).ParamName);

            // Whatever is returned can be designed with: every edge inside (0, fs/2), a band's edges in order.
            var rng = new Random(1500);
            for (int trial = 0; trial < 400; trial++)
            {
                double fs = Math.Pow(10, rng.Next(-3, 7));
                double r1 = Math.Pow(10, rng.Next(-300, 3)) * (1 + rng.NextDouble());
                double r2 = Math.Min(1000, rng.Next(2) == 0 ? r1 * (1 + Math.Pow(10, -rng.Next(1, 16))) : r1 + rng.Next(1, 200));
                if (!(r2 > r1)) continue;
                double e1 = 0.1 * fs, e2 = 0.2 * fs, e3 = 0.3 * fs, e4 = 0.4 * fs;
                int t = trial;
                void Edge(double hz) => Check.True(hz > 0 && hz < fs / 2, $"trial {t}: edge {hz} outside (0, {fs / 2}) at {r1}/{r2} dB");
                void Band((int Order, double Low, double High) v) { Edge(v.Low); Edge(v.High); Check.True(v.Low < v.High, $"trial {t}: {v}"); }
                void Try(Action a) { try { a(); } catch (ArgumentException) { } }
                Try(() => Edge(IirFilter.ButterworthOrder(e1, e2, r1, r2, fs).Cutoff));
                Try(() => Edge(IirFilter.ButterworthOrder(e2, e1, r1, r2, fs).Cutoff));
                Try(() => Edge(IirFilter.Chebyshev2Order(e1, e2, r1, r2, fs).StopEdge));
                Try(() => Edge(IirFilter.Chebyshev2Order(e2, e1, r1, r2, fs).StopEdge));
                Try(() => Band(IirFilter.ButterworthOrder(e2, e3, e1, e4, r1, r2, fs)));
                Try(() => Band(IirFilter.ButterworthOrder(e1, e4, e2, e3, r1, r2, fs)));
                Try(() => Band(IirFilter.Chebyshev2Order(e2, e3, e1, e4, r1, r2, fs)));
                Try(() => Band(IirFilter.Chebyshev2Order(e1, e4, e2, e3, r1, r2, fs)));
            }
        }

        // ---------- Subnormal flush ----------

        // The flush stayed on from the largest sample since Reset, so after one sample of 1 a later signal of 1e-307 (a
        // normal double) was wiped to 0 by Process and by Filter. Graded against a plain direct form II transposed run.
        public void Iir_ATinySignalAfterALargeOneIsNotFlushed()
        {
            foreach (double level in new[] { 1e-307, 3e-308, 1e-306, 1e-300 })
            {
                var f = IirFilter.ButterworthLowPass(2, 0.1, 1);
                var x = new double[6000];
                x[0] = 1;
                for (int i = 5000; i < x.Length; i++) x[i] = level;
                double[] expected = Reference(f, x);
                double[] filtered = f.Filter(x);
                var streamed = new double[x.Length];
                for (int i = 0; i < x.Length; i++) streamed[i] = f.Process(x[i]);
                for (int i = 5000; i < x.Length; i++)
                {
                    Check.True(Math.Abs(streamed[i] - expected[i]) <= 1e-9 * level, $"Process {level} at {i}: {streamed[i]} vs {expected[i]}");
                    Check.True(Math.Abs(filtered[i] - expected[i]) <= 1e-9 * level, $"Filter {level} at {i}: {filtered[i]} vs {expected[i]}");
                }
            }
        }

        private static double[] Reference(IirFilter f, double[] input)
        {
            var data = (double[])input.Clone();
            foreach (var q in f.Sections)
            {
                double a = 0, b = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    double x = data[i], y = q.B0 * x + a;
                    a = q.B1 * x - q.A1 * y + b;
                    b = q.B2 * x - q.A2 * y;
                    data[i] = y;
                }
            }
            return data;
        }
    }
}
