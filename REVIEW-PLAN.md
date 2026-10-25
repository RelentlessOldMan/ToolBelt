# Independent review plan

A full-codebase review, done one section at a time. Each round: independently review **one** section → verify and
fix the bugs → cut a release → move on to the next round. This file is the tracker; it is updated at the end of every round.

## Round protocol

1. **Review.** Two fresh reviewer agents that have no context from this session, run in parallel, each given only
   the section's file list and one lens:
   - *Correctness* — wrong results, numerical accuracy and edge cases (empty, NaN, ±∞, int overflow, extreme
     parameters), off-by-one errors, and docs that promise something the code doesn't do. For math, check against
     the reference (SciPy, R, the formula).
   - *Robustness* — hostile or malformed input, hangs and unbounded loops, resource leaks or dispose problems,
     thread safety and re-entrancy, culture and encoding issues, and netstandard2.0 vs net8.0 differences.

   Reviewers report findings only; they don't edit anything.
2. **Verify.** Each finding gets a failing test (or a concrete repro) before it counts. Findings that can't be
   reproduced are dropped. A finding that is real but has no wrong behaviour (docs or hardening) is counted
   separately.
3. **Fix.** Fix every confirmed bug and keep its test as a regression test. Then check:
   - all test suites green;
   - both core TFMs build with 0 warnings;
   - the SciPy reference generators re-run if their outputs are touched;
   - the README updated if the public API changed;
   - the section's files still pass their tests when the library is built with `CheckForOverflowUnderflow=true`
     (drop-in users may have it on). Build in a scratch worktree with the property added to
     `src/ToolBelt/ToolBelt.csproj` only, so test-side arithmetic stays unchecked.
4. **Release.** Bump the version, commit (`Review round N: <section>`), push, tag `v0.N.0`, and create a GitHub
   release (`gh release create`) whose notes list the fixes.
5. **Record.** Update the tracker table and the round log below, then start the next round.

**Re-review rule.** A section goes back on the queue if its last pass found **≥ 3 confirmed bugs**, or any
**severe** one (hang, crash on plausible input, silently wrong numbers, security issue). A section is done when a
pass finds ≤ 1 minor bug.

## Sections

Order is by risk: newest or unreviewed code and numerics first. Line counts cover source only (tests in parallel
folders). "Prior" is the confirmed-fix count from the 2026-10-05 sweep. That sweep only covered code changed since
`7ddbb24`, so it is a hint, not a pass.

| #  | Section | Scope (src) | ~Lines | Prior | Passes | Bugs (last / total) | Status |
|----|---------|-------------|-------:|------:|-------:|---------------------|--------|
| 1  | Filters & control | `Signal/IirFilter`, `Signal/FirFilter`, `Control/*` (Waves 87/88/88b, never reviewed) | 2,100 | — | 1 | 16 / 16 | re-queue (2 severe) |
| 2  | Signal analysis | rest of `Signal/*` (FFT, Welch, multitaper, Hilbert, resample, pulse/cycle measurements, unwrap…) | 1,850 | ~2 | 1 | 24 / 24 | re-queue (1 severe) |
| 3  | Statistics | `Numerics/` Distributions, SpecialFunctions, HypothesisTests, Anova, Multiple/LinearRegression, DistributionFit, PermutationTest, Bootstrap, ConfidenceInterval, Correlation, ChangePoint, Trend; `Quality/*` | 3,300 | ~10 | 2 | 22 / 53 | re-queue (22 bugs) |
| 4  | Numerics core | the rest of `Numerics/*` (interpolation, root finding, integration, linear algebra, polynomial, streaming stats, random, fractions, units…) | 2,950 | ~3 | 2 | 28 / 71 | re-queue (5 severe) |
| 5  | Charts | `Visualization/` charts: SvgChart, Annotations, Waterfall, TimingDiagram, HexBin, MultiPanel, AxisTicks, ScatterMatrix, BoxPlot, LinePlot, PlotFrame, Histogram, BandChart, ErrorBarChart, HeatMap, Colorbar, Colormap, Palettes | 3,700 | ~3 | 2 | 28 / 61 | re-queue (5 severe) |
| 6  | Binary formats & images | `Binary/*`, `Visualization/` PngReader, PngWriter, Bmp, ImageBuffer, SvgDocument, SvgUtils | 2,300 | ~3 | 2 | 10 / 29 | re-queue (10 bugs) |
| 7  | Documents & config | `Documents/*` (PDF, DOCX, report templates), `Configuration/*` | 2,900 | ~6 | 2 | 20 / 47 | re-queue (20 bugs, 6 severe) |
| 8  | IO | `IO/*` (tar, directory copy, FileWatcher, atomic writes…) | 2,950 | ~3 | 2 | 22 / 67 | re-queue (22 bugs, 2 severe) |
| 9  | Collections | `Collections/*` | 3,400 | ~1 | 2 | 11 / 39 | re-queue (11 bugs, 1 severe) |
| 10 | Concurrency | `Threading/*`, `Resilience/*`, `Process/*`, `Runtime/*` | 2,300 | ~2 | 2 | 25 / 65 | re-queue (1 severe) |
| 11 | Text & small types | `Text/*`, `Identifiers/*`, `Enums/*`, `Guards/*` | 2,900 | ~2 | 1 | 26 / 26 | re-queue (1 severe) |
| 12 | Logging, diagnostics, CLI | `Logging/*`, `Diagnostics/*`, `Cli/*` | 2,900 | ~2 | 2 | 17 / 49 | re-queue (17 bugs, 1 severe) |
| 13 | Security, net, time | `Security/*`, `Net/*`, `Time/*` | 2,250 | ~3 | 2 | 17 / 56 | re-queue (17 bugs) |
| 14 | Objects, functional, grids, graphs, intervals | `Objects/*`, `Functional/*`, `Grids/*`, `Graphs/*`, `Intervals/*` | 3,200 | ~1 | 2 | 19 / 63 | re-queue (4 severe) |
| 15 | Windows satellite | `src/ToolBelt.Windows/*` | 3,100 | ~3 | 2 | 26 / 61 | re-queue (1 severe) |
| 16 | WPF satellite | `src/ToolBelt.Wpf/*` | 2,600 | ~5 | 1 | 21 / 21 | re-queue (1 severe) |
| 17 | WinForms + tooling + docs | `src/ToolBelt.WinForms/*`, `samples/*`, `scripts/*` (reference generators), README accuracy, packaging/build | 2,300 | ~2 | 1 | 24 / 24 | re-queue (1 severe) |

After all 17: re-run the sections flagged by the re-review rule, worst first. Re-review order: 4 (done, round 18),
5 (done, round 19), 14 (done, round 20), 10 (done, round 21), 15 (done, round 22), 3 (done, round 23), 8 (done, round 24), 9 (done, round 25), 7 (done, round 26), 13 (done, round 27), 6 (done, round 28), 12 (done, round 29), then the 1-severe sections (1, 2, 11, 16, 17). **next: 1**

## Releases

There is no version or tag yet. Round 1 adds `<Version>` (a shared `Directory.Build.props`) and starts at
`v0.1.0`; each round bumps the minor version.

| Release | Round | Section | Bugs fixed | Commit |
|---------|-------|---------|-----------:|--------|
| v0.1.0 | 1 | Filters & control | 16 | (see tag) |
| v0.2.0 | 2 | Signal analysis | 24 | (see tag) |
| v0.3.0 | 3 | Statistics | 31 | (see tag) |
| v0.4.0 | 4 | Numerics core | 43 | (see tag) |
| v0.5.0 | 5 | Charts | 33 | (see tag) |
| v0.6.0 | 6 | Binary formats & images | 19 | (see tag) |
| v0.7.0 | 7 | Documents & config | 27 | (see tag) |
| v0.8.0 | 8 | IO | 45 | (see tag) |
| v0.9.0 | 9 | Collections | 28 | (see tag) |
| v0.10.0 | 10 | Concurrency | 40 | (see tag) |
| v0.11.0 | 11 | Text & small types | 26 | (see tag) |
| v0.12.0 | 12 | Logging, diagnostics, CLI | 32 | (see tag) |
| v0.13.0 | 13 | Security, net, time | 39 | (see tag) |
| v0.14.0 | 14 | Objects, functional, grids, graphs, intervals | 44 | (see tag) |
| v0.15.0 | 15 | Windows satellite | 35 | (see tag) |
| v0.16.0 | 16 | WPF satellite | 21 | (see tag) |
| v0.17.0 | 17 | WinForms satellite, tooling, docs | 24 | (see tag) |
| v0.18.0 | 18 | Numerics core, second pass | 28 | (see tag) |
| v0.19.0 | 19 | Charts, second pass | 28 | (see tag) |
| v0.20.0 | 20 | Objects, functional, grids, graphs, intervals, second pass | 19 | (see tag) |
| v0.21.0 | 21 | Concurrency, second pass | 25 | (see tag) |
| v0.22.0 | 22 | Windows satellite, second pass | 26 | (see tag) |
| v0.23.0 | 23 | Statistics, second pass | 22 | (see tag) |
| v0.24.0 | 24 | IO, second pass | 22 | (see tag) |
| v0.25.0 | 25 | Collections, second pass | 11 | (see tag) |
| v0.26.0 | 26 | Documents & config, second pass | 20 | (see tag) |
| v0.27.0 | 27 | Security, net, time, second pass | 17 | (see tag) |
| v0.28.0 | 28 | Binary formats & images, second pass | 10 | (see tag) |
| v0.29.0 | 29 | Logging, diagnostics, CLI, second pass | 17 | (see tag) |

## Round log

<!-- One entry per round: date, section, findings raised / confirmed bugs / docs-or-hardening / rejected, the
     severe ones in a line each, and whether the section is re-queued. -->

### Round 1 — 2026-10-07 — Filters & control

Reviewers: one for correctness and one for robustness. They raised 25 findings between them, with a large overlap. **16 confirmed
bugs** (each has a failing-first regression test in `FilterReviewTests` / `ControlReviewTests`), 3 doc-only, 1 rejected.

- **Severe:** IIR designs worked in physical units (ω ∝ fs), so the gain products over 2N roots overflowed or
  underflowed. Orders that the API accepts threw, or silently returned an all-zero filter: 243 of 4,000 random
  designs at fs from 1 to 1e6 failed, and so did any band design at order 22 with fs = 1e7. Fix: design at fs = 2,
  as SciPy does, carry the gain as a logarithm, and spread it over the sections only if it is extreme. Afterwards
  3,000 random designs from fs = 1e-5 to 1e9 at every order all matched SciPy (worst difference 2e-4 dB).
- **Severe:** PID anti-windup assumed Ki > 0. A reverse-acting loop, which is what `PidTuning` returns for a falling
  step, never came off saturation.
- **Moderate:**
  - PID: a NaN or ∞ measurement or time step poisoned the integral for good.
  - Kalman: NaN noise was accepted, one NaN measurement poisoned the filter for good, and there was no `Reset`.
  - Tiny ripple (10^x − 1 cancelling) gave NaN poles that were silently dropped, then an IndexOutOfRangeException.
  - `KaiserParameters` returned a negative tap count from int overflow.
- **Minor:**
  - `Deadband.Apply(NaN)` threw ArithmeticException, and `DeadbandFilter` re-reported +∞ on every sample.
  - The slew limiter with an ∞ rate and dt = 0 held its value and reported IsLimiting.
  - `Sections` / `Taps` exposed their backing arrays.
  - Biquad factories threw the wrong exception for extreme gain or Q.
  - The order estimators had no 1000 dB attenuation cap.
  - Chebyshev I ripple was unbounded.
  - `PidGains` accepted Ti ≤ 0 (Ki = ∞, or NaN for `default`).
  - `StepResponse` produced garbage on an overflowing step.
  - The FIR filter gave a misleading error when the window sums to zero.
  - `IirFilter.Reset(NaN)` was accepted.
- **Doc-only:** argument errors now name the public parameter (they said `fs`, `beta`, `edges`); `StepResponse`'s
  undershoot wording; a note that NaN input stays in IIR state.
- **Rejected:** a cap on FIR tap count. `int.MaxValue` taps → OutOfMemoryException is an honest failure for an absurd
  request.
- **Re-queued:** yes (2 severe, 16 bugs).

### Round 2 — 2026-10-07 — Signal analysis

Reviewers: one for correctness and one for robustness. They raised 35 findings between them, which came to 29 distinct
issues after overlap. **24 confirmed bugs** (each has a failing-first regression test in `SignalReviewTests`), 4
doc-only, 1 hardening fix with no feasible test, 0 rejected.

- **Severe:** `SavitzkyGolay` fitted every window with normal equations on raw positions 0..w−1, so moderate
  orders returned silently wrong numbers: (51, 10) was off by 3e-3 and (41, 14) by more than the signal itself. Fix:
  precompute the w coefficient vectors once with Householder QR on an abscissa scaled to [−1, 1]. It now matches a
  60-digit mpmath reference to 1e-13 (SciPy 1.12's own `savgol_coeffs` fails on these cases: lstsq's rcond truncates
  its unscaled Vandermonde).
- **Moderate:**
  - Out-of-range double → int casts gave int.MinValue (x64, before .NET 9), clamped to the wrong end: `Quantizer.Code`
    of a large over-range or +∞ input returned code 0, `FrequencyGrid.NearestBin(1e12)` returned bin 0, and
    `Resample.ToRate` at a huge ratio returned 1 sample.
  - `Resample.ToRate` pinned both endpoints, so its spacing was (N−1)/(M−1), not source/target: a 5% time-scale
    error on a 10-sample record.
  - `ZeroCrossing.Find` threw ArithmeticException on a NaN (`Math.Sign`).
  - `DbFs` of digital silence threw (for a parameter named `ratio`) instead of returning −∞.
  - `EnvelopeFollower`: one NaN or ∞ sample poisoned it for good, and an ∞ time constant was accepted.
  - `CycleMeasurements`: one NaN sample made min/max NaN, so it silently found 0 cycles.
  - `CycleMeasurements`: the hysteresis only armed edges and never confirmed them, so a runt pulse counted as a cycle
    with a NaN high time, which made `MeanDutyCycle` NaN. A level near the top gave every cycle NaN duty.
  - `Hampel`: NaN samples were never replaced but were counted as outliers.
  - `GccPhat`: `epsilon` was an absolute threshold, so a 1e-9-amplitude signal was zeroed and returned −(len−1).
