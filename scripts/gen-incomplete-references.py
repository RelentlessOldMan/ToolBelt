"""Generates the differential-harness data for the regularized incomplete beta and gamma functions:
tests/ToolBelt.MathChecks/Data/incomplete-beta.tsv and incomplete-gamma.tsv, graded by IncompleteFunctionsGradedTests.

Each reference is computed at 50 digits by a method that never cancels (an all-positive series, a complement formed at
two working precisions that must agree, or quadrature over the smaller tail), so it is good to ~1e-30 relative. The test
grades ToolBelt in units of eps * (1 + kappa), where kappa = sum over inputs v of |v * d ln F / d v| is F's condition
number: input rounding alone moves F by ~eps * kappa, so 16 units is the bar for a backward-stable algorithm.

Usage (needs mpmath; single process, a full grid takes a few hours):
  python scripts/gen-incomplete-references.py grid    rebuild both grids, keeping any kappa already computed
  python scripts/gen-incomplete-references.py kappa   fill kappa for the rows the test listed in Data/*.need.tsv
kappa is computed only where it matters: a row within 16 eps passes at any kappa, so the test writes the rows over that
without a kappa to incomplete-beta.need.tsv / incomplete-gamma.need.tsv, and the kappa step merges them in. A blank kappa
grades as 0, the strictest reading.
"""
import os, sys, time
from mpmath import mp, mpf, hyp1f1, gammainc, quad, exp, log, log1p, loggamma, sqrt, inf, fabs
from mpmath.libmp.libhyper import NoConvergence

mp.dps = 50
DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'tests', 'ToolBelt.MathChecks', 'Data')
TAB, NL = chr(9), chr(10)

# ---------- I_x(a, b) ----------

def beta_quad(x, a, b):
    """I_x(a,b) by integrating the density over the smaller tail, breakpoints around the mode."""
    lnB = loggamma(a) + loggamma(b) - loggamma(a + b)
    f = lambda t: exp((a - 1) * log(t) + (b - 1) * log(1 - t) - lnB)
    s = a + b; mean = a / s; sd = sqrt(a * b / (s * s * (s + 1)))
    pts = [mean + k * sd for k in (-60, -30, -15, -8, -4, -2, -1, 0, 1, 2, 4, 8, 15, 30, 60)]
    lower = x <= mean
    lo, hi = (mpf(0), x) if lower else (x, mpf(1))
    # Boundary layer: past the mode the density decays from the endpoint x on the scale L = 1/|d ln f/dt| there,
    # which can be far below sd (x = 100 * mean at b = 1e15). Breakpoints at x -/+ L * 2^j resolve it.
    slope = abs((a - 1) / x - (b - 1) / (1 - x))
    L = 1 / slope if slope else sd               # x at the mode: no boundary layer
    for j in range(0, 80):
        p = x - L * 2 ** j if lower else x + L * 2 ** j
        if not (lo < p < hi): break
        pts.append(p)
    inner = [p for p in sorted(set(pts)) if lo < p < hi]
    if (a < 1 and lower) or (b < 1 and not lower): raise ValueError('quadrature is unreliable at the singular endpoint')
    v, err = quad(f, [lo] + inner + [hi], maxdegree=10, error=True)
    # A lower tail far below the double range (4.9e-324) is 0 as a double, however rough its last digits.
    if lower and abs(v) + err < mpf(10) ** -400: return mpf(0)
    # Error relative to the value returned: the tail itself for I = v, and 1 - v (about 1) for I = 1 - v.
    if not err <= abs(v if lower else 1 - v) * mpf(10) ** -30: raise ValueError('quadrature error %s' % mp.nstr(err, 3))
    return v if lower else 1 - v

def _f21(p, q, x):
    """2F1(p, 1; q; x) summed directly: every term is positive, so no cancellation; NoConvergence past 60000."""
    total = term = mpf(1); eps = mpf(10) ** -(mp.dps + 5)
    for n in range(60000):
        term *= (p + n) / (q + n) * x
        total += term
        if term < eps * total and (p + n) * x < (q + n): return total
    raise NoConvergence('2F1 series')

def _tail(x, a, b):
    """I_x(a,b) by the all-positive series x^a (1-x)^b / (a B) 2F1(a+b, 1; a+1; x)."""
    lnB = loggamma(a) + loggamma(b) - loggamma(a + b)
    return exp(a * log(x) + b * log1p(-x) - lnB) / a * _f21(a + b, a + 1, x)

