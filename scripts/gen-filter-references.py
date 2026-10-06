"""Generates tests/ToolBelt.Tests/Signal/FilterReferenceData.cs: SciPy reference designs for Signal/IirFilter.cs
(butter/cheby1/cheby2/ellip/bessel as second-order sections: frequency response, group delay, sosfilt and sosfiltfilt outputs) and Signal/FirFilter.cs
(firwin taps, kaiserord), plus the buttord/cheb1ord/cheb2ord/ellipord order estimates.

Usage: python scripts/gen-filter-references.py   (needs numpy + scipy)
Section ordering is SciPy's own and may differ from ours, so the IIR rows compare order-independent quantities: the
complex response at a set of frequencies, and filter outputs on a fixed test signal (equal up to rounding).
"""
import numpy as np
from scipy import signal

N_SIGNAL = 240

def test_signal():
    i = np.arange(N_SIGNAL, dtype=float)
    return 1.5 + np.sin(0.05 * i) + 0.5 * np.sin(0.9 * i) + 0.3 * np.cos(2.1 * i + 0.4) + 0.004 * i

FS = 1000.0
# (prototype, band, order, passband ripple dB, stop-band attenuation dB, f1, f2, Bessel norm). For one-edge bands f1 is
# the cutoff (Chebyshev II: the stop-band edge).
IIR = [
    ("Butterworth", "LowPass", 1, 0, 0, 100.0, 0.0, ""),
    ("Butterworth", "LowPass", 2, 0, 0, 50.0, 0.0, ""),
    ("Butterworth", "LowPass", 5, 0, 0, 120.0, 0.0, ""),
    ("Butterworth", "LowPass", 8, 0, 0, 30.0, 0.0, ""),
    ("Butterworth", "HighPass", 3, 0, 0, 40.0, 0.0, ""),
    ("Butterworth", "HighPass", 6, 0, 0, 200.0, 0.0, ""),
    ("Butterworth", "BandPass", 2, 0, 0, 50.0, 150.0, ""),
    ("Butterworth", "BandPass", 3, 0, 0, 10.0, 300.0, ""),
    ("Butterworth", "BandStop", 2, 0, 0, 45.0, 55.0, ""),
    ("Butterworth", "BandStop", 3, 0, 0, 100.0, 250.0, ""),
    ("Chebyshev1", "LowPass", 3, 1.0, 0, 100.0, 0.0, ""),
    ("Chebyshev1", "LowPass", 4, 0.5, 0, 80.0, 0.0, ""),
    ("Chebyshev1", "HighPass", 5, 0.1, 0, 150.0, 0.0, ""),
    ("Chebyshev1", "BandPass", 4, 1.0, 0, 60.0, 120.0, ""),
    ("Chebyshev1", "BandStop", 3, 2.0, 0, 200.0, 300.0, ""),
    ("Chebyshev2", "LowPass", 1, 0, 20.0, 100.0, 0.0, ""),
    ("Chebyshev2", "LowPass", 4, 0, 40.0, 100.0, 0.0, ""),
    ("Chebyshev2", "LowPass", 7, 0, 60.0, 150.0, 0.0, ""),
    ("Chebyshev2", "HighPass", 5, 0, 50.0, 80.0, 0.0, ""),
    ("Chebyshev2", "BandPass", 3, 0, 40.0, 50.0, 200.0, ""),
    ("Chebyshev2", "BandStop", 4, 0, 30.0, 140.0, 160.0, ""),
    ("Elliptic", "LowPass", 1, 1.0, 40.0, 100.0, 0.0, ""),
    ("Elliptic", "LowPass", 2, 0.5, 30.0, 100.0, 0.0, ""),
    ("Elliptic", "LowPass", 3, 1.0, 40.0, 100.0, 0.0, ""),
    ("Elliptic", "LowPass", 6, 0.1, 80.0, 120.0, 0.0, ""),
    ("Elliptic", "LowPass", 9, 0.01, 120.0, 200.0, 0.0, ""),
    ("Elliptic", "HighPass", 5, 0.5, 60.0, 250.0, 0.0, ""),
    ("Elliptic", "BandPass", 4, 1.0, 50.0, 100.0, 140.0, ""),
    ("Elliptic", "BandPass", 3, 0.2, 70.0, 20.0, 400.0, ""),
    ("Elliptic", "BandStop", 5, 0.5, 60.0, 180.0, 220.0, ""),
    ("Bessel", "LowPass", 1, 0, 0, 100.0, 0.0, "phase"),
    ("Bessel", "LowPass", 4, 0, 0, 50.0, 0.0, "phase"),
    ("Bessel", "LowPass", 8, 0, 0, 30.0, 0.0, "delay"),
    ("Bessel", "LowPass", 6, 0, 0, 80.0, 0.0, "mag"),
    ("Bessel", "LowPass", 15, 0, 0, 60.0, 0.0, "phase"),
    ("Bessel", "LowPass", 25, 0, 0, 40.0, 0.0, "mag"),
    ("Bessel", "HighPass", 5, 0, 0, 100.0, 0.0, "mag"),
    ("Bessel", "BandPass", 3, 0, 0, 50.0, 150.0, "phase"),
    ("Bessel", "BandStop", 4, 0, 0, 100.0, 300.0, "delay"),
]
FREQS = [0.0, 5.0, 33.0, 50.0, 100.0, 149.0, 250.0, 400.0, 499.0]
GD_FREQS = [1.0, 20.0, 70.0, 333.0]
SAMPLE_AT = [0, 1, 2, 7, 30, 119, 200, 239]