- **Minor:**
  - `Hampel` / `MedianFilter` sized their buffers from the window, so a huge window on short input overflowed or ran
    out of memory.
  - `MedianFilter`: even-count edge windows took the upper middle value rather than the mean of the middle pair.
  - `WelchPsd` with segment length 2 (Hann/Blackman are all zeros) returned a NaN PSD.
  - NaN sample rates, overlap and floor were accepted by `<= 0` checks in `FrequencyGrid`, `Goertzel`, `Hilbert`,
    `Spectrum`, `WelchPsd`, `PulseMeasurements` and `Resample`.
  - `TimeDelayEstimate`: NaN input silently returned lag 0.
  - `TimeDelayEstimate`: ties (all-zero input) returned the most negative lag.
  - `PeakInterpolation`: a NaN in bin 0 stuck `ArgMax` there.
  - `PeakInterpolation`: an explicit `bin` at a local minimum was "interpolated".
  - `PulseMeasurements` accepted percentages (10, 90) and swapped references, giving a misleading error or a negative
    rise time.
  - FFT twiddles built by repeated multiplication gave a round-trip error of 5e-11 at 2²⁰; with direct twiddles it is
    now under 2e-14.
  - `PhaseUnwrap` with a tolerance below π widened jumps (0.6π became −1.4π).
  - `Rms` overflowed for 1e200.
- **Doc-only:**
  - `Gaussian` named the wrong parameter.
  - `Goertzel`: the A·N rule at DC and Nyquist, and its accuracy limits.
  - `CrossCorrelate`'s zero-lag index.
  - `WelchPsd`'s conventions relative to SciPy (symmetric window, no detrend).
- **Hardening:** the power-of-two padding loops in Bluestein and the correlations wrapped to 0 and spun forever past
  2³⁰. They now throw. This was not reproduced, because it needs more than 8 GB.
- **Lead for section 4:** `Polynomial.Fit` itself still uses the ill-conditioned normal equations.
- **Re-queued:** yes (1 severe, 24 bugs).

### Round 3 — 2026-10-24 — Statistics

Reviewers: one for correctness and one for robustness. Their findings consolidated to 31 distinct issues, and fixing
them turned up 3 more. **31 confirmed bugs** (each has a failing-first regression test in `StatisticsReviewTests`, or
new mpmath rows in `DistributionReferenceData`), 2 doc-only, 1 hardening fix, 0 rejected. On HEAD the new tests fail,
hang (2) or stack-overflow (1); the only ones that pass there are an equivalence guard for the rewritten run rules
and a pin for the newly documented tau-a.

- **Severe:**
  - `ChangePoint.DetectShift` maximised the unweighted |Δmean|, which favours the ends: {0 ×5, 10 ×4, 20} returned
    9, not 5. It now maximises k(n−k)/n·Δ², which minimises the total squared error (checked by brute force).
  - `SpecialFunctions.Gamma(NaN)` recursed until a stack overflow.
  - `KolmogorovSmirnovTwoSample` looped forever on a NaN.
  - The incomplete gamma's 100,000-term cap broke chi-square at large df: `ChiSquareCdf(1e9; 1e9)` was 4e-6 off, and
    at 1e10 it returned 0.42. Fix: Temme's uniform expansion for large a (Distributions and the private copies).
  - `LinearRegression.Fit` on constant x of 0.1 ×3 saw an sxx of 2e-34 of round-off and returned slope ≈ 1e33.
  - `PermutationTest`: one infinite rearrangement made the tie tolerance infinite (p = 1), and NaN data gave p = 0.
- **Moderate:**
  - Incomplete beta and gamma accuracy at large parameters: `RegularizedBetaI(0.5; 1e6, 1e6)` was off by 2.8e-9 and
    `RegularizedGammaQ` at 1e8 by 1.9e-8. Fixed with BASYM, a stable beta prefix, and the large-b continued
    fraction start (TOMS 708).
  - `LnGamma(1e-8)` was off by 2.7e-10 and `LnGamma(1e-310)` returned ∞; `Erf` of a tiny argument returned 0.
  - Anova's and GaugeRR's private F tails (300 iterations, cancelling LnGamma) were off by 5e-11 at df 1e5, and
    Anova accepted NaN df. They now carry synced copies of the Distributions machinery (the drop-in law forbids a
    shared dependency).
  - Trend: n(n−1)/2 overflowed int at 46,342 points, `Math.Sign(NaN)` threw ArithmeticException, and S overflowed.
    Now capped at `Trend.MaxPoints` (65,536).
  - Pearson and R² overflowed or underflowed in √(sxx·syy) near 1e±80.
  - One- and two-sample t, Anova, GaugeRR and ProcessCapability tested for exactly zero variance, so constant data
    (0.1 ×3) gave a round-off t of 1.4, an F or %GRR from noise, or Cp ≈ 1e16. Fixed with shifted means and
    round-off floors.
  - `Bootstrap` passed a NaN confidence through to an IndexOutOfRangeException, and `ConfidenceInterval` blamed
    parameter "p" for it.
  - `DistributionFit.FitGamma` threw on tight data (CV < 1.4e-7), which aborted `FitAll`.
  - `GaugeRR`: all-identical readings gave Unacceptable, ndc = int.MaxValue and NaN.
  - `ControlChart`: a NaN counted as both above and below, so it completed runs and trends, and a NaN center line
    flagged every window.
- **Minor:**
  - `Log1p(∞)` was NaN, so `StudentTPdf(1e160)` and `ChiSquarePdf(∞)` were NaN.
  - `StudentTQuantile` was pinned at 1.34e154 in the extreme tails: x² overflowed, and so did the bisection's lo·hi.
    Now 1e-15 from mpmath out to 3e299, and −∞ only where the true quantile overflows.
  - `ExponentialCdf` / `ExponentialQuantile` cancelled for small arguments (now expm1 / log1p).
  - `NormalQuantile` had 1e-9 relative error near ½.
  - ProcessCapability PPM took 1 − Φ, so a far upper spec gave 0 ppm; a NaN sigma passed the `<= 0` check.
  - `ChiSquareGoodnessOfFit` accepted proportions as expected counts (totals not checked) and NaN or negative counts.
  - `MultipleRegression` reported a NaN as "the predictors are collinear".
  - Result lists were the backing arrays (`Coefficients`, `Residuals`, `NullDistribution`, `Distribution`…): a
    cast back to double[] rewrote what `Predict` uses.
  - `DistributionFit` overflowed for values near 1e308.
  - Validation gaps: NaN df, probabilities, bounds and spec limits; infinite sd or rate; an unvalidated
    `BlandAltman` z (a negative z swapped the limits); `RequireOrdered` named no parameter.
  - NaN in the rank tests, Bootstrap and TheilSen sorted silently into the result; `ChangePoint` on all-NaN returned
    (1, −1).
  - Found while fixing: constant data in `ControlChart.Individuals` put every point beyond the limits (the mean an
    ulp off with a zero moving range); and Sen's / Theil–Sen pairwise differences overflowed to ∞ slopes for values
    of opposite sign near 1e308, which then overflowed the even median.
- **Doc-only:** `ChiSquareIndependence` applies no Yates correction (R and SciPy do for 2×2) and has the
  small-expected-count caveat; ProcessCapability's first line claimed BCL-only but it needs Distributions.cs.
- **Hardening:** the run and trend rules were O(n·runLength). They are now one pass (200,000 points with a
  run length of 100,000 took over a minute).
- **Re-queued:** yes (7 severe, 31 bugs).

### Round 4 — 2026-10-24 — Numerics core

Reviewers: one for correctness (30 findings) and one for robustness (31), about 45 distinct issues after overlap.
**43 confirmed bugs** (each has a failing-first regression test in `NumericsCoreReviewTests`), 2 doc-only, 2
hardening fixes, 0 rejected.

- **Severe:**
  - `Polynomial.Fit` solved the normal equations on raw powers. A quadratic at x ≈ 1e6 fitted with RMS residual
    3.8 (numpy 2e-4), and x in [0, 1e-4] threw "singular". It now uses Householder QR on centred and scaled x.
  - `Polynomial.CubicRoots` used absolute 1e-12 thresholds, so it dropped real roots of small-scale cubics (roots
    0.001/0.002/0.003 came back as two). `QuadraticRoots` cancelled: x² − 1e8·x + 1 gave a small root 25% off.
  - `MonotoneCubic` lacked the Fritsch–Carlson extremum rule and overshot the data it promises never to overshoot.
  - `LinearAlgebra` used an absolute 1e-14 pivot threshold: det(diag(1e-20, 1e-20)) returned 0 and a well-conditioned
    1e-15 diagonal was "singular".
  - `ToleranceCheck` passed NaN samples and accepted NaN limits.
  - `MovingAverage`'s running sum drifted for good (1e17 then 1s returned 0), and one ∞ made it NaN forever.
  - `Integration.AdaptiveSimpson` had no evaluation cap: a NaN tolerance, an infinite bound or a large integrand ran
    ~2⁵⁰ calls. It now validates its inputs, has a `maxEvaluations` budget, a round-off stop and throws on a
    non-finite f.
  - `Primes.Next` past the last long prime wrapped negative and never returned; trial division took 35 s for
    `IsPrime(2⁶³ − 25)` and 54 s to factor (2³¹ − 1)². Now deterministic Miller-Rabin and Pollard–Brent (both
    instant), `LargestLongPrime` and `MaxSieveLimit`.
- **Moderate:**
  - `RootFinding`: `Math.Sign(NaN)` threw from Bisection and Brent; a reversed bracket "converged" after one step;
    absolute tolerances meant a root at 1.4e10 never converged and Newton's ±1e-7 step vanished at 3e10.
  - `ZScore` gave −1s for constant data (0.1 ×3).
  - `Rounding` / `EngineeringNotation`: 10^k overflowed (NaN for 1.2e-310); "G" format switched to scientific
    (`Format(120, 2)` was "1.2E+02"); values past the Y prefix, ∞ and NaN didn't round-trip; 'μ' wasn't parsed.
  - `LookupTable.InverseLookup` returned NaN on a flat run it accepts.
  - `Means` overflowed or underflowed (AM of two 1e308s, RMS of 1e±200, harmonic of a subnormal).
  - `Vector2.Length` overflowed and `Normalized` returned (0, 0) for (1e200, 1e200); on net48 NaN payloads broke
    the Equals/GetHashCode contract.
  - `ScatteredInterpolation` weights overflowed to ∞/∞.
  - `RunningStatistics`, `ExponentialMovingAverage`, `MinMaxScaler`, `WeightedAverage`, `Histogram`,
    `EmpiricalDistribution` and `Percentile` accepted NaN or ∞ silently (Histogram counted NaN as underflow; it now
    has `NaNCount`). `MinMaxScaler` overflowed on a 1e308 span.
  - `Fraction.ToDouble` overflowed to NaN for big numerators and denominators (now correctly rounded), `Parse`
    accepted a zero denominator, and `default(Fraction)` was 0/0.
  - `RandomUtils`: huge weights summed to ∞, `NextDouble(min, max)` could return max, Gaussian/exponential
    parameters weren't validated.
  - `Integration.Simpson` with an odd interval count closed with the trapezoid rule (O(h²)); it now uses SciPy's
    end correction.
- **Minor:** `Histogram` bin assignment disagreed with `BinLowerBound` at the edges and exposed its bins array;
  `StreamingQuantile` jumped to the median at exactly five values; `Differentiation`'s fixed step failed at 1e12
  and accepted duplicate x; `UnitConvert` C → C was not exact, 1e308 km → km overflowed, and `TryParse` accepted
  "5 bananas"; `BaseConverter` accepted duplicate digits and lower-cased with the culture; `Polynomial()` (empty)
  gave degree −1; internal arrays were exposed through casts; `Sieve(int.MaxValue)` threw OverflowException;
  int-overflow and parameter-name exceptions in several validators; `AkimaSpline` / `Interpolation.Clamp` gaps.
- **Doc-only:** `Angle.NormalizeDegrees(−0.0)` (now returns +0) and `Vector2.Angle`'s range at −0.
- **Hardening:** `Uncertainty` with an uncertainty below the value's precision, or near the double range, now
  throws or scales safely; `DeterministicRandom` overrides the .NET 6 `Random` members (`NextInt64`, `NextSingle`,
  span `NextBytes`), which otherwise bypassed the seeded generator.
- **Also fixed:** a round-3 leftover. v0.3.0's equal-totals check in `ChiSquareGoodnessOfFit` broke the MathChecks
  cross-check, which fed it unequal totals; the test now scales expected to the observed total.
- **Re-queued:** yes (8 severe, 43 bugs).

### Round 5 — 2026-10-25 — Charts

Reviewers: one for correctness (19 findings) and one for robustness (15), about 33 distinct issues after overlap.
**33 confirmed bugs** (each has a failing-first regression test in `ChartsReviewTests`), 0 doc-only, 1 left by
choice, 0 rejected.