def _cheap(x, a, b):
    """True when 2F1(a+b, 1; a+1; x) needs well under 60000 terms: the term ratio (a+b+n)x/(a+1+n) drops
    below 1 after n0 = ((a+b)x - a - 1)/(1 - x) terms, then shrinks geometrically."""
    if x >= 1: return False
    return max(mpf(0), ((a + b) * x - a - 1) / (1 - x)) < 20000

def beta_ref(x, a, b):
    x, a, b = mpf(x), mpf(a), mpf(b)
    if (x <= a / (a + b) or x < 0.5) and _cheap(x, a, b):
        try: return _tail(x, a, b)
        except NoConvergence: pass
    if x > mpf(10) ** -45 and _cheap(1 - x, b, a):                      # 1 - x is exact at 50 digits
        # r = 1 - tail cancels, and the tail's own error is ~10^-dps * |ln B| (4.5e21 at a = 1e20), so r is formed
        # at increasing working precisions, and kept once two agree to 1e-30 and r > 0.
        try:
            prev = None
            for extra in (30, 120, 400, 800):
                with mp.workdps(mp.dps + extra + int(mp.log10(1 + abs(a) + abs(b)))):
                    r = 1 - _tail(1 - x, b, a)
                if prev is not None and r > 0 and abs(r - prev) <= abs(r) * mpf(10) ** -30: return +r
                prev = r
        except NoConvergence: pass
    return beta_quad(x, a, b)

# ---------- P(a, x), Q(a, x) ----------

def _gamma_quad(a, x, lower):
    f = lambda t: exp((a - 1) * log(t) - t - loggamma(a))
    m, sd = a, sqrt(a)
    pts = set(p for p in (m + k * sd for k in (-60, -30, -15, -8, -4, -2, -1, 0, 1, 2, 4, 8, 15, 30, 60)) if p > 0)
    lo, hi = (mpf(0), x) if lower else (x, inf)
    if not lower and x < 1:                    # 1/t-like integrand at tiny a: one breakpoint per decade
        pts |= set(mpf(10) ** k for k in range(int(mp.floor(mp.log10(x))), 1))
    if a < 1: raise ValueError('quadrature is unreliable for the singular integrand at a < 1')
    v, err = quad(f, [lo] + [p for p in sorted(pts) if lo < p < hi] + [hi], maxdegree=10, error=True)
    if not err <= abs(v) * mpf(10) ** -30: raise ValueError('quadrature error %s' % mp.nstr(err, 3))
    return v

def _p(a, x):
    if max(mpf(0), x - a - 1) < 20000:
        try: return exp(a * log(x) - x - loggamma(a + 1)) * hyp1f1(1, a + 1, x, maxterms=60000)
        except NoConvergence: pass
    return _gamma_quad(a, x, True)

def _q(a, x):
    if x > a + 1 or a < 1:
        try: return gammainc(a, x, inf, regularized=True)
        except NoConvergence: pass
    return _gamma_quad(a, x, False)

def gamma_ref(a, x):
    """(P, Q), each computed directly (neither as 1 minus the other), and checked to sum to 1."""
    a, x = mpf(a), mpf(x)
    p, q = _p(a, x), _q(a, x)
    if abs(p + q - 1) > mpf(10) ** -30: raise ValueError('P + Q - 1 = %s' % mp.nstr(p + q - 1, 5))
    return p, q

# ---------- the grids ----------

SHAPES = [1e-300, 1e-20, 1e-5, 0.01, 0.1, 0.5, 1, 2, 5, 10, 50, 100, 1e3, 1e4, 1e5, 1e6, 1e8, 1e10, 1e15, 1e20]
GAMMA_SHAPES = [1e-300, 1e-20, 1e-5, 0.01, 0.1, 0.5, 1, 1.5, 2, 5, 10, 30, 50, 100, 1e3, 1e4, 1e5, 1e6, 1e8, 1e10,
                1e15, 1e20]

def beta_points():
    """x at multiples of the mean a/(a+b) and at fixed points, for every pair of shapes; then the mirror of each x < ½,
    (fl(1 - x), b, a), whose own 1 - x is exact, so ToolBelt reaches it through the complement machinery."""
    pts = []
    for a in SHAPES:
        for b in SHAPES:
            m = a / (a + b); xs = set(v for v in (1e-300, 1e-10, 0.01, 0.3, 0.5, 0.7, 0.99, 1 - 1e-10))
            xs |= set(m * f for f in (1e-10, 0.01, 0.5, 0.9, 1, 1.1, 2, 10) if 0 < m * f < 1)
            pts += [(x, a, b) for x in sorted(xs)]
    seen = set(pts)
    for x, a, b in list(pts):
        if x < 0.5 and 1.0 - x != 1.0 and (1.0 - x, b, a) not in seen:
            seen.add((1.0 - x, b, a)); pts.append((1.0 - x, b, a))
    return pts

