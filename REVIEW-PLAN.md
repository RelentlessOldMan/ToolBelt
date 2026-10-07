# Independent review plan

A full-codebase review, done one section at a time. Each round: independently review **one** section → verify and
fix the bugs → cut a release → pause (compact). This file is the tracker; it is updated at the end of every round.

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
   - the README updated if the public API changed.
4. **Release.** Bump the version, commit (`Review round N: <section>`), push, tag `v0.N.0`, and create a GitHub
   release (`gh release create`) whose notes list the fixes.
5. **Record.** Update the tracker table and the round log below, then pause for compaction.

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
| 2  | Signal analysis | rest of `Signal/*` (FFT, Welch, multitaper, Hilbert, resample, pulse/cycle measurements, unwrap…) | 1,850 | ~2 | 0 | – / 0 | **next** |
| 3  | Statistics | `Numerics/` Distributions, SpecialFunctions, HypothesisTests, Anova, Multiple/LinearRegression, DistributionFit, PermutationTest, Bootstrap, ConfidenceInterval, Correlation, ChangePoint, Trend; `Quality/*` | 3,300 | ~10 | 0 | – / 0 | queued |
| 4  | Numerics core | the rest of `Numerics/*` (interpolation, root finding, integration, linear algebra, polynomial, streaming stats, random, fractions, units…) | 2,950 | ~3 | 0 | – / 0 | queued |
| 5  | Charts | `Visualization/` charts: SvgChart, Annotations, Waterfall, TimingDiagram, HexBin, MultiPanel, AxisTicks, ScatterMatrix, BoxPlot, LinePlot, PlotFrame, Histogram, BandChart, ErrorBarChart, HeatMap, Colorbar, Colormap, Palettes | 3,700 | ~3 | 0 | – / 0 | queued |
| 6  | Binary formats & images | `Binary/*`, `Visualization/` PngReader, PngWriter, Bmp, ImageBuffer, SvgDocument, SvgUtils | 2,300 | ~3 | 0 | – / 0 | queued |
| 7  | Documents & config | `Documents/*` (PDF, DOCX, report templates), `Configuration/*` | 2,900 | ~6 | 0 | – / 0 | queued |
| 8  | IO | `IO/*` (tar, directory copy, FileWatcher, atomic writes…) | 2,950 | ~3 | 0 | – / 0 | queued |
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