def design(proto, band, order, rp, rs, f1, f2, norm):
    btype = {"LowPass": "lowpass", "HighPass": "highpass", "BandPass": "bandpass", "BandStop": "bandstop"}[band]
    wn = [f1, f2] if band in ("BandPass", "BandStop") else f1
    if proto == "Butterworth":
        return signal.butter(order, wn, btype=btype, fs=FS, output="sos")
    if proto == "Chebyshev1":
        return signal.cheby1(order, rp, wn, btype=btype, fs=FS, output="sos")
    if proto == "Chebyshev2":
        return signal.cheby2(order, rs, wn, btype=btype, fs=FS, output="sos")
    if proto == "Elliptic":
        return signal.ellip(order, rp, rs, wn, btype=btype, fs=FS, output="sos")
    return signal.bessel(order, wn, btype=btype, fs=FS, norm=norm, output="sos")

def group_delay(sos, f):
    total = 0.0
    for sec in sos:
        _, gd = signal.group_delay((sec[:3], sec[3:]), w=[f], fs=FS)
        total += gd[0]
    return total

x = test_signal()
iir_rows, iir_out, iir_gd = [], [], []
for idx, spec in enumerate(IIR):
    sos = design(*spec)
    _, h = signal.sosfreqz(sos, worN=FREQS, fs=FS)
    for f, hv in zip(FREQS, h):
        iir_rows.append((idx, f, hv.real, hv.imag))
    y = signal.sosfilt(sos, x)
    yy = signal.sosfiltfilt(sos, x)
    for i in SAMPLE_AT:
        iir_out.append((idx, i, y[i], yy[i]))
    for f in GD_FREQS:
        iir_gd.append((idx, f, group_delay(sos, f)))

FIR = [
    ("LowPass", 31, 100.0, 0.0, "Hamming", 0.0),
    ("LowPass", 30, 100.0, 0.0, "Hann", 0.0),
    ("LowPass", 51, 250.0, 0.0, "Blackman", 0.0),
    ("LowPass", 1, 100.0, 0.0, "Hamming", 0.0),
    ("LowPass", 25, 60.0, 0.0, "Rectangular", 0.0),
    ("LowPass", 41, 150.0, 0.0, "Kaiser", 5.0),
    ("HighPass", 33, 200.0, 0.0, "Hamming", 0.0),
    ("HighPass", 45, 50.0, 0.0, "Kaiser", 8.6),
    ("BandPass", 64, 100.0, 200.0, "Hamming", 0.0),
    ("BandPass", 41, 20.0, 480.0, "Blackman", 0.0),
    ("BandStop", 55, 90.0, 110.0, "Hann", 0.0),
]
WIN = {"Hamming": "hamming", "Hann": "hann", "Blackman": "blackman", "Rectangular": "boxcar"}
fir_rows = []
for idx, (band, taps, f1, f2, win, beta) in enumerate(FIR):
    window = ("kaiser", beta) if win == "Kaiser" else WIN[win]
    cutoff = [f1, f2] if band in ("BandPass", "BandStop") else f1
    pass_zero = band in ("LowPass", "BandStop")
    h = signal.firwin(taps, cutoff, window=window, pass_zero=pass_zero, fs=FS)
    fir_rows.append((band, taps, f1, f2, win, beta, list(h)))

KAISER = [(60.0, 20.0), (40.0, 50.0), (21.0, 100.0), (80.0, 5.0), (30.0, 10.0)]
kaiser_rows = []
for a, w in KAISER:
    n, beta = signal.kaiserord(a, w / (FS / 2))
    kaiser_rows.append((a, w, int(n), float(beta)))

# Order estimates: (kind, pass edges, stop edges, gpass, gstop). One-edge specs have a single pass/stop edge. Band-stop
# rows are checked loosely: SciPy finds their passband edges with fminbound (xatol 1e-5), we use the exact optimum.
ORDER_SPECS = [
    ([100.0], [150.0], 1.0, 40.0), ([100.0], [120.0], 0.5, 60.0), ([200.0], [210.0], 0.1, 80.0), ([30.0], [200.0], 3.0, 20.0),
    ([250.0], [180.0], 1.0, 50.0), ([60.0], [40.0], 0.2, 30.0), ([400.0], [380.0], 2.0, 70.0),
    ([100.0, 200.0], [60.0, 260.0], 1.0, 40.0), ([300.0, 320.0], [280.0, 345.0], 0.5, 60.0), ([20.0, 400.0], [10.0, 450.0], 0.3, 25.0),
    ([50.0, 300.0], [100.0, 200.0], 1.0, 40.0), ([100.0, 260.0], [150.0, 190.0], 0.5, 60.0), ([40.0, 450.0], [100.0, 120.0], 2.0, 30.0),
]
ORDER_FUNCS = [("Butterworth", signal.buttord), ("Chebyshev1", signal.cheb1ord), ("Chebyshev2", signal.cheb2ord), ("Elliptic", signal.ellipord)]
order_rows = []
for kind, fn in ORDER_FUNCS:
    for wp, ws, gp, gs in ORDER_SPECS:
        n, wn = fn(wp if len(wp) > 1 else wp[0], ws if len(ws) > 1 else ws[0], gp, gs, fs=FS)
        wn = list(np.atleast_1d(wn)) + [0.0]
        order_rows.append((kind, wp + [0.0], ws + [0.0], gp, gs, int(n), wn[:2]))