def gamma_points():
    pts = []
    for a in GAMMA_SHAPES:
        xs = set(a * f for f in (1e-10, 0.01, 0.5, 0.9, 0.99, 1, 1.01, 1.1, 2, 10, 100))
        xs |= set((1e-300, 1e-10, 0.01, 0.3, 1, 3, 30, 300, 700))
        pts += [(a, x) for x in sorted(v for v in xs if v > 0)]
    return pts

def fmt(v): return mp.nstr(v, 20, min_fixed=1, max_fixed=0)

# ---------- condition numbers ----------

H = mpf(10) ** -20

def kappa(f, v):
    """sum |v_i * d ln f / d v_i|, by central differences at 50 digits."""
    v = [mpf(t) for t in v]; total = mpf(0)
    for i in range(len(v)):
        up = list(v); dn = list(v); up[i] *= 1 + H; dn[i] *= 1 - H
        total += fabs((log(f(*up)) - log(f(*dn))) / (2 * H))
    return total

# ---------- files ----------

def read(path):
    if not os.path.exists(path): return []
    with open(path) as f: return [l.rstrip(NL).split(TAB) for l in f if l.strip() and not l.startswith('#')]

def write(path, header, rows):
    with open(path, 'w', newline='') as f:
        f.write('# ' + TAB.join(header) + NL)
        for r in rows: f.write(TAB.join(r) + NL)

BETA, GAMMA = os.path.join(DATA, 'incomplete-beta.tsv'), os.path.join(DATA, 'incomplete-gamma.tsv')
BETA_HEADER = ['x', 'a', 'b', 'I_x(a,b)', 'kappa']
GAMMA_HEADER = ['a', 'x', 'P(a,x)', 'Q(a,x)', 'kappa_P', 'kappa_Q']

def grid():
    os.makedirs(DATA, exist_ok=True)
    old = {tuple(r[:3]): r[4] for r in read(BETA)}
    rows = []
    for x, a, b in beta_points():
        key = (repr(x), repr(a), repr(b)); t0 = time.time()
        try: v = fmt(beta_ref(x, a, b))
        except Exception as e: print('beta', key, type(e).__name__, e); continue
        rows.append(list(key) + [v, old.get(key, '')])
        print('beta', TAB.join(key), v, '%.1fs' % (time.time() - t0), flush=True)
    write(BETA, BETA_HEADER, rows)
    old = {tuple(r[:2]): r[4:6] for r in read(GAMMA)}
    rows = []
    for a, x in gamma_points():
        key = (repr(a), repr(x))
        try: p, q = gamma_ref(a, x)
        except Exception as e: print('gamma', key, type(e).__name__, e); continue
        rows.append(list(key) + [fmt(p), fmt(q)] + old.get(key, ['', '']))
        print('gamma', TAB.join(key), flush=True)
    write(GAMMA, GAMMA_HEADER, rows)

def fill_kappa():
    """The test writes need files of the key columns (plus P or Q for gamma) of rows over 16 eps that have no kappa."""
    need = set(tuple(r) for r in read(BETA[:-4] + '.need.tsv'))
    rows = read(BETA)
    for r in rows:
        if tuple(r[:3]) in need and not r[4]:
            r[4] = mp.nstr(kappa(lambda x, a, b: beta_ref(x, a, b), [float(t) for t in r[:3]]), 6)
            print('beta', r, flush=True)
    write(BETA, BETA_HEADER, rows)
    need = set(tuple(r) for r in read(GAMMA[:-4] + '.need.tsv'))
    rows = read(GAMMA)
    for r in rows:
        for which, col in ((0, 4), (1, 5)):
            if tuple(r[:2]) + ('PQ'[which],) in need and not r[col]:
                r[col] = mp.nstr(kappa(lambda a, x: gamma_ref(a, x)[which], [float(t) for t in r[:2]]), 6)
                print('gamma', r, flush=True)
    write(GAMMA, GAMMA_HEADER, rows)

if __name__ == '__main__':
    {'grid': grid, 'kappa': fill_kappa}[sys.argv[1] if len(sys.argv) > 1 else 'grid']()