- **Severe:**
  - The raster charts (`LinePlot`, `BandChart`, `ErrorBarChart`, `BoxPlot`) cast mapped coordinates to int and
    walked every pixel. A value far outside an explicit range hung (y = 1e7 against [0, 1]), or wrapped the int and
    threw. They now map in doubles, skip non-finite points and clip lines with Liang–Barsky before stepping (new
    public `ImageBuffer.DrawLineClipped`).
  - `ImageBuffer.DrawLine` / `DrawRectangle` stepped off-canvas pixels one by one: `DrawLine(int.MinValue, …,
    int.MaxValue, …)` never returned, and `Math.Abs` of the difference threw. `new ImageBuffer(65536, 16384)` wrapped
    `width × height × 4` to a zero-length buffer, and `HeatMap` with a big `CellSize` did the same.
  - `Annotations` raster lines (callouts, thick reference lines) had the same pixel walk: a callout with dx = 1e10
    hung, and a 1e4 px pen cost thickness² per pixel.
  - `AxisTicks.CountTicks` cast to long, so epoch-nanosecond ranges (1.7e18) wrapped and a hopeless step "fitted";
    `Time(0, 1e16)` gave no ticks. A huge `MaxXTicks` spent seconds building ticks that cannot be drawn, and a
    `TimingDiagram` with `Width = 1e17` hatched forever (x + 6 == x).
  - `HeatMap` blanked every finite cell when one value was ∞, and `Histogram` discarded the finite range when one
    value was ∞ (and gave NaN edges for a ±1e308 span).
  - `BoxPlot.Compute` let NaN into the quartiles, and whiskers could end inside the box.
- **Moderate:**
  - `AxisTicks`: narrow spans far from zero (1 to 1.00001, 12345670 to 12345680) produced duplicate labels; ±1e308
    gave an infinite step; a tight log range inside one decade had no ticks.
  - `TimingDiagram`: epoch-scale ticks collided (now first label + "+Δ"); `FormatSeconds` chose its unit before
    rounding (0.9999999 s gave "1000 ms"); a marker accepted a NaN time.
  - `SvgChart`: auto-ranged bars were cut to half width at the ends, and one explicit limit past the data collapsed
    the axis (same fallback bug in `Waterfall.ValueRange`).
  - `PlotFrame.Map/Unmap` overflowed on a ±1e308 range; `HexBin` gave a cell to a point far outside the frame.
  - `Annotations` dropped a band on a log axis when a bound was ≤ 0 (now clamped to the axis minimum).
  - `Histogram` values on an edge landed in the wrong bin, and min > max or non-finite bounds were accepted;
    `HeatMap` accepted min > max.
- **Minor:** `Palettes.FromHex` accepted whitespace ("#fffff ", "# ff"); the shared
  palettes and `TimingSignal.Changes` could be cast back to arrays/lists and edited; validation gaps
  (`whiskerReach` NaN, `bins == int.MaxValue`, NaN colormap stops, infinite `PlotFrame` size, `FormatTime`
  decimals, `TimingDiagram` lane sizes, null `FontFamily` / `Colormap` / column NREs); several errors named `o`, `y1`
  or nothing instead of `options`, `y2`, `changes`.
- **Left by choice:** `BoxStats.Outliers` stays a public `double[]` (changing it breaks the API).
- **Re-queued:** yes (8 severe, 33 bugs).

### Round 6 — 2026-10-25 — Binary formats & images

Reviewers: one for correctness (11 findings) and one for robustness (16), about 22 distinct issues after overlap.
**19 confirmed bugs** (18 with a failing-first regression test in `BinaryImagesReviewTests`; the checked-arithmetic
bug was confirmed with a checked build), 2 doc-only, 2 hardening items left, 0 rejected.

- **Severe:**
  - `VarInt.DecodeUnsigned` accepted a 10th byte with bits above bit 63 and silently dropped them (wrong value).
  - `QuotedPrintable.Decode` cast each literal character to a byte, so 'Ł' (U+0141) became 'A'. Characters
    outside ASCII now throw.
  - The section broke when dropped into a project built with `CheckForOverflowUnderflow=true`: `Fnv1a`, `Crc16`,
    `Base85`, `Bits.PopCount`, `EndianConverter.Write*`, `VarInt.EncodeSigned`, the PNG/BMP byte packing and
    `ImageBuffer`'s `(uint)` bounds checks all threw `OverflowException`. Wrapping arithmetic is now `unchecked`.
- **Moderate:**
  - `PngReader`: a wide 16-bit image (40M × 1 RGBA16, under `MaxPixels`) wrapped `rowBytes` negative after
    inflating; the size is now checked in long before any decompression.
  - `Bmp.Encode` computed `w × 32` in int (negative stride for w ≥ 2^26); `PngWriter` computed the scanline buffer
    size in int (wraps near the 2 GB limit, fixed by inspection — the test would need a 2 GB image).
  - `SvgDocument` wrote NaN/∞ as "NaN"/"Infinity" and tiny negatives as "-0", accepted an infinite canvas, and
    passed control characters and lone surrogates into the XML. It now matches `SvgUtils` (inlined, drop-in law).
  - Segment range checks (`offset + count > length`) overflowed in `Crc32`, `Crc32Hasher`, `Crc16Hasher`,
    `Adler32Hasher` and `EndianConverter`, so a huge count passed the check.
  - `ImageBuffer.Blit(this, …)` with an offset smeared (read pixels it had already overwritten).
- **Minor:**
  - `Base32` ignored everything after the first '=' and, like `CrockfordBase32`, accepted lengths no encoder
    produces (1, 3 or 6 symbols in the last group).
  - `CrockfordBase32` folded case with `ToUpperInvariant`, so U+017F (long s) decoded as 'S'.
  - `Base64Url` let `Convert` accept '+', '/' and whitespace.
  - `QuotedPrintable` rejected transport padding after a soft-break '=' and kept trailing whitespace before a hard
    line break (RFC 2045 says drop it).
  - `SvgDocument.Text` threw NullReferenceException for a null fill, anchor or font family; `SvgUtils` transforms
    and `StrokeAttributes` named every bad argument `value`.
- **Doc-only:** `Crc16` said XMODEM (it is CCITT-FALSE / IBM-3740, check 0x29B1); `Base64Url` said it accepts
  padding (it rejects it, and the tests assert that).
- **Hardening left:** `Base58.Decode` is quadratic (1.3 s for 80k characters); `PngReader.MaxPixels` is a const and
  a missing IEND is accepted.
- **Cross-cutting (not this section):** a library-only checked build still fails 57 tests elsewhere — Grids (26),
  Collections (13), Net (5), Numerics (4, including `DeterministicRandom`), Documents (2), Guards, Runtime and
  Charts' `Palettes`. Each later round now runs the checked build for its own section (see the protocol).
- **Re-queued:** yes (3 severe, 19 bugs).

### Round 7 — 2026-10-25 — Documents & config

Reviewers: one for correctness and one for robustness, 29 distinct findings after overlap plus 2 doc items.
**27 confirmed bugs** (24 failing-first regression tests in `DocumentsConfigReviewTests`; the checked-arithmetic
bugs were confirmed with a checked build, and the multi-line PDF table width and the false drop-in header by
inspection), 3 doc-only, 1 hardening item, 0 rejected.

- **Severe:**
  - `HtmlReport.CodeBlock` escaped the language with the text escaper, so a `"` broke out of the `class`
    attribute (markup injection).
  - `ConfigBinder` replaced every nested object with a new one, discarding initializer defaults even when the section
    had no keys, and skipped get-only nested properties. It now binds only a configured section, into the existing
    instance.
  - `ConfigBinder` called `Activator.CreateInstance` on every class-typed property, so a `Uri` (or any type without a
    parameterless constructor) threw `MissingMethodException` even when unconfigured; a configured one is now a
    binding error.
  - `ConfigBinder` could not bind `Guid` or `DateTimeOffset` (`Convert.ChangeType` has no conversion for them).
  - `PdfWriter` read the PNG IHDR fields without checking the chunk length: `IndexOutOfRangeException` instead of
    `ArgumentException`.