def r(v):
    return repr(float(v))

out = []
out.append("// <auto-generated> by scripts/gen-filter-references.py (SciPy " + __import__("scipy").__version__ + "). Do not edit by hand.")
out.append("namespace ToolBelt.Tests.Signal")
out.append("{")
out.append("    internal static class FilterReferenceData")
out.append("    {")
out.append("        public const double SampleRate = " + r(FS) + ";")
out.append("        public const int SignalLength = " + str(N_SIGNAL) + ";")
out.append("")
out.append("        // Designs: (prototype, band, order, passband ripple dB, stop-band attenuation dB, f1, f2, Bessel norm).")
out.append("        public static readonly (string Prototype, string Band, int Order, double PassRipple, double StopAttenuation, double F1, double F2, string Norm)[] IirDesigns =")
out.append("        {")
for proto, band, order, rp, rs, f1, f2, norm in IIR:
    out.append(f'            ("{proto}", "{band}", {order}, {r(rp)}, {r(rs)}, {r(f1)}, {r(f2)}, "{norm}"),')
out.append("        };")
out.append("")
out.append("        // (design index, frequency Hz, group delay in samples: scipy group_delay summed over sections)")
out.append("        public static readonly (int Design, double Frequency, double Samples)[] IirGroupDelay =")
out.append("        {")
for d, f, g in iir_gd:
    out.append(f"            ({d}, {r(f)}, {r(g)}),")
out.append("        };")
out.append("")
out.append("        // (design index, frequency Hz, Re H, Im H)")
out.append("        public static readonly (int Design, double Frequency, double Re, double Im)[] IirResponse =")
out.append("        {")
for d, f, re, im in iir_rows:
    out.append(f"            ({d}, {r(f)}, {r(re)}, {r(im)}),")
out.append("        };")
out.append("")
out.append("        // (design index, sample index, sosfilt output, sosfiltfilt output) on TestSignal().")
out.append("        public static readonly (int Design, int Index, double Causal, double ZeroPhase)[] IirOutputs =")
out.append("        {")
for d, i, y, yy in iir_out:
    out.append(f"            ({d}, {i}, {r(y)}, {r(yy)}),")
out.append("        };")
out.append("")
out.append("        // (band, taps, f1, f2, window, kaiser beta, firwin taps)")
out.append("        public static readonly (string Band, int Taps, double F1, double F2, string Window, double Beta, double[] Expected)[] FirDesigns =")
out.append("        {")
for band, taps, f1, f2, win, beta, h in fir_rows:
    out.append(f'            ("{band}", {taps}, {r(f1)}, {r(f2)}, "{win}", {r(beta)}, new[] {{ ' + ", ".join(r(v) for v in h) + " }),")
out.append("        };")
out.append("")
out.append("        // (attenuation dB, transition width Hz, kaiserord taps, kaiserord beta) at SampleRate.")
out.append("        public static readonly (double Attenuation, double Width, int Taps, double Beta)[] Kaiser =")
out.append("        {")
for a, w, n, beta in kaiser_rows:
    out.append(f"            ({r(a)}, {r(w)}, {n}, {r(beta)}),")
out.append("        };")
out.append("")
out.append("        // (kind, pass edge(s), stop edge(s) — second 0 for one-edge specs, gpass, gstop, SciPy order, SciPy Wn (second 0 for one edge)) at SampleRate.")
out.append("        public static readonly (string Kind, double P0, double P1, double S0, double S1, double PassRipple, double StopAttenuation, int Order, double W0, double W1)[] Orders =")
out.append("        {")
for kind, wp, ws, gp, gs, n, wn in order_rows:
    out.append(f'            ("{kind}", {r(wp[0])}, {r(wp[1])}, {r(ws[0])}, {r(ws[1])}, {r(gp)}, {r(gs)}, {n}, {r(wn[0])}, {r(wn[1])}),')
out.append("        };")
out.append("    }")
out.append("}")

path = "tests/ToolBelt.Tests/Signal/FilterReferenceData.cs"
with open(path, "w", encoding="utf-8", newline="\n") as fh:
    fh.write("\n".join(out) + "\n")
print(f"wrote {path}: {len(iir_rows)} response rows, {len(iir_out)} output rows, {len(fir_rows)} FIR designs")
