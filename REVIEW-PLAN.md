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
| 3  | Statistics | `Numerics/` Distributions, SpecialFunctions, HypothesisTests, Anova, Multiple/LinearRegression, DistributionFit, PermutationTest, Bootstrap, ConfidenceInterval, Correlation, ChangePoint, Trend; `Quality/*` | 3,300 | ~10 | 1 | 31 / 31 | re-queue (7 severe) |
| 4  | Numerics core | the rest of `Numerics/*` (interpolation, root finding, integration, linear algebra, polynomial, streaming stats, random, fractions, units…) | 2,950 | ~3 | 1 | 43 / 43 | re-queue (8 severe) |
| 5  | Charts | `Visualization/` charts: SvgChart, Annotations, Waterfall, TimingDiagram, HexBin, MultiPanel, AxisTicks, ScatterMatrix, BoxPlot, LinePlot, PlotFrame, Histogram, BandChart, ErrorBarChart, HeatMap, Colorbar, Colormap, Palettes | 3,700 | ~3 | 1 | 33 / 33 | re-queue (8 severe) |
| 6  | Binary formats & images | `Binary/*`, `Visualization/` PngReader, PngWriter, Bmp, ImageBuffer, SvgDocument, SvgUtils | 2,300 | ~3 | 1 | 19 / 19 | re-queue (3 severe) |
| 7  | Documents & config | `Documents/*` (PDF, DOCX, report templates), `Configuration/*` | 2,900 | ~6 | 1 | 27 / 27 | re-queue (5 severe) |
| 8  | IO | `IO/*` (tar, directory copy, FileWatcher, atomic writes…) | 2,950 | ~3 | 1 | 45 / 45 | re-queue (6 severe) |
| 9  | Collections | `Collections/*` | 3,400 | ~1 | 1 | 28 / 28 | re-queue (6 severe) |
| 10 | Concurrency | `Threading/*`, `Resilience/*`, `Process/*`, `Runtime/*` | 2,300 | ~2 | 1 | 40 / 40 | re-queue (7 severe) |
| 11 | Text & small types | `Text/*`, `Identifiers/*`, `Enums/*`, `Guards/*` | 2,900 | ~2 | 1 | 26 / 26 | re-queue (1 severe) |
| 12 | Logging, diagnostics, CLI | `Logging/*`, `Diagnostics/*`, `Cli/*` | 2,900 | ~2 | 1 | 32 / 32 | re-queue (3 severe) |
| 13 | Security, net, time | `Security/*`, `Net/*`, `Time/*` | 2,250 | ~3 | 0 | – / 0 | **next** |
| 14 | Objects, functional, grids, graphs, intervals | `Objects/*`, `Functional/*`, `Grids/*`, `Graphs/*`, `Intervals/*` | 3,200 | ~1 | 0 | – / 0 | queued |
| 15 | Windows satellite | `src/ToolBelt.Windows/*` | 3,100 | ~3 | 0 | – / 0 | queued |
| 16 | WPF satellite | `src/ToolBelt.Wpf/*` | 2,600 | ~5 | 0 | – / 0 | queued |
| 17 | WinForms + tooling + docs | `src/ToolBelt.WinForms/*`, `samples/*`, `scripts/*` (reference generators), README accuracy, packaging/build | 2,300 | ~2 | 0 | – / 0 | queued |

After all 17: re-run the sections flagged by the re-review rule, worst first.

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