- **Moderate:**
  - `PdfWriter`'s hard split of a word wider than the line was quadratic (25 s for a 1.2 M-character blob), and so was
    the outline writer (12.7 s for 60k headings). Both are linear now.
  - `ReportBuilder` anchor de-duplication could reuse an id (headings "x-1", "x", "x" gave "x-1" twice).
  - `MarkdownReport.CodeBlock` always used a three-backtick fence (a ``` line in the code closed it) and put the
    language in raw; `Code` escaped backticks with backslashes, which are literal inside a code span.
  - Markdown escaping missed `<`, `&` and `~`; a lone CR in a table cell ended the row.
  - `ReportBuilder.ToMarkdown` wrote the title, headings, paragraphs, bullets and callouts raw, so "# x", "*x*",
    "- x" or "1. x" in report text turned into Markdown syntax (the HTML, PDF and DOCX outputs show it literally).
  - A bare number was a valid duration in `ConfigBinder` and `EnvironmentVariables` and meant days ("30" → 30 days).
    It is now rejected as ambiguous.
  - `PdfWriter` accepted a NaN or infinite margin or font size; `DocxWriter.Image` accepted an infinite or huge width
    and cast it to a garbage EMU extent.
  - Booleans, enums and flags parsed inconsistently: the binder rejected "yes"/"on" and accepted undefined enum
    numbers; both rejected [Flags] combinations such as "Read, Exec".
- **Minor:**
  - The `ConfigBinder` header claimed it was self-contained but used `TypeUtils` and `HumanDuration`; the
    conversions are now inlined.
  - `HtmlReport.Table` threw on a null row and emitted ragged rows; PDF table widths measured a multi-line cell as one
    line; tabs, DEL and surrogate pairs measured or printed wrong in the PDF (a pair printed "??").
  - The binder skipped `IList<T>`, `IReadOnlyList<T>` and `IEnumerable<T>` properties and silently dropped list
    items after an index gap.
  - Checked-arithmetic failures in the PDF zlib Adler bytes, the PNG unfilter, `Code`'s per-line count and the DOCX
    image extent.
  - `SettingsStore` could not save NaN or ±∞ and threw on a locked or unreadable file.
  - `ReportTemplates` printed "-0" for a delta that rounds to zero.
  - DOCX tables had no `tblGrid` (schema-invalid), and heading levels 4–6 were all the same size.
- **Doc-only:** `HtmlReport.Title` claimed to emit an `<h1>`; URLs are not sanitized (a `javascript:` link stays
  live); figure SVG is inlined verbatim and must be trusted.
- **Hardening:** `EnvironmentVariables.WithPrefix` compares keys case-sensitively off Windows, so `APP_X` and `app_x`
  no longer collide on Linux.
- **Checked build:** the section is clean; 55 failures remain elsewhere (round 6's 57 minus the two PDF image ones).
- **Re-queued:** yes (5 severe, 27 bugs).

### Round 8 — 2026-10-25 — IO

Reviewers: one for correctness (27 findings) and one for robustness (a probe program against links, encodings,
huge sizes and races). **45 confirmed bugs**: 39 regression tests in `IOReviewTests` (37 failing first; the
cross-platform `SafeFileName` test only fails off Windows, and the `ReadEntryBytes` cap is a new parameter), 6 by
inspection (races and leaks); 3 hardening items, 3 doc-only, 0 rejected.

- **Severe:**
  - `ZipUtils` and `TarUtils` extraction wrote through a junction or symlink already in the destination, escaping
    the target. Every existing path component is now checked and a link is refused.
  - `ZipUtils.CreateFromDirectory` followed junctions in the source, and an archive created inside its own source
    tried to zip itself (`IOException`). It now walks without following links, skips its own output, and deletes a
    partial archive on failure.
  - `LineReader` split on raw 0x0A bytes, so UTF-16/32 text came back as corrupted lines. It now rejects encodings
    whose CR/LF aren't single bytes, and skips a UTF-8 BOM (offsets were off by 3).
  - `ChecksumManifest.Create` without an `onError` callback silently left out unreadable subtrees; it now throws.
- **Moderate:**
  - `ZipUtils.AddOrReplaceEntry` left duplicates; an entry resolving to the root threw the wrong exception;
    `ReadEntryBytes` had no size cap (new `maxBytes`). `TarUtils` dropped empty directories and extracted sparse
    entries as garbage (now rejected).
  - `CsvLine.Parse` accepted an unterminated quote; `CsvDialect.Detect` picked ',' for "1,5;2,5" data and counted
    integers and "1,000" as decimal-comma evidence; `CsvBinder` couldn't round-trip `Guid`, `TimeSpan`,
    `DateTimeOffset`, a UTC `DateTime` or an empty nullable.
  - `ByteSize.Parse("1,5 KB")` was 15,000 bytes, and `Format(1048575)` printed "1024.00 KiB".
  - `DotEnv` kept quotes when a quoted value had an inline comment, cut unquoted values at any `#`, and didn't handle
    a BOM or `export<TAB>`.
  - `FileRetention.MaxTotalBytes` deleted the newest large file but kept older small ones.
  - `FileWatcher` reported a deleted empty directory as a file, double-reported a pending rename after a rescan,
    could miss changes between its first snapshot and enabling events, raised `Changed` after `Dispose`, leaked
    stability probes, and scanned every snapshot key on each deletion (now O(1) with ancestor counts).
  - `SafeFileName` used the current OS's invalid set (Linux-made names broke on Windows) and missed `CON .txt`,
    `COM0`/`LPT0`, superscript-digit forms and `CONIN$`/`CONOUT$`.
  - `AtomicFile` dropped the encoding preamble and leaked its temp file on a failed write; it and `JsonFile` used a
    temp name 37 characters longer than the target (PathTooLong near MAX_PATH).
  - `JsonFile` rejected null for `Nullable<T>`; `JsonLines.Read` and `TryLoad` checked arguments late.
  - `StreamUtils.Copy` read once more after cancellation; `TeeStream.Dispose` leaked the copy if the primary threw.
  - `DirectoryUtils.EnsureEmpty` on a junction emptied the link's target; it now throws.
- **Minor:** overflow in `FixedWidth` positions and `HexDump` offsets/huge `bytesPerLine`; `SafeFileName` truncation
  split surrogate pairs; relative paths kept a trailing separator; deletes threw when an item vanished meanwhile;
  `CsvBinder`'s drop-in header was false; on .NET 6+ cloud-file placeholders are no longer treated as links.
- **Hardening:** `CsvDialect.FormatNumber` falls back to G17 where "R" doesn't round-trip (.NET Framework);
  `FileRetention` re-checks the wildcard (the netfx 8.3 quirk matches "*.log" to "a.logx").
- **Doc-only:** `ChecksumManifest` and `DirectoryUtils.Copy` follow file symlinks; `FileRetention.KeepAtLeast` wording.
- **Checked build:** the section is clean; the same 55 failures remain elsewhere.
- **Re-queued:** yes (6 severe, 45 bugs).

### Round 9 — 2026-10-25 — Collections

Reviewers: one for correctness and one for robustness (a probe program against deep inputs, huge capacities,
overflow and mutation during enumeration). **28 confirmed bugs**: 22 failing-first regression tests in
`CollectionsReviewTests` (two of them crashed the test process with a stack overflow), the checked build for the
sketch hashing, 3 by inspection; plus 2 guard tests (permutation order, a Trie-vs-SortedSet differential) and 2
doc-only items, 0 rejected.

- **Severe:**
  - `CartesianProduct.Of(List<List<T>>)` bound to the `params` overload (T = `List<T>`) and returned one-element
    tuples of the inner lists. The `params` form now takes at least two sequences; zero or one go through the list
    overload.
  - `Sampling.WeightedPickIndex` accepted infinite weights, and when the total overflowed to infinity it always
    fell through to the last index, even one with weight 0. Weights are now scaled by the largest, and the
    fall-through picks the last positive weight.
  - `CountMinSketch`, `BloomFilter`, `CountingBloomFilter` and `BitSet.PopCount` threw `OverflowException` on every
    operation in a checked build (hash multiplies weren't `unchecked`).
  - `Deque` enumeration while pushing never ended (no version check).
  - `Trie.WithPrefix` and `Remove` recursed once per character: a 200,000-character key crashed the process. Both
    are iterative now, with the same sorted output.
  - `Combinatorics.Permutations` shared one working array between enumerations, so two enumerations (or one
    restarted after being abandoned) interfered.
- **Moderate:**
  - `CountMinSketch` mixed hashes too weakly: keys differing only in high bits collided in every row (now the full
    MurmurHash3 finalizer).
  - `Permutations` nested one iterator per element and overflowed the stack on a long list (now an explicit stack,
    same swap order).
  - `TtlCache` with `TimeSpan.MaxValue` threw on `Set` (now saturates), and expired keys that were never read again
    accumulated forever (`Set` now prunes when the count doubles).
  - `LruCache`, `Batch` and `TopN` allocated their full capacity up front: `int.MaxValue` threw `OutOfMemoryException`.
- **Minor:** `BitSet` and `FenwickTree` constructor overflow near `int.MaxValue` (and Fenwick's update walk);
  `Deque` index arithmetic; `CircularBuffer` enumeration didn't detect modification; `RangeSorted` checked null only
  on enumeration; `NearestBy` with a NaN key returned 0 (now throws); `Aggregation.Summarize` dropped NaN from
  Min/Max; `SamplingPlans.WeightedWithoutReplacement` lost the proportions of subnormal weights;
  `CountingBloomFilter` and `CountMinSketch` doc crefs to `BloomFilter` broke the drop-in law.
- **Hardening:** `ObjectPool` kept a double-returned instance twice (two renters shared it), reset instances it
  then dropped, and accepted a null from the factory; `ReservoirSampler.Sample` could be cast back to the internal
  `List<T>`; `DataTableLite.ToString` used the current culture.
- **Doc-only:** `DataTableLite` `Distinct`/`GroupBy`/`InnerJoin` compare boxed values (int 1 ≠ long 1);
  `TtlCache.Count` is O(n).
- **Checked build:** the section is clean; 42 failures remain elsewhere (55 at v0.8.0, minus the 13 Collections ones).
- **Re-queued:** yes (6 severe).

### Round 10 — 2026-10-25 — Concurrency
Reviewers: one for correctness and one for robustness (dispose races, cancellation, timeouts, throwing callbacks,
child processes that never read stdin or leave grandchildren holding the pipes). **40 confirmed bugs**: 32 with
failing-first regression tests in `ConcurrencyReviewTests` (33 tests; one crashed the test host), 8 by inspection;
plus 4 doc-only items. Two existing timing tests in `PauseAndRaceTests` got wider margins (10–30 ms windows flaked
when pinned to one core).

- **Severe:**
  - `ProcessRunner`: a throwing `OnOutputLine`/`OnErrorLine` callback escaped on the reader thread and crashed the
    process. Now the run finishes and the first callback exception is rethrown to the caller.
  - `ProcessRunner`: the timeout didn't bound the run. Writing stdin to a child that never reads it blocked before
    the wait began, and a grandchild holding the output pipes kept `Run`/`RunAsync` waiting after the kill. Stdin is
    now written in the background and the output drain is bounded (a second's grace after a kill).
  - `ConcurrentPipeline`: an item accepted just as `CompleteAsync` ran could be left in the buffer unprocessed (the
    completed check and the enqueue weren't atomic). A producer still blocked for space now throws instead.
  - `ConcurrentPipeline`: a throwing `onError` killed the consumer, so Block producers waited forever.
  - `PeriodicWorker`: `Start` ran the loop on the caller's thread, so a zero interval with synchronous work never
    returned, and `Dispose` from a single-threaded (UI) context deadlocked. The loop runs on the pool now; a
    throwing `onError` no longer kills it.
  - `ParallelUtils.ForEachAsync`: on caller cancellation or a source-enumeration exception it returned while bodies
    were still running (and enumeration failures skipped the waiting entirely).
  - `CircuitBreaker` half-open admitted every caller, not one trial; a caller's own cancellation counted as a
    failure, and a stale call finishing after the circuit opened could close it or restart the timer.
- **Moderate:**
  - `ProcessRunner`: `Encoding` didn't apply to stdin (UTF-8 text arrived as "??"); an invalid timeout threw only
    after the child had started; `RunAsync` leaked its timer per call; a failed `Start` leaked the `Process`.
  - `ShellOpen` on macOS/Linux passed the target through the argument string: a path with spaces split, quotes
    injected arguments, and a leading '-' was read as an option.
  - `WhichExe` on Windows returned an extensionless file (a Unix script like `npm`) ahead of `npm.cmd`.
  - `Retry`: `MaxDelay = Timeout.InfiniteTimeSpan` capped every delay at -1 ms, which Task.Delay treats as forever; a negative `BaseDelay` failed only at the first retry, replacing the operation's exception; the default
    delay threw past ~24.8 days (now waited in pieces).
  - `AsyncLock`/`Bulkhead` `Dispose` disposed the semaphore under queued waiters (they hung, and a held
    handle's release threw). Queued callers now get `ObjectDisposedException`; in-flight work finishes.
  - `PeriodicWorker`: a failing custom delay was unobserved and silently ended the loop; an interval past
    Task.Delay's limit failed only inside the loop.
- **Minor:** `AtomicDouble.Add` spun forever on NaN (compared doubles, not bits); `ConcurrentPipeline` Block capacity
  counted the in-flight item (now buffered items only, as for the drop policies); second `Dispose` on the worker and
  pipeline threw; `AsyncEvent` parallel invoke dropped cancelled handlers; `AsyncCountdownEvent.AddCount` overflowed;
  `WhenAllOrFirstException` ignored a cancelled member until every other task finished; `WithTimeout` named the wrong parameter;
  `TokenBucketRateLimiter` accepted NaN and ∞; `CreateLinkedTimeout` leaked its registration when `CancelAfter`
  threw; `PauseTokenSource.Toggle` raced (two toggles could both pause); `Jitter` lost sub-millisecond delays on
  netstandard2.0 and its cref to `RetryPolicy` broke the drop-in law.
- **Doc-only:** `Jitter` ranges are half-open; `MemoryPressure` on netstandard2.0 reports the heap size (deltas can
  be negative); `AppInfo.Version` is the AssemblyVersion; the tree kill is .NET Core 3.0+ only.
- **Not changed:** `AsyncEvent` sequential invoke stops at a cancelled handler (by design); the
  `TaskExtensions` name clash (documented); `ProcessRunner` has no argument-list overload yet; wall-clock-based
  timings in `StartupTiming`.
- **Checked build:** the section is clean; the same 42 failures remain elsewhere.
- **Re-queued:** yes (7 severe).

### Round 11 — 2026-10-25 — Text & small types
Reviewers: one for correctness and one for robustness (Unicode edge cases, lone surrogates, huge inputs, aliased
enums, non-canonical id text). **26 confirmed bugs**: 25 with failing-first regression tests in `TextReviewTests`
(28 tests), 1 by inspection; plus 2 doc-only items. The existing `NaturalComparer` tests that pinned "x01" equal to
"x1" were updated to the new tie-break, and the differential reference now models it.

- **Severe:** `WordWrap.Wrap` hard-broke an over-long word by re-copying the remainder each line, which is quadratic:
  300k characters at width 1 took 31 s. It now walks an offset.
- **Moderate:**
  - `TextDiff.Lines` built the full n×m LCS table even for nearly identical inputs (20k lines with one change took 6 s
    and 1.6 GB). The common prefix and suffix are matched first.
  - `Slug.Slugify` could exceed `maxLength` by one (a separator plus a char appended past the limit) and threw on an
    unpaired surrogate (`Normalize`).
  - `CaseConverter.ToWords` split at combining marks and dropped them (decomposed "café" became "cafe"), and treated
    letters outside the BMP as separators.
  - `Truncate.WithEllipsisOnWord` dropped a word when the cut landed exactly on a space, kept the spaces before the
    ellipsis, and only knew `' '` as whitespace.
  - `NaturalComparer` compared "a01" and "a1" equal, so a `SortedSet`/`SortedDictionary` silently dropped one. The
    leading-zero count now breaks ties.
  - `EnumMap` gave each alias its own slot (wrong `Count`, writes through one name invisible through the other).
  - `ShortGuid.Decode` accepted '+', '/' and stray low bits (many strings per GUID); `UlidGenerator.DecodeTimestamp`
    accepted a first char above '7' (timestamp past 48 bits) and never checked the random part.
  - `SnowflakeIdGenerator` returned negative ids once the timestamp passed 41 bits.
  - `NanoId`/`UlidGenerator`: the default `Random` on .NET Framework seeds from the tick count, so instances created
    together repeated. The netstandard2.0 build now seeds from a GUID (by inspection).
- **Minor:** `EnumExtensions.GetFlags` included composite members for `[Flags]` enums and subset members for plain
  enums (contrary to its docs); `GetValues`/`GetNames`/`EnumMap.Keys` handed out the shared cache arrays; `JaroWinkler`
  could score above 1 (`prefixScale × maxPrefix > 1`) and accepted NaN; `QueryString.Parse` didn't decode '+';
  `AnsiText.Strip`/`TextAlign` left the final byte of escapes with intermediates (`ESC ( B`); `TextAlign` counted wide
  BMP emoji as one column and padded truncation with ' ' instead of `pad`; `GlobMatcher` with `ignoreCase` folded
  range endpoints (`[Z-a]` became empty); `Indent` treated whitespace-only lines as content when indenting but blank
  when dedenting (and left short ones untouched); `TemplateFormatter` accepted `{a{b}`; `Guard.Index` passed for a
  negative count; `Soundex` kept an accented first letter ("Ä626"); `PercentEncoding` with '%' in `safe` didn't
  round-trip; `TokenizerOptions` accepted null delimiter/quote sets and failed mid-tokenize.
- **Doc-only:** `CommandLineBuilder` output is not safe for `CommandLineToArgvW`/`ProcessStartInfo.Arguments` or
  cmd.exe (trailing backslash, `\"` injection), now stated prominently; `StringSimilarity` examples named
  `LevenshteinDistance.Between` (it's `Distance`) and a method group that doesn't convert.
- **Not changed:** `CommandLineBuilder` gets no Windows-quoting mode (the round-trip with `CommandLineSplitter` is
  its contract); `GlobMatcher` `?` matches one UTF-16 unit (documented); `QueryString.Format` with an unpaired
  surrogate differs by runtime (documented); `TextDiff` is still O(n·m) over the changed middle.
- **Checked build:** the section is clean; the same 42 failures remain elsewhere.
- **Re-queued:** yes (26 bugs, 1 severe).

### Round 12 — 2026-10-25 — Logging, diagnostics, CLI
Reviewers: one for correctness and one for robustness (locked or missing files, unreachable hosts, re-entrant sinks,
hostile cultures, infinities, broken writers). **32 confirmed bugs**: 28 with failing-first regression tests in
`LoggingReviewTests` (28 tests), 4 by inspection; plus 5 doc-only items. No existing tests needed changes.

- **Severe:**
  - `FileSink` rotation threw when another process held the log or a backup open without delete sharing (a log
    viewer, a backup tool), and left the sink with a disposed writer, so every later event threw too. A failed
    rotation now keeps appending to the current file and retries on the next write.
  - `AsyncLogSink` deadlocked when the inner sink logged back into the same sink on the consumer thread (for example
    a first-chance monitor reporting the sink's own failure) and the buffer was full. Such re-entrant events now go
    straight to the inner sink; a second level is dropped and counted.
  - `SyslogSink` over TCP to an unreachable host blocked the logging thread for about 21 s per event, under the lock.
    The connect is now capped by a new `ConnectTimeout` (default 3 s), and failed connects back off (1 s doubling to
    60 s) and count events as failed without touching the network.
- **Moderate:**
  - `RouterSink`: a throwing route starved every route after it. All routes now run and the failure is rethrown.
  - `RateLimitedSink` keyed on `Level|Category|Message`, so ("x|y", "z") and ("x", "y|z") collided and one was
    swallowed.
  - `ExceptionUtils`: `Describe` indented by visit order rather than tree depth and was quadratic in the number of
    siblings (2000 siblings gave 4 MB); `RootCause` looped forever on a cyclic chain and `Flatten` grew without bound
    on one (by inspection); `IsTransient` called file-not-found, path-too-long and end-of-stream transient.
  - `DiskSpace`: `Preflight` threw `DriveNotFoundException` for a missing drive instead of reporting not writable;
    `ShortfallBytes` overflowed to a negative number; `EstimateBytes` accepted infinite rates; UNC paths failed with
    a cryptic error (and the docs recommended volume GUID paths, which don't work either).
  - `Sparkline` ignored a single explicit bound (the scan overwrote it) and threw `IndexOutOfRangeException` on
    infinities and on ranges that overflow a double.
  - `ConsoleApp` returned 70 instead of 130 when the cancellation surfaced wrapped in an `AggregateException`
    (`Task.Wait`).
  - `ConsoleSpinner.Dispose` threw on a broken writer and then never stopped the animation loop.
- **Minor:** `AsyncLogSink` double dispose disposed the inner sink twice; `StructuredTextFormatter` accepted an
  invalid timestamp format and failed on the first event; `ScopedContext.Push("")` added a stray "/";
  `HealthCheck` accepted timeouts beyond the 24.8-day timer limit; `MetricsRegistry.Record` accepted NaN (poisoning
  every statistic); `Benchmark`, `FirstChanceRecord` and `Prompt` labels used the current culture; `ConsoleTable`
  threw on a null header; `Prompt.Text` accepted whitespace-only input and `Prompt.Integer` a default outside the
  range; `EnvironmentReport` uptime was off by an hour across DST (by inspection); the spinner line wrapped on a
  narrow console and could not be redrawn (by inspection); eight drop-in headers claimed "fully self-contained" but
  needed `Log.cs` and friends (by inspection).
- **Doc-only:** `StructuredTextFormatter` is not quote-safe CSV and its default space delimiter cannot separate
  values with spaces; `RateLimitedSink` summaries are written on the next event or on dispose (no timer);
  `ConsoleApp` hooks Ctrl+C only, not SIGTERM; `Prompt.Choose` reads a number as a position first; `ConsoleTable`
  widths are UTF-16 characters.
- **Not changed:** `AsyncLogSink.Emit` after dispose still throws (pipeline contract); `MetricsRegistry` keeps
  every sample (documented); `Prompt.Choose` precedence.
- **Checked build:** the section is clean; the same 42 failures remain elsewhere.
- **Re-queued:** yes (3 severe).

### Round 13 — 2026-10-25 — Security, net, time
Reviewers: one for correctness and one for robustness (DST transitions, calendar edges, hostile servers, stalled
peers, huge timeouts, malformed stored hashes). **39 confirmed bugs**: 37 with failing-first regression tests in
`SecurityNetTimeReviewTests` (39 failing-first tests plus 2 guards), 2 by inspection; plus 1 doc-only item.
`CronSchedulerTests` now pin the scheduler to UTC, since it reads schedules in a time zone (default Local).

- **Severe:**
  - `CronSchedule`/`CronScheduler` knew nothing of daylight saving: the scheduler read the fields against a fixed
    offset, so "30 2 * * *" drifted an hour after a transition, a time skipped by spring-forward never ran, and a
    time repeated by fall-back ran twice. New `GetNextOccurrence(after, timeZone)` follows Vixie cron rules (a skipped
    time runs at the transition, a repeated time runs once; `*` minute/hour fields run at every matching instant),
    and `CronScheduler` takes an optional `timeZone` (default Local).
  - `HttpDownload` resume appended a 206 body whatever its Content-Range said, so a server answering from another
    offset corrupted the file. The range start must now equal the partial length (a range from 0 restarts).
  - `HttpDownload` wrote straight into the destination, so a failed download destroyed an existing good file. The
    body now goes to `destination.part`, moved over the destination only when complete and verified.
  - `HttpDownload` hung forever on a body that stopped sending (the client's `Timeout` covers headers only). New
    `ReadTimeout` (default 60 s) fails the read with a retryable `TimeoutException`.
- **Moderate:**
  - `CronScheduler.RunDue` threw when a job's schedule had no later occurrence; the job now gets a null `NextRun`
    and the failure goes to `onError`.
  - `CronSchedule`: a day-of-week step from a value ("1/2") also matched Sunday (7); Feb 29 across a non-leap century
    year (2096 → 2104) was beyond the 4-year search (now 8); near `DateTime.MaxValue` it threw
    `ArgumentOutOfRangeException` instead of `InvalidOperationException`; a step near `int.MaxValue` overflowed;
    `*/1` in day-of-month counted as restricted (Vixie treats any field starting with `*` as unrestricted).
  - `HttpDownload`: a 416 for an already complete file failed instead of finishing; a non-206 2xx during resume kept
    the old byte count; an unfollowed redirect was retried; the client's own timeout was not retried.
  - `TcpLineClient`: a read timeout lost the partial line (the `StreamReader` buffer was abandoned); no line length
    limit (a peer without newlines grew memory without bound; new `MaxLineLength`, default 1 Mi characters); timeouts
    over 24.8 days overflowed; port and timeouts were validated only after connecting; `Send` sent embedded CR/LF as
    extra lines.
  - `ConstantTime.Equals(string, string)` compared UTF-8 bytes, so strings differing only in lone surrogates (both
    encoded as U+FFFD) compared equal. It now compares UTF-16 code units.
  - `PasswordHasher`: a stored hash with an absurd iteration count tied up a thread for hours (new `MaxIterations`
    = 10,000,000, enforced by parse and hash); an empty salt or digest was not a `FormatException`.
  - `CidrRange.TryParse` accepted IPv4 shorthand ("10/8" meant 0.0.0.10/8); `Addresses()` on IPv6 threw only when
    enumerated; a /0 block skipped 255.255.255.255 (by inspection).
- **Minor:** `Iso8601Duration` accepted non-ASCII digits, a trailing newline and an empty `T` section;
  `HumanDuration.Format` threw on `TimeSpan.MinValue` and rounded 9,999,999 days minus a tick up to 10,000,000 days;
  `BusinessDays.CountBusinessDays` threw on a range ending at `DateTime.MaxValue`, and `AddBusinessDays(int.MinValue)`
  overflowed; `PortCheck.IsReachable` and `HostInfo.Resolve` rejected timeouts over 24.8 days; `CryptoRandom.Password`
  accepted undefined class bits, and `Token` with a huge length overflowed with the wrong parameter name; four
  security drop-in headers claimed "fully self-contained" but need `ConstantTime`, `CryptoRandom` or `KeyDerivation`
  (by inspection).
- **Doc-only:** `TcpLineClient` with a null read timeout waits indefinitely.
- **Not changed:** `SecretsFile` temp-file permissions on Unix (unconfirmed; Windows-first library).
- **Checked build:** the section is clean; the same 42 failures remain elsewhere (one timing flake in
  `PauseAndRaceTests` under one-core low priority, which passed 3 of 3 reruns).
- **Re-queued:** yes (4 severe).

### Round 14 — 2026-10-25 — Objects, functional, grids, graphs, intervals
Reviewers: one for correctness and one for robustness (deep and cyclic graphs, hostile types, huge inputs).
**44 confirmed bugs**: 42 with failing-first regression tests in `ObjectsGraphsReviewTests` (37 failing-first
tests, several covering more than one bug, plus 4 guards pinning output order and naive equivalence), 1 found by the
checked build (the existing Grids tests fail there), 1 by inspection.

- **Severe** (each crashed the process with a stack overflow):
  - `DeepEquals`, `PropertyDiff`, `FlattenObject` and `UnflattenObject` recursed once per level, so a linked chain
    a few thousand deep (or a 100k-segment key) killed the process. All four now walk with an explicit stack and keep
    their old output order.
  - `DeepEquals` and `PropertyDiff` recursed without end on a list that contains itself: collections were not
    tracked as visited. Every non-leaf reference pair is now tracked.
  - `TopologicalSort`'s cycle finder recursed once per vertex; a 100k-vertex cycle overflowed. It is now iterative.
- **Severe** (checked build): `FloodFill`, `ConnectedComponents2D` and `GridPathfinding` bounds-checked neighbors with
  `(uint)x >= (uint)n`, which throws `OverflowException` for a negative index when the drop-in is compiled with
  checked arithmetic, so every fill, labeling and path search failed there. They now compare explicitly.
- **Moderate:**
  - `RangeMap.Set`/`Remove` rebuilt and re-sorted the whole map on every call (50k sets took over a minute); they
    now binary-search the affected segments. `RangeSet.Except` was quadratic (15 s for 100k intervals); now linear.
  - `DeepEquals`/`PropertyDiff`: infinity and NaN never equalled themselves under a float tolerance; hash sets
    compared by enumeration order; multi-dimensional arrays ignored shape; `BigInteger` and other formattable structs,
    `Type`/`MemberInfo`, and `ReadOnlyMemory<T>` were walked member by member (throwing, or equal when not); value
    tuples and other field-only structs always compared equal.
  - `PropertyPath`: a missing dictionary key was not `KeyNotFoundException`; the key type came from the concrete
    type's generic arguments (wrong for a subclass of `Dictionary<int,…>`); `TryGet` threw on an unconvertible key;
    `Set` through a struct member changed a copy; a `new` property threw `AmbiguousMatchException`.
  - `ExpressionAccessor` matched names case-insensitively and reached non-public members, treated a private setter as
    writable, named the wrong parameter for a missing member, and threw `NullReferenceException` writing null to a
    value-type member (now stores default).
  - `ObjectMapper`: a struct target lost every value (mapped into a copy); null into a value-type property threw; a
    private setter was written.
  - `ActivatorUtils` took the first fitting constructor, not the most specific; could not create a struct with no
    arguments; threw on an open generic; wrapped constructor exceptions in `TargetInvocationException`.
  - `TypeUtils.FriendlyName` reversed jagged array ranks and garbled types nested in generic types; `DefaultValue`
    accepted void and open generics; `GetDerivedTypes` returned open generics (so `ScanAndCreate` reported errors).
  - `TopologicalSort` located the cycle start with the default comparer, not the graph's (an exception for a
    case-insensitive graph); `AttributeCache` ignored `inherit` for properties; `Interval.Equals` used `CompareTo`
    and so disagreed with `GetHashCode`.
- **Minor:** `UnflattenObject` accepted text straight after `]` ("a[0]b"); `Graph.BreadthFirst`/`DepthFirst`
  validated the start vertex only when enumerated; `Graph.AddEdge` accepted a NaN weight; `Memoize` named the null
  argument "key"; seven graph and interval drop-in headers claimed "fully self-contained" (by inspection).
- **Checked build:** the 26 Grids failures are gone (see above); 16 failures remain in other sections, all present
  at v0.13.0, and the `PauseAndRaceTests` timing flake did not recur.
- **Re-queued:** yes (7 severe).

### Round 15 — 2026-10-25 — Windows satellite
Reviewers: one for correctness and one for robustness (DPI virtualization, handle lifetimes, hostile names and
sizes, cross-user and cross-thread behaviour). **35 confirmed bugs**: 20 with failing-first regression tests in
`WindowsReviewTests` (19 tests; the shortcut test covers two bugs), 15 by inspection. Shell-link limits were measured
first: arguments and description round-trip up to 260 characters and come back garbled beyond; the working directory
and icon path come back silently truncated beyond 260; a target of 260 or more fails with `E_FAIL`.

- **Severe:**
  - `FileAssociation.GetProgId` used `ASSOCSTR` 11, which is the info-tip property list
    (`prop:System.ItemTypeText;…`), not the ProgID (20). It never returned a ProgID.
  - `ScreenCapture` in a DPI-unaware process (the .NET default without a manifest) captured the top-left quarter of
    a 200% screen at 1920×1080. Each capture now runs per-monitor-v2 aware, so sizes and coordinates are physical
    pixels, and the thread's awareness is restored afterwards.
  - `ResourceSampler` normalised by `Environment.ProcessorCount`, which is the sampler's own affinity or job limit.
    Pinned to one core, it reported one busy thread on a 12-core machine as 65% instead of about 8%. It now uses
    `GetActiveProcessorCount(ALL_PROCESSOR_GROUPS)`.
  - `JobObject`'s finalizer closed the job handle, so a garbage collection after the object's last use killed every
    kill-on-close child while it was still wanted. The finalizer is gone; an undisposed job stays open until process exit.
  - `RegistryUtils.GetValue<uint>`/`<ulong>` returned the default for any DWORD/QWORD with the high bit set: .NET
    reads them as negative int/long and `Convert` range-checks. They are now reinterpreted bit for bit.
  - `RegistryUtils.GetValue<T>` parsed strings in the current culture: "1.5" read as 15 under de-DE. It now uses the
    invariant culture.
  - `WindowUtils.GetWindowTitle`/`TopLevelWindows` used `GetWindowText`, which sends `WM_GETTEXT` to windows of this
    process and so hung whenever such a window's thread was blocked (a deadlock if it was waiting on the caller).
    They now use `InternalGetWindowText`, which sends no message.
- **Moderate:**
  - `MonitorInfo` reported DPI-virtualized bounds in an unaware process, so they disagreed with `DpiInfo`. It now
    enumerates per-monitor-v2 aware (physical pixels).
  - `SingleInstanceApp`:
    - the gate mutex was session-wide while the pipe was per user, so another account holding the name made every
      launch a secondary with no primary to reach. The mutex name now includes the SID;
    - handlers ran concurrently and out of order (one `Task.Run` per launch). Delivery is now a serial in-order queue;
    - a message the primary would reject (over 1000 arguments, or a 32 KB string) retried until the 5 s timeout. It now
      fails at once;
    - an app id of about 175 characters or more overflowed the pipe-name limit, so every second launch waited 5 s and lost
      its arguments. Ids are now limited to 100 characters;
    - a second `Dispose` threw `ObjectDisposedException`.
  - `SingleInstance.TryAcquire` threw `UnauthorizedAccessException` when the name belonged to an elevated or
    other-user process. It now returns a non-owning instance.
  - `ShortcutUtils`: values longer than the shell stores came back truncated or garbled. `Read` used `MAX_PATH`
    buffers, which also cut a 260-character working directory or icon path. `Create` now rejects values longer than
    the limits, and `Read` uses larger buffers.
  - `EventLogWriter`: `log` and `source` were put into the HKLM path unchecked. `UnregisterSource("Application",
    log: "")` would have deleted the whole Application log key, and a backslash reached nested keys. Both names are
    now validated.
  - `RunElevated.RestartCurrent` under `dotnet app.dll` relaunched the bare `dotnet` host. It now passes the entry
    assembly. A raw argument string had no safe way to carry paths with spaces, so the new
    `RunElevated.JoinArguments` quotes a list by the `CommandLineToArgvW` rules.
  - `ClipboardUtils.GetText` read text until a NUL with no bound, past the end of an unterminated block. It is now
    bounded by `GlobalSize` (by inspection).
- **Minor:**
  - With tests: `ShortcutUtils.Create` with a target of 260 or more characters threw an opaque `COMException`.
    `StorageDeviceInfo.Describe` rejected `\\?\C:\…` and `\\.\C:` paths as network paths.
  - By inspection:
    - `SingleInstanceApp` could resend a delivered message after a failed acknowledgement. It leaked the identity
      and process handles. It disposed the cancellation source while the listener might still be running.
    - `RegistryUtils.DeleteKey` leaked a key handle.
    - `EventLogWriter` had no finalizer, and its truncation could split a surrogate pair.
    - `ClipboardUtils` ignored a failed `GlobalLock` in `SetData` and failed `EmptyClipboard` calls. `GetImage`
      overflowed its `int` size cast.
    - `StorageDeviceInfo` described the host volume for a mount-folder path. A short seek-penalty reply read as an SSD.
    - `DriveAndVolumeInfo.FixedDrives` let one failing drive abort the whole enumeration.
    - `ScreenCapture` computed the buffer size in `int` and called `GetDIBits` with the bitmap still selected.
- **Existing test updated:** `WindowsWaveTests.SingleInstance_NoPrimaryListening_TimesOutQuickly` held the old
  session-wide name. It now holds the per-user one.
- **Checked build:** not applicable. The satellite's arithmetic is Win32 sizes, which this round now range-checks
  explicitly.
- **Re-queued:** yes (7 severe).

### Round 16 — 2026-10-25 — WPF satellite
Reviewers: one for correctness and one for robustness (threading and dispatcher context, re-entrancy from
`CollectionChanged`, hostile property values, XAML-shaped inputs). **21 confirmed bugs**: 20 with failing-first
regression tests in `WpfReviewTests`, 1 by inspection. One finding was rejected: `BindingFailureListener` keeps every
failure, as its doc says, and `Clear` exists.

- **Severe:**
  - `AsyncRelayCommand` awaited with `ConfigureAwait(false)`, so the end-of-run `CanExecuteChanged` was raised on a
    pool thread whenever the task really went asynchronous. A bound `Button` then threw a cross-thread exception
    and stayed disabled. It now resumes on the caller's context.
- **Moderate:**
  - `NumericInputBehavior`:
    - `DecimalPlaces` above 15 made `Math.Round` throw on coercion, focus loss and stepping. Rounding is now capped at
      15 digits (more are still displayed);
    - a `Step` of 0, a negative or `NaN` crashed the Up/Down keys and the wheel. `StepBy` now ignores it.
  - `IncrementalCollection`: a `CollectionChanged` handler that called `ResetAsync` mid-page let the stale page's
    remaining items be appended to the new list. The load now stops as soon as the generation changes.
  - `RegexValidationRule` and `RuleBuilder.Matches`: a match past the one-second timeout threw
    `RegexMatchTimeoutException` out of validation. It now counts as invalid. An invalid `Pattern` threw only on
    the first validation; the setter now rejects it.
  - `EnumToBooleanConverter`: a plain XAML `ConverterParameter=Red` is a string, so it never matched, and
    `ConvertBack` wrote the string back to the enum property. String parameters are now parsed as the enum.
  - `PlotPresenter`: every `Source` change refitted the figure, discarding the user's zoom and pan on each frame of
    a live plot. The doc said fitting happens when the source "first becomes known". Only a first source or one of a
    different size now refits.
- **Minor:**
  - `AsyncRelayCommand`: a throwing `CanExecuteChanged` handler left `IsExecuting` stuck at true.
  - `NumericInputBehavior`:
    - a 400-digit paste was accepted and coerced to "∞";
    - `StepValue("5", -1, 0.5)` with no decimals returned "5": 4.5 rounded back. A sub-unit step now moves one unit;
    - "50000" was accepted for the range [−100, −10] although even "−50000" is out of range.
  - `CountToVisibilityConverter` showed `0L`, `0.0`, `0u` and other non-`int` zero counts as Visible.
  - `FileDropBehavior`: a folder named `x.csv` passed the `.csv` filter, although the doc says folders are excluded.
    `"*.*"` became `".*"`, which matched nothing; `"*.*"` and `"*"` now accept every file.
  - `PlotPresenter`:
    - a non-positive or `NaN` `WheelFactor` crashed `OnMouseWheel`; the setter now rejects it;
    - a huge wheel delta could overflow the power (by inspection);
    - `ZoomAbout` with `minZoom` 0 could produce a scale WPF cannot invert (below about 5e-8, where
      `Matrix.HasInverse` treats the determinant as zero), so panning and `ViewToContent` threw. Such a zoom now
      leaves the view unchanged.
  - `ThemeManager.Apply`:
    - set `Requested` before resolving the dictionary, so a failed apply of an unregistered theme left it wrong;
    - with one dictionary registered for both themes, it never updated `Effective` or raised `ThemeChanged`.
- **Checked build:** not applicable (satellite).
- **Re-queued:** yes (21 bugs, 1 severe).

### Round 17 — 2026-10-25 — WinForms satellite, tooling, docs
Reviewers: one for correctness and one for robustness (hostile property values, disposal and handle lifetime,
multi-monitor placement, script argument handling, generator reproducibility). **24 confirmed bugs**: 15 with
failing-first regression tests in `WinFormsReviewTests`, 9 by repro or inspection (scripts, samples, docs). One finding
was not acted on: no `global.json` pins the SDK. Pinning one would break machines that lack that SDK, and the build is
green on the installed SDK 10.

- **Severe:**
  - `FormStatePersistence.EnsureOnScreen` kept a placement larger than every screen when its caption was reachable.
    Windows then shrank the window about its own origin, so `"-2400,100,4000,900"` on one 1920-wide monitor reopened
    fully off-screen. A placement that does not fit the working areas' bounding box is now relocated and shrunk.
- **Moderate:**
  - `ControlExtensions` ran the delegate inline on a disposed control, i.e. on the worker thread. The synchronous
    helpers now throw `ObjectDisposedException`; `BeginInvokeIfRequired` drops the action, also when the handle dies
    while it posts.
  - `PlotPictureBox`: `WheelFactor`, `MinZoom` and `MaxZoom` accepted 0, negatives and `NaN` (a crash on the next
    wheel event), and `PanBy(NaN)` made painting throw. All are now validated.
  - `WaitCursorScope` restored a per-scope snapshot, so scopes disposed out of order left the wait cursor stuck on.
    Open scopes are now counted.
  - Menu `ICommand` binding: a long-lived command rooted every bound menu item, so a closed but undisposed dialog
    leaked. The command now holds the item weakly.
  - `Shortcut` clashed with `System.Windows.Forms.Shortcut` (CS0104 in any file using both namespaces). Renamed
    `MenuShortcut`.
  - `MenuShortcut` accepted keys a menu item rejects ("A", "Shift+A", "Esc", "Ctrl+LButton"), so the builder threw
    `InvalidEnumArgumentException`. Parsing now follows `ToolStripManager.IsValidShortcut`.
  - `AutoSizeColumns` cap was undone at once by a grid in a continuous auto-size mode. Capped columns now stop
    auto-sizing.
  - `scripts/test.ps1`: PowerShell swallowed `--` and bound the next runner argument to `-Configuration`, so the
    README's `-- --list` built a configuration named `--list` and ran everything. `-v` bound to `-Verbose`. Arguments
    are now parsed by hand.
- **Minor:**
  - `FormState`'s constructor kept `Minimized`, which `Serialize` wrote and `TryParse` rejected.
  - `PlotPictureBox.ZoomAbout` with `minZoom` 0 could underflow to zoom 0; such a zoom now leaves the view unchanged.
    The wheel power is clamped (by inspection). `BitmapFromRgba`'s size check overflowed `int`, and the bitmap
    leaked if filling it threw (by inspection).
  - `MenuShortcut.Format` showed alias names ("Ctrl+Next", "Ctrl+Oem2"); it now writes "PageDown", "/" and so on, and
    parsing reads them back. `TryParse("Ctrl+A,B")` OR-ed the list into Ctrl+C.
  - A throwing `CanExecute` left the command subscribed to an orphaned item.
  - `SaveCsv` truncated the target file before checking `grid` for null.
  - `BindEnum` listed aliased values twice; `SetSelectedEnum` with a value not in the list kept the old selection.
  - `scripts/coverage.ps1` broke on a repo path with spaces; the `gen-*.py` generators wrote relative to the current
    directory; `gen-png-fixtures.py` used `assert` (skipped by `python -O`) and its comment misdescribed the skip.
  - `samples/Benchmarks` boxed results in `GC.KeepAlive`, reporting allocations for allocation-free calls.
  - The README Layout missed `Objects/ObjectMembers.cs` (which three drop-in headers tell you to copy),
    `scripts/coverage.ps1` and five test files.
- **Docs only:** `PlotPictureBox.Image` must be replaced before the old image is disposed; a form maximized then
  minimized is captured as Normal; `ControlExtensions` runs inline before a handle exists, as `InvokeRequired` does.
- **Checked build:** not applicable (satellite and tooling).
- **Re-queued:** yes (24 bugs, 1 severe).

### Round 18 — 2026-10-25 — Numerics core, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 4 with 8 severe).
The robustness lens focused on finite overflow at ±1e308, subnormals, checked arithmetic and round-trip formatting.
**28 confirmed bugs**, each with a failing-first regression test in `NumericsCoreReview2Tests` (one of them is in the
test framework).

- **Severe:**
  - `RootFinding.Newton` returned Converged on any tiny step; a wrong or huge derivative (x − 1 with f' = 1e300)
    "converged" at 5. A tiny step now needs a small residual or a sign change within the tolerance.
  - `LinearAlgebra` measured singularity against the whole matrix's largest entry, rejecting diag(1e20, 1). The
    threshold is now per original row.
  - Cubic `Polynomial.RealRoots` merged roots 1e-5 apart (tolerance far above round-off) and lost or overflowed roots
    at extreme scales. Rewritten: power-of-two scaling, a round-off discriminant tolerance, deflation from the
    largest root.
  - `DeterministicRandom.NextBytes` / wide `Next` threw under checked arithmetic.
  - `Check.Close` (test framework) passed when exactly one side was NaN (`NaN > tol` is false). It hid one wrong
    result among the new tests; no older test was masked.
- **Moderate:** finite-overflow NaN/∞ in `RunningStatistics`, `MovingAverage`, `WeightedAverage`, `ZScore`,
  `Interpolation`, `Vector2.Lerp`, `LookupTable`, `MonotoneCubic`, `AkimaSpline`, `StreamingQuantile`,
  `ScatteredInterpolation.ToGrid` (also ∞·0 = NaN at i = 0), `Integration`, `EmpiricalDistribution.Quantile`, each
  fixed by a scaled or halved fallback taken only on overflow; `RoundToSignificantDigits` and `EngineeringNotation`
  double rounding (now decimal-digit based; `TryParse` rejects overflow); `Differentiation` steps below the double
  spacing and Richardson's nominal step; `Uncertainty` rounding to ∞; `Means.Arithmetic` with a true infinity.
- **Minor:** `UnitConvert` ulp-off exact ratios (now decimal); `Determinant` intermediate overflow; subnormal
  quadratics; `MinMaxScaler` subnormal range; `Vector2.Normalized` of infinite vectors; `LookupTable`/`Cdf` NaN;
  `Histogram` negative index under checked arithmetic; `ZScore` negative sd; `StreamingQuantiles(null)`.
- **Test fixed, not code:** one new test expected "(1.70 ± 0.50)e308"; the PDG rule gives "(1.7 ± 0.5)e308", the same
  as at e8.
- **Checked build:** numerics failures gone (DeterministicRandom ×3, Fraction); 8 remain in other sections (Net ×5,
  Guards, ArrayPoolScope, Palette); nothing new.
- **Re-queued:** yes (28 bugs, 5 severe).

### Round 19 — 2026-10-25 — Charts, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 5 with 8 severe).
**28 confirmed bugs**, each with a failing-first regression test in `ChartsReview2Tests`.

- **Severe:**
  - `AxisTicks.Linear` never finished for subnormal ranges (the step underflowed to 0); spans below 1e-250 now widen
    around their midpoint. Tiny ranges such as [1.6e-19, 1.7e-19] collapsed every tick onto 0: ticks are now parsed
    from their exact decimal digit × 10^exponent.
  - `Histogram.Bin` indexed at int.MinValue for subnormal ranges (NaN quotient), and one bin over ±1e308 got a NaN edge.
  - `TimingDiagram` with a huge `LaneHeight` (1e17) hatched forever; small ones drew negative rects. `LaneHeight` must
    now be in (6, 1e6].
  - `Annotations` thick lines repainted the whole pen square at every step (a 2000-px line on 1000² ran for minutes);
    only the newly covered part is painted now, and widths past int wrapped to a 1-px line.
  - `BandChart` filled one column per sample, leaving gaps; every column between samples is filled now.
- **Moderate:** `AxisTicks.FormatTime` rounding carry ("1:051.000") and long wrap past 9.2e18 s; `AxisTicks.Log10` at
  the double limits; `BoxPlot` percentile NaN between infinities; `TimingDiagram` lone change at epoch-nanosecond
  timestamps (0/0 x) and null signals; `Waterfall` tick counts uncapped and an explicit `ValueMin` past the data
  swapped into the maximum; `HexBin` likewise; `BandChart`/`ErrorBarChart` infinite explicit bounds and ±1e308 spans;
  `HeatMap` one limit past the data and the clamped huge span; `ScatterMatrix` Pearson r at 1e-90 / 1e200 scales;
  `LinePlot.Render` mapped differently from the `Frame` it returns (reversed and ±1e308 ranges); `SvgChart` lone
  point between gaps not drawn, and clipping NaN when a segment's span overflows; `Colormap` NaN for stops ±1e308
  apart and an unstable sort reordering hard edges; `MultiPanel.Compose` size wrap and an infinite `Gap`.
- **Minor:** `LinePlot` null series threw NullReferenceException.
- **Declined:** a bin-count memory cap, `Series` keeping list references, Margin/FontFamily validation, ScatterMatrix
  null Group (already an ArgumentNullException), a Log10 tick-count limit.
- **Checked build:** the same 8 failures as round 18 (Net ×5, Guards, ArrayPoolScope, Palette); nothing new.
- **Re-queued:** yes (28 bugs, 5 severe).

### Round 20 — 2026-10-25 — Objects, functional, grids, graphs, intervals, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 14 with 7 severe).
**19 confirmed bugs**, each with a failing-first regression test in `ObjectsGraphsReview2Tests`.

- **Severe:**
  - `PropertyDiff` re-walked shared subgraphs on every path: equal diamond chains 40 deep never finished. Pairs found
    equal are now remembered; a differing shared node is still reported on each path.
  - `FileInfo` / `DirectoryInfo` hung `DeepEquals`, `PropertyDiff` and `Flatten` (Root/Parent/Directory return a new
    object each call, so the cycle check never fired). They are leaves now, compared by type and full path.
  - `DeepEquals` / `PropertyDiff` called any two `Quaternion`s or `Matrix4x4`s equal (only properties were read).
    A struct's public fields are now always compared.
  - `TypeUtils.TryConvert` to an enum from double/float reinterpreted the bits (1.0 became Sunday) and accepted
    bool/char; only integers and whole, in-range floating values convert now.
- **Moderate / minor:** `TryConvert` parsed "1,5" as 15 for floating targets and threw for `void`/open generics;
  `ActivatorUtils` "created" `int?` with no arguments (null), invoked by-ref-like/pointer types, and preferred an
  `object` parameter over an interface or `Nullable` one; `ExpressionAccessor` missed base-interface members, wrote a
  struct setter into a copy of the box, and threw NullReferenceException for a typed null into a value member;
  `ObjectMapper` read the hidden base property instead of the `new` one; `PropertyPath` array index out of range
  escaped `TryGet` as IndexOutOfRangeException; `ShortestPath.Dijkstra` reported +∞-weight targets as found and missed
  negative edges the search never reached; `FloodFill` and `GridPathfinding` allocated per-cell arrays for the whole
  grid (10+ GB for a short path on a huge predicate grid); `default(Result<T>).ToString()` printed "Failure()".
- **Behaviour change:** `PropertyDiff` on a value tuple now reports each differing field (`Item1`, `Item2`) rather than
  one difference for the whole tuple.
- **Declined / doc-only:** static reflection caches pinning collectible assemblies; `Regex` / `StringBuilder` comparing
  equal by their public properties (by contract); grid `Dijkstra` negative cost (doc now says it throws when reached).
- **Checked build:** the same 8 failures as round 19 (Net ×5, Guards, ArrayPoolScope, Palette); nothing new.
- **Re-queued:** yes (19 bugs, 4 severe).

### Round 21 — 2026-10-25 — Concurrency, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 10 with 7 severe).
**25 confirmed bugs**, each with a failing-first regression test in `ConcurrencyReview2Tests` except where noted.

- **Severe:** `RetryPolicy.DelayForAttempt` with a zero base and exponential backoff returned the cap from attempt 1025
  on (0 · 2^1024 is NaN), or waited forever with an infinite cap. A zero base now stays zero.
- **Moderate / minor:**
  - `Retry` delays were rounded to whole milliseconds on .NET Framework (now built from ticks; by inspection).
  - `CircuitBreaker`: a call started before the circuit opened could reopen it after it had re-closed (an open
    generation is now tracked); a clock stepping back kept it open for the length of the jump.
  - `Throttle` and `TokenBucketRateLimiter`: a clock stepping back refused or stopped refilling for the length of the
    jump; `Throttle.TimeUntilNext` overflowed with a `MaxValue` interval; `TokenBucket.TryAcquire` with a count above
    capacity starved forever (it now throws).
  - `TaskExtensions.WhenAllOrFirstException` was quadratic (40000 tasks took 12 s).
  - `TaskRace.Hedged` restarted the hedge timer on every failure, and rejected `Timeout.InfiniteTimeSpan` (and threw for
    delays over 24.8 days); those now never hedge.
  - `ProcessRunner.RunAsync` started the child for an already-cancelled token; an exception capturing output (2 GB
    StringBuilder limit) escaped on the reader thread (by inspection).
  - `WhichExe` skipped quoted PATH entries on Windows, and threw for a path `GetFullPath` rejects.
  - `ParallelUtils.ForEachAsync` kept every started task alive until the end (25 MB for 300000 items).
  - `ConcurrentPipeline`: DropOldest added a wake-up per eviction (100000 drops left 99999 pending); `Dispose` from
    `onError` deadlocked.
  - `PeriodicWorker`: `Dispose` from `onError` deadlocked; a `Start` racing `Dispose` could run the loop on a disposed
    token source (by inspection).
  - `AsyncLazy`: a factory throwing synchronously threw from `Value` instead of caching a faulted task.
  - `AsyncEvent.InvokeSequentialAsync`: a cancellation discarded earlier handlers' failures.
  - `AsyncLock` / `KeyedLock` releasers: two threads disposing at once both released (by inspection).
  - `ArrayPoolScope` indexer overflowed under checked arithmetic (covered by the checked build).
- **Declined / doc-only:** an `EnqueueAsync` for the Block policy (feature request); process output is captured as text
  lines, not bytes (documented); `WhichExe` doesn't check the Unix exec bit or limit matches to PATHEXT; pipeline
  `TryEnqueue` doc now says DropOldest can evict an accepted item, and the misplaced `Dispose` doc is back on `Dispose`.
- **Checked build:** 7 failures (Net ×5, Guards, Palette); `ArrayPoolScope` is fixed; nothing new.
- **Re-queued:** yes (25 bugs, 1 severe).

### Round 22 — 2026-10-25 — Windows satellite, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 15 with 7 severe).
**26 confirmed bugs**, each with a failing-first regression test in `WindowsReview2Tests` except where noted.

- **Severe:** `SingleInstance` held a thread-owned mutex. When the acquiring thread exited, the mutex was abandoned and
  the next launch became a second primary. The same thread asking twice also owned the gate twice. The gate is now held
  by a dedicated background thread, and `Dispose` works from any thread.
- **Moderate / minor:**
  - `SingleInstance`: a name taken by another kind of kernel object threw instead of reporting the gate as held.
  - `SingleInstanceApp`:
    - An infinite forward timeout made one 1 ms attempt, and `MaxValue` overflowed. Both now wait without limit.
    - A stalled primary stretched the timeout by up to 5 s.
    - A launch whose primary exited while it waited was lost; it now becomes the primary.
    - Launches past the 100-launch pre-subscription queue were acknowledged, then dropped; they are now refused.
    - Handlers ran after `Dispose` (by inspection).
  - `RegistryUtils`:
    - A high-bit DWORD/QWORD failed or went negative for wider and floating-point targets.
    - Nullable and enum targets always returned the default.
    - An unreadable key threw `SecurityException`; `KeyExists` now returns true and reads return the default.
  - `DpiInfo`:
    - `Scale` overflowed in int arithmetic.
    - `Monitors` added a 0×0 entry for a monitor whose info query failed (by inspection).
  - `Elevation` read the thread token while impersonating. It now reads the process token through the
    `GetCurrentProcessToken` pseudo-handle, and Administrators membership comes from the token groups.
  - `SetThreadDpiAwarenessContext` is now guarded before Windows 10 1607 in `MonitorInfo`, `ScreenCapture` and
    `DpiInfo` (by inspection).
  - `StorageDeviceInfo`:
    - `\\?\Volume{…}\` was rejected as a network path.
    - `\\.\UNC\` was not seen as a network path.
    - A mapped network drive reached the device query (by inspection).
    - Descriptors over 1 KB were truncated (by inspection).
  - `EventLogWriter` (by inspection):
    - The finalizer could close the handle mid-write.
    - The message DLL path assumed 64-bit Windows.
  - `ClipboardUtils` (by inspection):
    - A failed multi-format set left partial data.
    - `RegisterClipboardFormat` failure went unchecked.
    - A huge 24-bit DIB overflowed while being decoded.
  - `IdleTime`: a last-input tick ahead of the current tick read as about 49.7 days idle (by inspection).
  - `JobObject`: limits set from two threads at once could lose one (by inspection). A 32-bit limit over 4 GiB now
    throws `ArgumentOutOfRangeException`.
- **Declined / doc-only:**
  - Forwarding between elevated and non-elevated launches of one user can fail, because the pipe accepts only the
    same owner (documented).
  - `AwarenessScope` must be disposed on its own thread (documented).
  - A clipboard read can hang on a hung delayed-rendering owner.
  - Registering a source that already exists under another log.
  - `DeleteValue` throws on access denied.
- **Checked build:** not run (satellite).
- **Re-queued:** yes (26 bugs, 1 severe).

### Round 23 — 2026-10-25 — Statistics, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 3 with 7 severe).
**22 confirmed bugs**, each with a failing-first regression test in `StatisticsReview2Tests`. None severe.

- **Scale invariance** (data near 1e±170 squared to zero or infinity): the one-sample and two-sample t-tests (Welch df
  went NaN), `Correlation.Pearson`, `LinearRegression.Fit`, `ChangePoint.DetectShift`, `Anova.OneWay` and
  `GaugeRR.Analyze` now work relative to a power-of-two scale of the data, so normal-range results keep their bits.
- **Distributions:**
  - `ChiSquareQuantile` and `FQuantile` failed to converge far in the lower tail (p = 1e-300). Newton now jumps down
    when it creeps on a steep power law, and both start from the lower tail's leading term.
  - ln Γ lost ~1e-5 of its value near its zeros at 1 and 2 (lnΓ(1 + 1e-12) wrong in the 4th digit). A ζ-series now
    covers [0.8, 1.2] and [1.8, 2.2] in all four synced copies.
  - The regularized upper incomplete gamma for a tiny shape kept few digits (1 − P); it is now formed directly.
- **DistributionFit:** `FitGamma` refused a sample with a value far below the mean as constant; `FitWeibull` failed
  for a sample spanning more than the double range. Its private ln Γ is resynced with the shared copy.
- **ConfidenceInterval.ForProportion:** overflowed past ~1.07 billion trials; bounds strayed just outside [0, 1].
- **Anova.OneWay:** NaN and ∞ now throw `ArgumentException` (they gave F = NaN).
- **MultipleRegression:** a constant response read as R² = 0 / adjusted NaN; a null first row threw
  `NullReferenceException`.
- **RobustRegression.TheilSen:** more than 65,536 points overflowed the slope list; now refused up front.
- **PermutationTest:** paired differences that overflow now throw; `MeanDifference` no longer overflows to NaN.
- **Bootstrap / EmpiricalDistribution quantiles:** a constant statistic gave 0.30000000000000004 for 0.3; −∞ to +∞
  interpolated to NaN.
- **Declined:** `ConfidenceInterval.ForMean` at 1e308 and `ChiSquareIndependence` on 1e200 counts (inherent
  overflow); coefficient-array allocations.
- **Checked build:** same 7 baseline failures (Net ×5, Guards, Palette), none new.
- **Re-queued:** yes (22 bugs).

### Round 24 — 2026-10-25 — IO, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 8 with 6 severe).
**22 confirmed bugs**, each with a failing-first regression test in `IOReview2Tests`. Two severe.

- **Severe — `DirectoryUtils.Copy` into the source through a junction** (`j → src`, copy `src` to `j\c`) nested
  forever. The check now resolves links on both paths (net6+), and the source's folders are listed before anything is
  written, so any route that slips past copies once instead of looping.
- **Severe — writing through hard links:** tar, zip and `Copy` with overwrite wrote into an existing hard link at the
  target, changing the file's other name, possibly outside the destination. Tar and zip now write a temp file beside
  the target and move it into place; `Copy` deletes the target first.
- **Archives:**
  - A truncated tar overwrote the existing file with a short one (the reader ends the data early without error);
    the length is now checked before the move, and the temp file is removed.
  - Tar extracted a pax global header as a file; non-file entry types are now skipped (and left out of listings).
  - A tar entry resolving to the destination itself ("." or "...") is now `InvalidDataException`.
  - Names with ':' (an alternate data stream on Windows) or NUL are refused by both.
  - `ZipUtils.ReadEntryBytes` pre-allocated the declared size (a forged header asked for gigabytes).
  - Created archives kept '\' in names on Windows; separators are now '/'.
- **FileWatcher:** a filtered watcher missed directory moves (the OS filter dropped them); filtering is now done in
  the watcher with DOS wildcard rules. A rename onto a pending rename lost the earlier source's delete. No error
  event after dispose.
- **Streams/text:** `TeeStream.Dispose` leaked the primary when the copy's flush threw; `LineReader` offsets were
  relative to where reading started; `DotEnv` dropped the backslash of an unknown escape (`C:\Users` read `C:Users`);
  `CsvLine` accepted a quote or line break as the delimiter; `CsvBinder` could not bind a struct and formatted floats
  without round-trip precision.
- **Files:** `ByteSize.Parse` lost precision on large byte counts (double); `FileRetention` `*.*` skipped extension-less
  files, and `ShiftNumbered(keep: int.MaxValue)` probed every number; `TempFile`/`TempDirectory` left read-only
  contents behind; `JsonFile.Save` dropped the target's Hidden attribute; `DeleteTree` threw when a folder vanished.
- **Checked build:** caught `keep + 1` overflowing in `ShiftNumbered`'s cleanup (fixed); otherwise the same 7 baseline
  failures (Net ×5, Guards, Palette).
- **Re-queued:** yes (22 bugs, 2 severe).

### Round 25 — 2026-10-25 — Collections, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 9 with 6 severe).
**11 confirmed bugs**, 9 with a failing-first regression test in `CollectionsReview2Tests`; the two huge-capacity
overflows need multi-gigabyte arrays and were confirmed by the reviewers' runs and by inspection. One severe.

- **Severe — `ObjectPool.Return` race:** two concurrent returns of one instance both passed the "already pooled?"
  check while resetting, so it was pooled twice and two renters got it. An instance being reset now counts as pooled.
- **TopN.Add:** a comparer throwing mid-sift dropped a retained item (the slot was not yet counted).
- **BloomFilter / CountingBloomFilter:** a NaN false-positive rate built a 1-bit filter that matched everything.
- **CountMinSketch:** counts and `TotalCount` wrapped negative; they now saturate at `long.MaxValue`.
- **Aggregation.Summarize:** finite values whose sum overflows gave an infinite mean and deviation; recomputed on
  power-of-two-scaled values in that case only.
- **TtlCache.Set:** a clock with a positive offset threw near the end of the calendar instead of saturating.
- **Deque / CircularBuffer:** a change between `GetEnumerator()` and the first `MoveNext` went undetected; the
  buffer's index arithmetic overflowed past a capacity of 2^30; the deque's growth overflowed past 2^30 elements.
- **Also:** `ConsoleHelpersTests.Spinner_AnimatesInTheBackground` was flaky on one throttled core (fixed 150 ms
  sleep); it now polls.
- **Unconfirmed:** `ReservoirSampler` skip precision after ~1e16 items.
- **Checked build:** same 7 baseline failures, none new.
- **Re-queued:** yes (11 bugs, 1 severe).

### Round 26 — 2026-10-25 — Documents & config, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 7 with 5 severe).
**20 confirmed bugs**, each with a failing-first regression test in `DocumentsConfigReview2Tests` (22 tests). Six severe.

- **Severe — `ConfigBinder` decimal comma:** `double`/`float`/`decimal` went through `Convert.ChangeType`, which
  allows thousands separators, so "0,5" bound as 5 and "2,50" as 250. Floating point now parses with
  `NumberStyles.Float` (invariant); a comma is a binding error.
- **Severe — `ConfigBinder` get-only list:** `List<string> Hosts { get; } = new()` was skipped and its items dropped.
  It is now filled in place; a read-only property with nothing fillable is an error.
- **Severe — `ConfigBinder` unsupported types:** configured values for `Uri`, structs, dictionaries and lists of
  objects were silently ignored (a dictionary or `List<Endpoint>` was even created empty). Now an error naming the key.
  Also: a throwing setter, getter or constructor is a binding error for its key instead of a raw `TargetInvocationException`.
- **Severe — `SettingsStore.LoadFrom`:** a value the type rejects (a validating setter that throws) escaped, so the
  app crashed on every launch. Any non-fatal deserialize failure now moves the file aside and uses defaults.
- **Severe — `PdfWriter.Table`:** a row taller than the space under a repeated header was clipped, and a header
  taller than a page swallowed every row. Rows now continue across pages; the header repeats only if a line still fits.
- **Severe — `ReportTemplates.Comparison`:** with `flagPercent: 0` ("any change") a metric changing from a baseline of
  0 was never flagged, and the verdict said nothing changed. (At a positive threshold it stays unflagged, as before.)
- **PdfWriter:** five WinAnsi glyphs (macron, ±, µ, ·, ÷) had Arial/Times New Roman widths, not Adobe's, so text
  overran the margin; huge column weights (1e308) wrote NaN into the content; unpaired surrogates went into
  UTF-16 metadata strings (now U+FFFD); `Code` split a surrogate pair across lines.
- **ReportBuilder (Markdown):** the slug dropped combining marks, other numbers and line breaks (TOC links to a
  multi-line heading were dead, and the TOC entry itself broke across lines); metadata values, table and figure
  captions and image alt text were not made single-line; lines indented four spaces or a tab became code blocks;
  the table headers array was kept by reference.
- **MarkdownReport:** a lone CR ended a quote; image and link destinations with spaces or unbalanced parentheses were
  written raw (now in angle brackets).
- **DocxWriter:** a lone CR was written raw instead of as `<w:br/>`; `default(DocxRun)` threw `NullReferenceException`.
- **EnvironmentVariables.GetList(name, null)** threw `NullReferenceException`.
- **Also:** `ConcurrencyReview2Tests.Hedged_AFailureDoesNotRestartTheHedgeTimer` flaked on one throttled core (300 ms
  slack on an absolute time); it now measures from the second start with ~600 ms between right and wrong.
- **Not fixed:** a heading with nothing GitHub keeps still gets the id "section" (an HTML id can't be empty);
  `PdfWriter.Heading(size: 700)` leaves blank pages (pathological).
- **Checked build:** same 7 baseline failures; the hedge flake above was the only new one.
- **Re-queued:** yes (20 bugs, 6 severe).

### Round 27 — 2026-10-25 — Security, net, time, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 13 with 4 severe).
**17 confirmed bugs** (four found by both reviewers), each with a failing-first regression test in
`SecurityNetTimeReview2Tests` (20 tests). None rated severe.

- **CronSchedule (zone overload):** a search starting in the last minute before a spring-forward transition began
  at the transition itself, so the make-up run for a skipped time was missed ("30 2 * * *" after 01:59:30 EST gave
  the next day's 02:30). The gap is now checked at the start too.
- **CronScheduler:** one job with no occurrence (Feb 30) made `Start` throw and no job ran (it now gets a null
  `NextRun` and goes to `onError`); an `onError` that threw escaped `RunDue` and silently stopped the loop (now
  dropped); `Dispose` from `onError` called by the loop deadlocked waiting for itself.
- **CidrRange:** "010.0.0.0/8" was read as octal (8.0.0.0/8); a leading zero is now rejected. `default(CidrRange)`
  threw `NullReferenceException` (now `InvalidOperationException`; `ToString` is empty).
- **IpUtils.IsPrivate:** an IPv4-mapped address (`::ffff:10.0.0.1`, as dual-mode sockets report peers) was not
  private, while `IsLoopback` already unwrapped it.
- **TcpLineClient:** an encoding with a preamble (`Encoding.UTF8`) sent a byte order mark before the first line, and
  a peer's BOM came back in the first line; an overlong line threw away the buffered lines after it, or returned its
  own tail as the next line. Now only that line is dropped.
- **HttpDownload:** a `ReadTimeout` over ~24.8/49.7 days threw from `CancelAfter` mid-download (now waits forever);
  a negative `RetryBaseDelay` hung forever (-1 ms) or replaced the real error (now rejected up front; the product is
  capped); the idle timer also ran through the disk write and `OnProgress`, so a slow callback looked like a
  stalled server; a 206 without Content-Range was appended blindly (now an error, partial deleted).
- **PasswordHasher:** a forged stored digest of any length set how many bytes PBKDF2 derived, bypassing the
  `MaxIterations` work cap; the digest must now be 32 bytes.
- **KeyDerivation:** `keyLength: int.MaxValue` threw `OverflowException`, and an undefined `KdfHash` ran as SHA-256;
  both are now `ArgumentOutOfRangeException`.
- **HumanDuration.Format:** a non-zero value under a millisecond gave "" or "-", which `Parse` rejects (now "0ms").
- **SecretsFile.Set:** lone surrogates were silently replaced by U+FFFD when saved (values changed, keys merged);
  now rejected.
- **Not fixed:** `Iso8601Duration` rejects comma decimals and fractions on non-second components (documented
  subset); a backward clock jump does not recompute `NextRun` (covered by the clock-change wording).
- **Checked build:** same 7 baseline failures, none new.
- **Re-queued:** yes (17 bugs).

### Round 28 — 2026-10-25 — Binary formats & images, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 6 with 3 severe).
**10 confirmed bugs** (one found by both reviewers), each with a failing-first regression test in
`BinaryImagesReview2Tests` (11 tests), except the four marked "by inspection", which need inputs of hundreds of MB.
None rated severe. The decoders survived 200k fuzzed PNGs, 200k BMPs and 300k random strings with only the
documented exceptions.

- **ImageBuffer.DrawLine:** an off-canvas endpoint cut the segment to the canvas and redrew between the rounded
  ends, so pixels were lost or shifted (23% of random lines differed from the same line on a bigger canvas).
  The on-canvas part of the exact Bresenham line is now plotted, column by column (or row by row).
- **ImageBuffer:** the size check used int.MaxValue rather than the largest array, so just under it gave
  `OutOfMemoryException`; `Blit` near int.MaxValue and `DrawLineClipped` with a huge rectangle overflowed in a
  checked build.
- **Bmp.Decode** (by inspection): a 32768 × 16385 image passed the size check and failed in `ImageBuffer` with
  `ArgumentOutOfRangeException` instead of `InvalidDataException`.
- **SvgUtils / SvgDocument escaping:** tab, LF and CR were written raw, which parsers turn into spaces in an
  attribute and CR into LF in text; they are now character references.
- **SvgDocument:** a negative width, height, corner radius, stroke width or font size was written out (SVG calls
  it an error) and a NaN corner radius was dropped; now `ArgumentOutOfRangeException`, before anything is written.
- **QuotedPrintable.Decode:** a run of blanks was rescanned for each blank (quadratic: seconds at 80k); a final
  '=' threw while "= " was a soft break (RFC 2045, `quopri`); `QuotedPrintableTests` pinned the old throw and now
  uses "abc=4".
- **Base32 / CrockfordBase32 / Base85** (by inspection): capacity sums overflowed int for valid inputs of a few
  hundred MB; computed in long.
- **Luhn** (by inspection): the int sum overflowed past ~240M digits and gave a wrong answer; kept mod 10.
- **Documented:** `Base58.Encode` is quadratic like `Decode`; for keys, hashes and IDs.
- **Not fixed:** non-canonical trailing bits in Base32/Base64url/Crockford and over-long zero VarInts are accepted
  (RFC "MAY"); PngReader accepts out-of-order PLTE/tRNS and a repeated IHDR; SvgDocument still leaves a half-written
  element when a NaN coordinate throws mid-shape.
- **Checked build:** same 7 baseline failures, none new.
- **Re-queued:** yes (10 bugs).

### Round 29 — 2026-10-25 — Logging, diagnostics, CLI, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 12 with 3 severe).
**17 confirmed bugs** (two found by both reviewers), each with a failing-first regression test in
`LoggingReview2Tests` (17 tests, 16 failing on the old code; the other guards pruning), except the three marked
"by inspection". One rated severe.

- **AsyncLogSink (severe):** two blocking async sinks that log into each other (a cycle, or a chain whose inner
  sink reports back) deadlocked: each consumer waited for space in the other's full queue. An event emitted into a
  blocking async sink from another async sink's consumer is now written straight to the inner sink.
- **ExceptionUtils.Describe:** the first stack frame came out as "at at" and the others weren't indented under
  their exception; a `Data` value (or message) whose `ToString` threw took the report down (now a placeholder);
  an indent per level made a 5000-deep chain 25M characters (capped at 32 levels).
- **MetricsRegistry:** rounding put the mean of three 0.1s above Max and P95 of three equal samples below Min
  (both clamped); two `double.MaxValue` samples gave an infinite mean; infinity is now rejected like NaN.
  **Benchmark** (by inspection): the same mean and percentile formulas, clamped the same way.
- **FirstChanceMonitor:** the re-entrancy guard was one static flag, so every other monitor was blind to
  exceptions inside one monitor's reporter; it is now per monitor.
- **SyslogSink:** a non-ASCII message was sent without the BOM RFC 5424 requires for UTF-8 (`Tcp_UsesOctetCountingFraming`
  pinned the old bytes and now expects the BOM); a facility over 23 produced another facility's PRI or an invalid
  header (now `ArgumentOutOfRangeException`); UDP resolved the host on every event (by inspection; now once a minute,
  with the TCP back-off after a failed lookup).
- **AnsiStyle:** a short reset `ESC[m` inside styled text ended the outer style; it is re-applied like `ESC[0m`.
- **FileSink:** rotation probed every backup index up to `maxFiles`, so `maxFiles: int.MaxValue` hung; it now shifts
  only the existing run of backups.
- **RateLimitedSink:** a summary whose write threw lost the event that triggered it (and was retried each event);
  the suppressed count was an int that wrapped (by inspection; now long).
- **RouterSink:** `AddRoute` from inside a route's sink threw "Collection was modified"; routes are copy-on-write.
- **ConsoleApp.RunAsync:** a broken stderr turned the exit code into an `IOException`.
- **ConsoleSpinner:** an interval over int.MaxValue ms faulted the animation loop; now rejected.
- **Not fixed:** spinner width counts UTF-16 units; U+2028 passes through the formatters; `ProgressBar` rounding;
  `Data` formatted in the current culture; undefined `AnsiColor` values; a throwing exception `ToString` in the
  formatters and `SyslogSink.Emit`; CR/LF injection in the Plain/Compact formats (by design); `AsyncLogSink.Dispose`
  has no drain timeout.
- **Checked build:** same 7 baseline failures, none new.
- **Re-queued:** yes (17 bugs, 1 severe).
