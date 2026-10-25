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
| 8  | IO | `IO/*` (tar, directory copy, FileWatcher, atomic writes…) | 2,950 | ~3 | 0 | – / 0 | **next** |
| 9  | Collections | `Collections/*` | 3,400 | ~1 | 0 | – / 0 | queued |
| 10 | Concurrency | `Threading/*`, `Resilience/*`, `Process/*`, `Runtime/*` | 2,300 | ~2 | 0 | – / 0 | queued |
| 11 | Text & small types | `Text/*`, `Identifiers/*`, `Enums/*`, `Guards/*` | 2,900 | ~2 | 0 | – / 0 | queued |
| 12 | Logging, diagnostics, CLI | `Logging/*`, `Diagnostics/*`, `Cli/*` | 2,900 | ~2 | 0 | – / 0 | queued |
| 13 | Security, net, time | `Security/*`, `Net/*`, `Time/*` | 2,250 | ~3 | 0 | – / 0 | queued |
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
