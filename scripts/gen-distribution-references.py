"""Generates tests/ToolBelt.Tests/Numerics/DistributionReferenceData.cs: 40-digit mpmath reference values for the
normal, Student-t, chi-square and F functions in Numerics/Distributions.cs, concentrated in the hard regimes (deep
tails, tiny x, huge or fractional degrees of freedom) where double-precision libraries disagree.

Usage: python scripts/gen-distribution-references.py   (needs mpmath; scipy only seeds root finding)
Inputs are written with repr(float) so they round-trip to the identical doubles in C#.
"""
from mpmath import mp, mpf, betainc, erfc, sqrt, findroot, gamma, hyp1f1, exp, gammainc, inf
from scipy import stats

mp.dps = 40
rows = []  # (function, a, b, x, expected)

def add(fn, a, b, x, val):
    rows.append((fn, a, b, x, val))

def phi(z):            # standard normal CDF
    z = mpf(z)
    return erfc(-z / sqrt(2)) / 2

def t_cdf(x, v):
    x, v = mpf(x), mpf(v)
    tail = betainc(v / 2, mpf(1) / 2, 0, v / (v + x * x), regularized=True) / 2
    return tail if x < 0 else 1 - tail

def t_lower_tail(t, v):  # P(T < -|t|)
    t, v = mpf(t), mpf(v)
    return betainc(v / 2, mpf(1) / 2, 0, v / (v + t * t), regularized=True) / 2

def chi_lower(x, k):
    # P(a, y) = y^a e^-y / Γ(a+1) · 1F1(1; a+1; y): all-positive series, no cancellation, any a.
    a, y = mpf(k) / 2, mpf(x) / 2
    return y ** a * exp(-y) / gamma(a + 1) * hyp1f1(1, a + 1, y, maxterms=10**7)

def chi_upper(x, k):
    # Real upper incomplete gamma (1 − P can pick up a complex rounding artifact from hyp1f1 at very large a).
    return gammainc(mpf(k) / 2, mpf(x) / 2, inf, regularized=True)

def f_lower(x, d1, d2):
    x, d1, d2 = mpf(x), mpf(d1), mpf(d2)
    return betainc(d1 / 2, d2 / 2, 0, d1 * x / (d1 * x + d2), regularized=True)

def f_upper(x, d1, d2):
    x, d1, d2 = mpf(x), mpf(d1), mpf(d2)
    return betainc(d2 / 2, d1 / 2, 0, d2 / (d1 * x + d2), regularized=True)

def _bracketed(g, center):
    # Start narrow (the scipy seed is already within a few percent) and widen only if needed.
    lo, hi, step = center - mpf("0.05"), center + mpf("0.05"), mpf("0.05")
    for _ in range(400):
        if (g(lo) < 0) != (g(hi) < 0):
            return findroot(g, (lo, hi), solver="illinois", tol=mpf(10) ** -32, maxsteps=500)
        step *= 2
        lo, hi = center - step, center + step
    raise ValueError("no bracket")

# Equations are solved as ln(F(y) / target) = 0, not F(y) − target = 0: findroot's tolerance is absolute, so with a
# target like 1e-100 any |F − target| looks "converged" and it would stop at a bracket edge.
def solve(f, target, guess):
    """Root of f(y) = target on the real line (f monotone, positive), bracketed then Illinois."""
    c = mpf(guess) if guess == guess and abs(guess) != float("inf") else mpf(0)
    lt = mp.log(target)
    return _bracketed(lambda y: mp.log(f(y)) - lt, c)

def solve_pos(f, target, guess):
    """Positive root, also solved in log space for y, so roots spanning many decades (1e-120, 1e50) are found."""
    c = mp.log(mpf(guess)) if guess == guess and 0 < guess < float("inf") else mpf(0)
    lt = mp.log(target)
    return mp.exp(_bracketed(lambda s: mp.log(f(mp.exp(s))) - lt, c))

# Normal
for z in [-37.0, -20.0, -8.0, -3.0, -0.5, 0.1, 1.0, 5.0, 8.0]:
    add("NormalCdf", 0, 1, z, phi(z))
for p in [1e-300, 1e-100, 1e-20, 1e-10, 0.001, 0.25, 0.6, 0.975, 1 - 1e-10]:
    pm = mpf(p)
    if p < 0.5:
        q = solve(phi, pm, stats.norm.ppf(p))
    else:
        q = solve(lambda y: erfc(mpf(y) / sqrt(2)) / 2, 1 - pm, stats.norm.ppf(p))
    add("NormalQuantile", 0, 1, p, q)

# Student-t
for v in [0.3, 1.0, 3.5, 30.0, 150.0, 1076.2855996657001, 1e4, 1e5, 1e7, 1e9]:
    for x in [-37.0, -10.0, -2.0, -0.3, 1e-9, 0.7, 5.0]:
        add("StudentTCdf", v, 0, x, t_cdf(x, v))
for v in [0.3, 1.0, 3.5, 30.0, 1e4, 1e7, 1e9]:
    for p in [1e-15, 1e-6, 0.025, 0.4, 0.5 + 1e-12, 0.975, 1 - 1e-9]:
        u = mpf(p) if p < 0.5 else 1 - mpf(p)
        t = solve_pos(lambda y: t_lower_tail(y, v), u, abs(stats.t.ppf(min(p, 1 - p), v)))
        add("StudentTQuantile", v, 0, p, -t if p < 0.5 else t)

# Chi-square
for k in [0.2, 0.5148241228912671, 1.0, 4.5, 60.0, 1e5]:
    for f in [1e-6, 0.3, 1.0, 2.5]:
        x = k * f
        add("ChiSquareCdf", k, 0, x, chi_lower(x, k))
    for p in [1e-12, 3.387483502998552e-07, 0.05, 0.5, 0.95, 1 - 1e-10]:
        g = stats.chi2.ppf(p, k)
        q = solve_pos(lambda y: chi_lower(y, k), mpf(p), g) if p <= 0.5 else solve_pos(lambda y: chi_upper(y, k), 1 - mpf(p), g)
        add("ChiSquareQuantile", k, 0, p, q)

# F
for d1, d2 in [(0.7, 3.0), (5.0, 12.0), (996.8231650075872, 93.6624351281333), (50.0, 1e4)]:
    for x in [0.05, 0.8, 2.0, 7.0]:
        add("FCdf", d1, d2, x, f_lower(x, d1, d2))
    for p in [1e-9, 0.1, 0.5, 0.99]:
        g = stats.f.ppf(p, d1, d2)
        q = solve_pos(lambda y: f_lower(y, d1, d2), mpf(p), g) if p <= 0.5 else solve_pos(lambda y: f_upper(y, d1, d2), 1 - mpf(p), g)
        add("FQuantile", d1, d2, p, q)

out = []
out.append("// <auto-generated> by scripts/gen-distribution-references.py (mpmath, 40 digits). Do not edit by hand.")
out.append("namespace ToolBelt.Tests.Numerics")
out.append("{")
out.append("    internal static class DistributionReferenceData")
out.append("    {")
out.append("        // (function, a, b, x-or-p, expected): a/b are the distribution parameters (df, or mean/sd for the normal).")
out.append("        public static readonly (string Fn, double A, double B, double X, double Expected)[] Rows =")
out.append("        {")
for fn, a, b, x, val in rows:
    out.append(f'            ("{fn}", {repr(float(a))}, {repr(float(b))}, {repr(float(x))}, {mp.nstr(val, 20, min_fixed=1, max_fixed=0)}),')
out.append("        };")
out.append("    }")
out.append("}")
open("tests/ToolBelt.Tests/Numerics/DistributionReferenceData.cs", "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
print(len(rows), "reference rows written")
