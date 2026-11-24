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
4. **Release.** Bump the version (`scripts/bump-version.ps1`), commit (`Review round N: <section>`), push, tag `v<version>`, and create a GitHub
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
| 1  | Filters & control | `Signal/IirFilter`, `Signal/FirFilter`, `Control/*` (Waves 87/88/88b, never reviewed) | 3,400 | — | 13 | 2 / 127 | clean (2 bugs, none severe) |
| 2  | Signal analysis | rest of `Signal/*` (FFT, Welch, multitaper, Hilbert, resample, pulse/cycle measurements, unwrap…) | 1,850 | ~2 | 13 | 5 / 175 | re-queue (5 bugs, none severe) |
| 3  | Statistics | `Numerics/` Distributions, SpecialFunctions, HypothesisTests, Anova, Multiple/LinearRegression, DistributionFit, PermutationTest, Bootstrap, ConfidenceInterval, Correlation, ChangePoint, Trend; `Quality/*` | 3,300 | ~10 | 13 | 2 / 172 | clean (2 bugs, none severe) |
| 4  | Numerics core | the rest of `Numerics/*` (interpolation, root finding, integration, linear algebra, polynomial, streaming stats, random, fractions, units…) | 3,100 | ~3 | 13 | 5 / 217 | re-queue (5 bugs, none severe) |
| 5  | Charts | `Visualization/` charts: SvgChart, Annotations, Waterfall, TimingDiagram, HexBin, MultiPanel, AxisTicks, ScatterMatrix, BoxPlot, LinePlot, PlotFrame, Histogram, BandChart, ErrorBarChart, HeatMap, Colorbar, Colormap, Palettes | 3,750 | ~3 | 13 | 9 / 222 | re-queue (9 bugs, none severe) |
| 6  | Binary formats & images | `Binary/*`, `Visualization/` PngReader, PngWriter, Bmp, ImageBuffer, SvgDocument, SvgUtils | 3,500 | ~3 | 12 | 1 / 97 | clean (1 bug, none severe) |
| 7  | Documents & config | `Documents/*` (PDF, DOCX, report templates), `Configuration/*` | 2,900 | ~6 | 13 | 10 / 216 | re-queue (10 bugs, none severe) |
| 8  | IO | `IO/*` (tar, directory copy, FileWatcher, atomic writes…) | 4,900 | ~3 | 13 | 10 / 209 | re-queue (10 bugs, none severe) |
| 9  | Collections | `Collections/*` | 6,000 | ~1 | 12 | 4 / 132 | re-queue (4 bugs, none severe) |
| 10 | Concurrency | `Threading/*`, `Resilience/*`, `Process/*`, `Runtime/*` | 3,100 | ~2 | 12 | 5 / 157 | re-queue (5 bugs, none severe) |
| 11 | Text & small types | `Text/*`, `Identifiers/*`, `Enums/*`, `Guards/*` | 4,300 | ~2 | 10 | 2 / 113 | clean (2 bugs, none severe) |
| 12 | Logging, diagnostics, CLI | `Logging/*`, `Diagnostics/*`, `Cli/*` | 2,900 | ~2 | 13 | 6 / 187 | re-queue (6 bugs, none severe) |
| 13 | Security, net, time | `Security/*`, `Net/*`, `Time/*` | 3,450 | ~3 | 12 | 6 / 152 | re-queue (6 bugs, none severe) |
| 14 | Objects, functional, grids, graphs, intervals | `Objects/*`, `Functional/*`, `Grids/*`, `Graphs/*`, `Intervals/*` | 3,200 | ~1 | 11 | 2 / 176 | clean (2 bugs, none severe) |
| 15 | Windows satellite | `src/ToolBelt.Windows/*` | 3,100 | ~3 | 13 | 13 / 189 | re-queue (13 bugs, none severe) |
| 16 | WPF satellite | `src/ToolBelt.Wpf/*` | 3,950 | ~5 | 13 | 6 / 165 | re-queue (6 bugs, none severe) |
| 17 | WinForms + tooling + docs | `src/ToolBelt.WinForms/*`, `samples/*`, `scripts/*` (reference generators), README accuracy, packaging/build | 2,300 | ~2 | 12 | 2 / 140 | clean (2 bugs, none severe) |

After all 17: re-run the sections flagged by the re-review rule, worst first. Re-review order: 4 (done, round 18),
5 (done, round 19), 14 (done, round 20), 10 (done, round 21), 15 (done, round 22), 3 (done, round 23), 8 (done, round 24), 9 (done, round 25), 7 (done, round 26), 13 (done, round 27), 6 (done, round 28), 12 (done, round 29), then the 1-severe sections (1 (done, round 30), 2 (done, round 31), 11 (done, round 32), 16 (done, round 33), 17 (done, round 34)).

Third pass (every section was re-queued by its second pass), worst first: severe count, then bugs in the last pass:
7 (done, round 35), 4 (done, round 36), 5 (done, round 37), 14 (done, round 38), 2 (done, round 39), 8 (done, round 40), 15 (done, round 41), 10 (done, round 42), 12 (done, round 43), 9 (done, round 44), 3 (done, round 45), 13 (done, round 48), 16 (done, round 49), 11 (done, round 50), 17 (done, round 51), 6 (done, round 52), 1 (done, round 53); cross-cutting: throwing callbacks (done, round 54); cross-cutting: cancellation and dispose races (done, round 55); cross-cutting: non-finite and extreme numerics (done, round 56); cross-cutting: the whole suite on .NET Framework 4.8 and in the checked build (done, round 57). Fourth pass, every section re-queued, worst first (severe count, then bugs in the last pass): 8 (done, round 58), 14 (done, round 59), 17 (done, round 60), 4 (done, round 61), 5 (done, round 62), 7 (done, round 63), 13 (done, round 64), 15 (done, round 65), 3 (done, round 66), 1 (done, round 67), 6 (done, round 68), 12 (done, round 69), 2 (done, round 70), 9 (done, round 71), 10 (done, round 72), 16 (done, round 73), 11 (done, round 74). Fifth pass, same rule and order (severe count, then bugs in the fourth pass): 8 (done, round 75); 7 (done, round 76); 14 (done, round 77); 12 (done, round 78); 15 (done, round 79); 5 (done, round 80); 10 (done, round 81); 16 (done, round 82); 1 (done, round 83); 6 (done, round 84); 4 (done, round 85); 2 (done, round 86); 3 (done, round 87); 17 (done, round 88); 11 (done, round 89); 9 (done, round 90); 13 (done, round 91). Sixth pass, same rule and order (severe count, then bugs in the fifth pass): 13 (done, round 92); 14 (done, round 93); 8 (done, round 94); 4 (done, round 95); 9 (done, round 96); 10 (done, round 97); 11 (done, round 98); 6 (done, round 99); 7 (done, round 100); 5 (done, round 101); 16 (done, round 102); 12 (done, round 103); 2 (done, round 104); 17 (done, round 105); 15 (done, round 106); 1 (done, round 107); 3 (done, round 108). Seventh pass, same rule and order (severe count, then bugs in the sixth pass; ties by section number): 7 (done, round 109); 15 (done, round 110); 9 (done, round 111); 8 (done, round 112); 6 (done, round 113); 10 (done, round 114); 2 (done, round 115); 12 (done, round 116); 16 (done, round 117); 3 (done, round 118); 1 (done, round 119); 17 (done, round 120); 5 (done, round 121); 4 (done, round 122); 11 (done, round 123); 13 (done, round 124); 14 (done, round 125). Eighth pass, same rule and order (severe count, then bugs in the seventh pass; ties by section number): 7 (done, round 126); 9 (done, round 127); 10 (done, round 128); 6 (done, round 129); 4 (done, round 130); 15 (done, round 131); 17 (done, round 132); 1 (done, round 133); 5 (done, round 134); 12 (done, round 135); 14 (done, round 136); 2 (done, round 137); 8 (done, round 138); 16 (done, round 139); 3 (done, round 140); 11 (done, round 141); 13 (done, round 142). Ninth pass, same rule and order (severe count, then bugs in the eighth pass; ties by section number): 8 (done, round 143); 15 (done, round 144); 14 (done, round 145); 3 (done, round 146); 10 (done, round 147); 5 (done, round 148); 13 (done, round 149); 1 (done, round 150); 7 (done, round 151); 12 (done, round 152); 2 (done, round 153); 11 (done, round 154); 17 (done, round 155); 4 (done, round 156); 9 (done, round 157); 16 (done, round 158); 6 (done, round 159). Tenth pass, same rule and order (severe count, then bugs in the ninth pass; ties by section number): 7 (done, round 160), 12 (done, round 161), 4 (done, round 162), 14 (done, round 163), 15 (done, round 164), 10 (done, round 165), 13 (done, round 166), 5 (done, round 167), 2 (done, round 168), 6 (done, round 169), 1 (done, round 170), 3 (done, round 171), 8 (done, round 172), 9 (done, round 173), 11 (done, round 174: clean), 16 (done, round 175), 17 (done, round 176). Eleventh pass, same rule and order (severe count, then bugs in the tenth pass; ties by section number; 11 is clean): 4 (done, round 177), 5 (done, round 178), 14 (done, round 179: clean), 12 (done, round 180), 7 (done, round 181), 16 (done, round 182), 10 (done, round 183), 15 (done, round 184), 17 (done, round 185), 3 (done, round 186), 2 (done, round 187), 8 (done, round 188), 9 (done, round 189), 1 (done, round 190), 6 (done, round 191), 13 (done, round 192). Twelfth pass, same rule and order (severe count, then bugs in the eleventh pass; ties by section number; 11 and 14 are clean): 13 (done, round 193), 7 (done, round 194), 8 (done, round 195), 5 (done, round 196), 4 (done, round 197), 2 (done, round 198), 9 (done, round 199), 1 (done, round 200), 3 (done, round 201), 12 (done, round 202), 16 (done, round 203), 17 (done, round 204), 6 (done, round 205), 10 (done, round 206), 15 (done, round 207). Thirteenth pass, same rule and order (severe count, then bugs in the twelfth pass; ties by section number; 6, 11, 14 and 17 are clean): 2 (done, round 208), 4 (done, round 209), 5 (done, round 210), 15 (done, round 211), 8 (done, round 212), 12 (done, round 213), 16 (done, round 214), 1 (done, round 215), 3 (done, round 216), 7 (done, round 217); **next: 13**, then 10, 9 (round 46 was a cross-section round on flaky-test root
causes; its fixes are counted in the totals of 3, 10 and 15. Round 47 was the incomplete beta/gamma differential
harness; its 4 fixes are counted in section 3.)

## Releases

Round 1 added `<Version>` (a shared `Directory.Build.props`) at `v0.1.0`, and each round to 56 bumped the minor
version. From round 57 versions are date-based, `YYYY.M.D.N` (see CLAUDE.md, "Git and releases").

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
| v0.30.0 | 30 | Filters & control, second pass | 9 | (see tag) |
| v0.31.0 | 31 | Signal analysis, second pass | 23 | (see tag) |
| v0.32.0 | 32 | Text & small types, second pass | 12 | (see tag) |
| v0.33.0 | 33 | WPF satellite, second pass | 17 | (see tag) |
| v0.34.0 | 34 | WinForms + tooling + docs, second pass | 12 | (see tag) |
| v0.35.0 | 35 | Documents & config, third pass | 17 | (see tag) |
| v0.36.0 | 36 | Numerics core, third pass | 24 | (see tag) |
| v0.37.0 | 37 | Charts, third pass | 21 | (see tag) |
| v0.38.0 | 38 | Objects, functional, grids, graphs, intervals, third pass | 19 | (see tag) |
| v0.39.0 | 39 | Signal analysis, third pass | 13 | (see tag) |
| v0.40.0 | 40 | IO, third pass | 19 | (see tag) |
| v0.41.0 | 41 | Windows satellite, third pass | 12 | (see tag) |
| v0.42.0 | 42 | Concurrency, third pass | 8 | (see tag) |
| v0.43.0 | 43 | Logging, diagnostics, CLI, third pass | 18 | (see tag) |
| v0.44.0 | 44 | Collections, third pass | 13 | (see tag) |
| v0.45.0 | 45 | Statistics, third pass | 25 | (see tag) |
| v0.46.0 | 46 | Flaky-test root causes; huge-shape incomplete beta | 7 | (see tag) |
| v0.47.0 | 47 | Incomplete beta/gamma differential harness | 4 | (see tag) |
| v0.48.0 | 48 | Security, net, time, third pass | 16 | (see tag) |
| v0.49.0 | 49 | WPF satellite, third pass | 20 | (see tag) |
| v0.50.0 | 50 | Text & small types, third pass | 17 | (see tag) |
| v0.51.0 | 51 | WinForms, tooling and docs, third pass | 18 | (see tag) |
| v0.52.0 | 52 | Binary formats & images, third pass | 18 | (see tag) |
| v0.53.0 | 53 | Filters & control, third pass | 20 | (see tag) |
| v0.54.0 | 54 | Throwing callbacks (cross-cutting) | 36 | (see tag) |
| v0.55.0 | 55 | Cancellation and dispose races (cross-cutting) | 17 | (see tag) |
| v0.56.0 | 56 | Non-finite and extreme numerics (cross-cutting) | 34 | (see tag) |
| v2026.11.13.0 | 57 | The whole suite on .NET Framework 4.8 and in the checked build (cross-cutting) | 4 | (see tag) |
| v2026.11.13.1 | 58 | IO, fourth pass | 39 | (see tag) |
| v2026.11.13.2 | 59 | Objects, functional, grids, graphs, intervals, fourth pass | 27 | (see tag) |
| v2026.11.13.3 | 60 | WinForms, tooling and docs, fourth pass (plus 3 in section 14) | 25 | (see tag) |
| v2026.11.13.4 | 61 | Numerics core, fourth pass | 24 | (see tag) |
| v2026.11.13.5 | 62 | Charts, fourth pass | 24 | (see tag) |
| v2026.11.13.6 | 63 | Documents and config, fourth pass | 29 | (see tag) |
| v2026.11.13.7 | 64 | Security, net, time, fourth pass | 16 | (see tag) |
| v2026.11.13.8 | 65 | Windows satellite, fourth pass | 25 | (see tag) |
| v2026.11.13.9 | 66 | Statistics, fourth pass | 23 | (see tag) |
| v2026.11.13.10 | 67 | Filters & control, fourth pass | 14 | (see tag) |
| v2026.11.13.11 | 68 | Binary formats & images, fourth pass | 10 | (see tag) |
| v2026.11.13.12 | 69 | Logging, diagnostics, CLI, fourth pass | 30 | (see tag) |
| v2026.11.13.13 | 70 | Signal analysis, fourth pass | 23 | (see tag) |
| v2026.11.13.14 | 71 | Collections, fourth pass | 19 | (see tag) |
| v2026.11.13.15 | 72 | Concurrency, fourth pass | 24 | (see tag) |
| v2026.11.13.16 | 73 | WPF satellite, fourth pass | 24 | (see tag) |
| v2026.11.13.17 | 74 | Text & small types, fourth pass | 20 | (see tag) |
| v2026.11.13.18 | 75 | IO, fifth pass | 24 | (see tag) |
| v2026.11.13.19 | 76 | Documents & config, fifth pass | 24 | (see tag) |
| v2026.11.13.20 | 77 | Objects, functional, grids, graphs, intervals, fifth pass | 29 | (see tag) |
| v2026.11.13.21 | 78 | Logging, diagnostics, CLI, fifth pass | 19 | (see tag) |
| v2026.11.14.0 | 79 | Windows satellite, fifth pass | 15 | (see tag) |
| v2026.11.14.1 | 80 | Charts, fifth pass | 21 | (see tag) |
| v2026.11.14.2 | 81 | Concurrency, fifth pass | 15 | (see tag) |
| v2026.11.14.3 | 82 | WPF satellite, fifth pass | 20 | (see tag) |
| v2026.11.15.0 | 83 | Filters & control, fifth pass | 14 | (see tag) |
| v2026.11.16.0 | 84 | Binary formats & images, fifth pass | 10 | (see tag) |
| v2026.11.16.1 | 85 | Numerics core, fifth pass | 23 | (see tag) |
| v2026.11.16.2 | 86 | Signal analysis, fifth pass | 17 | (see tag) |
| v2026.11.16.3 | 87 | Statistics, fifth pass | 13 | (see tag) |
| v2026.11.16.4 | 88 | WinForms + tooling + docs, fifth pass | 17 | (see tag) |
| v2026.11.16.5 | 89 | Text & small types, fifth pass | 15 | (see tag) |
| v2026.11.16.6 | 90 | Collections, fifth pass | 16 | (see tag) |
| v2026.11.16.7 | 91 | Security, net, time, fifth pass | 11 | (see tag) |
| v2026.11.16.8 | 92 | Security, net, time, sixth pass | 3 | (see tag) |
| v2026.11.16.9 | 93 | Objects, functional, grids, graphs, intervals, sixth pass | 3 | (see tag) |
| v2026.11.16.10 | 94 | IO, sixth pass | 4 | (see tag) |
| v2026.11.16.11 | 95 | Numerics core, sixth pass | 8 | (see tag) |
| v2026.11.16.12 | 96 | Collections, sixth pass | 6 | (see tag) |
| v2026.11.16.13 | 97 | Concurrency, sixth pass | 3 | (see tag) |
| v2026.11.16.14 | 98 | Text & small types, sixth pass | 4 | (see tag) |
| v2026.11.16.15 | 99 | Binary formats & images, sixth pass | 3 | (see tag) |
| v2026.11.16.16 | 100 | Documents & config, sixth pass | 10 | (see tag) |
| v2026.11.16.17 | 101 | Charts, sixth pass | 9 | (see tag) |
| v2026.11.16.18 | 102 | WPF satellite, sixth pass | 13 | (see tag) |
| v2026.11.16.19 | 103 | Logging, diagnostics, CLI, sixth pass | 15 | (see tag) |
| v2026.11.16.20 | 104 | Signal analysis, sixth pass | 17 | (see tag) |
| v2026.11.16.21 | 105 | WinForms, tooling, docs, sixth pass | 9 | (see tag) |
| v2026.11.16.22 | 106 | Windows satellite, sixth pass | 9 | (see tag) |
| v2026.11.16.23 | 107 | Filters & control, sixth pass | 9 | (see tag) |
| v2026.11.16.24 | 108 | Statistics, sixth pass | 12 | (see tag) |
| v2026.11.16.25 | 109 | Documents & config, seventh pass | 20 | (see tag) |
| v2026.11.17.0 | 110 | Windows satellite, seventh pass | 14 | (see tag) |
| v2026.11.17.1 | 111 | Collections, seventh pass | 22 | (see tag) |
| v2026.11.17.2 | 112 | IO, seventh pass | 8 | (see tag) |
| v2026.11.17.3 | 113 | Binary formats & images, seventh pass | 6 | (see tag) |
| v2026.11.17.4 | 114 | Concurrency, seventh pass | 14 | (see tag) |
| v2026.11.17.5 | 115 | Signal analysis, seventh pass | 8 | (see tag) |
| v2026.11.17.6 | 116 | Logging, diagnostics, CLI, seventh pass | 13 | (see tag) |
| v2026.11.17.7 | 117 | WPF satellite, seventh pass | 11 | (see tag) |
| v2026.11.17.8 | 118 | Statistics, seventh pass | 10 | (see tag) |
| v2026.11.17.9 | 119 | Filters & control, seventh pass | 8 | (see tag) |
| v2026.11.17.10 | 120 | WinForms + tooling + docs, seventh pass | 10 | (see tag) |
| v2026.11.17.11 | 121 | Charts, seventh pass | 9 | (see tag) |
| v2026.11.17.12 | 122 | Numerics core, seventh pass | 12 | (see tag) |
| v2026.11.17.13 | 123 | Text & small types, seventh pass | 4 | (see tag) |
| v2026.11.17.14 | 124 | Security, net, time, seventh pass | 4 | (see tag) |
| v2026.11.17.15 | 125 | Objects, functional, grids, graphs, intervals, seventh pass | 9 | (see tag) |
| v2026.11.17.16 | 126 | Documents & config, eighth pass | 9 | (see tag) |
| v2026.11.17.17 | 127 | Collections, eighth pass | 5 | (see tag) |
| v2026.11.17.18 | 128 | Concurrency, eighth pass | 6 | (see tag) |
| v2026.11.17.19 | 129 | Binary formats & images, eighth pass | 3 | (see tag) |
| v2026.11.17.20 | 130 | Numerics core, eighth pass | 6 | (see tag) |
| v2026.11.17.21 | 131 | Windows satellite, eighth pass | 12 | (see tag) |
| v2026.11.17.22 | 132 | WinForms + tooling + docs, eighth pass | 8 | (see tag) |
| v2026.11.17.23 | 133 | Filters & control, eighth pass | 10 | (see tag) |
| v2026.11.17.24 | 134 | Charts, eighth pass | 18 | (see tag) |
| v2026.11.17.25 | 135 | Logging, diagnostics, CLI, eighth pass | 9 | (see tag) |
| v2026.11.18.0 | 136 | Objects, functional, grids, graphs, intervals, eighth pass | 9 | (see tag) |
| v2026.11.18.1 | 137 | Signal analysis, eighth pass | 8 | (see tag) |
| v2026.11.18.2 | 138 | IO, eighth pass | 8 | (see tag) |
| v2026.11.18.3 | 139 | WPF satellite, eighth pass | 4 | (see tag) |
| v2026.11.18.4 | 140 | Statistics, eighth pass | 6 | (see tag) |
| v2026.11.18.5 | 141 | Text & small types, eighth pass | 8 | (see tag) |
| v2026.11.18.6 | 142 | Security, net, time, eighth pass | 12 | (see tag) |
| v2026.11.18.7 | 143 | IO, ninth pass | 6 | (see tag) |
| v2026.11.18.8 | 144 | Windows satellite, ninth pass | 6 | (see tag) |
| v2026.11.18.9 | 145 | Objects, functional, grids, graphs, intervals, ninth pass | 8 | (see tag) |
| v2026.11.18.10 | 146 | Statistics, ninth pass | 8 | (see tag) |
| v2026.11.18.11 | 147 | Concurrency, ninth pass | 3 | (see tag) |
| v2026.11.18.12 | 148 | Charts, ninth pass | 13 | (see tag) |
| v2026.11.18.13 | 149 | Security, net, time, ninth pass | 14 | (see tag) |
| v2026.11.18.14 | 150 | Filters & control, ninth pass | 8 | (see tag) |
| v2026.11.18.15 | 151 | Documents & config, ninth pass | 20 | (see tag) |
| v2026.11.19.0 | 152 | Logging, diagnostics, CLI, ninth pass | 11 | (see tag) |
| v2026.11.19.1 | 153 | Signal analysis, ninth pass | 10 | (see tag) |
| v2026.11.19.2 | 154 | Text & small types, ninth pass | 5 | (see tag) |
| v2026.11.19.3 | 155 | WinForms + tooling + docs, ninth pass | 5 | (see tag) |
| v2026.11.19.4 | 156 | Numerics core, ninth pass | 8 | (see tag) |
| v2026.11.19.5 | 157 | Collections, ninth pass | 5 | (see tag) |
| v2026.11.19.6 | 158 | WPF satellite, ninth pass | 5 | (see tag) |
| v2026.11.19.7 | 159 | Binary formats & images, ninth pass | 10 | (see tag) |
| v2026.11.19.8 | 160 | Documents & config, tenth pass | 12 | (see tag) |
| v2026.11.19.9 | 161 | Logging, diagnostics, CLI, tenth pass | 4 | (see tag) |
| v2026.11.19.10 | 162 | Numerics core, tenth pass | 12 | (see tag) |
| v2026.11.19.11 | 163 | Objects, functional, grids, graphs, intervals, tenth pass | 7 | (see tag) |
| v2026.11.19.12 | 164 | Windows satellite, tenth pass | 8 | (see tag) |
| v2026.11.19.13 | 165 | Concurrency, tenth pass | 8 | (see tag) |
| v2026.11.19.14 | 166 | Security, net, time, tenth pass | 3 | (see tag) |
| v2026.11.19.15 | 167 | Charts, tenth pass | 15 | (see tag) |
| v2026.11.19.16 | 168 | Signal analysis, tenth pass | 4 | (see tag) |
| v2026.11.19.17 | 169 | Binary formats & images, tenth pass | 3 | (see tag) |
| v2026.11.19.18 | 170 | Filters & control, tenth pass | 3 | (see tag) |
| v2026.11.19.19 | 171 | Statistics, tenth pass | 6 | (see tag) |
| v2026.11.19.20 | 172 | IO, tenth pass | 4 | (see tag) |
| v2026.11.19.21 | 173 | Collections, tenth pass | 4 | (see tag) |
| v2026.11.19.22 | 174 | Text & small types, tenth pass | 2 | (see tag) |
| v2026.11.19.23 | 175 | WPF satellite, tenth pass | 10 | (see tag) |
| v2026.11.19.24 | 176 | WinForms + tooling + docs, tenth pass | 7 | (see tag) |
| v2026.11.19.25 | 177 | Numerics core, eleventh pass | 9 | (see tag) |
| v2026.11.19.26 | 178 | Charts, eleventh pass | 11 | (see tag) |
| v2026.11.19.27 | 179 | Objects, functional, grids, graphs, intervals, eleventh pass | 2 | (see tag) |
| v2026.11.19.28 | 180 | Logging, diagnostics, CLI, eleventh pass | 6 | (see tag) |
| v2026.11.19.29 | 181 | Documents & config, eleventh pass | 12 | (see tag) |
| v2026.11.20.0 | 182 | WPF satellite, eleventh pass | 6 | (see tag) |
| v2026.11.20.1 | 183 | Concurrency, eleventh pass | 4 | (see tag) |
| v2026.11.20.2 | 184 | Windows satellite, eleventh pass | 3 | (see tag) |
| v2026.11.20.3 | 185 | WinForms + tooling + docs, eleventh pass | 5 | (see tag) |
| v2026.11.20.4 | 186 | Statistics, eleventh pass | 6 | (see tag) |
| v2026.11.20.5 | 187 | Signal analysis, eleventh pass | 8 | (see tag) |
| v2026.11.20.6 | 188 | IO, eleventh pass | 12 | (see tag) |
| v2026.11.20.7 | 189 | Collections, eleventh pass | 8 | (see tag) |
| v2026.11.20.8 | 190 | Filters & control, eleventh pass | 7 | (see tag) |
| v2026.11.20.9 | 191 | Binary formats & images, eleventh pass | 4 | (see tag) |
| v2026.11.20.10 | 192 | Security, net, time, eleventh pass | 11 | (see tag) |
| v2026.11.20.11 | 193 | Security, net, time, twelfth pass | 6 | (see tag) |
| v2026.11.20.12 | 194 | Documents & config, twelfth pass | 6 | (see tag) |
| v2026.11.20.13 | 195 | IO, twelfth pass | 8 | (see tag) |
| v2026.11.20.14 | 196 | Charts, twelfth pass | 11 | (see tag) |
| v2026.11.20.15 | 197 | Numerics core, twelfth pass | 15 | (see tag) |
| v2026.11.20.16 | 198 | Signal analysis, twelfth pass | 15 | (see tag) |
| v2026.11.21.0 | 199 | Collections, twelfth pass | 4 | (see tag) |
| v2026.11.21.1 | 200 | Filters & control, twelfth pass | 6 | (see tag) |
| v2026.11.21.2 | 201 | Statistics, twelfth pass | 6 | (see tag) |
| v2026.11.21.3 | 202 | Logging, diagnostics, CLI, twelfth pass | 8 | (see tag) |
| v2026.11.21.4 | 203 | WPF satellite, twelfth pass | 8 | (see tag) |
| v2026.11.21.5 | 204 | WinForms + tooling + docs, twelfth pass | 2 | (see tag) |
| v2026.11.21.6 | 205 | Binary formats & images, twelfth pass | 1 | (see tag) |
| v2026.11.21.7 | 206 | Concurrency, twelfth pass | 5 | (see tag) |
| v2026.11.22.0 | 207 | Windows satellite, twelfth pass | 9 | (see tag) |
| v2026.11.22.1 | 208 | Signal analysis, thirteenth pass | 5 | (see tag) |
| v2026.11.22.2 | 209 | Numerics core, thirteenth pass | 5 | (see tag) |
| v2026.11.22.3 | 210 | Charts, thirteenth pass | 9 | (see tag) |
| v2026.11.23.0 | 211 | Windows satellite, thirteenth pass | 13 | (see tag) |
| v2026.11.23.1 | 212 | IO, thirteenth pass | 10 | (see tag) |
| v2026.11.23.2 | 213 | Logging, diagnostics, CLI, thirteenth pass | 6 | (see tag) |
| v2026.11.23.3 | 214 | WPF satellite, thirteenth pass | 6 | (see tag) |
| v2026.11.23.4 | 215 | Filters & control, thirteenth pass | 2 | (see tag) |
| v2026.11.23.5 | 216 | Statistics, thirteenth pass | 2 | (see tag) |
| v2026.11.23.6 | 217 | Documents & config, thirteenth pass | 10 | (see tag) |

## Round log

<!-- One entry per round: date, section, findings raised / confirmed bugs / docs-or-hardening / rejected, the
     severe ones in a line each, and whether the section is re-queued. -->
### Round 217: Documents & config, thirteenth pass (v2026.11.23.6)

Two fresh reviewers (correctness, robustness): 10 bugs, none severe, so section 7 is re-queued (10 >= 3). The
correctness reviewer confirmed three, the robustness reviewer eight (one shared); six of the ten were BUGLIST suspects
(R109-5, R109-7 / R126-2, R126-1, part of R160-1, part of R194-1). The tests are in `DocumentsConfigReview217Tests`
(10; each fails on the old code, run by name under a 60 s timeout, and on .NET Framework 4.8): GetDouble graded
against the exact midpoints of 4,000 random doubles, the bullet indent over random fonts, sizes and levels, the key
casing against a fixed fold table on both runtimes. Clean: the PDF structure (xref offsets, lengths, outline counts,
string escaping) over 300 random documents with hostile strings on both runtimes, 300 random DOCX documents through a
strict XML reader, PdfWriter's guards at extreme sizes, margins and levels, ConfigBinder's double, float, decimal and
ulong parsing at halfway and overflow cases on both runtimes, SettingsStore's corrupt, locked and odd-encoding files.

- **EnvironmentVariables.GetDouble**: on .NET Framework the parse was the runtime's, one ulp off for some 17-digit
  inputs (9.8598758830020559e-205). It takes the correctly rounded value, with the same netstandard2.0 block as
  CsvDialect, CsvBinder and ConfigBinder.
- **Durations** (ConfigBinder, EnvironmentVariables): a colon form with more than 7 fraction digits was refused by
  TimeSpan.TryParse on net8 and off on net48; the fraction is cut to ticks, as the unit forms are. The two copies of
  the grammar had drifted; EnvironmentVariables holds ConfigBinder's copy again.
- **PdfWriter wrapping**: spaces at a line break started the next line, so it was indented. A continuation line drops
  them.
- **PdfWriter.BulletList**: at large sizes a nested item started past the right margin (Courier 150, level 1: x = 564
  of 558). The nesting gives way first, then the bullet's gap, so the bullet and the widest character fit.
- **PdfWriter.Code** (R109-7, R126-2): lines wrapped by UTF-16 count, so a line with emoji wrapped early. A
  surrogate pair is one column (round 2's test of an unsplit pair now fills the line with 83
  columns, not 82).
- **ConfigBinder.Bind<T>** (R160-1): the root type's constructor exception escaped as TargetInvocationException. It is
  rethrown as itself, as nested types' already were.
- **HtmlReport.CodeBlock** (R109-5): whitespace in the language split it into two CSS classes. It becomes '-'.
- **HtmlReport.ImageData** (R126-1): any MIME type text was accepted, so null wrote "data:;base64," and other text a
  broken URI. It must be type/subtype of RFC 6838 name characters.
- **ConfigLayers key casing**: OrdinalIgnoreCase folds by the runtime's tables, so keys differing in a letter cased only
  on net8 (or only on net48) were one key on one runtime and two on the other. Keys fold by one fixed rule on both.
- **ConfigLayers.TryGet** (R194-1): a key held only as null answered true with a null value. It is not found.

Not counted, R217-1: see BUGLIST.

### Round 216: Statistics, thirteenth pass (v2026.11.23.5)

Two fresh reviewers (correctness, robustness): 2 bugs, none severe, so section 3 is clean (2 < 3). Both reviewers found
the first; the robustness reviewer found the second. The tests are in `StatisticsReview216Tests` (2; each fails on the
old code, run by name under a 60 s timeout): the intervals graded against the same data scaled down by 2^-10 over
3,000 random samples near double.MaxValue, the KS tail against SciPy's kstwo (and mpmath at 3e6 points, where kstwo is
1e-4 off) and the stopped Smirnov sum against the full one. Clean: about 4,000 points of the StudentT and ChiSquare
CDF, PDF, survival and quantiles against mpmath (df 2.3e-308 to 1e300), the StudentT quantile across df 0.05 to 1e30,
the one-sample KS up to 100,000 points against SciPy and up to 1,000 against an independent 30-digit matrix form,
PermutationTest's exact paths against brute-force enumeration (800 trials with ties), Anova.FUpperTailProbability
bit for bit against FSurvival, GaugeRR's long-format checks, NaN and infinite inputs across the section.

- **ConfidenceInterval** (ForMean, ForMeanT, ForStandardDeviation, ForVariance): a sample whose standard deviation is
  past double.MaxValue (half at -MaxValue, half at +MaxValue) overflowed with nothing to fall back on, so the mean
  intervals were (-inf, inf) where they are about +-3.5e307 and the standard deviation's was (inf, inf) where its lower
  bound is 1.6e308. Such a sample is taken in units of 16, and a margin or bound past MaxValue is formed again there.
- **HypothesisTests.KolmogorovSmirnovOneSample**: past 100,000 points the tail below 1e-4 was Stephens' asymptotic
  form, 6% off at p = 5e-56 (n = 100,001; one point more than 100,000 with the same D dropped p by 6%). The doubled
  exact Smirnov tail is used at any size, where Stephens' form puts the tail below 2e-4.

Not counted, R216-1: ProcessCapability throws when sigma overflows though Cp and Cpk would be representable; the
Weibull and Gamma fits' KS statistics on near-constant data (0.632 and 0.5 on {2, 2, 2 + 4.4e-16}); ChiSquareQuantile
at p = 0.01, k = 0.1 may be 10x less accurate than its conditioning (from a flawed reference, unconfirmed).

### Round 215: Filters & control, thirteenth pass (v2026.11.23.4)

Two fresh reviewers (correctness, robustness): 2 bugs, none severe, so section 1 is clean (2 < 3). Both reviewers found
the first; the robustness reviewer found the second. The tests are in `FiltersControlReview215Tests` (2; each fails on
the old code, run by name under a 60 s timeout): the slew limiter graded against the exact gap over 200,000 tie trials,
the band-pass taps against the same design in 256-bit fixed point over random narrow bands. Clean: 900 and 600 random
IIR designs against SciPy across all families, FIR designs against firwin (400 and 800 specs), Deadband against an
exact reference over 900,000 trials, Filter, Process, Reset and FiltFilt against SciPy over 300 runs,
StepResponse.Analyze against an independent implementation over 2,000 cases and FitStep on exact models,
PidController against a reference simulation over 3,000 runs, PidTuning against its formulas, round 200's
FirstOrderLag and Kalman fixes, and the Control and Signal tests on .NET Framework 4.8.

- **SlewRateLimiter.Update**: the gap was rounded before it was compared with the step, so a true gap just past the
  step that rounded to exactly the step fired neither test: the output jumped to the target and IsLimiting read false
  (from 2 toward -1e-16 with a rate of 1 per second it reached -1e-16 in one second). The rounding error of the gap
  (TwoSum) breaks the tie.
- **FirFilter band-pass and band-stop design**: each tap was the difference of two sinc terms, which cancel for a
  narrow band, and the band centre's cosines were formed from a rounded π·m·f: a 1e-9-wide band's taps were 6e-8 off
  and a band by Nyquist 1.6e-4. Each band's term is a product (cosine of the centre times the half-width's sinc), and
  the cosines are reduced exactly (Dekker's product split into an integer, an exact fraction and the product error).

Not counted, R215-1: elliptic designs at orders 16, 24 and 25 refused as within rounding of the unit circle; orders 20
to 23 differ from SciPy by 0.03 dB on the steep part (both meet the spec); FiltFilt on a Bessel band-pass with poles
near z = 1 differs from SciPy by 2.4e-9; IIR designs with edges near 0 or Nyquist differ by 1e-6 to 2e-5.

### Round 214: WPF satellite, thirteenth pass (v2026.11.23.3)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 16 is re-queued (6 >= 3). The
correctness reviewer confirmed three, the robustness reviewer five (two shared); four of the six were BUGLIST suspects
(R102-2, R102-5 / R117-5, and two parts of R158-1 / R203-1). The tests are in `WpfReview214Tests` (6; each fails on the
old code, run by name under a 60 s timeout), ZoomAbout graded against the exact zoom over 20,000 random mirrored and
non-uniform views. Clean: NumericInputBehavior.Coerce against a decimal reference over 200,000 trials, Messenger
re-entrancy, collection and ordering (and an 8-thread stress run), ValidationObservableObject with re-entrant handlers,
IncrementalCollection's join and cancel paths, NotifyTaskCompletion with faulted and cancelled tasks, ZoomAbout on
ordinary views over 100,000 trials, EnumToBooleanConverter's [Flags] lists.

- **ThemeManager.Apply** (R158-1): an app that merged both theme dictionaries kept the other behind the slot it was
  given, so Light reported Light and showed Dark; after Dark the list held two Darks and Light could never show again.
  On the first Apply the first theme dictionary found is the slot and the others are removed.
- **ThemeManager.FollowSystem** (R102-5, R117-5): created on an STA thread that then ended, it was accepted and queued
  every re-apply to a dispatcher nothing would pump, for the life of the process. It throws, as the doc says; if the
  thread ends while it is on, it turns itself off at the next setting change.
- **PlotPresenter.ZoomAbout**: the signed M11 was the scale and M11 and M22 were clamped apart, so a y-up view
  (M22 = -1) was refused with the default bounds, or with [0.02, 64] flipped and shrunk with its anchor lost, and a
  non-uniform view lost its anchor. The scale is the x axis's length and both axes scale by one factor.
- **CountToVisibilityConverter** (R102-2): peeking with MoveNext used up the first item of a one-shot source (a data
  reader, a consuming enumerable), so the ItemsControl bound to it lost data. A sequence without a count is checked
  through its WPF default view, which snapshots it once and is shared with an ItemsControl bound to it.
- **CountToVisibilityConverter** (R158-1, R203-1): '\0' was Collapsed though a char is not a count ('a' was Visible),
  and DBNull was Visible. A char is present and DBNull counts as null; the doc now names TimeSpan and enum values as
  present.
- **IncrementalCollection**: a loader that returned a null Task threw a bare NullReferenceException, which LastError
  then showed. It is an InvalidOperationException, as a null page is.

Not counted, R214-1: FitMatrix may return a non-invertible matrix at extreme sizes; NotifyTaskCompletion on a dispatcher
that never pumps never finishes; NullToVisibilityConverter shows DependencyProperty.UnsetValue; BooleanAndConverter is
true for no values; CountToVisibilityConverter off a dispatcher thread still enumerates a sequence without a count.

### Round 213: Logging, diagnostics, CLI, thirteenth pass (v2026.11.23.2)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 12 is re-queued (6 >= 3). The
robustness reviewer confirmed the DiskSpace underflow; the correctness reviewer confirmed none. The other five were the
reviewers' plausible findings, confirmed here (one, the HealthReport indentation, found on reading the line-ending
finding). The tests are in `LoggingReview213Tests` (5; each fails on the old code, run by name under a 60 s timeout).
Clean: StructuredTextFormatter's round 202 escaping against a naive split over 2 million random trials, SyslogSink's
octet counting over loopback TCP and UTF-8-safe UDP truncation, FileSink rotation with a UTF-16 BOM, AsyncLogSink writes
racing Dispose under every overflow policy, MetricsRegistry against an exact rational reference over 3,000 extreme
trials, Sparkline over 200,000 extreme series, ScopedTimer across the whole DateTimeOffset range. Refuted: R116-1
(ConsoleTable stayed aligned over 20,000 random tables of SGR-styled cells).

- **DiskSpace.EstimateBytes**: a positive rate over a positive duration whose product underflowed (5e-324 B/s for half
  a second) estimated 0 bytes, where the doc says "rounded up". It is at least 1. The docs now also say the product is a
  double, so past 2^53 bytes it carries a double's rounding (documentation).
- **ScopedTimer**: an injected clock that stepped back mid-block (a wall clock adjusted) reported a negative duration.
  It reports zero.
- **HealthReport.ToString**: a probe message of several lines put its later lines at column 0, where they read as lines
  of the report's own (the bug round 202 fixed in EnvironmentReport), and the lines ended with '\n' where the other
  reports use Environment.NewLine. Later lines are indented under the probe, and lines end with Environment.NewLine.
- **SyslogSink**: one astral character in the host, app name, proc ID or category became two `_`, one per UTF-16 unit.
  It is one.
- **AsyncLogSink**: pool work started by the inner sink carries the consumer's flow, so when a blocking queue was full
  its event was refused with a raw InvalidOperationException and lost. It is written through to the inner sink, as an
  event from another sink's consumer is.

Not counted, R213-1: FileSink.Rotate when the old stream's Dispose throws; ConsoleSpinner's character widths on .NET
Framework's older Unicode data; the JSON formatter leaves U+2028 and U+2029 unescaped (valid JSON); the Ctrl+C path of
ConsoleApp.

### Round 212: IO, thirteenth pass (v2026.11.23.1)

Two fresh reviewers (correctness, robustness): 10 bugs, none severe, so section 8 is re-queued (10 >= 3). Both
reviewers found the PAX sparse file and the duplicate-name zip read; the robustness reviewer confirmed the manifest and
ShiftNumbered bugs, the correctness reviewer the coalescer order and the tar directory time. The read-only oldest copy
was the correctness reviewer's plausible finding, confirmed here; the zip directory time was found on reading, and the
renamed-and-changed file (an R195-1 suspect) by the new differential test. The tests
are in `IOReview212Tests` (10; each fails on the old code, run by name under a 60 s timeout). Clean: round 195's
rounding fixes on .NET Framework, 22 decimal edge inputs on both runtimes, 19,941 random manifest round trips, LineReader
against a naive splitter over 50,000 random byte strings read 1 to 5 bytes at a time, FileRetention's wildcards against
Directory.GetFiles over 2,481 patterns. Refuted: R112-3 (clearing ReadOnly does not follow a symlink), consecutive PAX
headers, and R172-1's dotless-i ByteSize and trailing no-break space suspects.

- **TarUtils.ExtractToDirectory**: a PAX sparse file (GNU.sparse.* records, the 0.1 and 1.0 forms) was written out as its
  raw map and data, a file of the wrong content and size; only the old GNU 'S' type was refused. Both are refused.
- **TarUtils / ZipUtils.ExtractToDirectory**: two entries for one directory left the first entry's time; files, and GNU
  tar, take the last. The last wins.
- **ZipUtils.ReadEntryBytes**: of several entries with one name it read the first, where extraction leaves the last.
- **ChecksumManifest**: a path written `././b` lost only one `./` and came back as `./b`, which verified as missing. Every
  leading `./` is stripped. A file name with an unpaired surrogate was written as U+FFFD and could never verify; Create
  reports it through onError, and the path methods refuse it (ArgumentException).
- **FileRetention.ShiftNumbered**: a path ending in a dot or space (which Windows trims) found the file but moved names
  built from the untrimmed path, so the rotation threw FileNotFoundException. The full path is the base. A read-only copy
  falling off the end threw UnauthorizedAccessException and nothing rotated; ReadOnly is cleared first, as Prune does.
- **FileChangeCoalescer** (FileWatcher): changes came out in first-seen order, so "C b; R a->c; R b->a" was released as
  "Created a | Renamed a -> c", which replayed in order moved the new a away. Changes now come out in an order that
  replays: a change at a path waits for the rename that vacated it (and stays pending with it), and a cycle of renames (a
  swap through a temporary name) has its earliest rename split into Deleted and Created. A file renamed and also changed
  ("R a->c; M c", or "M a; R a->c") came out only as Renamed; it is Renamed followed by Changed at the new path.
  `FileWatcherTests.Coalesce_NetEffects` and round 195's model test now expect that pair.

Not counted, R212-1: CSV numeric fields with a trailing NUL (BCL parsing), FileRetention's pattern `.` matching nothing
where the BCL reads it as `*`, a rename over an existing file with no Deleted event, zip Unix symlinks extracted as plain
files, GNU multivolume entries skipped, ZipUtils.MadeBy finding a PK\5\6 inside the archive comment,
ExtractToDirectory(overwrite: false) on a zip with duplicate names.

### Round 211: Windows satellite, thirteenth pass (v2026.11.23.0)

Two fresh reviewers (correctness, robustness): 13 bugs, none severe, so section 15 is re-queued (13 >= 3). The
correctness reviewer confirmed GetSpace and the robustness reviewer the three ShortcutUtils bugs; the rest were their
plausible findings, confirmed here or fixed on reading (two are documentation). The tests are in
`WindowsReview211Tests` (11; each fails on the old code, run by name under a 60 s timeout). Clean: JoinArguments over
40,000 random argument lists through CommandLineToArgvW, GetSpace on 17 path spellings against DriveInfo, every DPI
awareness scope read back, shortcut round trips with odd Unicode and at the 259/260 limits, window titles up to 65,535
characters, registry keys 19 to 67 levels deep, 400 bit-exact Complex round trips, 50 rounds of 8 parallel privilege
scopes, round 207's JobObject.Start fixes.

- **DriveAndVolumeInfo.GetSpace**: the extended-length rewrite looked at the path as given, so a short relative path
  under a deep current directory (298 characters in full) reached the API relative and failed with Win32Exception,
  where its full form worked. The full path decides.
- **ShortcutUtils.Read**: a damaged .lnk (a valid header over zeros, or one truncation of a real shortcut) threw a bare
  COMException 0x80070490; only E_FAIL was translated. Any COM or argument error from Load is InvalidDataException; a
  missing or locked file still passes through as its own exception.
- **ShortcutUtils.Create**: a `\\?\C:\...` target was refused by the shell, and the refusal (and that of `sub\a:b`,
  which was taken for a URL scheme and never resolved) came out as an ArgumentException naming no parameter. The
  prefix is dropped, a colon after a separator is not a scheme, and the shell's refusal names `targetPath`.
- **ShortcutUtils** URL and shell targets: `https://...`, `mailto:` and the Recycle Bin are stored as item ID lists, and
  Read returned an empty target. Read falls back to the shell parsing name, which Create now takes back (a `::{guid}`
  name was resolved as a relative file).
- **PowerStatus**: a battery state Windows reports as unknown (flag 255) printed "no battery". PowerStatusInfo has
  BatteryStateKnown, and ToString says "battery unknown".
- **FileAssociation**: a path was taken as an extension (`C:\x\y.txt` was answered for .txt). Characters invalid in a
  file name are refused.
- **ClipboardUtils.DecodeDib**: a DIB whose last row ended at its last pixel, without the row padding, was refused as
  truncated.
- **StorageBusType**: NVMe over Fabrics (20) read as Unknown. It is Nvmeof.
- **RunElevated.RestartCurrent**: under the dotnet host the relaunch dropped the host's options (`exec --runtimeconfig`,
  `--roll-forward`), which could bind the app to another runtime, and it passed no working directory, so the elevated
  process began in System32. The host options come from the process's own command line, and the current directory is
  passed.
- **EventLogWriter**: a truncated message reserved 14 characters for its 13-character marker, one short of the limit.
- **SystemMemory** (documentation): TotalPageFileBytes and AvailablePageFileBytes are the commit limit and available
  commit (physical memory plus page files), not the page file; they had no docs and the names suggested otherwise.
- **RegistryUtils.SetValue** (documentation): a NaN's sign and payload are lost through the text form (4 of 4,262
  random round trips); this is now stated, with the QWORD alternative.

Not counted: DpiInfo.Monitors throws where per-monitor v2 is refused while MonitorInfo falls back to v1 (the class
requires Windows 10 1703, as documented); FileAssociation's "Pick an app" fallback (documented); window titles of
65,536 characters or more read "" (Windows reports length 0). The rest is R211-1.

Test fix: `DocumentsConfigReview151Tests.Settings_LoadsDuringSavesNeverGetSilentDefaults` loaded for a fixed 3 s and
required over 100 loads; on a loaded machine it got 97 and failed. It now runs 200 loads (60 s cap).

### Round 210: Charts, thirteenth pass (v2026.11.22.3)

Two fresh reviewers (correctness, robustness): 9 bugs, none severe, so section 5 is re-queued (9 >= 3). The tests are
in `ChartsReview210Tests` (9; each fails on the old code, run by name under a 60 s timeout; the Waterfall label test
fails only on .NET Framework 4.8, where it was proved). They are differential: BoxPlot outliers against exact
BigInteger quartiles and fences over 20,000 random data sets, MultiPanel plot sizes across 300 random figures, raster
bands that tile a range against one band over it, in-frame line paths over 1,000 random charts. Clean: Histogram
counts and edges against a naive binner and exact decimals, HeatMap colours, Colorbar and Colormap ends, LinePlot
edge cases, text fitting on huge strings (linear), raster hashes identical on .NET Framework 4.8 and .NET 8. R196-1's
BandChart and CeilMicro suspects were refuted.

- **BoxPlot.Compute**: the fences were rounded, so a value exactly on one was an outlier ({0.1, 0.7} at k = 0.5 made
  0.1 an outlier; 118 of 20,000 random data sets were wrong), and an upper fence that overflowed kept +inf inside it
  ({0, 0, 1e308 x3, inf} had no outliers and a whisker at inf). Near a fence, or past an overflowed one, values are
  compared with the exact fence from the exact quartiles.
- **BoxPlot.Render**: with no finite value the axis ran from +inf to -inf and nothing was drawn. It is [0, 1], as
  LinePlot's is.
- **MultiPanel.Layout**: margins and plot sizes were rounded sums, so plot areas differed by an ulp between columns
  and rows (90 of the test's panels). They are snapped to a power-of-two grid 2^40 times finer than the panel, where
  every sum is exact.
- **Annotations** (raster): a thick Dashed or Dotted line re-stamped the whole pen square at every dash start (a
  Dotted line 1e6 wide took 14 s on a 2000-px image); the last square is skipped across gaps too. Bands that tile a
  range blended their shared row twice, a darker seam; each band covers the pixels whose centres are in [low, high).
- **Histogram.Bin**: Min = double.MaxValue with Max automatic gave five edges all equal to it (and the mirror at
  -double.MaxValue). The range opens on the other side and counts only from the bound.
- **Waterfall**: the flat-data label used "R", which on .NET Framework printed 0.84551240822557006 as
  0.84551240822557, another double. "R" is checked and falls back to "G17".
- **SvgChart** (Line): a segment clipped from its far end wrote back an uncut end an ulp off, so the next segment
  began a fresh subpath (2,090 of 3,000 random in-frame lines were split, differently on .NET Framework). Uncut ends
  stay exact.

Not counted: BoxPlot.Render on all-infinite data was found by both reviewers (counted once). The rest is R210-1.

### Round 209: Numerics core, thirteenth pass (v2026.11.22.2)

Two fresh reviewers (correctness, robustness): 5 bugs, none severe, so section 4 is re-queued (5 >= 3). The tests are
in `NumericsCoreReview209Tests` (5; each fails on the old code, run by name under a 60 s timeout). They are
differential: Evaluate at infinity against the leading term's sign, the quadratic against exactly representable close
roots, WeightedAverage against exact BigInteger rounding over 20,000 random dyadic data sets, Format's mantissa just
below powers of ten from 1e-300 to 1e300, AdaptiveSimpson against 2Lc for c + x/10^k over ranges near the double
limit. Clean: Fraction.ToDouble (24,000 exact cases), Percentile and EmpiricalDistribution, Primes against a sieve
and a BigInteger reference, Determinant against exact Bareiss, Means and ZScore at extremes, ToleranceCheck, and
Histogram binning. R95-1 and R122-5 were refuted on .NET Framework 4.8 (Pow(1, inf) is 1 there; Factorize without
UInt128 was right near 2^63).

- **Polynomial.Evaluate**: Horner started from 0, so 0 times infinity made every polynomial NaN at +-inf (the
  constant 5 included). It starts from the leading coefficient.
- **Polynomial.RealRoots** (quadratic): the discriminant was a rounded b^2 - 4ac, which cancelled for close roots
  (1 + 3*2^-27 and 1 + 2^-25, exact coefficients, came back as one root). It is formed from exact products.
- **WeightedAverage.Compute**: the deviations' sum and the quotient were rounded before the value nearest 0 was added
  back, so a small mean of larger values lost hundreds of ulps (a mean of 1/68 came back 482 ulps off). The sums,
  products and quotient carry their rounding errors.
- **Uncertainty.Format**: the exponent came from Math.Log10, which rounds up just below a power of ten, so
  9.999999999999997e22 printed as (0.99999999999999970 +- ...)e23. It comes from the shortest digits.
- **Integration.AdaptiveSimpson**: the overflow retry scaled every sample by 2^-1100, which flushed samples below 2^26
  to 0, so 1 over +-1e308 integrated to 0, not infinity. It tries 2^-550 first, and 2^-1100 only if that overflows.

Not counted: a one-bin Histogram wider than double.MaxValue reports an infinite BinWidth (the width is not
representable). The rest is R209-1.

### Round 208: Signal analysis, thirteenth pass (v2026.11.22.1)

Two fresh reviewers (correctness, robustness): 5 bugs, none severe, so section 2 is re-queued (5 >= 3). The tests are
in `SignalReview208Tests` (6; each fails on the old code, run by name under a 60 s timeout). They are differential:
Spectrum and the two midpoints against exact BigInteger rounding over random integers, subnormals and wide ranges,
WelchPsd against the mean of one-segment estimates over segments 10^300 apart. Clean: Fft forward and inverse at every
length to 300 against a naive DFT (1e-300 to 1e300), Quantizer.Code against exact codes (600,000), Goertzel, WelchPsd
against the SciPy formula, Multitaper, CrossCorrelationPeak ties, MedianFilter and Hampel with signed zeros,
infinities, NaN and subnormals, no writes to caller arrays and no shared state. R104-3 was refuted off exact ties.

- **Spectrum.Amplitude** (and Power, AmplitudeDb): |X| was multiplied by a rounded 1/N or 2/N, so 3 at N = 10 read
  0.30000000000000004 (24% of random integer bins were an ulp off), and 5eps at N = 10, an exact tie, gave eps for 0.
  It divides by N or N/2, both exact.
- **WelchPsd.Estimate**: one scale came from the whole signal's peak, so a quiet segment beside a loud one underflowed
  when squared: [1, 1, 1e-200, -1e-200] in segments of 2 at 1e-100 Hz gave 0 for 1e-300 at Nyquist, and 1e140 beside
  1e300 came back 2e-4 off. Each segment is scaled by its own peak, and each bin keeps its sum's exponent.
- **MedianFilter** and **Hampel**: the midpoint of two values halved each first, so a subnormal beside 2^-1000 or more
  lost its last bit ((2^21 + 1)eps and 2^-1000 gave a midpoint an ulp low). The sum is scaled by 2^54 wherever it fits,
  so it rounds once.
- **TimeDelayEstimate.CrossCorrelationPeak**: the exact tie-break ran up to the main budget of terms, each far dearer
  than a flop, so ones(640) against ones(524288) took 30 s. Its budget is about one FFT's worth of terms (1.6 s).

Not counted: Fft.Inverse breaks exact subnormal ties inconsistently ([15eps, 0 x5] gives 2eps and 3eps for 2.5eps, the
FFT's documented rounding), PeakInterpolation's height near a zero peak from three negative samples (1e-14, inherent),
and a GCC-PHAT near-tie (R168-1). The rest is R208-1.

### Round 207: Windows satellite, twelfth pass (v2026.11.22.0)

Two fresh reviewers (correctness, robustness): 9 bugs, none severe, so section 15 is re-queued (9 >= 3). Both found
the deep registry keys, the deletes of an over-long path, the out-of-order privilege scopes and JobObject.Start's
arguments; the robustness reviewer also the exited process and the DIB header, the correctness reviewer the Complex
round trip. R193-2 (with R189-2 and R206-2, the same overrun) was fixed from the bug list. The tests are in
`WindowsReview207Tests` (11; each fails on the old code, run by name under a 60 s timeout; the exited-process one only
on one core at low priority, where the old code read a stale sample 33 to 41 times in 60, and never at full speed in
180). Clean: JoinArguments over 300,000 random argument lists through CommandLineToArgvW, PrivilegeScope.List against
`whoami /priv`, PowerScheme against `powercfg /list`, ScreenCapture byte-exact at 200% from a DPI-unaware process,
SingleInstance's acquire and dispose races, and 600,000 fuzzed DecodeDib and DecodeDropFiles inputs.

- **SingleInstanceApp.Start** (R193-2, R189-2, R206-2): against a primary that took the connection and stalled, the
  first forward spent the timeout and the takeover check's second forward got its own 1 s floor, so a 1 s timeout
  took 2 s (3 s under load); with no listener, a 1.5 s timeout took 3 s. Every attempt now shares one deadline, at
  least 1 s (so a zero timeout still reaches a running primary) and never more.
- **RegistryUtils**: Win32 opens at most 32 levels in one call, so a key deeper than that read as absent (KeyExists
  false, GetValue the default), DeleteKey left it and reported it missing, and SetValue could not write one. Long
  paths are opened and created 16 levels at a time.
- **RegistryUtils.DeleteKey and DeleteValue** (R110-3): a path with a name over 255 characters threw
  ArgumentException, although no such key can exist and both return false for a missing key, as the readers report.
- **RegistryUtils.GetValue<Complex>**: SetValue wrote "<1.5; -2>", which Complex.Parse refuses, so it read back as 0.
  That form is parsed.
- **PrivilegeScope** (R164-1): disposing the outer of two scopes for one privilege turned it off while the inner was
  still open. Scopes are counted per privilege; the last to close restores the state from before the first.
- **PrivilegeScope.Name** (R184-1): kept the caller's casing ("seshutdownprivilege"); it is Windows' spelling.
- **ResourceSampler.Sample** (R131-1): just after an exit the process can still be in the system's list, so it read
  0% and a working set of tens of kilobytes instead of throwing as documented. It checks for the exit after reading.
- **JobObject.Start** (R106-7, R131-1): a blank file name or working directory and a command line over 32,766
  characters threw Win32Exception naming only the file; they throw ArgumentException, and a missing working directory
  DirectoryNotFoundException. A CreateProcess failure now gives Windows' reason.
- **ClipboardUtils.DecodeDib**: a BI_BITFIELDS header of 41 to 51 bytes put the colour masks across its end, and the
  mask bytes were decoded as pixels. It is refused.

Not counted: SetImage then GetImage drops alpha, as GetImage documents (it keeps alpha only with a declared mask).
The rest is R207-1, which replaces R184-1. In the second full verify a section 14 timing test (RangeMap descending
inserts, bound 4 s) took 4.9 s beside the other suites and about 1.5 s alone three times: R207-2.

### Round 206: Concurrency, twelfth pass (v2026.11.21.7)

Two fresh reviewers (correctness, robustness): 5 bugs, none severe, so section 10 is re-queued (5 >= 3). Both found
the quoted-semicolon and large-bucket bugs; the robustness reviewer also the leading space and the concurrent Dispose
(R97-3), the correctness reviewer the inline Dispose (R128-1). Every one was reproduced on .NET 8 and .NET Framework
4.8. The tests are in `ConcurrencyReview206Tests` (7: six failing on the old code, run by name under a 60 s timeout,
and a guard that a Dispose from a callback on the work's token, run by the first Dispose's cancel, does not wait on
the loop). Clean under stress: AsyncLock, KeyedLock, Bulkhead and PauseTokenSource under random cancellation; Jitter
over 2 million extreme draws; CircuitBreaker against a naive model over 20,000 trials; Throttle and CircuitBreaker at
TimeSpan.MaxValue with the clock at its limits.

- **WhichExe.Find** split PATH at every `;` before removing quotes, so a quoted folder holding one (`"C:\a;b"`) became
  two broken entries and the command was not found; cmd ran it. Windows splits only outside quotes.
- **WhichExe.Find** did not trim an entry, so `C:\a; C:\b` (a leading space) was read as a relative folder on .NET 8
  (not found) but trimmed on .NET Framework (found), as cmd does. Spaces around an entry are dropped on Windows.
- **PeriodicWorker.Dispose** (R97-3): the second of two concurrent calls returned at once while the first was still
  waiting, the iteration still running (20 of 30 rounds). Every Dispose waits for the loop.
- **PeriodicWorker.Dispose** (R128-1): a Dispose from a continuation the work completed inline (a TaskCompletionSource
  without RunContinuationsAsynchronously), or straight from the work's synchronous part, waited for the loop it was
  running on and hung. It stops the loop without waiting, as from onError.
- **TokenBucketRateLimiter** (R183-1): above 2^53, `tokens - count == tokens`, so a bucket of 1e17 granted every
  acquire and never emptied. The count is kept exact as a pair of doubles (a two-sum carry), and the full check uses
  the exact count; a 1e16 bucket polled each millisecond still refills (round 128).

Not counted, logged: the Windows suite's SingleInstanceApp forward-timeout test took 3.04 s against its 2.5 s bound in
two full verifies running beside the core suite (rounds 205 and 206), passing alone every time; three times the 1 s
timeout suggests a stage that restarts it, so it is R206-2 for section 15's reviewers. A Documents test (loads during
saves, which wants over 100 loads) saw 97 once under the same load. The rest is R206-1, which replaces R183-1.

### Round 205: Binary formats & images, twelfth pass (v2026.11.21.6)

Two fresh reviewers (correctness, robustness): 1 bug, none severe, so section 6 is clean (1 < 3). The correctness
reviewer found it; the robustness reviewer confirmed none. The randomized differential trials (4,000 PNGs from an
independent encoder over every colour type, depth, filter and Adam7, through the array and a 1-to-7-byte stream;
42,000 corrupted PNGs and 30,000 corrupted BMPs; the codecs and checksums against BCL, BigInteger and bitwise
references; SvgUtils.Number against an exact reference over 200,000 doubles; DrawLine against Bresenham over 200,000
lines) found nothing else on .NET 8 or .NET Framework 4.8. The tests are in `BinaryImagesReview205Tests` (2; each fails
on the old code, run by name under a 60 s timeout): every character from U+2300 to U+2BFF and the two other blocks
graded against Unicode 15.0's East_Asian_Width W list.

- **SvgUtils.EstimateTextWidth**: the docs give emoji a full em, but the wide emoji in the Basic Multilingual Plane
  (the watch U+231A, the high-voltage sign U+26A1, the check mark U+2705, the star U+2B50 and the rest of
  East_Asian_Width W from U+2300 to U+2BFF), Hangul Jamo Extended-A and the vertical forms measured 556, as did a
  symbol or keycap digit in its emoji form (U+2764 U+FE0F). They take an em; a letter before U+FE0F keeps its width.

A Windows-suite timing test (SingleInstanceApp's 1 s forward timeout took 3 s) failed once in the first verify under
load and passed three times alone and in the full suite; nothing in that assembly changed. Base58's measured cost
(R191-1) and the minor suspects are logged as R205-1, which replaces R191-1.

### Round 204: WinForms + tooling + docs, twelfth pass (v2026.11.21.5)

Two fresh reviewers (correctness, robustness): 2 bugs, none severe, so section 17 is clean (2 < 3). Both were R132-1
suspects, confirmed by the robustness reviewer; the correctness reviewer confirmed none. The tests are in
`WinFormsReview204Tests` (2; each fails on the old code, run by name under a 60 s timeout): the smooth boundary over
60 random zooms from 3 to 3e4 and positions, graded against ViewToContent, and 2,000 random modifier lists graded
against the set of modifiers they name.

- **PlotPictureBox** (R132-1): with Smooth on, a magnified view was left to GDI+'s bicubic stretch, which misplaces
  pixel boundaries in proportion to the zoom (a black|white boundary meant for view x 100.25 landed at 92.6 at zoom
  1000, 85.5 at 2000 and 141 at 5000), so overlays drawn through ContentToView missed the image once MaxZoom was
  raised. A magnified smooth view is now interpolated in the control (bicubic, a = -0.5, premultiplied, the edge pixels
  repeated so the border stays solid), as the nearest-neighbour path already maps each pixel itself; four rows are
  held at a time, so a large view at a zoom just above 1 does not allocate per source row.
- **MenuShortcut.TryParse** (R132-1): "Ctrl+Ctrl+S" parsed as Ctrl+S. A repeated modifier is refused (Parse throws
  FormatException), as nothing else Format never writes is taken.

Refuted: R185-1's coverage.ps1 -targetdir quoting (each argument arrives whole on PowerShell 5.1 with a space in the
path); FormState.Serialize writing text TryParse refuses for a state without a positive size or with an overflowing
edge (TryParse documents both limits, and Capture never makes such a state). Not counted, logged as R204-1.

### Round 203: WPF satellite, twelfth pass (v2026.11.21.4)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 16 is re-queued (8 >= 3). The
robustness reviewer confirmed seven (three of them R182-1 suspects), the correctness reviewer three (two shared). The
tests are in `WpfReview203Tests` (8; each fails on the old code, run by name under a 60 s timeout): the one-unit step
graded against decimal arithmetic over 50,000 random values and place counts, and the keystroke filter at
int.MaxValue places against the same filter with its 9s built in full over 20,000 random values and bounds.

- **NumericInputBehavior.StepValue**: a step past double.MaxValue was clamped only after rounding, so at DecimalPlaces
  above 0 the rounding parsed "Infinity.00" and threw FormatException out of the arrow-key and mouse-wheel handlers.
  The sum is clamped first.
- **NumericInputBehavior.StepValue**: a step under half a display unit added one unit in binary and rounded to
  nearest, which rounded straight back where a unit is one or two ulps: the arrow keys did nothing at
  -48183132800358.88 (two places) or -44584.89780993429 (eleven). The neighbouring double is rounded toward the step,
  which gives the next shown value.
- **NumericInputBehavior.IsAcceptablePartial**: each keystroke after the separator built a string of DecimalPlaces 9s:
  OutOfMemoryException at int.MaxValue, 0.2 s per keystroke at 50 million. At most 30 are built; more cannot move
  the parsed double.
- **BindingFailureListener.WriteLine** (R182-1): only "Error:" lines were recorded, so a critical line was always
  dropped and a warning even with IncludeWarnings. Critical and (with IncludeWarnings) Warning lines are recorded, as
  on the other three entry points.
- **BindingFailure.Path** (R182-1): the path ran to the first ';', so "Map[a;b].Name" read "Map[a". It runs to WPF's
  "; DataItem=", or past bracketed indexers when that is missing.
- **PlotPresenter.ResetView**: a MaxZoom below about 5e-8 set a scale WPF treats as singular, after which
  ViewToContent threw InvalidOperationException. Such a view is refused, as ZoomAbout and Fit refuse it.
- **ValidationRuleSet Range**: a NaN bound was refused for double, float and Half only; an NFloat NaN minimum was
  accepted and ignored, and a NaN maximum was reported as "min must not exceed max". Any INumberBase type is checked.
- **AsyncRelayCommand.ExecuteAsync** (R182-1): a CanExecuteChanged handler that threw at the start stopped the
  operation from running, and was then reported twice. The operation runs; a single failure is thrown as itself and
  several in an AggregateException, the operation's first.

Not counted, logged as R203-1 (it replaces R182-1).

### Round 202: Logging, diagnostics, CLI, twelfth pass (v2026.11.21.3)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 12 is re-queued (8 >= 3). The
correctness reviewer confirmed six, the robustness reviewer three (one shared, and two of them R116-4 and R116-6, open
since round 116). The tests are in `LoggingReview202Tests` (9; each fix's test fails on the old code, run by name under
a 60 s timeout): AnsiStyle graded against a terminal model over 5,000 random nested sequences, ProgressBar against
exact k / w over 20,000 random widths, every order of up to six signed zeros, and the rewritten formatter escaping
against the previous algorithm over 20,000 random values and delimiters.

- **AnsiStyle.Bold / Dim**: bold (1) and dim (2) shared one group, so a 22;2 inside Bold left the text dim but not bold
  (the 2 counted as restoring bold's group), and 22;1 inside Dim the same. They are separate groups that 22 ends
  together.
- **EnvironmentReport.ToText**: a value of several lines put its later lines at column 0, where they read as entries
  of their own. Later lines are indented under the entry.
- **ExceptionUtils.Describe**: a Data key of several lines was written as is (values were indented), so a second line
  could read as a new root exception. Keys are indented like values.
- **ConsoleApp.Run / RunAsync**: an unexpected failure is documented as one line on stderr, but a multi-line message
  took several, as did a usage error's ArgumentException on .NET Framework ("Parameter name: ..."). Line breaks become
  spaces.
- **ProgressBar.Render** (R116-4): the "a rounding short of a whole block" tolerance was a fixed 1e-9, too wide at 20
  blocks (0.95 - 3e-11 drew 19 blocks beside 94%) and too narrow at large widths (0.29 at 1e8 filled 28,999,999). It
  scales with the product (a few ulps).
- **MetricsRegistry**: Min, Max and the quantiles depended on the order signed zeros arrived ([+0, -0, +0] gave a P50
  of -0 beside a Min and Max of +0; [-0, +0, +0] a Max of -0). -0 now orders below +0 throughout.
- **StructuredTextFormatter** (R116-6): escaping was O(message x delimiter): 4.8 s for a 100,000-character message
  under a 16,384-character delimiter, 0.4 s for the overlap trim alone. Delimiter removal and the trailing-overlap trim
  run on a KMP automaton, linear in the value.
- **FirstChanceMonitor.Observe**: a clock that threw left the exception uncounted and unreported. It falls back to the
  system clock, as Logger does.

Refuted: exponential AggregateException.ToString on .NET Framework within the 64-level limit (quadratic: 229,334
characters in 13 ms at 64 levels), and formatter field splitting (two fuzzes, 500,000 lines, none wrong). Not counted,
logged as R202-1.

### Round 201: Statistics, twelfth pass (v2026.11.21.2)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 3 is re-queued (6 >= 3). The
correctness reviewer confirmed four, the robustness reviewer two (one of them R140-1, open since round 140). The
tests are in `StatisticsReview201Tests` (6; each fails on the old code, run by name under a 60 s timeout): the fits
graded for scale invariance under 2^+-1000 over 300 random samples and against mpmath, the beta fraction against
its gamma and chi-square limits in all four copies, and the regression slope against its exact sign over 400 random
integer designs.

- **DistributionFit.FitLogNormal**: the log of each value was a difference of two logs near +-693 (each rounded to an
  ulp of that size) once a value was more than 50% from the reference, so sigma lost about 1e-13 for data far from 1
  (the reviewer's ten values near 2^1000: 0.4865568117100873 for ...1192). The log of the ratio is taken directly
  wherever the ratio is a normal double.
- **DistributionFit.FitGamma**: the shape and scale lost about 1e-13 the same way.
- **DistributionFit.FitWeibull**: the shape lost about 1e-14 the same way.
- **DistributionFit.FitGamma**: a fitted scale below the smallest normal double (5.4e-309 for normal data near 1e-307)
  was refused as "outside the double range", and FitAll left Gamma out without saying so. Only an infinite or
  non-positive scale is refused now.
- **The incomplete beta's continued fraction** (Distributions, SpecialFunctions, Anova, GaugeRR): n*(b - n) was formed
  before the * x and overflowed for b near double.MaxValue, so I_x(a, b), FCdf, FSurvival and
  Anova.FUpperTailProbability at a denominator df of 8.5e307 or more came out NaN where they are the chi-square limit
  (FSurvival(0.5, 1e8, MaxValue) is 1). (b - n) * x is formed first. FQuantile there, which the reviewer suspected
  inherited the NaN, now matches the chi-square quantile.
- **MultipleRegression.Fit** (R140-1): with y near 1e300 over x near 1e-300 and an exact slope of 0, the scaled
  solve's rounding crumb times the unit ratio (2^1995) was past double.MaxValue, so the slope read -Infinity
  (x = {1, 2, 3, 4} * 2^-1000, y = {1.001, 1, 1, 1.001} * 1e300; LinearRegression gives 0). A coefficient that
  overflows is solved again in exact rational arithmetic (fraction-free elimination on the normal equations), so it is
  0 or an infinity of the exact sign.

Refuted: R108-4 (the F tails at d1 near 2.3e-308 under d2 up to 1e308 match mpmath to about 1e-16) and R186-1's
Mann-Kendall median of -inf and +inf (unreachable: only adjacent pairs can give an infinite slope). Not counted,
logged as R201-1 (it replaces R186-1).

### Round 200: Filters & control, twelfth pass (v2026.11.21.1)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 1 is re-queued (6 >= 3). The robustness
reviewer confirmed four; the Kalman gain case is the mirror of its kept-weight case, found while fixing it, and the
windup rounding surfaced in the regression test for the first. The tests are in `FiltersControlReview200Tests` (6;
each fails on the old code, run by name under a 60 s timeout), graded against products formed from logarithms over
5,000 random extreme-range trials each.

- **PidController derivative**: kd * change was formed before dividing by dt and overflowed where the result fits
  (kd 1e245, change 1e278, dt 1e272 gave -Infinity, not -1e251); the windup fallback in units of 2^-600 overflowed the
  same way, so the output could land on the wrong limit (+1 for -1). The product and quotient are taken in an order
  that neither overflows nor goes subnormal where the result is normal.
- **PidController derivative on error**: the difference of two rounded errors lost a small measurement change next to
  a large setpoint (setpoint 1e17, measurement 0 then 1 gave 0, not -1). It is now the setpoint's change less the
  measurement's.
- **PidController windup**: the integral that puts the output at the limit can round (-1 - 1e234 is -1e234), and the
  output then came out 0 where it is the limit by construction. A step held at the limit returns the limit.
- **KalmanFilter1D.Update**, P' >= R: the weight kept for the prior underflowed to 0 where the kept term does not
  (estimate 1e150 with weight 1e-400 is 1e-250; the result was the measurement, 1e-290).
- **KalmanFilter1D.Update**, P' < R: the gain underflowed the same way, so a measurement of 1e300 with gain 1e-400 left
  an estimate of 0 rather than 1e-100. Both terms are formed as x * R / P' (or x * P' / R) in a safe order.
- **FirstOrderLag.Update**: e^(-dt/tau) underflowed to 0 past about 745 time constants, dropping a large value whose
  decayed part is still well above the input (1e300 after 800 tau is 3.7e-48, not the input 1e-300). The decay is
  applied in thirds where the factor underflows.

Refuted: R190-1's FiltFilt band-stop gap (against an exact filtfilt on the same sections ToolBelt's error is 1e-13 or
better; SciPy's own reaches 7.5e-12). Not counted, logged as R200-1 (it replaces R190-1).

### Round 199: Collections, twelfth pass (v2026.11.21.0)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 9 is re-queued (4 >= 3). The robustness
reviewer confirmed none; three of the four were the correctness reviewer's "plausible" items, counted here because
each contradicts a doc or throws where nothing says it should. The tests are in `CollectionsReview199Tests` (5; each
fails on the old code, run by name under a 60 s timeout; the two ToString ones fail on .NET Framework and pass on
.NET 8 either way). The text is graded against an independent reference (the exact decimal expansion cut to p digits
either way, each candidate read back by an exact BigInteger parser) over 6,000 random doubles and floats, and on .NET 8
against "R" as well. Two older tests held the old behaviour and were updated: round 4's BitSet test used set operations
that change nothing, and round 189's round-trip check read the text back with .NET Framework's Parse, which is not
correctly rounded (it now reads it exactly).

- **DataTableLite.ToString** on .NET Framework: the shortest round-trip search ran over the runtime's "E" formatting
  and Parse, which are not correctly rounded there, so 1,110 of 59,949 values came out with a digit too many
  (3.3515445649494128E-254), not the nearest (853041653452.9262 for ...9261), a tie broken the other way (float
  2397517.3 for .2), or as text that reads back as another double (7.359979136196296E+248). The digits are now worked
  out exactly against the value's rounding interval, the same on every runtime.
- **DataTableLite.OrderBy, OrderByDescending**: a column mixing int, long, double or decimal threw "Object must be of
  type Double." from the default comparer, which the docs did not say. Numbers of different types compare by exact
  value (long 2^53 + 1 above double 2^53), NaN first; documented.
- **ObjectPool.Return**: two Returns for the last slot both passed the capacity check and both reset, and the later
  was dropped after its reset, where the docs say a dropped instance is not reset. A Return still resetting holds its
  slot.
- **BitSet**: Set of a bit already set, Clear of a clear one, or a set operation that changed nothing stopped running
  enumerators. Only a change of some bit counts as a modification, as in HashSet; documented.

Refuted: ReservoirSampler's skip guard (the rounded threshold is within half an ulp, so a skip that passes it is a
whole ulp below the exact bound and cannot wrap), and CountingBloomFilter's Remove after saturation (no false
negatives; a saturated slot staying set is documented). Not counted, logged as R199-1 (it replaces R189-1).

### Round 198: Signal analysis, twelfth pass (v2026.11.20.16)

Two fresh reviewers (correctness, robustness): 15 bugs, none severe, so section 2 is re-queued. Four were open suspects
now confirmed and fixed (R104-2, R115-4, R115-5, R115-6) and one a logged limitation fixed (R153-1). The tests are in
`SignalReview198Tests` (16; each fails on the old code, run by name under a 60 s timeout). Where practical they are
differential: Welch and Multitaper against the same samples at normal scale scaled down exactly, Linear and ToRate
over sweeps of coincident points, ToRate's length and NearestBin against exact BigInteger midpoints, Decimate near
overflow against the in-range sum scaled by 2^100, MedianFilter on a changing list against the same values in an array.

- **Convolution, Multitaper, WelchPsd: ScaleB** below 2^-1000 took a 2^-1000 step that could land subnormal and round,
  then rounded again: an element c*2^-1050 = (1.5 - 2^-52) eps came out 2 eps. It now goes exactly to the bottom of the
  normal range first, so only the last step rounds.
- **Convolution.Convolve, CrossCorrelate**: the rescue scaled an overlap by its peaks, so a product far below them
  flushed to 0, and when the big ones cancelled it was the answer: [2^1000, 2^1000, 3*2^-1000] * [2^1000, -2^1000,
  2^1000] gave 0 for 3. Such an element is summed exactly and rounded once.
- **CycleMeasurements**: crossings were absolute positions, rounded to the precision of their index before the
  differences, so a perfect clock read jitter rising with record length (8.7e-11 peak to peak at 1e6 samples). The
  index and fraction are kept apart.
- **CycleMeasurements (R153-1)**: a level within the hysteresis band of the capture's minimum or maximum (0.05 on a 0/1
  square wave) could never arm, and gave 0 cycles. The band is clipped to the capture.
- **Resample.Linear, ToRate**: an output on an input instant came from a rounded step, so it was interpolated (6 ulps
  off, and NaN from a NaN neighbour). Positions are formed from integers, or i*source/target, rounded once.
- **Resample.ToRate length**: N*(t/s) rounded the ratio first (1269 samples from 7.8 to 11.7 Hz gave 1904 for 1903).
  It is corrected against the exact halves.
- **Resample.Decimate**: the overflow retry summed x/factor, so three double.MaxValue samples gave inf and constant
  blocks drifted. The retry is the in-range sum scaled by 2^-32.
- **SavitzkyGolay**: the doc said polynomials pass through unchanged; at the edges they come back within an ulp or so,
  now documented. A window of -0 alone gave +0 except at the identity order; it gives -0.
- **LevelConversions.DbToAmplitude, DbToPower**: rounding db/20 was amplified by ln 10*|db/20| (120 ulps at 6001 dB).
  10^(db/20) is split as 10^q*10^(r/20) with r exact.
- **Hampel (R115-4)**: an infinite median made the MAD NaN, so a finite sample among infinities was never flagged. A
  value equal to the median deviates by 0.
- **TimeDelayEstimate.CrossCorrelationPeak**: the lag was right but the value came from samples divided by their scales
  before multiplying, which underflowed ([1e200, 1e-200] against [-1e200, 1e-200] gave 0 for 1). A value whose bound is
  not small beside it is computed exactly.
- **FrequencyGrid.NearestBin**: f*N/fs rounded twice, so a frequency just off a midpoint went to the far bin. The bin
  is corrected against the exact midpoints.
- **PeakInterpolation.Parabolic**: (l - c) + (r - c) overflowed with each difference finite, and the offset came out 0.
  That case goes through the quarters too.
- **MedianFilter (R115-6)**: a list whose values changed between reads threw Array.Copy's bare length error. The input
  is read once.

Refuted: R104-6 (Goertzel's alias is exact). Not counted, logged as R198-1.

### Round 197: Numerics core, twelfth pass (v2026.11.20.15)

Two fresh reviewers (correctness, robustness), aimed at the files round 177 left uncovered: 15 bugs, none severe, so
section 4 is re-queued. The tests are in `NumericsCoreReview197Tests` (14; each fails on the old code, run by name
under a 60 s timeout; the degenerate-source ones hung there). Where practical they are differential: Lerp against the
same ends scaled down by 2^600, Remap against exact mantissa-and-exponent arithmetic, RoundToMultiple against
`decimal`, the subnormal standard deviation against exact integer quanta, FromBase against ToBase.

- **Interpolation.Lerp, Vector2.Lerp**: a + (b - a)*t overflowed for finite ends and a result in range:
  Lerp(1.5e308, 0, 1.5) gave -inf for -7.5e307. An overflowing product is redone in halves.
- **Interpolation.Remap, MinMaxScaler.Scale**: t = (value - lo)/(hi - lo) overflowed on a subnormal input span
  (Remap(1, 0, 1e-310, 0, 1e-300) gave inf for 1e10) and underflowed on a huge one (1e-320 onto [0, 1e300] gave 0).
  Then the product is formed with its exponents kept apart.
- **ScatteredInterpolation.InverseDistance**: radius = +inf excluded samples whose distance overflowed to inf, so
  one sample at (1e308, 1e308) gave NaN from (-1e308, -1e308) though no radius gives 2. An infinite radius excludes
  nothing.
- **Rounding.RoundToMultiple**: value/multiple read the binary values, so 0.15 to a multiple of 0.1 gave 0.1 (0.15 is
  stored just below), and the product k*multiple rounded again (3.7638018064963644e22 to a multiple of 6 moved). It now
  reads the shortest decimals, as RoundToSignificantDigits does, and rounds once.
- **RandomUtils.NextGamma, NextPoisson**: on a degenerate source (always 0, or always 1 - 2^-53) the rejection loops
  never ended. After 10,000 rejections they throw InvalidOperationException.
- **RandomUtils.NextGaussian**: mean + sd*z overflowed for a result in range (mean -1.5e308, sd 1.5e308). It is
  redone scaled by 1/16. A draw truly past the range is +-inf, now documented.
- **RandomUtils.NextDouble(min, max)**: reversed bounds threw a bare ArgumentException, where the other range
  methods throw ArgumentOutOfRangeException naming "min".
- **RunningStatistics, subnormal standard deviation**: the mean of {0, 15*2^-1074} rounded to 8 quanta, a whole
  quantum of error in every deviation, and the square root was then rounded twice (root, then scale). The sample SD
  came out 5e-323 for 5.4e-323. Small samples are now stepped scaled by 2^64, and a subnormal root is rounded once,
  exactly. The variance needed no such fix: an integer divisor of a 53-bit m2 never lands within a half-ulp of a
  subnormal midpoint, so its double rounding is harmless.
- **RootFinding.Bisection, Brent**: a bracket around a pole (1/x on [-1, 2]) closed on the sign change and reported
  Converged with a residual of 1e16. Convergence now needs |f| no larger than at the bracket's ends; a pole reports
  failure, and a jump still converges.
- **EngineeringNotation.Format**: -0 printed "0", which parses back as +0. It prints "-0".
- **EngineeringNotation.TryParse**: with a prefix the mantissa's own exponent went through long.TryParse, so
  "1e-99999999999999999999 k" failed where the same number without the prefix parsed as 0. Both saturate the same way.
- **BaseConverter.FromBase**: past long.MaxValue it threw the bare "Arithmetic operation resulted in an overflow.",
  undocumented. It throws a documented OverflowException naming the text and base.
- **UnitConvert.Convert, temperature**: a temperature below absolute zero converted (-500 C gave -226.85 K). It throws
  ArgumentOutOfRangeException naming "value", documented.

Refuted: Akima's derivative on a straight line, Percentile overshoot, NextGamma's Pow(1, inf). Not counted, logged as
R197-1.

### Round 196: Charts, twelfth pass (v2026.11.20.14)

Two fresh reviewers (correctness, robustness), aimed at the files round 178 left uncovered (SvgChart, Waterfall,
TimingDiagram, HexBin, ScatterMatrix, ErrorBarChart, AxisTicks, Palettes): 11 bugs, none severe, so section 5 is
re-queued. AxisTicks now settles every tick bound on the tick values themselves rather than on the quotient
min / step, and the regression tests grade it against a naive count of exact decimal ticks over randomized ranges. The
tests are in `ChartsReview196Tests` (12; each fails on the old code, run by name under a 60 s timeout).

- **AxisTicks.Linear and Time, loose**: the bounds came from min / step with a 1e-9 slack, and the quotient is off by
  more than that once it passes about 1e7. A data end lying on a tick fell outside it, so the axis got a whole extra
  step: Linear(2262700.476, 2262700.486, 9) started at .474, and Time(9866.4, 9866.400905337758, 10) at 9866.3998.
- **AxisTicks.Linear, tight**: the step search counted the same way, so it took a coarser step than fitted:
  Linear(-85.88, -85.87999994163204, 11, false) chose 1e-8 (6 ticks) where 5e-9 gives 11.
- **AxisTicks, tight ticks outside the range**: a tight tick was kept up to a billionth of a step outside the range,
  which is not small next to a range ending near zero. Linear(-9.53, -1.6e-11, 9, false) and Time(9.46e-13, 4.5, 3,
  false) had a tick at 0, and Log10(1e-163, 1.555e-93, false) kept 1e-163 below 1.0000000000000001e-163. HexBin and
  Waterfall draw those ticks as they come. Tight ticks are now exactly inside.
- **AxisTicks, [0, 0] at two ticks**: it opened to [-1, 1], which needs -1|0|1. The doc's only exception to maxTicks is
  a range that straddles zero. It opens to [0, 1].
- **AxisTicks, subnormal ranges**: a range under 1e-300 wide opened to its midpoint +- 1e-300, which crosses zero for
  [1.9e-322, 2.47e-322], and needed three loose ticks at maxTicks 2. It opens on the data's side of zero.
- **AxisTicks.Time labels**: under a minute every tick shares one s.fff format, which keeps 15 significant digits. All
  six ticks of [10, 10 + 5e-14] read "10.00000000000000s". Past 15 digits the exact linear labels are used.
- **SvgChart bars**: a finite bar whose pixel overflowed (1e300 on a 1e-10 range) was dropped, though a bar of 2 there
  draws full height. It is clipped at the edge, as a line to it is.
- **SvgChart legend**: on a plot too narrow for any text the width limit came out 0, which FitWidth reads as no limit,
  so a name ran 150 px past a 50 px canvas. No legend is drawn without room for one.
- **PlotFrame.ClipToRanges**: for a segment with both ends above YMax (1.6e308 to 1 over [0, 2e-323]) the edge
  quotients rounded to t0 = t1 = 1. The clip was accepted with the raw outer end, and SvgChart.Render threw an
  internal ArgumentOutOfRangeException naming "y". Segments with both ends past one edge are rejected exactly.
- **TimingDiagram offset labels**: the "+offset" labels that replace clashing tick labels also round to three
  decimals. A window two ulps wide read "+0.002 ps" twice next to "+4.441e-16 s". When they still clash, they are the
  exact differences of the ticks' decimals.
- **ScatterMatrix title**: the title was written without a width, so 200 W's ran 2,800 px across a 218 px canvas. It
  is fitted around its centre, as the other charts' titles are.

Not counted, logged as R196-1: the SvgChart log axis ignores MaxXTicks; scatter markers overhang the frame; the 4.5e15
step guard; Waterfall's "R" formatting on .NET Framework; a tight axis on a flat range returns the padded ticks;
TimingDiagram's SVG grows with LaneHeight; one ScatterMatrix histogram disagreement with a naive binner.

### Round 195: IO, twelfth pass (v2026.11.20.13)

Two fresh reviewers (correctness, robustness), also checking round 188's fixes and the areas it left uncovered (CSV,
DotEnv, the FileWatcher merge rules): 8 bugs, none severe, so section 8 is re-queued. A randomized model file system
(four names, up to six events, a deletion before each rename onto an existing file; 20,000 trials) now grades
FileChangeCoalescer: the output applied to the files before must give the files after. The rounding fixes are graded
against exact BigInteger midpoints over the whole range, subnormals and overflow included. The regression tests are in
`IOReview195Tests` (7, one a round-trip guard; each other fails on the old code, run by name under a 60 s timeout, the
rounding and word ones on .NET Framework 4.8).

- **ByteSize.TryParse**: digits were scanned with char.IsDigit, so "\u0663 KB" or a fullwidth "\uFF11 KB" reached
  BigInteger.Parse and threw FormatException out of TryParse (and Parse threw it unwrapped). Only ASCII digits count.
- **CsvDialect, CsvBinder**: "nan", "-NaN", "infinity" and "+Infinity" in any case were numbers on .NET 8 and errors on
  .NET Framework, so a file bound on one runtime and failed on the other, and Detect differed. NaN, Infinity and
  \u221E are read in any case, after an optional sign, on every runtime.
- **CsvDialect, CsvBinder (.NET Framework)**: doubles were not correctly rounded: about 2% of long decimals came out a
  unit in the last place off ("9.8598758830020559e-205"). The netstandard2.0 build reads the exact decimal with
  BigInteger and rounds half to even. FormatNumber and the binder's writer checked "R" with the old parse, which agreed
  with it on text that reads back wrong once rounded right; they check with the new one, so writes still round-trip.
- **CsvBinder floats (.NET Framework)**: float.Parse went through a double and rounded twice: "16777217.0000000001"
  gave 16777216, not 16777218. Floats are rounded once from the exact decimal.
- **ConfigBinder doubles (.NET Framework)**: the same unit-in-the-last-place errors (floats were already exact). It
  uses the same block, which CsvDialect, CsvBinder and ConfigBinder each hold: change one, change all three.
- **FileChangeCoalescer**: a rename onto a file deleted earlier in the batch, then deleted itself ("D a; R c->a; D a"),
  reported only c deleted; a's original file vanished from the output. The rename remembers that it replaced a file,
  and reports it gone when it goes or moves on.
- **FileChangeCoalescer**: a file new in the batch at a name it replaced ("D b; C b", or a new file renamed onto a
  deleted b) and then renamed away was reported as "Renamed b -> a", as if b's original had moved. It is a creation
  there and a deletion of b.
- **FileChangeCoalescer**: a renamed file deleted while another file had taken its old name ("R c->d; R a->c; D d")
  dropped the deletion of c's original, and a created file there stayed "Created" though c existed before. The
  occupant is marked as having replaced the original.

Not counted, logged as R195-1: a multi-line quoted DotEnv value; a file changed then renamed reports only the rename; a
single-event replacing move; the wildcard `a.*` not matching `a`; CsvBinder writing a Uri in its unescaped form and
DateTime.MinValue as Local ahead of UTC. Refuted from R172-1: "1 K\u0131B" is rejected, and NBSP parses the same on
both runtimes. IOReview172's range test graded against .NET Framework's own parse; it grades against the exact value.

### Round 194: Documents & config, twelfth pass (v2026.11.20.12)

Two fresh reviewers (correctness, robustness), also checking round 181's fixes: 6 bugs, none severe, so section 7 is
re-queued. The structural probes (PDF xref offsets, entry widths, object counts and stream lengths, a 120-row table
across pages, an empty document; DOCX parts, content types and relationships, illegal XML characters; MarkdownReport
tables under GFM) found nothing else, and the section's tests pass on .NET Framework 4.8. The regression tests are in
`DocumentsConfigReview194Tests` (6; each fails on the old code, run by name under a 60 s timeout).

- **ReportBuilder.ToDocx**: a table of more than 63 columns, which HTML and PDF render, failed the whole document with
  ArgumentException naming "headers" (Word's column limit). It goes in as consecutive tables of at most 63 columns,
  each labelled "Columns 1-63 of 70" and so on.
- **ReportTemplates.Comparison**: a value that shows as zero printed with a sign under formats the zero check could not
  parse back: "-0 %" and "+0 %" under "P0", "(0.00)" under "C", "-0.0 ms" under a literal suffix. The check compares
  the formatted size with formatted zero now.
- **ReportTemplates.Comparison**: a huge percentage printed every digit ("0.##"): 1e-300 to 1 gave a 303-digit
  change. From a million percent it is three significant digits, as tiny ones already were.
- **ConfigBinder**: a DateTime with an offset (`2024-01-01T00:00:00+05:00`) was converted to the machine's zone (Kind
  Local), so one file bound a different time on each machine. It is UTC, as "Z" is; without an offset the value is
  kept as written.
- **ConfigBinder**: a type with properties differing only in case ("Port", "port") had both filled from one key without
  a word, though the binder refuses case ambiguity elsewhere. Configuring one is an error naming both.
- **MarkdownReport lists**: an item that was only a block marker ("+", "1.", "1)", "#", ">") became an empty nested
  list, heading or quote, and its text was lost; round 109's fix covered only rule characters. The marker is escaped.

Not counted, logged as R194-1: a Comparison Delta of +Infinity for -MaxValue to MaxValue; A=Infinity or NaN rows not
flagged; the 0.005% display boundary; a bullet "a\n---" making a setext heading; ConfigLayers.TryGet answering true for
a key holding only null; `$` left for math renderers. `scripts/net48.sh Documents Configuration` does not build on its
own (PdfFeaturesTests needs the Visualization folder), so this section's net48 run names all three.

### Round 193: Security, net, time, twelfth pass (v2026.11.20.11)

Two fresh reviewers (correctness, robustness), also checking round 192's fixes: 6 bugs, none severe, so section 13 is
re-queued again. One was a regression from round 192 (the zone overload's end-of-calendar message). The differential
trials (cron against brute-force matchers, 1.6 million trials and 13 DST zones; IsoWeek against ISOWeek for every day;
CidrRange against BigInteger, the same on .NET 8 and .NET Framework 4.8; both duration parsers round-tripped and against
XmlConvert; PBKDF2, HMAC and AES-GCM against the BCL; BusinessDays; TcpLineClient framing) found nothing else. The
regression tests are in `SecurityNetTimeReview193Tests` (6; each fails on the old code, run by name under a 60 s
timeout, except the TcpLineClient one, which pins the behaviour its corrected doc now describes).

- **HttpDownload**: a resume answered 416 `bytes */1000` for a 1000-byte partial file finished before the MaxBytes
  check, so with MaxBytes 500 the 1000-byte file was kept, though the same file through a 206 was refused. It is held
  to MaxBytes now.
- **HttpDownload**: the same 416 path never called OnProgress, though every other path reports at least once. It
  reports the finished size.
- **CidrRange.TryParse**: round 192 refused a space after the slash but not one before it (`1.2.3.4 /32` parsed).
  Whitespace may surround the whole text only.
- **TcpLineClient.MaxLineLength** (doc): it said a run of invalid bytes counts as one character; each invalid sequence
  the decoder reports does (for UTF-8 often a byte), so `FF FF FF FF` overran a limit of 3 as too long, not as a bad
  line. The code is right (counting a run once would let a peer stream bad bytes forever); the doc is corrected.
- **CronSchedule.GetNextOccurrence(after, zone)** (round 192 regression): its walk stopping early because no later
  day can match (`31 9`, a 31 September) was reported as the calendar's end, not as unsatisfiable, and
  GetNextOccurrences would have returned an empty list for it. It decides as the fixed-offset overload does.
- **CronScheduler**: after a clock step back, a wildcard job (`0 * * * *`, `* * * * *`) was worked out from now, so a
  reading 10 ms back over a minute's start ran that minute again, though `0 0-23 * * *` ran once. A job never runs the
  occurrence it last ran again; a long step back still replays a wildcard job's other minutes, as cron does.

Not counted: a 204 answer leaves an empty file (no content is a faithful result). The R192-1 suspects about an invalid
Content-Range on .NET Framework and TcpLineClient's partial line after a read timeout were refuted; the rest are carried
to R193-1.

In the verify, `SingleInstanceApp_ForwardTimeout_BoundsAStalledPrimary` (Windows suite) took 3,043 ms against its
2.5 s bound; the suite passed on a rerun. It shows a real overrun in section 15 (a 1 s forward timeout takes about
2 s, the second forward keeping its 1 s connect floor), logged as R193-2 for that section's round.

### Round 192: Security, net, time, eleventh pass (v2026.11.20.10)

Two fresh reviewers (correctness, robustness): 11 bugs, one severe, so section 13 is re-queued and leads the twelfth
pass. Five were items of the logged suspect R166-1, now confirmed. The randomized differential trials (CidrRange
against BigInteger, cron against minute-by-minute matchers over a dozen zones, IsoWeek against the BCL for every day,
PBKDF2, HMAC and AES-GCM against the BCL, BusinessDays, UnixTime, TcpLineClient framing against hostile loopback
peers) found nothing else on .NET 8 or .NET Framework 4.8. The regression tests are in `SecurityNetTimeReview192Tests`
(12; each fix's test fails on the old code, run by name under a 60 s timeout, the Hashing one on .NET Framework, where
the old ParamName was null; one more guards growing ranges).

- **HttpDownload** (severe): with Resume, a server that answered every request with the same short range from byte 0
  (`bytes 0-5/20`) made the download loop forever, even with MaxAttempts = 1: each answer rewrote the partial file to
  the same bytes, and the retry for the rest was not counted as an attempt (thousands of requests a second until the
  caller's token stopped it). A short range that adds nothing now fails with HttpDownloadException.
- **CronSchedule** (R166-1): reaching the end of the calendar (`0 0 1 1 *` after 9999-01-02) was reported as "the
  expression is unsatisfiable", and GetNextOccurrences threw away the occurrences it had found. The message says the
  calendar ended, and GetNextOccurrences returns what it found.
- **Hashing.Sha256File and Sha512File**: an empty path failed inside FileStream, with ParamName null on .NET
  Framework, and a null path left the hash object undisposed. Both are checked first.
- **CidrRange.Contains**: a block of IPv4-mapped addresses (`::ffff:10.0.0.0/104`) did not contain the IPv4 address
  it names, though an IPv4 block contained the mapped form. Other IPv6 blocks, `::/0` included, still hold no IPv4
  address.
- **CidrRange.TryParse**: the prefix took a leading zero (`/024`), a space after the slash and any run of zeros, though
  leading-zero octets are refused. It is plain decimal now.
- **SecretsFile.LoadOrCreate**: a folder at the path gave an empty store, and only the first save failed. It throws
  ArgumentException.
- **HumanDuration** (R166-1): `- 5s`, `1d1d` (summed), `1s 1m` (out of order) parsed, but `5 s` did not. The sign is
  directly before the number, each unit appears once and largest first, and a space may come before a unit.
- **Iso8601Duration** (R166-1): `P1W2D` parsed as 9 days, though ISO 8601 makes the week form stand alone, and digits
  past the tick were dropped (`PT1.99999999S` gave 1.9999999 s). Weeks stand alone and finer digits round half up.
- **PasswordHasher.NeedsRehash**: a count of -5 answered false, as though the hash were strong enough. It throws
  ArgumentOutOfRangeException, as Hash does.
- **PasswordHasher.Verify**: strings Hash never writes verified, though the docs say they throw: a leading zero in
  the iteration count, whitespace around the salt or digest, a trailing newline. The base64 must be canonical.
- **TcpLineClient and PortCheck** (R166-1): port 0 was accepted, failing later as a SocketException or answering
  false as though closed. Both throw ArgumentOutOfRangeException.

`-PT0S` (accepted as zero, like `-0`) and a leading no-break space before a HumanDuration (trimmed, as string.Trim
does) are not counted. R166-1 is closed; what it left, with this round's suspects, is logged as R192-1.

### Round 191: Binary formats & images, eleventh pass (v2026.11.20.9)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 6 is re-queued (4 >= 3). Three were
items of the logged suspect R169-1, now confirmed. The randomized differential trials (codecs and checksums against the
BCL, BigInteger and bit-by-bit references; 6,000 valid and many corrupted PNGs; BMP, ImageBuffer, SVG) found nothing
else on .NET 8 or .NET Framework 4.8. The regression tests are in `BinaryImagesReview191Tests` (3, each failing on the
old code, run by name under a 60 s timeout).

- **SvgUtils.EstimateTextWidth**: every astral character that is not zero width counted a full em, so a math-bold A
  or a Deseret letter measured 1000 where the docs give non-ASCII letters the average (556). Wide astral blocks (emoji
  and pictographs, CJK extensions, kana supplements, Tangut) keep the em; the rest take 556.
- **SvgUtils.EstimateTextWidth** (R169-1): U+2028 and U+2029 measured as letters (556); they break a line, as \n
  does, and take no width. (U+0085 was already 0.)
- **SvgUtils.StrokeAttributes** (R169-1): a positive width that writes as 0 at six decimals (1e-7) gave
  stroke-width="0", which draws no stroke; FitWidth had the same rounding fixed. It writes 0.000001.
- **PngReader** (R169-1): the docs said data past the last scanline is always checksummed, but past the drain limit
  (as much again as the image, at least 64 KiB) the rest and the Adler-32 are not checked, by design, as libpng does.
  The docs now say so; the behaviour was already pinned by BinaryImagesReview4Tests, so this fix has no new test.

VarInt's overlong encodings and Fnv1a's lone-surrogate collisions reproduce but are the documented contracts, so
R169-1 is closed. Base58's quadratic time (R129-1) and the rest are logged as R191-1.

### Round 190: Filters & control, eleventh pass (v2026.11.20.8)

Two fresh reviewers (correctness, robustness): 7 bugs, none severe, so section 1 is re-queued (7 >= 3). One was a
logged suspect (R107-3, confirmed); R107-1 was refuted by both reviewers and R107-2 was not reproduced. The regression
tests are in `FiltersControlReview190Tests` (7, each failing on the old code, run by name under a 60 s timeout; the
Biquad one fails on .NET Framework and passes on .NET 8 either way).

- **FirstOrderLag.Update**: `1 - alpha` cancelled over a long step, so a lag at 1 stepped 40 time constants toward 0
  gave 0, not e^-40. The kept share is exp(-dt/tau), taken directly.
- **KalmanFilter1D.Update**: `1 - gain` rounded to 0 when the covariance was far above the noise (x0 1e8, P 1e16, R 1,
  z 0 gave 0, not 1e-8). The prior's share is R/P/(1 + R/P), graded against an exact BigInteger blend.
- **PidGains.Kd** (and Ki) were -0 for a reverse-acting PI, so ToString wrote "Kd=-0" on .NET 8 and "Kd=0" on .NET
  Framework. Zero gains are +0.
- **PidController**: an integral past about 4e488 ran out of the 2^600 units it was kept in, so Ki 1e-300 with an
  error of 1e300 over dt 1e200 stayed at 0 for good (1e200, 2e200, 3e200 expected). It is kept in units of 2^1200,
  graded against Ki * error * dt * k over 4,000 random tiny-gain, huge-step loops, both signs.
- **FirFilter.Design**: an edge whose ratio to the sample rate underflowed to 0 passed validation, so a low-pass then
  blamed taps and a high-pass or band edge was taken as 0; two edges rounding to one ratio made an empty band. Both
  are refused, naming the edge.
- **Biquad.ToString** used the default double format, which on .NET Framework wrote 15 digits (0.30000000000000004 as
  0.3, -0 as 0). It writes the shortest round-trip text on every runtime, graded against .NET 8's "R" on 4,000 random
  sections and read back bit for bit.
- **FopdtModel.FitStep** and **StepResponse.Analyze** (R107-3) blamed values when an explicit initial or final value
  made the step empty; they name that value.

R119-1 and R170-1's anti-windup point (a derivative kick or proportional step past a limit freezes integration for that
step) is standard conditional integration; it is now documented on PidController and closed. The rest is logged as
R190-1.

### Round 189: Collections, eleventh pass (v2026.11.20.7)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 9 is re-queued (8 >= 3). Two were
logged suspects (R127-1, and both halves of R173-1, re-confirmed). The regression tests are in
`CollectionsReview189Tests` (10, each failing on the old code, run by name under a 60 s timeout; the two
DataTableLite ones fail on .NET Framework and pass on .NET 8 either way).

- **BinaryHeap**, **TopN** and **IndexedPriorityQueue**: a comparer that pushed, popped or added to the structure it
  was ordering moved the slots the call had found, so the insert overwrote a live item (one lost, another duplicated,
  no exception); a full TopN threw IndexOutOfRangeException from inside the library. Such a change is refused with
  InvalidOperationException once the comparisons are done, before the call writes anything; the comparer's own change
  stands.
- **DisjointSet.Union** and **Connected** reported a bad a or b as Find's "x".
- **FenwickTree.ValueAt** reported a bad index as RangeSum's "from".
- **Aggregation.Pivot** (R127-1) handed the aggregator its own list, so one that cast it back and added to it changed
  the cell (103, not 3). It gets a read-only view.
- **Aggregation.Summarize** (R173-1): Welford's running mean was off by up to an ulp of the largest value, so a mean
  near zero after cancellation had the wrong sign ([0.1, 0.2, -0.30000000000000004] gave +1.4e-17; exact -9.3e-18).
  The mean is now the compensated (TwoSum) sum over n, with the division's remainder taken exactly, graded against a
  BigInteger mean over 2,000 cancelling groups.
- **DataTableLite.ToString** (R173-1) wrote a float with .NET Framework's "R" there (1.401298E-45 for 1E-45) and -0 as
  "0". One shortest round-trip formatter now runs on every runtime, graded against .NET 8's "R" on 4,000 random
  doubles and floats.

The StratifiedFraction rounding, the Bloom filters' rates at tiny n, and the rest of R173-1 and R157-1 are logged as
R189-1. The verify's Windows suite failed once on a timing bound while another job loaded the machine (it passes alone);
the overshoot is real and is logged as R189-2 for section 15.

### Round 188: IO, eleventh pass (v2026.11.20.6)

Two fresh reviewers (correctness, robustness): 11 bugs, plus one found while writing the tests, none severe, so
section 8 is re-queued (12 >= 3). Two were logged suspects (R94-1, R94-3, re-confirmed). The regression tests are in
`IOReview188Tests` (14, each failing on the old code, run by name under a 60 s timeout; the two .NET Framework ones
fail there and pass on .NET 8 either way).

- **DirectoryUtils** resolved a root ending in a dot or space ("data.", as EnumerateFiles lists it) with
  Path.GetFullPath, which makes it "data", a different folder: Delete removed that folder, and EnsureEmpty, Copy,
  EnumerateFiles and Summarize acted on it, while GetRelativePath threw "not inside". The name is kept, in a relative
  path too.
- **DirectoryUtils.Copy** on .NET Framework did not see a destination inside the source given through the source's
  8.3 short name (that GetFullPath keeps short names), so it copied the source into itself. The existing part of the
  path is expanded to long names first.
- **ZipUtils.CreateFromDirectory** and **TarUtils.CreateFromDirectory** (R94-1) skipped the archive and the run's
  temp file by comparing path strings, so with the archive path reaching the source through a junction (or a short
  name) the walk read its own open temp file and failed. The temp file's unique name, and the archive's name beside
  it, are recognised by any route.
- **TarUtils.CreateFromDirectory** replaced the archive with File.Move(overwrite), which dropped Hidden and refused a
  read-only archive; it replaces as extraction (and ZipUtils) do.
- **TarUtils.ListEntries** lost every entry after a small file on the .NET 8.0.3 runtime: TarReader on the metadata
  guard (a stream that cannot seek) skipped an unread entry's data without its padding, and a third entry failed with
  "Unable to parse number". Each entry's unread data is now read through first.
- **ZipUtils and TarUtils extraction** wrote an entry named as an earlier entry's 8.3 short name ("LONGFI~1.TXT" for
  longfilename.txt) into that file. Such an entry is refused, on the name as written (.NET's GetFullPath expands it).
- **ZipUtils.AddOrReplaceEntry** stamped every copied entry with this machine's host in "version made by": a zip made
  on Unix came back MS-DOS, so unzip there dropped the exec bit and read a backslash in a name as a separator. The
  original values are put back, graded entry by entry against the source archive.
- **ZipUtils and TarUtils create** stored a name with an unpaired surrogate as U+FFFD, so two such files made two
  entries of one name and extraction failed. Such names are refused (IOException; ArgumentException for
  AddOrReplaceEntry's entry name).
- **AtomicFile** with backup (R94-3): a name of 252 characters or more failed with "Unable to remove the file to be
  replaced". It is refused up front with a PathTooLongException naming the backup; nothing is written.
- **TempFile** accepted an extension ending in a dot or space (".", "txt.", "txt ", ".."), which Windows drops, so
  Path named a file that did not exist. Refused.
- **DirectorySnapshot.Take(recursive: false)** on .NET Framework listed with Directory.GetFiles, which also matches
  8.3 names, so "*.txt" took in a.txtx; it walks as the recursive snapshot does.

R112-3 was refuted by both reviewers and stays listed only in the round log. Everything else plausible is logged as
R188-1.

### Round 187: Signal analysis, eleventh pass (v2026.11.20.5)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 2 is re-queued (8 >= 3). Three were
logged suspects (R104-4, R115-1, R115-2, re-confirmed by both reviewers) and one part of R168-1. The regression tests
are in `SignalReview187Tests` (8, each failing on the old code, run by name under a 60 s timeout).

- **PeakInterpolation.Gaussian** fitted its parabola to logs of size 700, which cancel, so the offset depended on the
  scale: -0.000352 for values near 1.906 and -0.002262 for the same times 2^1000. It fits ln(left / c) and
  ln(right / c), so a power-of-two scale leaves the offset unchanged.
- **PeakInterpolation.Gaussian** returned the height as exp(ln c): a flat 1e300 came back 9.999999999999763e299,
  double.Epsilon came back 0, and SpectralPeak on [1, 7, 7, 7, 1] read 6.999999999999999. The height is c times the
  gain, so a flat peak is exact.
- **PeakInterpolation.Parabolic** formed left - 2c + right, which cancels on a near-flat peak: 55,921 ulps off the
  exact vertex. It is (left - c) + (right - c); graded against the exact rational vertex to 4 ulps.
- **Window.Create** (R168-1's suspect): Hann, Hamming, Blackman, Blackman-Harris and flat-top were not symmetric bit
  for bit at almost every length from 4 on. The first half is computed and mirrored.
- **LevelConversions.DbFs** sent a subnormal rms / fullScale to log10 with its digits lost: 1e-300 against 1.4e23
  read -6466.124 dB for -6462.923. Such ratios take the difference of the logs, as underflowing ones did.
- **TimeDelayEstimate.CrossCorrelationPeak** broke exact ties against its "nearest zero" rule: the direct sums that
  refine the FFT's pick round too, so [-2, 0, -1, -2, 2, 2] against [-2e100, -2e100, -1e100, 2e100] gave lag -2 where
  lags -1 and -2 are equal. Sums within their rounding bounds of the best are compared exactly (BigInteger), within
  the same work budget. Graded against BigInteger correlation on both the raw and the scaled path.
- **Resample.ToRate** (R115-1) rounded half lengths to even: 5 samples halved gave 2 and 7 gave 4. Halves round up.
- **FrequencyGrid.NearestBin** (R104-4, R115-2) sent exact ties to the even bin (2.5 to 2, 3.5 to 4). They go to the
  higher bin, as documented now.

Round 187 found Goertzel at 1e20 Hz and fs = 1 returns the exact DC alias, so R104-6's example does not reproduce;
the row stays until the claim is graded at a non-integer ratio. Everything else plausible is logged as R187-1.

### Round 186: Statistics, eleventh pass (v2026.11.20.4)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 3 is re-queued (6 >= 3). Three were
logged suspects (R108-1, R108-2, R118-1), now confirmed. The regression tests are in `StatisticsReview186Tests` (6,
each failing on the old code, run by name under a 60 s timeout).

- **PermutationTest.TwoSample** called rearrangements ties when their mean differences were within 1e-12 relative of
  the observed one, on both the exact and the Monte Carlo path: {5, 1720000000000004} against {2} gave p = 1 for an
  exact 2/3, and {1e13, 1} against {0, 0} gave 1 for 1/3. Splits are ranked by their group-A sum compared exactly
  (two-sum pairs, as Paired already does), with a tolerance of a few ulps of the summed magnitudes. Graded against
  enumeration and a replayed Monte Carlo draw in BigInteger.
- **MeasurementAgreement.BlandAltman** (R108-1): z times the sd overflowed before the bias was added, so nine -MaxValue
  and one +MaxValue against zeros gave an upper limit of +inf for a true 7.9e307. The limits are formed in the
  scaled units the mean and sd are computed in.
- **ControlChart.Individuals** (R108-1): 2.66 times the mean moving range overflowed before the mean was added, so a
  finite limit came out infinite. The same fix. Both are graded against the data over 16, times 16.
- **DistributionFit.FitWeibull** (R118-1): each log was rounded to an ulp of ln(max), so the shape was 9e-4 off at a
  CV of 1e-12 (4e-6 at 1e-10). Logs are taken about the maximum by log1p, as FitLogNormal does.
- **DistributionFit.FitWeibull** (R118-1): the log-likelihood summed terms of size n k ln(lambda) that cancel on tight
  data (3.6e-3 off at a CV of 1e-10, 0.42 at 1e-12), and FitAll ranks by AIC. It is formed from the centred logs.
  Both graded against an independent solve, to 1e-9 for the shape and 1e-11 for the log-likelihood.
- **DistributionFit.FitWeibull** (R108-2) said "All values are equal" for {1, 1 + 4eps, 1 + 8eps}, which FitNormal
  accepts. Only equal values are refused.

Everything else plausible is logged as R186-1. A note for later reviewers: SciPy 1.12's `t.ppf` is good to about
1e-9 and `kstest(method='exact')` was 2.5e-6 off at n = 150; grade against mpmath first.

### Round 185: WinForms + tooling + docs, eleventh pass (v2026.11.20.3)

Two fresh reviewers (correctness, robustness): 5 bugs, none severe, so section 17 is re-queued (5 >= 3). Two were
logged suspects (R105-14, R120-1), now confirmed. The regression tests are `DistributionReferenceReview185Tests` (1)
and the new `scripts/tooling-tests.sh` (9 checks, 8 failing on the old scripts), which verify.sh now runs.

- **gen-distribution-references.py** wrote cancellation noise as reference values: the F series formed the small mass
  as 1 - series at 400 digits, and for masses below about 1e-400 that is noise, so the table held
  FSurvival(30; 300, 1e8) = -1.6e-394 and FCdf(1e-6; 1e9, 3) = -1.67e-393. The true values are below the smallest
  double. A complement below 1e-350 is written as 0 (a mass the series sums directly is kept at any size), and the two
  rows are 0. The test checks every reference mass is in [0, 1] with no negative zero.
- **gen-distribution-references.py** (R120-1): the comment said the series takes "a few hundred terms"; the extreme
  rows take millions. It says so, and where the complement stops being exact.
- **checked-build.sh**: a worktree path ending in a backslash kept it (only "/" was stripped), so the lock went inside
  the folder: the script created the folder, refused it as not a scratch worktree and left it behind, and "dir\" and
  "dir" took different locks, so two runs on one folder were not stopped. Both separators are stripped.
- **gen-incomplete-references.py** (R105-14): a point whose reference now fails was dropped from the rewritten table,
  losing the good row, and the run exited 0. The old row is kept and the run exits 1.
- **test.ps1**: `-Configuration` with no name after it went to every runner as a filter and built Debug, and
  `-Configuration -Configuration Release` built into `bin\-Configuration` and died at the launch. Both are refused
  before any build.

Everything else plausible is logged as R185-1.

### Round 184: Windows satellite, eleventh pass (v2026.11.20.2)

Two fresh reviewers (correctness, robustness): 3 bugs, none severe, so section 15 is re-queued (3 >= 3). The
regression tests are in `WindowsReview184Tests` (3, each failing on the old code, run by name under a 60 s timeout).

- **RunElevated.JoinArguments** let a NUL through: `["a\0b", "c"]` joined to a command line that reads back as just
  `["a"]`. It throws ArgumentException (ParamName `arguments`).
- **RunElevated.Start** passed a NUL in the file name, arguments or working directory to the shell, which cut the text
  there (a different file, or a shorter command line). Each throws ArgumentException with its ParamName.
- **SingleInstanceApp.Start** with a zero forward timeout gave each attempt 1 ms, so a launch that fell between two
  clients (while the listener built its next pipe) was lost to a primary that was up: about 1 in 75. Each attempt gets
  at least 1 s (100 ms still lost launches on a loaded machine); 3,200 zero-timeout launches all arrived.

Everything else plausible is logged as R184-1.

### Round 183: Concurrency, eleventh pass (v2026.11.20.1)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 10 is re-queued (4 >= 3). The
regression tests are in `ConcurrencyReview183Tests` (5: four failing on the old code, run by name under a 60 s timeout,
and a guard that a Dispose with an item still buffered is still reported).

- **ConcurrentPipeline.CompleteAsync** threw "disposed before its buffer drained" when the last item failed and
  `onError` (or another thread) disposed the pipeline: every item had been processed, but the failure path returned
  without a verdict. It records the buffer as drained when nothing is left in it.
- **ConcurrentPipeline.CompleteAsync** threw the same for a pipeline that never held an item, disposed and then
  completed (R165-1). A Dispose that finds the buffer empty counts as drained; an enqueue after it still throws.
- **TokenBucketRateLimiter.TryAcquire** refused 3 from a full bucket of capacity 2.9999999995 as "could never be
  acquired", though its grant tolerance accepts it (R114-7). The capacity check uses the same tolerance.
- **ProcessRunner.Run** (synchronous) waited on a `WhenAll` that completes on a pool thread, so with the pool busy a
  child that met its deadline was reported TimedOut after the whole timeout (3 of 12 concurrent runs on .NET Framework,
  with no cap). It waits on a countdown the reader threads signal.

Everything else plausible is logged as R183-1.

### Round 182: WPF satellite, eleventh pass (v2026.11.20.0)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 16 is re-queued (6 >= 3). The
regression tests are in `WpfReview182Tests` (6, each failing on the old code, run by name under a 60 s timeout).

- **PlotPresenter.ZoomAbout** scaled by target / current, which can miss the clamped target by an ulp: about 2% of
  zooms left the scale just outside [minZoom, maxZoom] (0.019999999999999997 under a minimum of 0.02). The scale is
  clamped after scaling; 20,000 random zooms stay inside.
- **RecordingDialogService.Calls** was the live list (R102-4): a reference taken earlier grew with later calls, and a
  cast to `List<DialogCall>` could clear the record. It is a snapshot.
- **RuleBuilder.Range** compared strings in the current culture when T was `object` or `IComparable` (only T ==
  string was ordinal): "chyba" failed ["a", "d"] under cs-CZ. Strings compare ordinally under any T a string is.
- **NumericInputBehavior** accepted text past the separator that no further digit could bring into range: "1.5" and
  "0." for [5, 10] at two decimals ("1.5" became "5.00" on losing focus), and the negative mirror. Graded against
  every completion of the text over 20,000 random ranges.
- **NumericInputBehavior** refused what its own Coerce and StepValue write, so it could not be edited (R175-1):
  "10" for Minimum 10 above Maximum 5, "0.3" for [0.25, 0.26] at one decimal. The filter's limits are those the clamp
  maps the range's ends to.
- **NumericInputBehavior.Normalize** left a lone sign or separator ("-", ".", "-.") in the box on losing focus
  (R175-1): the filter accepts it while typing and Coerce reads no number in it. It is cleared.

R117-1 is refuted: NumericRangeValidationRule accepted and refused exactly what WPF's DoubleConverter does over
300,000 random texts in 10 cultures. Everything else plausible is logged as R182-1.

### Round 181: Documents & config, eleventh pass (v2026.11.19.29)

Two fresh reviewers (correctness, robustness): 12 bugs, none severe, so section 7 is re-queued (12 >= 3). The
regression tests are in `DocumentsConfigReview181Tests` (12, each failing on the old code, run by name under a 60 s
timeout; the float and Unicode ones on .NET Framework 4.8, where those bugs live).

- **ConfigBinder** bound a section into a computed get-only property (`Child => new Inner()`) and lost it silently. A
  get-only property whose getter returns a new instance on each read is now an error.
- **ConfigBinder** read a property whose name holds the separator (`Port_Name` beside `Port` with "_") as the other's
  nested key: valid settings were refused ("nested keys cannot be bound"), and `Max__Size` with "__" created an empty
  `Max`. Each property's keys now leave out those of a longer sibling.
- **ConfigBinder and EnvironmentVariables** echoed the whole raw value in a conversion error (a 1,000,000-character
  value made a 1,000,000-character message). Values are cut at 100 characters, never inside a surrogate pair, with the
  length.
- **ConfigBinder** refused `-922337203685.4775808s` (TimeSpan.MinValue): the range check ran on the magnitude before
  the sign.
- **ConfigBinder** on .NET Framework bound a float rounded twice (as a double, then to even):
  `1.0000000596046448` was 1, not 1.0000001. The narrowed double is now checked against the exact decimal at the
  midpoints on each side; 3,000 random floats, just above, below and on each midpoint, match on both runtimes.
- **ConfigLayers** let a null in a higher layer hide a lower layer's value: provenance named the null's layer, and Bind
  took the property's default, from no layer at all. A null is unset, as the binder reads it.
- **SettingsStore** loaded a file holding only `null` as defaults with no warning, and the next save replaced it
  (R160-1's suspect). It is set aside like a corrupt file. The class doc now says a UTF-16 or UTF-32 file with a
  byte-order mark is read as such, as it always was.
- **ReportBuilder** slugs dropped U+2028 and U+2029, which the heading writes as spaces, so the TOC link pointed at
  `#ab` beside the `a-b` anchor.
- **ReportBuilder** slugs on .NET Framework dropped the letters, marks and numbers of newer Unicode versions (Adlam,
  Osage, Syriac Supplement, CJK extensions: 27,397 code points its tables call unassigned, plus U+111C9, a mark they
  call punctuation). A table generated from .NET 8's keeps them on every runtime.
- **MarkdownReport.Raw** trimmed the first line's indentation, so an indented code block became a paragraph. Only
  leading blank lines and trailing white space are trimmed.
- **ReportTemplates.Comparison** never flagged a change from 0 when the threshold was above 0 (the default 5 among
  them), and said no metric had changed.
- **ReportTemplates.Comparison** printed "-0" in the A and B columns (-0.0, or a value rounding to zero); the Change
  column never did.

Everything else plausible is logged as R181-1.

### Round 180: Logging, diagnostics, CLI, eleventh pass (v2026.11.19.28)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 12 is re-queued (6 >= 3). The
regression tests are in `LoggingReview180Tests` (8, each failing on the old code, run by name under a 60 s timeout).

- **MetricsRegistry** mean: once the plain sum overflowed, the fallback summed v * 2^-1024, which made every sample
  below about 4 subnormal. Beside Max, Max, -Max, -Max, a 1e-5 averaged 1.4e-10 relative off and a 1e-20 averaged 0
  (3e-300 alone averaged 0 too). The sum is now exact: Shewchuk partials in two tiers (samples of at least 2^-958
  scaled by 2^-64, smaller ones unscaled), divided by the count and rounded once. 2,000 random trials match a
  correctly rounded reference.
- **MetricsRegistry** mean of one -0 sample was -0 on .NET 8 and +0 on .NET Framework (the clamp to [min, max] used
  Math.Min and Math.Max, which order signed zeros differently). The correctly rounded mean needs no clamp; it is +0 on
  both.
- **Sub-millisecond timeouts** were truncated to 0 by the timers: a HealthCheck probe with a one-tick timeout raced
  Task.Delay(0) and timed out 49 times in 50; a SyslogSink ConnectTimeout of 0.5 ms never waited for the connect, so
  every first send failed into the back-off; an AsyncLogSink drainTimeout of 0.5 ms did not wait at all. Each rounds
  up to a whole millisecond where it waits.
- **ConsoleSpinner.Stop** called by the writer while drawing for the Message setter deadlocked: Stop held the gate
  and waited for the loop, which was blocked on the gate in Tick. Stop no longer waits on the loop from any thread
  that holds the gate.
- **HealthCheck** probes ran on the thread pool. With it starved (one core), a probe that blocked 400 ms against a
  100 ms timeout reported Healthy, as the timeout's timer had no thread. Synchronous probes and an async probe's
  synchronous start now run on threads of their own.
- **Prompt.Choose** trimmed the input but not the option text, so an option " Beta " could not be chosen by its
  text (R135-1's suspect). Both are trimmed.

Everything else plausible is logged as R180-1.

### Round 179: Objects, functional, grids, graphs, intervals, eleventh pass (v2026.11.19.27)

Two fresh reviewers (correctness, robustness): 2 bugs, none severe, so section 14 is clean (2 < 3). The regression
tests are in `ObjectsGraphsReview179Tests` (2, each failing on the old code, run by name under a 60 s timeout). The
reviewers also checked RangeMap, RangeSet and IntervalTree against per-cell brute-force models (3,000 random trials
each, one of 20,000 operations), Flatten and Unflatten on keys built from path syntax, Dijkstra against Bellman-Ford,
and grid search up to int.MaxValue cells, with no failures.

- **ShortestPath.BreadthFirst** walked an edge of weight +inf as one hop: a lone a->b edge of +inf gave Found with
  Distance 1, where Dijkstra and MinimumSpanningTree treat it as no edge (R163-1's first suspect). It is skipped; 400
  random graphs match Dijkstra on the same graph with every finite weight set to 1.
- **ExpressionAccessor.Getter, Setter** given typeof(void) threw InvalidOperationException from the cast to it. Void,
  pointer, by-ref and open generic types are rejected first with ArgumentException("type") (the last three were only
  by chance, from the expression library's own parameter name).

Everything else plausible is logged as R179-1.

### Round 178: Charts, eleventh pass (v2026.11.19.26)

Two fresh reviewers (correctness, robustness): 11 bugs (three found by both, one found by a new test), none severe, so
section 5 is re-queued. The regression tests are in `ChartsReview178Tests` (9, each failing on the old code, run by
name under a 60 s timeout), graded against decimal arithmetic on the printed bounds, pixel scans of the margin, and
renders of the same chart with a large finite value in place of a huge or infinite one.

- **LinePlot pixels**: halves rounded to even, Annotations rounds away from zero, so a scatter point and a VerticalLine
  at the same x landed a column apart for a quarter of the x values. LinePlot rounds as Annotations does.
- **LinePlot scatter markers**: the 3x3 marker of a point on the plot edge painted into the margin and over the frame.
  It is cut to the plot area, as BoxPlot's outliers already were.
- **Annotations raster callout, tail**: the tail took the raw pixel offset (the SVG layer clamps it into the plot), so a
  callout near an edge drew its leader through the margin to the image edge. Both layers share one clamp.
- **Annotations raster callout, marker**: the 5x5 marker of a point on the frame painted 2 px outside. It is clipped.
- **Annotations raster arrowheads**: the wings of a head at a point on the frame reached 4 px into the margin. Raster
  arrows are cut to the plot area.
- **Annotations short arrows**: an 8 px head on a 2 px arrow put its base 6 px behind the tail, and the SVG shaft ran
  backwards past it. The head is no longer than the arrow, its width in proportion.
- **Annotations SVG arrows on a huge frame**: dx² + dy² overflowed past 1.3e154 px, so the direction was zero and the
  head three equal points. The direction is taken from halved, scaled deltas, as the raster layer already did.
- **Annotations labels**: band, arrow and line labels had no vertical clamp. A thin band at the bottom put its label
  below the plot, an arrow whose tail was on the top edge put its label above it, and a line on an 8 px plot put its
  label below it. Baselines are held inside the plot, as callouts' were.
- **BoxPlot huge and infinite values**: (v - min) / range * height overflowed for a Q3 of 4.5e307 (data max
  double.MaxValue) or +inf on a 0..10 axis, so the box sides and upper whisker were dropped instead of clipped at the
  top edge. The mapped fraction is held to a million plot heights.
- **Histogram edges**: lo + span*i/bins in doubles put a quarter of decimal edges an ulp off: 70.9..115.8 in 12 bins
  gave 93.35000000000001, and -0.46 on the edge of [-0.5, -0.42] in 2 bins was counted in the bin below. Each edge
  is worked exactly on the shortest decimals of lo and hi and rounded once (one double division when it is exact, so
  ten million bins take about 60 ms).
- **Colormap channels**: halves rounded to even, so the midpoint of 0..1 gave 0 and of 1..2 gave 2. They round halves
  up, as the chart layers do.

R167-1's suspects are settled: the Histogram and Colormap ones are fixed above, BandChart near MaxValue did not
reproduce in two million trials, and BoxPlot draws nothing for all-infinite data. Everything else plausible is logged
as R178-1.

### Round 177: Numerics core, eleventh pass (v2026.11.19.25)

Two fresh reviewers (correctness, robustness): 9 bugs (two found by both), none severe, so section 4 is re-queued. The
regression tests are in `NumericsCoreReview177Tests` (9, each failing on the old code, run by name under a 60 s
timeout; the ToString one fails on .NET Framework 4.8, where the bug lives), graded against exact BigInteger sums,
differences and shortest digits over randomized trials.

- **MovingAverage**: once the window's plain sum overflowed, every sample was summed scaled by 2^-32, which rounded tiny
  ones: beside 1e308, 1e308, -1e308, -1e308, 3e-300 averaged 38 ulps off and two 1e-320s averaged 0. Samples below
  2^-990 are kept apart and the window is summed exactly; a cancelled sum is divided unscaled.
- **RunningStatistics.Sum**: the plain running sum was read until it overflowed, so 1e308, 61, -1e308 summed to 0. It is
  an exact (Shewchuk) sum in two tiers, rounded once.
- **MonotoneCubic**: the cubic was evaluated in plain doubles, so on increasing data it stepped down by an ulp inside a
  segment. It is evaluated in double-double with an error bound, falling back to exact arithmetic, and rounded once,
  so it is monotone ulp by ulp.
- **StreamingQuantile**: past five values the estimate was the middle marker, which starts at the median whatever p is:
  on 1..6, p = 0.99 jumped from 4.96 to 3. It interpolates the markers at rank 1 + (n - 1)p.
- **Angle.DifferenceDegrees**: to % 360 - from % 360 was rounded at the size of the difference, so (359, 1e-14) gave 1,
  not 1.00000000000001. The difference is carried exactly (TwoSum) and rounded once.
- **RandomUtils.NextGamma** (shape below 1): the scale went into the Gamma(k + 1) draw before U^(1/k) shrank it, so
  NextGamma(0.5, double.MaxValue) overflowed 58% of the time (the true rate is erfc(1), about 16%) and
  NextGamma(0.001, double.MaxValue) gave NaN. The scale is applied last, through logs when the product is tiny.
- **Interpolation.Lerp, Vector2.Lerp**: an infinite first end gave NaN (Lerp(inf, 1, 0.5)); infinite ends take
  (1 - t)a + tb, so Lerp(a, b, t) is Lerp(b, a, 1 - t).
- **Differentiation.Derivative, RichardsonDerivative**: x was never checked, so a non-finite x blamed h or came back
  NaN. It throws ArgumentOutOfRangeException for x.
- **Vector2.ToString, Polynomial.ToString**: on .NET Framework they wrote 15 digits, "(0.3, 0.333333333333333)", which
  do not read back. They write the shortest round-trip digits exactly as .NET Core does (ties to even, exponent form
  from 1E+17 and below 1E-04) on every framework; 20,000 random values match .NET 8 and read back exactly (4.8's own double.Parse is not correctly rounded and misreads some of them, as it does .NET 8's strings).

Everything else plausible is logged as R177-1.

### Round 176: WinForms + tooling + docs, tenth pass (v2026.11.19.24)

Two fresh reviewers (correctness, robustness): 6 bugs, and a seventh found by the regression test's random trials; none
severe, so section 17 is re-queued. This ends the tenth pass: sixteen sections stay re-queued, and the eleventh pass
runs 4, 5, 14, 12, 7, 16, 10, 15, 17, 3, 2, 8, 9, 1, 6, 13. Five of the bugs were logged suspects (R105-3, R105-6, R105-7, R105-9,
R120-2), now reproduced. The regression tests are in `WinFormsReview176Tests` (4, each failing on the old code, run by
name under a 60 s timeout) and the new `scripts/bump-version-tests.ps1` (600 random versions graded against a model on
a scratch copy; 311 fail on the old script), which verify.sh now runs.

- **PlotPictureBox** (R105-6): a right, middle or X release during a left-button pan ended the pan with the left button
  still held: OnMouseUp ended it for any button, and WinForms' release of capture after that button-up ended it again.
  Only the left release ends it now, and the plot takes capture back.
- **PlotPictureBox.ZoomAt** (R105-7): a factor of 1 above a lowered MaxZoom snapped the view down to the limit.
- **PlotPictureBox.ZoomAt** (found by the test): a factor of 1 inside the limits still moved the offset by rounding and
  raised ViewChanged. A factor of 1 leaves the view alone.
- **bump-version.ps1** (R105-9): N had no cap: it wrote 2026.9.27.65535, which fails every build (CS7034), after
  writing it back. It refuses past 65534 and leaves the file alone.
- **bump-version.ps1** (R120-2): an impossible date (2026.13.1.0) counted as later than today, so N went on bumping on
  it. It is refused.
- **SetSelectedEnum** (R105-3): in an editable box, text the user had typed stayed when the selection was already what
  was set (or already empty, with GetSelectedEnum returning null). The box shows the selected item's text, or none.
- **AutoSizeColumns**: on a grid whose AutoSizeColumnsMode is Fill it sized nothing to content (the fill weights won),
  and a cap pinned every column at the cap. Fill columns stop filling and are sized to content.

R120-4 (WaitCursorScope's counter before the setter) is removed: neither reviewer could reach a throwing setter. R105-4
is narrowed (Post does not throw; the update is lost). Everything else plausible is logged as R176-1.

### Round 175: WPF satellite, tenth pass (v2026.11.19.23)

Two fresh reviewers (correctness, robustness): 9 bugs, and a tenth found by the regression test's random trials; none
severe, so section 16 is re-queued. Seven of them were logged suspects (R102-1, R102-2, R102-4, R117-1, R117-2,
R117-4, R139-1), now reproduced. The regression tests are in `WpfReview175Tests` (7, each failing on the old code, run
by name under a 60 s timeout).

- **IncrementalCollection** (R102-1): a CollectionChanged handler that reset the list during ResetAsync's clear had its
  state overwritten by the outer reset: IsLoading read false while the newer reset's page loaded, and after a short
  page HasMoreItems went true again, the loader ran past the end, and the outer call returned 0. The outer reset now
  leaves the state to the newer one and returns its first-page load.
- **IncrementalCollection** (found by the test): a PropertyChanged handler that reset the list when IsLoading went true
  superseded the load, which still called the loader, at the new list's offset. It returns before calling it.
- **RuleValidatedObject.SetAndValidate**: a PropertyChanged handler that threw skipped validation, so A = 50 against
  Range(0, 10) was stored with HasErrors false. The value is validated, then the handler's exception is rethrown.
- **CountToVisibilityConverter** (R102-2): an IEnumerable whose GetEnumerator returns null threw NullReferenceException;
  it is empty now.
- **CountToVisibilityConverter** (R117-2): Complex(0, 5) and Complex(-1, 5) were Collapsed, as IsPositive reads the real
  part. A number that is not real is Visible unless it is zero or has a NaN part (Complex.IsNaN is false for
  (Infinity, NaN), so Complex is handled by its parts).
- **ThemeManager.ThemeFromRegistryValue** (R139-1): a QWORD AppsUseLightTheme of 0 (a boxed long) read as Light. Any
  integer 0 is Dark.
- **RecordingDialogService** (R117-4): in Strict mode an unscripted dialog threw before it was recorded; it is recorded
  first.
- **RecordingDialogService** (R102-4): OpenFiles handed back the enqueued array itself and the record kept the caller's
  FileDialogOptions, so changing either afterwards changed the answer and the record. Both are copies.
- **NumericRangeValidationRule** (R117-1): with an infinite bound, "1e400" passed as +Infinity. A number too large for a
  double is outside every range; 2000 random literals near the limit in four cultures agree with an independent
  overflow reference. The culture's infinity symbol still passes an infinite bound.
- **PlotPresenter.ResetView** (R139-1): the scale went to 1 whatever the limits, so with MinZoom 2 a zoom by 1 jumped
  to 2. It is clamped to [MinZoom, MaxZoom].

R102-6 (a NaN bound) was already fixed and is removed. Everything else plausible is logged as R175-1.

### Round 174: Text & small types, tenth pass (v2026.11.19.22)

Two fresh reviewers (correctness, robustness): 2 bugs, none severe, so section 11 is clean (2 < 3). The regression
tests are in `TextReview174Tests` (2, each failing on the old code, run by name under a 60 s timeout: the TextAlign one
on .NET 8 and .NET Framework 4.8, the CaseConverter one on .NET Framework, where that bug is).

- **TextAlign** dropped only the first U+FE0F starting the text or the ellipsis, so the next one joined the pad or the
  character before the ellipsis and made it emoji-wide: `Fit("\uFE0F\uFE0F", 1, Right, '█')` measured 2,
  `PadLeft("\uFE0F\uFE0Fx", 4, '·')` 5, and "❤abc" fitted to 2 with "\uFE0F\uFE0F…" 3. The whole leading run is
  dropped now. 5000 random texts of selectors, escapes, narrow and wide characters, with five pads and selector-led
  ellipses, pad to exactly the width and fit within it.
- **CaseConverter** on .NET Framework left the combining ypogegrammeni U+0345 a mark in upper-case styles, where .NET 8
  gives 'Ι' ("a\u0345b" in CONSTANT_CASE was "AΙB" on one and "A\u0345B" on the other); the gap table of .NET 8's
  mappings lacked it. It is in the table, and the class doc lists it among the letters that come back changed.

R123-3's GlobMatcher and id-generator parts were probed clean on both runtimes and under tr-TR; removed. Everything
plausible is logged as R174-1.

### Round 173: Collections, tenth pass (v2026.11.19.21)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 9 stays re-queued (4 ≥ 3). The
regression tests are in `CollectionsReview173Tests` (5, each failing on the old code, run by name under a 60 s timeout:
the two NaN-key tests on .NET Framework 4.8, where that bug is, and the other three on .NET 8 and .NET Framework).

- **Keys holding a NaN** inside a tuple (ValueTuple or Tuple, nested too) or boxed in an `object` key hashed by the
  NaN's bits on .NET Framework, so (NaN, 1) and (NaN with another payload, 1), which are Equals, were two keys:
  Counter, OrderedSet, Summarize and Pivot counted them twice, CountMinSketch under-counted, and
  `SequenceComparer.Ordered<object>()` gave two Equals sequences different hash codes (`Unordered` called them
  unequal). The value-keyed comparer copied into the twelve keyed files now walks tuples, and keys of an open type,
  hashing doubles, floats and decimals by value; equality is unchanged. This confirms and fixes R111-1's NaN half;
  its decimal half (1.0m against 1.00m inside a tuple) does not reproduce, the Framework's tuple hash being by value
  there. 300 random key sets agree with a canonical reference on both runtimes.
- **Aggregation.Summarize** started its sum at +0, so a group of −0 values had a sum of +0 and a mean of −0. The sums
  start at −0 (the IEEE sum of nothing); 2000 groups of signed zeros and cancelling pairs match a sequential IEEE sum.
- **Counter.MostCommon** broke ties in dictionary order, so after a, b, c, remove a, add d it gave d, b, c. Each count
  now carries the order its item was first added, and ties keep it (an item removed to zero and added again counts
  from then); the doc says so. 500 random add/remove sequences match a stable-sorted model. This fixes R127-1's first
  half.
- **CountMinSketch** checked width and depth each against Array.MaxLength but not their product, so 0x7FFFFFC7 by
  0x7FFFFFC7 threw OutOfMemoryException; width × depth past Array.MaxLength is now ArgumentOutOfRangeException
  ("depth"). This confirms and fixes R96-3.

Everything plausible is logged as R173-1.

### Round 172: IO, tenth pass (v2026.11.19.20)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 8 stays re-queued (4 ≥ 3). The
regression tests are in `IOReview172Tests` (4, each failing on the old code, run by name under a 60 s timeout: the
ByteSize two on .NET 8 and .NET Framework 4.8, the CsvDialect one on .NET Framework, where the bug is, and the tar one on
.NET 8, the only build with TarUtils).

- **ByteSize.TryParse** rounded twice: decimal.TryParse keeps 28 or 29 digits, then Math.Round rounded again, so
  "0.49999999999999999999999999999999" gave 1 byte and "1.4999999999999999999999999999999999 B" gave 2. The value is
  now taken exactly from its digits as a BigInteger ratio and rounded once, half away from zero. 3000 trials with
  fractions up to 80 digits near midpoints, in every unit, match an exact reference.
- **ByteSize.Format** worked in double, dividing by 1000 or 1024 per unit, and then "F" (which rounds through 15 digits
  on .NET Framework): 1,125,000,000,000,000,000 bytes was "1.12 EB" on .NET 8 and "1.13 EB" on .NET Framework, and
  long.MaxValue with 15 decimals "9.223372036854773 EB" (…770 on .NET Framework) for …776. It now rounds the exact
  quotient in integers, half away from zero, the same on every runtime; the doc says so. 5000 trials, a third at exact
  midpoints, match a decimal reference.
- **CsvDialect.TryParseNumber** (and the numeric test Detect uses) returned false on .NET Framework for a number past
  double's range ("1e400", "1.7976931348623159e308"), so ParseNumber threw where .NET Core gave infinity (CsvBinder
  already handled this). When TryParse fails on well-formed text, the magnitude of its digits now gives ±infinity or ±0.
  2000 random exponents from ±300 to ±2000 agree on both runtimes.
- **TarUtils' metadata guard** refused a PAX global header followed by a GNU long name as "a mix of GNU and PAX metadata
  headers"; TarReader returns a "g" as an entry of its own and reads the archive. A "g" now ends a run; a GNU long name
  then a PAX "x" before one entry is still refused.

R112-5 (a stale size after two PAX headers) is refuted: TarReader itself refuses an "x" after an "x", so the stale
size is never used; removed. Everything plausible is logged as R172-1.

### Round 171: Statistics, tenth pass (v2026.11.19.19)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 3 stays re-queued (6 ≥ 3). The
regression tests are in `StatisticsReview171Tests` (6, each failing on the old code on .NET 8 and .NET Framework 4.8,
run by name under a 60 s timeout).

- **ChiSquareQuantile** past p = ½ and **ChiSquareInverseSurvival** below q = ½ gave 5e-324 where the quantile lies
  below the smallest double and is documented as 0: at tiny k the CDF at ε already passes ½ (k = 0.00143: 0.586). The
  upper tail at ε now decides it there (1 − p is exact past ½). This confirms and fixes R108-3's first half. 3000
  trials with k from 1e-4 to 0.05 return 0 exactly when P(X > ε) < q and invert to the ulp otherwise.
- **MannWhitneyU** with every value tied at 1,000,003 against 1,000,010 gave p = NaN for 1: the tie term t³ − t over
  N(N − 1) rounded past N + 1 and the variance went negative. The term is formed as ratios now and the variance is
  clamped at 0. 300 trials match a decimal naive correction.
- **KolmogorovSmirnovOneSample** from 141 to about 1e5 points was up to 2.7% off SciPy (Stephens' asymptotic form):
  n = 276, D = 0.0725 gave 0.10504 for 0.10451. The exact matrix form now runs to 1,000 points, and to 100,000 while
  n·D^(3/2) < 1.4; otherwise Pelz–Good, as SciPy. 48 random (n, D) pairs from 141 to 99,999 points match SciPy to
  5e-5 relative (worst seen 1.6e-5, where SciPy too uses Pelz–Good). The slowest call is about 23 ms.
- **PermutationTest.Paired** miscounted when the differences cancel: {1e17, 3, −1e17} gave mean 0 and p = 0.75 for
  mean 1 and p = ½, and {2, 5, −1, 2, 2e16, 6, 1} gave p = ½ for 3/128 (the 1e-12 relative tie tolerance of a large
  mean swallowed sums 2 apart). The mean is compensated now, and each sign pattern is compared with the observed one by
  its sum as a double-double, to 4n²ε²·Σ|d|, so the enumerated p-value is exact; the doc says differences that tie only
  in decimal (0.1 + 0.2 against 0.3) do not tie. 300 trials against BigInteger enumeration match exactly.
- **Correlation.Pearson** summed about the rounded means without the (Σd)²/n correction the section's other moments
  use: 16 points near 1e8 with a spread of 1e-5 gave r = 0.0807327123 for 0.0807328296. 400 trials of offset data
  (centres to 1e12, spreads down to 1e-12 of them) match r from exact BigInteger sums to 8nε.
- **The incomplete beta** with both shapes past 100 off the BASYM region used the Lentz fraction (and BGRAT where it
  applied), 100 to 700 ulps off: I(0.0017, 883, 558,023) was 0.9859943623125793 for …5598, F's upper tail at
  f = 1.0334, d1 = 3649, d2 = 571,911 was 0.0788087896438341 for …6892, and BGRAT's complement at a = 644, b = 463,799
  was 8e-14 off. That region now uses TOMS 708's BFRAC alone, as TOMS 708 does, in all four copies (Distributions,
  SpecialFunctions, Anova, GaugeRR). Against 40-digit references, 41 beta and 32 F cases hold (4·cond + 30) ulps of the
  smaller tail, cond being the condition in x; BFRAC also beat BGRAT on 31 of 33 cases in BGRAT's old region.

Anova's copy is bit-identical to FSurvival, and HugeShapeTests' copies-agree checks stay green. In MathChecks' graded
grid one row went past 16 units with no condition recorded, I(9e-13, 1000, 1e15) at 20 units (BGRAT's region before);
its κ is 329, now filled in, so it is well inside the bar. Everything plausible is logged as R171-1.

### Round 170: Filters & control, tenth pass (v2026.11.19.18)

Two fresh reviewers (correctness, robustness): 3 bugs, none severe, all in IirFilter. Three bugs re-queue section 1.
The regression tests are in `FiltersReview170Tests` (3, each failing on the old code on .NET 8 and .NET Framework 4.8,
run by name under a 60 s timeout).

- **GroupDelay** at and near a zero on the unit circle was wrong by up to 15 orders of magnitude: Notch(48000, 50, 30)
  gave 1.03e12 samples at its centre and 1.87e6 one part in 1e9 away, for 9167.39. The zeros came from the general
  root formula at a radius off 1 by about 1e-16, and each zero's term grows as (1 − ρ) / |e^(jω) − r|². A complex pair
  is now taken from the coefficients (ρ² = c2/c0, the angle from the exact discriminant), so a notch's zeros are
  exactly on the circle. The direct sum, used up to 1e-6 of the coefficients' size from a root, was still 4e-6
  relative off there: the roots are used from 1e-2. The trials cover 400 random notches at and around the centre.
- **GroupDelay** exactly on a zero gave a finite value for a first-order section: ButterworthLowPass(1, 100, 1000) had
  0.66246 samples at Nyquist, where |H| = 0 (the doc says undefined; orders 2 and up gave NaN). The factored form
  divided by exactly 0 and fell back to the direct sum. It is NaN now.
- **Elliptic designs** with poles crowded against the circle were accepted though their rounded sections miss the
  spec at the edge: EllipticHighPass(16, 2.775, 24.438, 0.0889, 15476) (edge at 5.7e-6·fs) had +0.90 dB at its edge
  for −2.775 dB, and round 133's order-11 high-pass had −0.4746 dB for −0.6746 dB (R133-1, confirmed). Each design
  now checks its sections against the exact design's gain at every edge (to 0.01 dB) and refuses one that misses it,
  naming the edge or the specification as before. Round 133's evaluation test now builds that filter from its sections.
  1500 random elliptic and Chebyshev I designs with edges from 1e-6·fs hold −rp at the edge to 0.02 dB.

The notch's null at its exact centre (about −200 dB and below) is set by rounding of the zeros' angle, which double
precision can't settle; MagnitudeDb now says so. FIR designs matched firwin to 6e-14 and KaiserParameters matched
kaiserord exactly. Filter, Process and FiltFilt matched sosfilt and sosfiltfilt. The IIR designs matched SciPy, and
.NET Framework agreed with .NET 8. Everything plausible is logged as R170-1.

### Round 169: Binary formats & images, tenth pass (v2026.11.19.17)

Two fresh reviewers (correctness, robustness): 3 bugs, none severe, all in PngReader. Three bugs re-queue section 6.
The robustness reviewer found nothing in its own probes. The regression tests are in `BinaryImagesReview169Tests` (3,
each failing on the old code on .NET 8 and .NET Framework 4.8, run by name under a 60 s timeout). The deflate one
covers 300 random images, compressed by DeflateStream at every level and split over IDATs at random.

- **PngReader** accepted a zlib stream whose deflate data has no final block (RFC 1951 needs one), as long as the
  Adler-32 matched. This confirms part of R159-1.
- **PngReader** accepted junk between the end of the deflate data and the Adler-32: it took the Adler from the last
  four bytes and never saw the bytes it skipped. DeflateStream ends quietly in both cases and doesn't say what input
  it left unread. The inflater's input now holds back the last byte of the deflate data and serves it on its own. A
  well-formed stream must ask for that byte, one that ends early never does, and one with no final block asks past it.
- **PngReader.Decode(byte[])** read the IHDR width as a signed int, so a width of 2^31 was reported as
  "-2147483648x1", where Decode(Stream) said "2147483648x1". Both report the value in the file now.

SvgUtils' width-estimate comment said a character written as U+FFFD counts 0, but it counts as U+FFFD. The comment
is corrected; the behaviour was right. R159-1 is closed: its inflate part is fixed here, its Base85 message part was
already fixed, and its whitespace part was refuted (each codec matches its own documentation). EndianConverter's
unaligned paths were probed clean. Everything still open, plausible but not confirmed, is logged as R169-1.

### Round 168: Signal analysis, tenth pass (v2026.11.19.16)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 2 stays re-queued. The regression tests
are in `SignalReview168Tests` (4, each failing on the old code, run by name under a 60 s timeout; the FFT one against
the definition for every power-of-two length to 32 and every position, the Hampel one over 5,000 random windows
scaled to the subnormal grid).

- **Fft, power-of-two lengths** spread an infinite sample through the butterflies as ∞ · 0 and ∞ − ∞:
  `Forward([0, ∞, 0, 0])` gave (∞, NaN) in bins the definition gives (∞, 0), and Inverse the same. Both reviewers
  found it. As the Bluestein lengths already did, the finite samples are transformed and each non-finite one is
  added to every bin directly.
- **Spectrum, WelchPsd, Multitaper and Hilbert.Envelope** took Complex.Magnitude, which for (∞, NaN) is NaN on .NET
  Core and ∞ on .NET Framework, so the same input read differently on the two. A value with an infinite part has
  magnitude ∞ on both now.
- **Hilbert.InstantaneousPhase** returned exactly −π for a negative constant (rounding noise of −2e-16 in the
  imaginary part), outside the documented (−π, π]. −π maps to π.
- **Hampel** rounded its medians and threshold on the subnormal grid, so `[−2, −2, 0, 3, 0, 0, 2]·ε` (half-window 3,
  one sigma) missed the outlier it has at scale 1. Data that small is worked on scaled by 2^600, which is exact.

Plausible, not confirmed, logged as R168-1.

### Round 167: Charts, tenth pass (v2026.11.19.15)

Two fresh reviewers (correctness, robustness): 15 bugs, one severe, so section 5 stays re-queued. AxisTicks was graded
against random reference trials for the first time, which found most of them. The regression tests are in
`ChartsReview167Tests` (14, each failing on the old code, run by name under a 60 s timeout; the label one on .NET
Framework 4.8, where the bug is), the step search graded against a brute-force 1-2-5 search over 20,000 random ranges.

- **AxisTicks.Linear (severe)** divided before subtracting, so a span of one ulp at 1e15 (1e15 + 8.125 to 1e15 + 8.25)
  came out 0. Log10(0) cast to int.MinValue and the step search walked 2³¹ candidates for minutes, then threw
  IndexOutOfRangeException. Every chart that auto-ranges through Linear reached it from ordinary data (SvgChart with
  MaxYTicks 7). It subtracts first now, halving only when the span overflows.
- **AxisTicks.Linear, tight** started its step search at span / (maxTicks − 1), so `Linear(0.03, 0.13, 2, false)` took
  step 0.1 (one tick) over 0.05, and stopped once the step passed the range, so `Linear(−1, 1, 2, false)` kept three
  ticks. Time had the second fault too. The search now starts a decade lower and goes on for a tight axis.
- **AxisTicks.Linear, tight** could return more than maxTicks (6 for `Linear(−1e100, −1.00000000000001e100, 5, false)`):
  the count used the floating quotient, the ticks their exact decimals. Ticks are counted as they are emitted. Time's
  day steps over-counted the same way (five ticks for maxTicks 4 at −5.8e24); it falls back to the linear ticks there.
- **AxisTicks.Linear and Time, tight** returned no ticks for a range a few ulps wide: the axis was unlabelled. The range
  ends are its ticks, as for an opened range.
- **AxisTicks.Time** threw ArgumentOutOfRangeException naming `seconds` for `Time(0, 1.73e308, 2)`: a day step past
  MaxValue gave 0 · ∞ = NaN, which got past an ∞-only guard.
- **AxisTicks.Time** labels from 2^53 s kept four significant digits, so distinct ticks read "5.028e+306s" twice. The
  digits follow the step.
- **AxisTicks.Time, loose** returned Min and Max that did not bracket the data (`Time(−3.5e-8, 8054.7, 10)` gave Min 0):
  clamped as Linear's.
- **AxisTicks.Log10, loose** added a decade below subnormal data (`Log10(1e-320, 1e-319)` started at 1e-321): the
  decades are checked against the data, and built from exact decimals rather than Math.Pow.
- **AxisTicks**' fallback labels for an opened range used "R", which reads "5E-324" on .NET Core and
  "4.94065645841247E-324" on .NET Framework. They use the fewest digits that read back, the same on both.
- **TimingSignal.FromSamples** held the last level through NaN samples, so a dropout drew as a valid high or low. A NaN
  sample is unknown.
- **SvgChart** padded a bar axis by the closest spacing of any bar, drawn or not: a NaN-y sample 2e-12 from a bar cut
  the end bars to half width. Only drawn bars count.
- **ErrorBarChart** markers and **BoxPlot** outliers on the plot edge painted a pixel into the margin: both are cut to
  the plot area.
- **ScatterMatrix** rounded r half to even, so an exact −0.125 read "r = −0.12". Halves round away from zero.

Checked and clean: Histogram.Bin against a naive counter (20,000 trials); SVG output of SvgChart, HexBin, Waterfall,
ScatterMatrix, TimingDiagram and MultiPanel under de-DE with hostile text; Colormap extremes; Palettes.FromHex;
BoxPlot.Compute. R134-1 is closed (its parts fixed here, AxisTicks now randomized, Palettes probed). Plausible, not
confirmed, logged as R167-1.

### Round 166: Security, net, time, tenth pass (v2026.11.19.14)

Two fresh reviewers (correctness, robustness): 3 bugs, none severe, so section 13 stays re-queued (three bugs re-queue
it). The regression tests are in `SecurityNetTimeReview10Tests` (3, each failing on the old code, run by name under a
60 s timeout), the cron one graded against a minute-by-minute matcher over 300 random fall-back zones.

- **CronSchedule.GetNextOccurrence(after, zone)** jumped from a day that did not match to two days before the next one
  that did, but only looked at days after the current wall date. A fall-back that carries the wall clock back across
  midnight (Newfoundland's at 00:01 until 2011) repeats the end of the previous day, and the jump leapt over that second
  pass: `* 23 * * 6` after 23:59:30 on 2010-11-06 ran a week late and `*/5 * 6 11 *` a year late. CronScheduler, which
  uses this overload, had the same bug. The walk no longer jumps while the offset can still fall within two days.
- **Hashing.CreateIncremental** threw ArgumentNullException naming `hashAlgorithm` for a default name (a bare
  resource key on .NET Framework) and CryptographicException for an unknown one. Both are ArgumentException naming
  `algorithm`.
- **TcpLineClient** sent a blank host to DNS and failed with SocketException "No such host is known"; PortCheck and
  HostInfo refuse one. It throws ArgumentException naming `host`.

Checked and clean: the cron fixed-offset overload over 2,954 random expressions against a brute-force walker (net8.0
and net48); BusinessDays against a day-by-day counter; IsoWeek over every day of the calendar against BCL ISOWeek;
Iso8601Duration round trips and cross-checks with XmlConvert; HumanDuration and UnixTime round trips. Samoa's skipped
day (R124-4), modelled with a custom zone that skips a whole day, runs fixed jobs at the transition and wildcard jobs
without make-up, as documented, on both frameworks; R124-4 keeps only TcpLineClient and HttpDownload off Windows.
Plausible, not confirmed, logged as R166-1.

### Round 165: Concurrency, tenth pass (v2026.11.19.13)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 10 stays re-queued. The regression tests
are in `ConcurrencyReview165Tests` (11: nine fail on the old code, run by name under a 60 s timeout, AtomicDouble's on
.NET Framework 4.8; two guard the new pipeline wake-up and the callback progress rule), graded against an exact
decimal reference, a bit-for-bit read-back over 300,000 random doubles, and the reflected wake-up count.

- **AtomicDouble.ToString** relied on "R", which on .NET Framework printed 0.84551240822557006 as "0.84551240822557"
  (one ulp lower read back) and -0.0 as "0". Negative zero prints "-0", and text that does not read back falls back to
  G17.
- **StartupTiming.Report** trimmed the last milestone's trailing spaces with the final newline, and a clock that stepped
  back under 0.05 ms printed "-0.0" on .NET Core and "0.0" on .NET Framework with a "-" step. Lines are joined, and
  tenths are rounded half away from zero from whole ticks, signed only when not zero.
- **TaskExtensions.WithCancellation** returned a finished task's result through a token already cancelled, where
  AsyncManualResetEvent and AsyncCountdownEvent document that a cancelled token wins (R97-1). It cancels, and observes
  the task.
- **PauseToken.WaitWhilePausedAsync** succeeded with a cancelled token while not paused (R114-1). It is cancelled.
- **ConcurrentPipeline** released its wake-up signal once per accepted item, so a consumer busy on a backlog let the
  count grow to one per item (1,000 after 1,000 items), and at int.MaxValue TryEnqueue would throw SemaphoreFullException
  for an item it had queued. At most one wake-up is pending.
- **A delegate returning a null task** threw NullReferenceException from Retry (retried with backoff until the attempts
  ran out), CircuitBreaker (counted as a failure, opening the breaker), Bulkhead, ParallelUtils.ForEachAsync, AsyncEvent,
  PeriodicWorker (work and delay) and the pipeline's consumer. Each reports InvalidOperationException, as AsyncLazy and
  TaskRace already did; Retry does not retry it and the breaker does not count it.
- **ProcessRunner** with no timeout waited for an OnOutputLine call however long it ran (6 s held the run 6 s), and for
  good on one that never returned (the second half of R128-2), though the docs bound that wait to a second. A finished
  callback counts as progress, so a slow one on a backlog still sees every line, and a stuck one lets the run return.
- **ParallelUtils.ForEachAsync** threw OperationCanceledException when the token was cancelled as the last body
  returned, though every item had finished (R147-2). It throws only when cancellation cut the work short or the token was
  cancelled before the call.

Checked and clean: Jitter over 2,000,000 trials at r just below 1 and tick counts up to long.MaxValue; KeyedLock,
AsyncLock and Bulkhead under randomized lock, cancel and dispose stress on net8.0 and net48; TaskRace over 400
randomized rounds; ConcurrentPipeline Complete and Dispose racing producers. A disposed, never-cancelled token source
behaves the same on both frameworks (the last item of R147-2, refuted); PeriodicWorker's interval limit is documented.
Plausible, not confirmed, logged as R165-1.

### Round 164: Windows satellite, tenth pass (v2026.11.19.12)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe (one found while fixing), so section 15 stays
re-queued. The regression tests are in `WindowsReview164Tests` (7: six fail on the old code, run by name under a 60 s
timeout; the seventh checks the new capture tiles' seams against a direct capture), graded under seven cultures against
the invariant text and against what Windows holds.

- **SystemMemory, DiskSpace and ResourceSample** ToString followed the current culture (17.554.702.336 bytes and
  "CPU 0,0%" in German), where OsVersion and MonitorDpi are invariant. All three are invariant.
- **ScreenRect** ToString printed negative coordinates (a monitor left of or above the primary) with the culture's
  minus, U+2212 in Swedish and a bidi mark in Persian. It is invariant, and so is MonitorDetails through it.
- **ScreenCapture** documented alpha as zero, while a capture through DWM came back 255 and the part off every monitor
  0. Captures are opaque: alpha is 255 on every pixel, as the docs now say.
- **ScreenCapture** passed a region wider than GDI's bitmap limit (135,000,000 x 1, about 134 million pixels) through
  validation and threw "GetDIBits failed" with a meaningless code. The region is captured in tiles of at most
  16,777,216 pixels, and a partial GetDIBits copy is an error rather than zero rows.
- **RegistryUtils.SetValue** on a path Windows refuses part way (600 levels, or 35,000 characters) threw IOException and
  left the levels it had made (30 of them). The first missing level is found one level at a time from its parent, the
  tree made is deleted, and the call throws ArgumentException(subKey).
- **ShortcutUtils.Create** stored a relative target off the drive root (`sub\x.exe` became `C:\sub\x.exe`, `..\x.exe`
  `C:\x.exe`). A relative target is resolved against the current directory, as Process.Start does; a bare name that is
  not a file there is left for the shell to find on the PATH, and a target holding `%` is kept for the shell to expand.

Checked and clean: no character ToUpperInvariant leaves distinct is folded by the kernel's upcase table (one direction
of R106-1, by brute force over the whole BMP; the other stays open), and 40 of 40 forwards at timeouts down to 0 ms. Plausible, not confirmed, logged as R164-1.

### Round 163: Objects, functional, grids, graphs, intervals, tenth pass (v2026.11.19.11)

Three fresh reviewers (correctness, robustness, and a second robustness probe of `Objects/`): 7 bugs, one severe, one
of them a logged suspect (R136-1), so section 14 stays re-queued. The regression tests are in `ObjectsReview163Tests`
(12, each failing on the old code, run by name under a 60 s timeout: ten on .NET 8, and the struct-equality and signed
NaN ones on .NET Framework 4.8), graded by round trips over randomized keys and against .NET Core's shortest float text.

- **Severe: ObjectMembers.KeyText** left bare a key that needed quotes and held both quote characters, so the path it
  printed read another key: Flatten and PropertyDiff named `"it's"` (with its quotes) as `["it's"]`, which reads the
  key `it's`, and a diff of one reported the other's value (R136-1: a key holding ']' and both quotes could not be
  spelled). Such a key is put in double quotes with its own doubled, and PropertyPath and UnflattenObject read a doubled
  quote as one.
- **PropertyPath** could not read back a key of a type that is not IConvertible: BigInteger, DateOnly, TimeOnly, Half,
  Int128, UInt128, nint, nuint and tuple keys threw "does not convert". Such a key is read by its type's own Parse, and a
  tuple by its items.
- **ObjectMembers.KeyText** printed a TimeOnly in the short pattern, so 03:04:05 and 03:04:06 were one key: Flatten
  threw on the duplicate and PropertyDiff reported two differences at one path. It prints "O".
- **ObjectMembers.KeyText** printed a tuple's items with the current culture: (1.5, 2) was "(1,5, 2)" in German and did
  not read back. Its items are printed as keys are.
- **TypeUtils.TryConvert** from double to float accepted a value the float does not name: 0.123456789012 became
  0.12345679f and 16777217.0 16777216f, where the same values to long or decimal are refused. A double converts when the
  float holds it or the float's shortest text reads back as it (0.1, 1e30), found exactly with BigInteger so .NET
  Framework agrees.
- **DeepEquals and PropertyDiff** compared a struct without its own Equals by bits on .NET Framework (0.0 and −0.0, or
  two NaNs, differed there and not on .NET Core). `ObjectMembers.ValueEquals` compares such a struct field by field.
- **TypeUtils.TryConvert** read "-NaN" as NaN on .NET Core and refused it on .NET Framework. It is NaN on both.

Checked and clean: RangeSet and RangeMap against bitmap and array models (20,000 trials and 20,000 operations),
IntervalTree against brute force, the graph algorithms against naive references, Memoize.ThreadSafe under 8-way
concurrency, AttributeCache under 2,000 parallel calls. Plausible, not confirmed, logged as R163-1.

### Round 162: Numerics core, tenth pass (v2026.11.19.10)

Two fresh reviewers (correctness, robustness): 12 bugs, two severe, two of them logged suspects (R122-2 and R122-4),
so section 4 stays re-queued. The regression tests are in `NumericsCoreReview162Tests` (12: eleven failing on the old
code, each run by name under a 60 s timeout, graded against exact BigInteger determinants, conversions and roundings;
plus a guard that regular integer, Gaussian (to 60×60) and Hilbert (to 11×11) matrices still solve).

- **Severe: LinearAlgebra (Determinant, Solve, Inverse)** scaled rows alone, so a matrix spanning the double range lost
  its small entries: {{1e305, 1e-223}, {1e239, 1e-287}} had determinant 0 and was "singular". A matrix whose entries
  span more than 2^52 is now scaled by rows and columns (powers of two, exact) with Olschowka and Neumaier's
  maximum-product matching, found by the Hungarian method on the exponents. Ruiz balancing, tried first, still left ten
  of 15,000 random determinants dominated by a single term wrong.
- **Severe: LinearAlgebra** let an elimination multiplier underflow on such matrices: {{1e-108, 1e-224}, {1e233, 1e212}}
  gave −1e9, not 1e104, and 40 of 3,000 wide-range determinants were wrong (0 of 24,000 now, at every spread).
- **LinearAlgebra.Solve and Inverse** accepted exactly singular matrices: the rounding left in the last pivot passed the
  row and column test ({{6, 7, -9}, {-5, -6, 4}, {-3, -4, -6}} solved to x ≈ 3e15; 721 of 20,000 random rank-deficient
  integer matrices, and 86 of 20,000 with rows and columns scaled). A pivot is now measured against the bound on what
  elimination combined into it, |a| + Σ|l|·|u|, at 8nε. Exactly singular and ill-conditioned matrices overlap (Hilbert
  10's last pivot is 360nε of its bound), so about 0.1% of rank-deficient matrices still pass, and a condition number
  past about 1e16 is treated as singular; Hilbert matrices to 11×11 solve.
- **UnitConvert.Convert** multiplied by the ratio rounded to a double, rounding twice: 9 m was 0.009000000000000001 km,
  and 9,361 of 49,600 whole-number conversions were off by an ulp. The ratio is kept as whole numbers in lowest terms,
  and value · num / den is rounded once.
- **EngineeringNotation.Format** past yocto and yotta rounded the binary value with "G": 1.25e27 at 2 digits was
  "1.2E+27" though 1.25e24 is "1.3 Y". It writes the digits rounded as in range.
- **Polynomial.RealRoots** returned −0 for a root at zero from x, x² + x and x², and +0 from x³. It is +0.
- **Integration.CumulativeTrapezoid** kept a plain running sum, so past double range opposite panels gave NaN where
  Trapezoid gave the total (R122-2). Those values come from the scaled sum, and the last equals Trapezoid's.
- **Integration.AdaptiveSimpson** returned NaN, with no exception, when two halves overflowed in opposite directions
  (x over ±1e200). It integrates again scaled by 2⁻¹¹⁰⁰: a cancellation that lands in range is that value, a true
  overflow is a signed ∞, and one left below the rounding of halves past the range (x over ±1e200: about 1e384 of
  noise) throws InvalidOperationException rather than return either.
- **Integration.AdaptiveSimpson** integrated a constant above 1.2e308 to ∞ over [0, 1]: the overflow fallback was 1.5
  times the sample. It takes the weighted mean of the samples.
- **RootFinding.Secant** with guesses far apart (1e308 and −1e308) overflowed x1 − x0, stepped to ∞ and failed as "f
  returned NaN". The step is taken from halves, and one that still overflows fails with its own message.
- **Angle.LerpDegrees** with t = ±∞ threw only when the ends differed; with equal ends it returned NaN. It throws.
- **BaseConverter** took UTF-16 code units as digits: "a" and an emoji made base 3 and printed lone surrogates, and two
  emoji were refused as a repeated digit (R122-4, with the −0 roots). Surrogates are refused by name.

Checked and clean: Fraction.ToDouble against a correctly rounded reference (200,000 cases to 1,200 bits),
RoundToSignificantDigits, Percentile bounds, Polynomial roots of 200,000 known cubics and quadratics, Primes.Factorize
of semiprimes to 2^63, Gcd and Lcm at the long extremes, Brent and Bisection at tolerance 0. Plausible, not confirmed,
logged as R162-1.

### Round 161: Logging, diagnostics, CLI, tenth pass (v2026.11.19.9)

Two fresh reviewers (correctness, robustness): 4 bugs, one severe, so section 12 stays re-queued. The regression tests
are in `LoggingReview161Tests` (6: five failing on the old code, three of them by overflowing the stack, each run by
name under a 60 s timeout; plus a guard that ordinary chains and wide aggregates keep the runtime's own text).

- **Severe: LogFormatters (Plain, JsonLine), StructuredTextFormatter, SyslogSink and ConsoleApp's verbose report**
  described an exception with Exception.ToString, which recurses once per inner exception and copies the inner text
  at each level: a chain 20,000 deep overflowed the stack and ended the process from inside a log call, and one
  10,000 deep took 12 s. Up to 64 levels (and 10,000 exceptions) keep ToString's text; past that, each of the first
  256 exceptions is written with its type, message and stack, walked without recursion.
- **The same describers** overflowed the stack on a cyclic chain (an inner exception's InnerException set back to
  the aggregate by reflection). The walk notes the repeat and stops.
- **EnvironmentReport.Capture** of a name holding a NUL ("PATH\0X") reported PATH's value under that name, since the
  OS reads a name only up to the NUL. Such a name is reported as not set.
- **AnsiStyle** read the colon form 4:0 (no underline) as 4, so Underline did not put its underline back after it.
  4:0 is a partial reset, as 24 is.

Checked and clean: syslog framing (RFC 5424 and 3164, octet counting, the UDP cap without split sequences, PRI,
reconnects over loopback), ExceptionUtils on deep, cyclic and throwing exceptions, MetricsRegistry percentiles and
concurrent recording, Sparkline with NaN, infinities and subnormals, ConsoleTable SGR widths. Five logged suspects
were found already fixed and dropped (R69-4, R103-3, R116-3, R116-7, R116-9). Plausible, not confirmed, logged as
R161-1.

### Round 160: Documents & config, tenth pass (v2026.11.19.8)

Two fresh reviewers (correctness, robustness): 11 bugs, none severe, two of them logged suspects (R100-2 and the
casing behind R109-1), and one more found by the gates, so section 7 stays re-queued. This starts the tenth pass. The regression tests are in
`DocumentsConfigReview160Tests` (12: nine failing on the old code on .NET 8 and the slug test on .NET Framework 4.8,
each run by name under a 60 s timeout; the binder and escaping tests fail on both; plus guards that a file held past
the retries still refuses to save and that a link to a file not made yet loads plain defaults). `DocumentsConfigReview151Tests.Save_ReadersNeverSeeAHalfWrittenFile` was flaky
under load (it could finish before enough reads overlapped a save) and now reads for at least 2 s. The 151 and 155
tests that fake another save now write the tagged temp name, and the name hash is unchecked (the overflow-checked
build threw from every save and load before that).

- **PdfWriter.Save and DocxWriter.Save** named the temp file after the target plus a suffix, so a legal name near 255
  characters failed with IOException. The temp is a short name of its own in the same folder.
- **PdfWriter.Save and DocxWriter.Save** failed at once when a reader held the old file (a viewer, File.ReadAllBytes).
  The replace gets about half a second of retries.
- **SettingsStore.LoadFrom** of a path ending in a directory separator loaded as "no file yet", and Save then made the
  folder and failed. It throws ArgumentException, as a directory path does.
- **SettingsStore.LoadFrom** during another instance's save returned defaults and refused to save (45% of loads beside
  two busy savers). A sharing violation gets about half a second of retries.
- **SettingsStore.LoadFrom** of a file not yet written waited 2 s and warned that a save was under way whenever any
  store's recent temp file was in the folder. Temp names carry a hash of their file's name, and only its own count.
- **SettingsStore.LoadFrom** of a file there for File.Exists but gone for the read three times running (another
  save's replace each time) took "no file yet" without asking whether a save was under way: silent defaults the next
  save would have written over the user's settings, in about one run in four of the round 151 concurrency test (found
  by the verify gate). A link to a file not made yet is checked at its target, and repeated misses warn.
- **SettingsStore** documented stale temps as removed after an hour; the code waits a day. The doc says a day.
- **ReportBuilder** escaped HTML with its own copy that wrote lone surrogates, NUL and other C0/C1 controls raw (the
  HtmlReport copy was fixed in round 151), so the page could not be written as UTF-8. It now writes U+FFFD as
  HtmlReport does.
- **ReportBuilder.Slug** lowered with ToLowerInvariant, which on .NET Framework leaves 379 letters (the Kelvin sign,
  Cherokee, Georgian Mtavruli, Osage and others) in upper case, so anchors differed from GitHub's and from the net8
  build (R109-1's cause; Deseret itself lowers on both). The missing mappings are applied first.
- **ConfigBinder and EnvironmentVariables.GetDouble** on .NET Framework refused "infinity" and "nan" in every case
  but one and read "-0" as +0 (R100-2). One invariant parse now reads them the same on every runtime.
- **ConfigBinder and EnvironmentVariables.GetDouble** refused "∞" and "-∞" on every runtime, though the overflow check
  exempted them. They bind to the infinities.
- **ConfigBinder** bound a nested section into the property's declared type, so a derived instance's own keys were
  dropped silently. It binds into the instance's type.

Checked and clean: PDF and DOCX structure against the writers' own readers and the earlier references, table and
callout HTML, the binder's collections, enums and nullable types, environment-variable lookups. Plausible, not
confirmed, logged as R160-1.

### Round 159: Binary formats & images, ninth pass (v2026.11.19.7)

Two fresh reviewers (correctness, robustness): 10 bugs, none severe, five of them logged suspects (R99-1, R113-1,
R113-2, R113-3 and the length half of R129-1), so section 6 stays re-queued. This ends the ninth pass. The regression
tests are in `BinaryImagesReview159Tests` (11: ten failing on the old code, run by name under a 60 s timeout, and a
guard that the Base58 length bound holds for every encoding); the `BinaryImagesReview3Tests` clipping reference now
encodes the new DrawLineClipped contract.

- **ImageBuffer.DrawLineClipped** drew Bresenham between the rounded clip ends, so its runs inside the rectangle shifted
  a pixel against the same line drawn whole ((37, 17)–(−1, 7) on a 20 × 15 canvas: every run; 4,092 of 20,000 random
  lines; R113-2). Each column (row) of the clipped part now takes the full line's rounded value, a tie toward the far
  end as DrawLine breaks it, kept in the rectangle; with integer endpoints the interior is DrawLine's pixels.
- **ImageBuffer.DrawLineClipped** with a backwards rectangle (left > right) drew pixels, and an axis-parallel line threw
  DivideByZeroException. An empty rectangle draws nothing.
- **PngReader.Decode(Stream)** counted each IDAT's 12 bytes of framing against the image-data cap, so a legal file in
  small IDATs (which Decode(byte[]) read) was refused as "far larger than the image". The cap counts the data; empty
  IDATs are bounded apart.
- **SvgUtils.EstimateTextWidth** measured a control character or unpaired surrogate as 0 (a lone high surrogate as an
  em), though EscapeText writes U+FFFD. It measures what is written.
- **SvgUtils.FitWidth** with a positive maxWidth below 0.0000005 wrote textLength="0", squeezing the text to nothing. A
  width that writes as 0 sets no limit.
- **SvgDocument** wrote a positive font size, radius, stroke width or rectangle size below 0.0000005 as "0", drawing
  nothing; it now refuses it, as the constructor does for the canvas.
- **Luhn.AppendCheckDigit** on a payload of the longest string threw OutOfMemoryException, not ArgumentException.
- **Base58.Encode** past the longest string threw OutOfMemoryException, not ArgumentException (R99-1, R129-1's length
  half). The bound is ⌈log58(first byte + 1) + 1.3657·(n − 1)⌉ plus the leading zeros.
- **Base32.Decode and CrockfordBase32.Decode** sized their output from the whole text, separators included: 16M spaces
  allocated 10 MB for three bytes (R113-1). The initial size is capped.
- **Bmp.Decode** read BI_BITFIELDS masks at 54 after a non-standard 41- to 51-byte header, inside the header, and
  refused valid masks (R113-3). The masks follow any header shorter than 52 bytes.

Checked and clean: PNG and BMP round trips against independent encoders, CRC and Adler references, the bit readers and
writers, varints, Base64Url, Base85, hex, EndianConverter on the probed paths, SvgDocument's escaping. Plausible, not
confirmed, logged as R159-1.

### Round 158: WPF satellite, ninth pass (v2026.11.19.6)

Two fresh reviewers (correctness, robustness): 3 confirmed bugs (the correctness reviewer found none), one from a
reviewer's "plausible" list and one logged suspect (R102-3), 5 in all, none severe, so section 16 stays re-queued. The
regression tests are in `WpfReview158Tests` (6: five failing on the old code, run by name under a 60 s timeout, and a
guard that the notifications still reach a live dispatcher).

- **NumericInputBehavior.Coerce and StepValue** could return a value outside [Minimum, Maximum] though a shown value
  fit (829 of 200,000 random cases): Math.Round(v, d) scales by 10^d and moved v once v·10^d passed 2^53 ("0" in
  [−1e17, −2124553058354439.25] at ten places gave −2124553058354439.0000000000, above the maximum), and the inward
  step from a rounded bound was never re-checked ([957144.2231336349, 1e16] gave 957144.2231336348). Values are now
  rounded as the decimal they read as (their shortest round-trip form), and a bound moves inward by rounding that
  decimal up or down; the text is that decimal, not "F" of the double.
- **NumericInputBehavior.Coerce was not idempotent**, so a box drifted each time it lost focus: −4.3076650725247685 at
  16 places gave −4.3076650725247700, then −4.3076650725247710 (209 of 100,000 values under 1,000 moved at 15 places).
  Rounding the shortest decimal leaves a rounded value alone. Graded against decimal arithmetic.
- **NotifyTaskCompletion.Completion** never completed when the watcher was created on a worker thread carrying a UI
  thread's DispatcherSynchronizationContext and that dispatcher shut down first: the shutdown check looked only for the
  current thread's own dispatcher. It uses the context's dispatcher.
- **HotkeyGesture.TryParse** accepted a repeated modifier ("Ctrl+Ctrl+K", "Ctrl+Control+K" read as Ctrl+K).
- **GlobalHotkeys.Register** with a null callback for a gesture this instance held threw InvalidOperationException
  ("already registered"), not ArgumentNullException (R102-3).

Checked and clean: HotkeyGesture text round trips over every accepted key and modifier set; NumericInputBehavior typing
each in-range value a character at a time in 7 cultures and ±∞ bounds; StepValue's direction; EnumToBooleanConverter
with Flags lists and numeric parts; RegexValidationRule's whole match; NumericRangeValidationRule parsing; validation
event order; Messenger against a naive model (3,000 × 40 operations); IncrementalCollection paging with a synchronous
loader. Not probed (no UI shown, no hotkeys or registry touched beyond unusual test gestures): the dialog service, drag
and drop, hotkeys against other applications, ThemeManager's registry and dispatcher paths.

### Round 157: Collections, ninth pass (v2026.11.19.5)

Two fresh reviewers (correctness, robustness): 3 confirmed bugs, one from the "plausible" list and one found beside a
fix, 5 in all, none severe, so section 9 stays re-queued. The regression tests are in `CollectionsReview157Tests` (7,
each failing on the old code, run by name under a 60 s timeout); round 44's even-size Bloom tests moved from 1004 items
to 1002, which the new sizing still makes even.

- **BloomFilter and CountingBloomFilter sizing**: the size grew until (1 − (1 − 1/m)^(kn))^k met the target, but that
  classic rate is below the true one, so every filter missed it: 1 item at 0.01 had 0.0121, 3 had 0.0108, 10 had 0.0104
  (averaged over 3,000 filters, matching the exact occupancy rate), and 300 had an exact rate of 0.010012. The rate is now exact, E[(X/m)^k]
  over the distribution of set bits, for up to a few thousand throws, and past that the bound E[f^D] (D the distinct
  bits among a query's k probes; occupancy is negatively associated), which is never below it. Graded against
  BigInteger Stirling sums and an occupancy recurrence.
- **Bloom probes**: a 32-bit hash taken mod m set the lowest 2^32 mod m bits once more often than the rest, so large
  filters had more false positives than their size gave (about 10% over at 1.6e9 bits; 0.535 of the set bits fell in a
  region of 0.529 at 1e8). Probes are 64-bit (splitmix64) and redrawn past the last whole multiple of m.
- **TtlCache**: a finite ttl whose expiry fell exactly on DateTimeOffset.MaxValue was taken as the saturated "never"
  and the entry never expired. Saturation is its own value now.
- **Aggregation.Summarize**: the mean of {−0, +0} was −0 on .NET Core and +0 on .NET Framework (Math.Min and Math.Max
  order signed zeros on Core only). It is clamped by comparison, and a zero mean is −0 only when every value was.
- **Aggregation.Summarize**: the max of {−0, +0} was −0 (the first zero was kept); −0 is the min and +0 the max.

Checked and clean: OrderedDictionary, OrderedSet, LruCache, Deque, CircularBuffer, IndexedPriorityQueue, BinaryHeap,
FenwickTree, DisjointSet, Trie (odd Unicode), BiMap, MultiDictionary, TopN, BitSet, Combinatorics, CartesianProduct
and the sorted-list helpers against naive models over random operations; reservoir and weighted sampling by
chi-square against the exact distributions; Count-Min error bounds; ObjectPool under 8 threads; DataTableLite sort
stability; NaN, signed-zero and decimal keys on .NET Framework 4.8. R96-1 (a never-expiring entry and a MaxValue
clock) is closed: it stays live.

### Round 156: Numerics core, ninth pass (v2026.11.19.4)

Two fresh reviewers (correctness, robustness): 4 confirmed bugs, and 4 more from their "plausible" lists and from
the fixes, 8 in all, one severe, so section 4 stays re-queued. The regression tests are in `NumericsCoreReview156Tests`
(9, each failing on the old code, run by name under a 60 s timeout).

- **AkimaSpline.Interpolate** (severe): with x spanning past 2^500 and every y below 2^-500, x is scaled down and y up,
  and the slope terms passed the double range in those units though the curve did not: {0, 1e-150, 1e200} against
  {0, 1e-200, 0} gave +∞ at 2.5e199 for about 7e148, in both variants. Such a value is unscaled before it is summed.
  Graded against the cubic on the spline's own slopes over random wide-x, tiny-y splines.
- **EmpiricalDistribution.Quantile** formed its rank as p·(n − 1), rounding a p that was already rounded, so it
  disagreed with Percentile by 1 to 4 ulps at fractional ranks (Quantile(0.7) over i·1e6, n = 198, was
  137899999.99999997 for 137900000), though the doc says they match. A p that is the nearest double to a decimal of
  up to 15 places is ranked from that decimal, rounded once; Percentile forms its rank the same way (and now snaps a
  percentile that is the nearest double to 100·k / (n − 1) to rank k, as Quantile did), so the two agree.
- **ScatteredInterpolation.InverseDistance**: a sample whose distance passed the double range redid every distance in
  quarters, which rounded distinct subnormal distances to 0; samples ε and 2ε away were then averaged as equally near
  (6.5 for 4.4), and that branch ignored power and nearest. Only the far distance is taken in quarters now.
- **ScatteredInterpolation weights**: a ratio dmin/d below the double range gave a weight of 0 even with a small power,
  where (1e-400)^0.001 is 0.40; such a ratio is taken through logarithms.
- **Angle.LerpDegrees** at t = 1 did not return `to` (−353.7 to 193.9 gave 193.9000000000001; 27% of one-decimal
  pairs were off). It steps from the nearer end, so both ends are exact.
- **RunningStatistics** variances in the subnormal range were rounded twice (stepping by 2^-1022): 145 of 20,000
  {0, d} pairs graded against BigInteger rounding were an ulp off. Its ScaleB is now the single-rounding one ZScore and
  WeightedAverage use.
- **RandomUtils.NextExponential** returned −0 for a draw of 0.
- **ToleranceCheck.Evaluate** with both limits +∞ (or both −∞) reported the first sample as the worst, every distance
  and half-distance being ∞; the sample itself decides there (documented).

Checked and clean: Primes on both MulMod paths (IsPrime to 200,000, strong pseudoprimes, Carmichael numbers, random
longs factored against BigInteger), Fraction.ToDouble against exact rounding, RoundToSignificantDigits, Means.Geometric,
Uncertainty.Format's exponent path, DeterministicRandom and Vector2 extremes. R122-1 (Akima −∞ near 1e307) is closed:
the robustness reviewer's 1,013 other non-finite results were all the cubic's own value past the double range. Not
counted: NextPoisson with λ ≥ 30 loops forever on a source that always returns 0 or 1 − 2^-53 (any rejection sampler
does; BUGLIST R156-1).

### Round 155: WinForms + tooling + docs, ninth pass (v2026.11.19.3)

Two fresh reviewers (correctness, robustness): 4 confirmed bugs, plus one in SettingsStore found by round 154's
flaky checked-build gate, 5 in all, none severe, so section 17 stays re-queued. The regression tests are in
`WinFormsReview155Tests` (4) and `DocumentsConfigReview155Tests` (1), each failing on the old code, run by name under
a 60 s timeout; the symbolic-link part of the SaveCsv test was also failed on its own. Documents and Configuration
tests run on .NET Framework 4.8 too.

- **SelectionToTsv** counted selected cells in hidden rows (SelectAll selects them): alone such a cell gave a
  header-only result (so CopySelectionToClipboard reported a copy), and beside a visible one it widened the span the
  visible rows were written over ("\t\tc" for "c"). Hidden rows are left out like hidden columns.
- **SaveCsv**'s write-then-move broke on targets that are not plain files: NUL threw FileNotFoundException for its
  temp file, an alternate data stream threw "The parameter is incorrect", and a symbolic link was replaced by a plain
  file while the file it named kept the old data. A device or stream is written directly, and a link is followed to
  its final target, which the move replaces.
- **ToCsv / SaveCsv** took a surrogate delimiter, which every encoder writes as U+FFFD: a saved file no reader could
  split again. It is refused (delimiter) like a quote or line break.
- **FormStatePersistence.Apply** with an empty state (default, or the out value of a failed TryParse) still set a
  manual start position and Normal, pinning a centred form to (0,0) and dropping a designer-set Maximized. A state
  without a positive size leaves the form alone (documented).
- **SettingsStore.LoadFrom** found the file missing while a fresh temp file showed another save under way, waited 50
  rounds of 10 ms for it, and if the saver was held up longer (a loaded machine) took defaults with no warning, so the
  next save replaced the user's settings. The wait is now two seconds by the clock; if it runs out with the save still
  apparently under way, the defaults come with a LoadWarning (saving is not refused, since the temp file may be a
  crashed save's).

Checked and clean: MenuShortcut's format/parse round trip over every key and modifier set, EnsureOnScreen
idempotence over 200,000 random cases, plot drawing at zoom 1 or less, and the gauge, filter and distribution
reference generators (re-run from copies, byte for byte). Suspects left (BUGLIST R155-1): an interrupted coverage.ps1
leaving an instrumented DLL, checked-build.sh's `git worktree prune` pruning other stale worktrees, and test.ps1
treating `-Configuration:Release` as a filter (it then fails loudly).

### Round 154: Text & small types, ninth pass (v2026.11.19.2)

Two fresh reviewers (correctness, robustness): one confirmed bug and four more from their "plausible" lists, 5 in
all, none severe, so section 11 stays re-queued. The regression tests are in `TextReview154Tests` (3, each failing on
the old code, run by name under a 60 s timeout). Text, Identifiers, Enums and Guards run on .NET Framework 4.8 too.

- **NGramSimilarity** took its grams over UTF-16 units, so two emoji from one surrogate block shared a "gram" (their
  high surrogate): Jaccard("x😀", "x😁", 1) was 0.5, not 1/3. Grams are over code points now (a lone surrogate is a
  unit of its own); the older naive reference in `TextRobustnessReview89Tests` counted UTF-16 units too and was moved
  with it. Graded against grams over characters on random text with pairs and lone surrogates (BUGLIST R98-1).
- **Indent.Dedent** kept at most one \r of a whitespace-only line, so "  \r\r" (two lines ended by lone CRs) came out
  "\r". Every \r stays.
- **TextAlign.Fit** took any ellipsis (three bugs; BUGLIST R98-2): a tab or other control character measured no
  columns, though a terminal shows a tab up to eight wide; a lone surrogate made the result invalid UTF-16; a leading
  combining mark joined the last character kept, so the cut did not show. Such an ellipsis is refused (ellipsis);
  escape sequences, a leading variation selector, and an ellipsis wider than the field (left out, now documented)
  are as before.

Not counted: CaseConverter's caseless-letter joins (documented), Slug's negative maxLength (documented, BUGLIST
R123-1), and TextDiff at its documented cell limit, which is correct but takes about 108 s and cannot be cancelled
(BUGLIST R154-1).

Gates: the checked build's run had one failure, round 151's Settings_ALoadDuringAnotherSaveWaitsForTheFile, while other
builds loaded the machine; it passed ten runs of ten alone. The bug behind it (a load whose wait runs out during
another save took defaults with no warning) is fixed in round 155.

### Round 153: Signal analysis, ninth pass (v2026.11.19.1)

Two fresh reviewers (correctness, robustness): 10 bugs, none severe, so section 2 stays re-queued. The regression
tests are in `SignalReview153Tests` (8, each failing on the old code, run by name under a 60 s timeout; the two
documentation fixes have none). Signal has no netstandard2.0-only paths; it runs on .NET Framework 4.8 too.

- **Fft, an infinite sample at a length that is not a power of two**: Bluestein's chirp convolution spread ∞·0 and
  ∞ − ∞ to every bin, so [∞, 0, 0] gave NaN in all three where the radix-2 path gives ∞ (forward and inverse). The
  finite samples go through the convolution and each non-finite one is added to every bin by the definition, a
  product with an exact zero adding nothing. Graded against that definition over random lengths and placements.
- **Fft, a bin worth exactly double.MaxValue** (Bluestein): it came back a rounding error over it and the scale-back
  overflowed to ∞ (Forward([Max, 0, 0]) bins 1 and 2; Inverse of five Maxes). Within 2^-40 it is the largest double.
- **PhaseUnwrap.GroupDelay on an uneven grid**: the secant over the two neighbours is the slope at their midpoint,
  so φ = −πf² at f = [0, 1, 10] gave 5 for 1. The second-order difference (NumPy's gradient) is used there.
- **PhaseUnwrap.GroupDelay past Array.MaxLength** threw OutOfMemoryException after the whole check; refused first.
- **WelchPsd's step** rounded to the nearest, ties to even, so the real overlap moved either way (0.75 at 10 samples
  was 0.8, at 14 it was 0.714; 0.5 at 5 was 0.6 where SciPy's n // 2 gives step 3). The overlap is rounded down to
  whole samples: the largest k with k / n ≤ overlap.
- **TimeDelayEstimate, a tie at extreme scales**: the inputs were divided by their peaks, which rounds, so past 1e140
  or under 1e-140 an exact tie between two lags went to the far one. They are divided by a power of two.
- **SavitzkyGolay, a constant double.MaxValue**: the weights sum to a hair over 1, so it came back ∞ (334 of 420
  window and order pairs up to 41), though a polynomial passes through. Within 2^-40 it is the largest double.
- **Hampel**: a NaN whose window median is NaN (−∞ and +∞ either side) was "replaced" by NaN and counted.
- **Resample.Decimate** dropped a trailing partial block without saying so (documented now), and
  **PhaseUnwrap.Wrap** gave NaN for ±∞ where its siblings refuse it (documented: an angle with no direction).

Not counted: WelchPsd and Multitaper pass NaN and ∞ samples through to the estimate (as SciPy does), and a
CycleMeasurements level within the hysteresis band of an extreme never arms (the documented band rule; in BUGLIST).

### Round 152: Logging, diagnostics, CLI, ninth pass (v2026.11.19.0)

Two fresh reviewers (correctness, robustness): 10 bugs, one severe, and an eleventh from the .NET Framework 4.8 run,
so section 12 stays re-queued. The regression tests are in `LoggingReview152Tests` (9, each failing on the old code,
run by name under a 60 s timeout). Logging, Diagnostics and Cli run on .NET Framework 4.8 too.

- **ConsoleSpinner, an interval under a millisecond (severe)**: the delay rounded down to Task.Delay(0) and the loop
  spun a whole core redrawing (630,000 writes in half a second). Refused (interval); 1 ms is the shortest.
- **ConsoleSpinner.Stop from inside a draw**: a writer that stops the spinner (a pipe-closed handler) made Stop wait
  for the loop it was running on, forever. It no longer waits for itself; the loop ends when the draw returns.
- **ConsoleSpinner, the colon form of a colour code** (ESC[38:5:196m), which AnsiStyle and ConsoleTable read as one:
  its ESC became a space, and it was shown as text and counted as 11 columns.
- **FileSink on a device**: "NUL" (or /dev/null) has no position or length, so the sink could not be created for it
  ("log nowhere"). A device gets no preamble check and never rotates; on .NET Framework, whose FileStream refuses
  devices, the Windows null device is a null stream.
- **FilterSink.MinimumLevel and RouterSink.AddLevelRoute** took an undefined level (99) and then dropped every event,
  Fatal too, without a word (two bugs). Refused (minimum), as the Logger already does.
- **Logger.Log** with an undefined level sent it to every sink past any minimum, written as "[99]". Refused (level).
- **EnvironmentReport.ToText and ExceptionUtils.Describe** trimmed all trailing white space, which changed the last
  value or message ("value  " came out "value"; two bugs). Only the last line break goes.
- **ExceptionUtils.Describe**: a message or data value of several lines had its later lines at column 0, where they
  read as a new root entry. They are indented under their exception.
- **AsyncLogSink, Dispose from the inner sink** (found when the 4.8 run lost an event in an older test): the pipeline
  cannot wait for its own consumer, so the close it scheduled ran at once, cancelled the consumer, and the events
  queued behind the call were lost. The drain is awaited from the pool, outside the consumer's flow.

Refuted: SyslogSink not backing off after a fresh connect fails (Connect backs off on every failure), FileSink's
case-insensitive backup match deleting another log's backups on Linux (the file pattern there is already
case-sensitive, so it can only under-match), and a Process handle left by SyslogSink (GetCurrentProcess opens none
for its Id). RateLimitedSink's summary of a run still under way may be written after a later event under
concurrency; not reproduced, so it is in BUGLIST.

### Round 151: Documents & config, ninth pass (v2026.11.18.15)

Two fresh reviewers (correctness, robustness): 20 bugs, one severe, so section 7 stays re-queued. The regression tests
are in `DocumentsConfigReview151Tests` (17, each failing on the old code, run by name under a 60 s timeout).
Documents and Configuration run on .NET Framework 4.8 too.

- **SettingsStore, a load during another instance's save (severe)**: File.Replace leaves no file at the name for a
  moment, and a load then took defaults with no warning, so the next save overwrote the user's settings (1,189 of
  68,449 live loads). While a temp file written in the last minute is there, a missing file is waited for (up to half
  a second), and a read that finds it gone looks again.
- **SettingsStore, several instances saving one file**: the move retry stopped when the target was missing (another
  replace under way), and the attribute read after the Exists check threw FileNotFoundException; both now carry on.
- **SettingsStore, attributes after the save**: a failure restoring Hidden or System rolled Value back while the
  file held the new data. It is best effort now.
- **SettingsStore, stale temps**: the cutoff was an hour, too close on a share whose clock runs behind (a day now),
  and File.Replace's "name~RF....TMP" leftovers were never cleaned up.
- **DocxWriter.Save and PdfWriter.Save** wrote straight over the file, so a reader (or a crash) caught a half-written
  document (20 torn reads in 2 s). Written to a temp file, flushed, then replaced.
- **DocxWriter**: only a PNG's 24-byte header was read, so a truncated PNG was embedded and Word showed it broken (the
  chunks are walked: IHDR, image data, IEND); tables past Word's 63 columns built and Word reported damage (refused,
  `DocxWriter.MaxColumns`).
- **PdfWriter**: the page-number footer ran off the page at large font sizes (x = -2169 at size 1000; it shrinks to
  fit), and words separated by tabs were split mid-word (tabs are spaces for wrapping).
- **ReportFigure** kept the caller's PNG array, so a buffer reused before the report was built changed it.
- **ReportTemplates.Comparison**: flagPercent = ∞ turned flagging off with "No metric changed by Infinity% or more"
  (refused), and a threshold or change under 0.005% was shown as "0%" (three significant digits there).
- **MarkdownReport**: a trailing '#' run after a space was a closing sequence ("# Issue #" rendered as "Issue"); a line
  break in a heading ended it; a paragraph starting "1. x" right after a numbered list joined it, and "- x" took in the
  bullet list after it (a comment separates them, in Raw too); empty code wrote one empty line between the fences.
- **HtmlReport**: NUL, lone surrogates and other control characters reached the page (NUL dropped by the parser, a lone
  surrogate not encodable as UTF-8). Each is U+FFFD.
- **ReportBuilder's heading slug**: invariant casing leaves U+0130 alone, so "İstanbul" got the anchor "İstanbul";
  GitHub lowers it as Unicode does.

Refuted: Nl and No characters in slugs (GitHub's \p{Word} keeps all numbers, as the code says), a word-final sigma, a
paragraph wrapping at emoji, the DOCX grid past 9,360 columns (now refused at 63), PDF xref offsets past 10 digits, and
"#hash" headings.

### Round 150: Filters & control, ninth pass (v2026.11.18.14)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 1 stays re-queued. The regression tests
are in `FiltersControlReview150Tests` (6, each failing on the old code, run by name under a 60 s timeout). Control and
Signal run on .NET Framework 4.8 too.

- **PidController, ∞ − ∞**: P (1e200 × 1e300) and D (over a subnormal dt) overflowed with opposite signs even in units
  of 2^-600, so the output was NaN, which the clamp passed through. The largest term, compared by logarithms, decides
  the sign of the overflowing output.
- **PidController, an integral past double.MaxValue**: with a small Ki and wide (or no) limits it was never taken
  (error·dt = 2e310 left it at 0, so the output stayed 0 where it is 2e10), and the saturation integral was dropped
  likewise. It is held in units of 2^600 while it must be. An older test (FiltersControlReview2Tests) that expected
  a finite output after an integral of 2e308 now expects +∞ (the true value with no limits) and checks that it
  unwinds.
- **PidTuning.ZieglerNichols and CohenCoon**: Td underflowed to 0 for a subnormal dead time, so a "PID" silently had
  no derivative (two bugs, one per rule). Refused with "model", as the ultimate-gain rule already did.
- **IirFilter.ButterworthOrder and Chebyshev2Order, band edges**: the small root −x + √(x² + prod) cancelled to 0 for a
  low edge far below the high one (pass 1e-17 to 0.3 gave a low edge of 0; two bugs, one per estimator). It is
  prod over the large root.
- **The same estimators at an extreme ripple**: the edge to design with rounded to exactly Nyquist (1e-300 dB against
  a few ulps more) or a band's two edges onto one frequency (999 dB against 1000). Refused (passRippleDb).
- **IirFilter's subnormal flush** stayed on from the largest sample since Reset, so after one sample of 1 a later
  1e-307 signal (a normal double) was wiped to 0, by Process and by Filter alike. The flush follows the input: on
  while it is at least 2^-969, or exactly 0 after such a signal (the decaying tail).

Checked and clean: 600 IIR designs of every family and band against SciPy, MagnitudeDb and GroupDelay against an
mpmath evaluation, FiltFilt, Filter, Process and Reset(value) against sosfiltfilt, sosfilt and zi, the four order
estimators against buttord, cheb1ord, cheb2ord and ellipord, FIR design and KaiserParameters against firwin and
kaiserord, FirstOrderLag, KalmanFilter1D, PidController against a naive reference, FitStep, StepResponse.Analyze, and
by reading the tuning rules, Smith's fit, Deadband, SchmittTrigger, SlewRateLimiter and the RBJ formulas. A pure-gain
section fed ∞ stays NaN until Reset, as documented.

### Round 149: Security, net, time, ninth pass (v2026.11.18.13)

Two fresh reviewers (correctness, robustness): 14 bugs, none severe, so section 13 stays re-queued. The regression
tests are in `SecurityNetTimeReview9Tests` (12: ten fail on the old code, run by name under a 60 s timeout; one covers
the new `HttpDownloadOptions.MaxBytes`, and one pins PBKDF2's output across the zeroing below). Security, Net and Time
run on .NET Framework 4.8 too.

- **TcpLineClient, strict encoding**: bad bytes made no characters, so a line of them was never held to MaxLineLength
  (3 MB of 0xFF read under a limit of 1000, then DecoderFallbackException at the LF; 0xFF forever ran without end).
  Each run of bad bytes counts as one character.
- **TcpLineClient, skipping an overlong line**: the rest was skipped without limit, and since data kept coming the read
  timeout never fired (374 MB consumed in 5 s). Each call throws InvalidDataException again after another
  MaxLineLength characters with no terminator.
- **TcpLineClient.Send**: a character the encoding cannot represent went out as '?' (ASCII "café" as "caf?") and the send
  succeeded. It throws ArgumentException naming the index.
- **TcpLineClient, HostInfo.Resolve**: a host the DNS API refuses (over 255 characters) was reported as parameter
  "hostName"; it is "host". **PortCheck.IsReachable** returned false for it, a caller's mistake read as unreachable.
- **TcpLineClient's bad-run counter** could overflow in the checked build after 2^31 runs on one client; it wraps.
- **HttpDownload, "bytes 0-99/*"**: a 206 with no total was taken as the whole file. It fails (HttpDownloadException):
  there is no telling whether more follows.
- **HttpDownload, a short 206**: a range covering only part of the file was retried whole as a truncated body, failing
  every attempt alike. Under Resume the rest is asked for next (not counted as an attempt); without it, it fails at once.
- **HttpDownload, the shared ".part"**: a non-Resume download deleted the partial file a Resume download had kept (and,
  where the OS allows, one another download was writing; BUGLIST R142-1). Without Resume the partial file has a name of
  its own.
- **HttpDownload, local work first**: the folder was created and, under Resume, the destination copied to ".part" before
  an unusable URL or answer failed (an ftp: URL left a 20 MB copy). Nothing local changes until the answer is known, and
  the default client refuses a non-HTTP URL up front (ArgumentException, url).
- **HttpDownload, a folder destination** (an existing folder, or a path ending in a separator) downloaded the whole body,
  then failed to replace it. It is refused up front.
- **HttpDownload, a sub-millisecond ReadTimeout** was truncated to 0 ms, so a body already received timed out. It is
  rounded up.
- **HttpDownload, no size cap**: a body with no declared size was written without limit. The new MaxBytes caps a
  declared size and a growing body; neither is retried.

Hardening (not counted): PBKDF2 clears the password's UTF-8 bytes and each block's U and T once used, and
PasswordHasher clears the derived key after encoding or comparing it (BUGLIST R124-3).

Checked and clean: CronScheduler under racing Start, Add, RunDue and Dispose and a Dispose from a job or onError,
CronSchedule at the calendar's ends in UTC, +14, -12 and five DST zones, fuzzing of Iso8601Duration, HumanDuration,
CidrRange, CronSchedule and PasswordHasher.Verify, SecretsFile against every bit flip and truncation, HttpDownload's
416, overrun, retry and cancellation paths, and AuthenticatedEncryption, Hmac, Hashing, ConstantTime, CryptoRandom,
IpUtils, BusinessDays, DateRange, IsoWeek, RelativeTime, TimeOfDayRange and UnixTime by reading.

Two older tests were adjusted: a local-folder failure is still caught before the request (by a read-only check now),
and the round 91 bad-bytes-in-an-overlong-line test uses a limit of 5, so its rest fits one skip.

### Round 148: Charts, ninth pass (v2026.11.18.12)

Two fresh reviewers (correctness, robustness): 13 bugs, none severe, so section 5 stays re-queued. The regression
tests are in `ChartsReview148Tests` (9: eight fail on the old code, run by name under a 60 s timeout; the ninth covers
the new `SvgUtils.FitWidth`, which the old code lacks). Visualization runs on .NET Framework 4.8 too.

Labels too wide for their room are now squeezed into it with the new `SvgUtils.FitWidth` (SVG `textLength` with
`lengthAdjust="spacingAndGlyphs"`): the whole text stays, on the chart.

- **Annotations, Callout and Arrow**: a label wider than the plot skipped the clamp and ran off the chart (an 80-W
  callout ended at x = 1061 on a 350 px plot). It starts at the edge it runs from and is squeezed to the plot.
- **Annotations, HorizontalLine**: the label, anchored at the plot's right, was never width-checked (it started at −485).
- **Annotations, VerticalLine**: the flip checked only the right edge, so a line near the left ran its label off the
  chart. It flips only where it fits, else squeezes into the roomier side.
- **Annotations, bands**: the label, left-aligned in the band, ran past the plot's right edge.
- **SvgChart legend, width**: a long series name put the box at x = −440, off the canvas. Names are squeezed to the plot.
- **SvgChart legend, height**: forty named series ran the box to y = 696 on a 400-high chart, the rest cut off. The rows
  that fit are drawn, ending in "+K more".
- **Titles** in SvgChart, HexBin, Waterfall, MultiPanel and TimingDiagram had no width check: a long one ran off both
  sides.
- **TimingDiagram**: the last tick label was centred on t1 with a 16 px right margin, so one wider than about 32 px
  ("1700000001 s") was clipped. A label that would run off the canvas ends (or starts) at its tick.
- **TimingSignal.FromSamples**: a NaN high threshold threw ArgumentException saying it was below the low one (a NaN low
  threshold threw ArgumentOutOfRangeException). Both throw ArgumentOutOfRangeException.
- **BandChart, centre outside the band**: the auto range came from the bounds only, so a centre line leaving the band
  (the excursion the chart is for) was clipped away. The centre counts toward the range.
- **BandChart, flat band**: with lower = upper everywhere (or all bounds NaN), the range was empty and the centre line was
  drawn flat on one row. Fixed by the same change.
- **SvgChart.RenderElement**: a NaN or infinite x or y offset failed inside SvgUtils.Number, naming "value". It is
  checked up front, naming x or y.
- **ScatterMatrix.Pearson**: the rounded mean of a constant column (0.1 seven times) is not the constant, so r ≈ ±2e-17
  came back (and an "r = 0.00" label) where null is documented. Constancy is checked on the values.

Checked and clean: HexBin with a tiny radius (it throws, so BUGLIST R121-1 is closed), constant 1e20 columns in HexBin
and ScatterMatrix, BandChart, ErrorBarChart and LinePlot at ±MaxValue and subnormal data, MultiPanel's shared and log
axes and size overflow, Waterfall, HeatMap, Colorbar, Colormap, Histogram and BoxPlot extremes, escaping of lone
surrogates and controls, and no NaN or ∞ in any attribute.

Still suspected: BUGLIST R134-1 (what remains of it).

### Round 147: Concurrency, ninth pass (v2026.11.18.11)

Two fresh reviewers (correctness, robustness): 3 bugs, 1 severe, so section 10 stays re-queued. The regression tests
are in `ConcurrencyReview147Tests` (3: the two fixes fail on the old code; the third pins the documented way out of
the first item and passes on both). Threading, Resilience and Runtime run on .NET Framework 4.8 too.

- **ConcurrentPipeline (severe, a contract that could not be kept, now documented)**: work the consumer callback
  starts and does not await (a Task.Run, a timer) inherits the callback's execution context, so until the callback
  returns it counts as the consumer: CompleteAsync from it returned at once with nothing drained, Dispose returned
  with the callback still running, and a Block TryEnqueue on a full pipeline threw where the docs promised a wait (the
  items lost). No runtime can tell such work from the callback's own awaited code, where waiting would wait for good,
  so the class docs now say so and name the way out: start the work under ExecutionContext.SuppressFlow, or after the
  callback returns. Recorded as BUGLIST R147-1.
- **TokenBucketRateLimiter**: a capacity below 1 was accepted, but no count of 1 or more fits under it, so every
  TryAcquire threw ArgumentOutOfRangeException("count"). The constructor refuses it (ParamName capacity).
- **StartupTiming.Report**: a clock that stepped back printed the step as "(+   -5.0 ms)". The sign is written once.

Checked and clean: TaskRace.Hedged with every attempt failing, ConcurrentPipeline DropOldest counts and a 50-round
producer/CompleteAsync stress (no lost or duplicated items), Dispose and throwing items during CompleteAsync, KeyedLock
under random cancellation, ParallelUtils' parallelism cap, Jitter's bounds near long.MaxValue, and ProcessRunner's
timeouts, exit codes and unterminated last line.

New suspects (BUGLIST R147-2): WhichExe returns PATHEXT's casing and resolves a named non-executable extension;
ParallelUtils may throw OperationCanceledException though every item finished; ProcessRunner.RunAsync may drop an
OnOutputLine exception in favour of a cancel; PeriodicWorker may refuse an interval just past int.MaxValue ms that
Task.Delay takes; a token whose source is disposed may behave differently on .NET Framework. None was probed.

### Round 146: Statistics, ninth pass (v2026.11.18.10)

Two fresh reviewers (correctness, robustness) and the fixing session: 8 bugs, none severe, so section 3 stays re-queued
(8 ≥ 3). The regression tests are in `StatisticsReview146Tests` (10, each failing on the old code, run by name under a
60 s timeout; Numerics runs on .NET Framework 4.8 too).

- **HypothesisTests.FTestEqualVariances**: far in a tail the statistic F = (s_a/s_b)² underflowed or overflowed, so
  spreads 1e-170 apart gave p = 0 (in either order) for a true 1.27e-170, and a subnormal F (1e-160 apart) put 5.6e-6
  relative error in p. Past d1·F/d2 < 1e-200 (or its mirror) the tail is the one at a representable point scaled by its
  leading power.
- **HypothesisTests.TwoSampleT, OneSampleT, FTestEqualVariances**: a sample spread past double.MaxValue (data near
  ±1.5e308) came back ∞: Welch's df was NaN and threw ArgumentOutOfRangeException, the pooled and one-sample t were 0,
  and the F-test's p was 0. Such samples are taken again scaled down by 2^-600 (t, df and F are scale-free; the F-test
  carries the scale in its logarithm).
- **HypothesisTests.TwoSampleT** (found while fixing): the difference of means was taken about a[0] for both samples,
  and a[0] was lost beside much larger values in b: {1, 2, 3} against {±1e300} gave half the true t. Each sample is
  taken about its own first value and the parts summed compensated.
- **HypothesisTests.TwoSampleT (pooled)**: na + nb − 2 overflowed int past 2^31 values in all (df −2^31). It is
  formed in double.
- **HypothesisTests.MannWhitneyU** and **PermutationTest.TwoSample**: two samples holding more than Array.MaxLength
  values together threw OverflowException; they throw ArgumentException naming b. (KolmogorovSmirnovTwoSample's
  na + nb in its scale factor is now in double too; reaching it needs 2^31 sorted values, so it has no test.)
- **KolmogorovSmirnovOneSample**: the exact one-sided tail summed ln C(n, j) plainly, putting 5.5e-10 relative error
  in p at n = 100,000 (7.8e-11 at 50,000) against mpmath. The sum is compensated and 1 − d − j/n is formed from the
  whole count n − j.
- **ConfidenceInterval.ForProportion**: the Wilson lower bound center − half cancelled at few successes (1 in 1e9 at a
  confidence of 1 − 1e-15 was 2e-13 relative off). It is p²/(denom·(center + half)), the bounds' product over the
  upper.
- **MultipleRegression RegressionResult.Predict**: each term was scaled to y units on its own, so with y near 1e306 the
  terms overflowed and summed ∞ − ∞ = NaN at the observed rows, where Fitted was finite; ConfidenceInterval and
  PredictionInterval inherited it. The prediction is summed in scaled units and scaled once, as Fitted is. (The
  response's centre is also held to the values' range: a rounded sum put the centre of sixty 1e25s above them all, with
  no wrong output found.)

Checked against the reviewer's note: the n = 100 one-sample KS discrepancy (6e-12) was a reference that doubled the
one-sided tail; there the exact Marsaglia-Tsang-Wang path is used and is right.

Still suspected: BUGLIST R108-1 to R108-5, R118-1 to R118-4 and R140-1.

### Round 145: Objects, functional, grids, graphs, intervals, ninth pass (v2026.11.18.9)

Two fresh reviewers (correctness, robustness): 8 bugs, 1 severe (an exponential walk), so section 14 stays re-queued.
The regression tests are in `ObjectsReview145Tests` (9, each failing on the old code, run by name under a 60 s timeout;
Objects and Functional run on .NET Framework 4.8 too).

- **FlattenObject, DeepEquals and PropertyDiff (severe)**: the made-object cutoff limited depth, not fan-out, so a type
  with k properties each making a new one of itself grew about k^5 values (k = 20 flattened to 3.4 million keys, k = 30
  ran out of memory, k = 40 compared for minutes). Below the first made value on a path, a walk now follows at most
  4,096 made values (`ObjectMembers.MaxComputedObjects`) and stops descending past that.
- **DeepEquals** and **PropertyDiff**: a pair first reached at the end of a chain of made objects had its own made
  members cut as too deep, and was skipped when a direct path reached it, so a difference there was missed (Equals
  returned true). A pair is compared again when a path reaches it that cuts less.
- **FlattenObject** had the same miss: an object first expanded past such a chain was recorded as-is from the direct
  path, so its made members reached no key. It is expanded again by the shorter path.
- **TypeUtils.TryConvert**: an enum to double or float rounded silently ((LongEnum)(2^53 + 1) became 2^53), where its
  number is refused. An enum is checked as its number.
- **TypeUtils.TryConvert**: a ulong-backed enum's upper half was refused from a decimal or a double (1e19). Both convert
  up to ulong.MaxValue.
- **ObjectMapper**: Ignore and ForMember matched ignoring case only, so with Name and NAME on the target, Ignore("NAME")
  ignored both and ForMember("NAME") set both. An exact name wins; case is ignored only when no property has the
  exact name.
- **ExpressionAccessor**: the compiled getters and setters (object-based and typed) threw NullReferenceException for a
  null instance; they throw ArgumentNullException("instance"), as GetValue and SetValue do.
- **Result.Try**: an exception whose Message getter throws let that exception out of Try; the type's name stands in.

Test fix: `SecurityNetTimeReview5Tests.Scheduler_JitterBackAroundAnAddKeepsADueRun` failed once in the verify under
load. Since round 142, Add wakes the scheduler's loop, whose own pass could read the advanced test clock and run the job
before the test's RunDue (the job still ran once). The test lets the loop park again before moving the clock.

Checked and clean: RangeMap, RangeSet and IntervalTree (randomized differential), GridPathfinding, FloodFill and
ConnectedComponents2D, the Graphs types (randomized differential, and edits during enumeration), interval hashing of
-0.0, PropertyPath with 200,000 segments, FriendlyName on deep and nested types, and the walks on CultureInfo,
encodings, TimeZoneInfo, XElement, Uri, Version, RuntimeType and exceptions.

Still suspected: BUGLIST R93-3, R125-1 to R125-4 and R136-1. RangeMap's per-operation renumber pass (O(n/256)) was not
probed.

### Round 144: Windows satellite, ninth pass (v2026.11.18.8)

Two fresh reviewers (correctness, robustness): 6 bugs, 1 severe (negative numbers reading back as the default), so
section 15 stays re-queued. The regression tests are in `WindowsReview144Tests` (4, each failing on the old code, run
by name under a 60 s timeout).

- **RegistryUtils.GetValue<T> (severe)** widened every DWORD as unsigned (and a negative QWORD to ulong) before
  converting, so a negative short, sbyte or short-backed enum written as a DWORD, or an int or int-backed enum written as
  a QWORD, overflowed and silently read back as the default, though SetValue had stored it by its bits. A target too
  narrow for the unsigned number now reads the bits as signed.
- **RegistryUtils.SetValue**'s doc promised every value read back unchanged, but -1L as a DWORD has the bits of
  0xFFFFFFFF and reads back into long as 4294967295 (as round 131 means it to). The doc states the rule: negative
  numbers read back into a type too narrow for the unsigned reading; a wider type reads the unsigned number.
- **RegistryUtils.SetValue** range-checked integer text given DWord or QWord as signed only, through RegistryKey in the
  current culture: "4294967295" was refused though "-1" was taken. Integer text follows the same rule as numbers,
  parsed invariantly; other text is refused naming `value`.
- **RegistryUtils.SetValue** created the subkey path before RegistryKey checked the value, so every refused write (text
  as a DWORD, a non-array as MultiString, a non-byte[] as Binary or None, a 16,384-character name) left new, empty keys
  behind. These are checked first.
- **RegistryUtils.SetValue** reported an undefined kind as RegistryKey's own parameter, "valueKind"; it is refused up
  front as ArgumentOutOfRangeException naming `kind`.
- **SingleInstanceApp.Start**: when a live primary took the message but its answer was lost (the client's cancel timer,
  or a primary that closed without answering), Forward rightly did not resend, but Start then failed to take the gate
  and forwarded again, so the primary handled the launch twice. A message that went out is never sent again; Start
  still takes over if the primary has exited.

Checked and clean: DpiInfo's awareness inside AwarenessScope (all five); GetSpace for long, device-spelled and UNC
directories with and without the trailing backslash; SingleInstanceApp forwards with timeouts of 0 to 20 ms (one
delivery each); relative shortcut paths; StorageDeviceInfo.Describe on long paths; struct layouts and offsets in
JobObject, StorageDeviceInfo, Elevation, MemoryStatus, IdleTime, OsVersionInfo, PowerStatus and PrivilegeScope;
ClipboardUtils' DIB and drop-file bounds; RunElevated's quoting; EventLogWriter's truncation.

Still suspected: BUGLIST R106-1 to R106-7, R110-1 to R110-4 and R131-1.

### Round 143: IO, ninth pass (v2026.11.18.7)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 8 stays re-queued. The regression tests
are in `IOReview143Tests` (6, each failing on the old code, run by name under a 60 s timeout; the overwrite ones run on
Windows, and on .NET Framework 4.8 too).

- **ZipUtils**, **TarUtils** and **DirectoryUtils.Copy** cleared a read-only target's flag before replacing it, so a
  replace that then failed (the target held open) left the old file unprotected. The flag goes back when the replace
  fails.
- The same overwrites dropped the target's Hidden (and System) attribute on net8, through File.Move(overwrite), where
  netstandard2.0's File.Replace kept it (DirectoryUtils dropped it on both), unlike AtomicFile and JsonFile. Every
  replace is a File.Replace now, on both builds.
- A target another process had open with FileShare.Delete was replaced on netstandard2.0 and refused on net8; it is
  replaced on both. DirectoryUtils' netstandard2.0 delete-then-move, which lost both copies when the move failed, is
  gone with it.
- **ZipUtils.ExtractToDirectory** and **TarUtils.ExtractToDirectory** created the destination (nested folders too)
  before the archive was opened, so a missing or corrupt archive left them behind. They are made once it reads.
- **CsvBinder**: a `char?` holding a space or a tab was written as itself and read back as null (the blank-means-null
  trim came first); only an empty field is null now.
- **CsvBinder**: a relative Uri with surrounding spaces came back trimmed; it is built from the untrimmed text, as a
  string is (an absolute one trims itself).

Checked and clean: FileRetention's wildcard matching against the BCL's Win32 expression matcher (400,000 random
patterns and names, no mismatch); CsvBinder round-trips of TimeSpan, float, DateTime and escaped Uri extremes;
FileChangeCoalescer's merge rules; FileWatcher's filter and rescan; DirectorySnapshot keys for trailing dots and
spaces; FileRetention.Prune, ShiftNumbered and TimestampedName; ByteSize, FixedWidth and CsvLine at their edges; the
JSON, CSV, dotenv, hex-dump, line-reader, stream and temp-file types for robustness.

Still suspected: BUGLIST R94-1, R94-3, R94-4, R112-1 to R112-5 and R138-1. R94-2 (a folder holding only the archive
left out) was fixed in round 138 and its row is gone.

### Round 142: Security, net, time, eighth pass (v2026.11.18.6)

Two fresh reviewers (correctness, robustness): 12 bugs, none severe, so section 13 stays re-queued. This ends the
eighth pass. The regression tests are in `SecurityNetTimeReview8Tests` (10, each failing on the old code, run by name
under a 60 s timeout).

- **HttpDownload** under Resume: a 206 whose Content-Range starts elsewhere than the byte asked for threw but kept the
  partial, so every later call asked for the same range and failed the same way. It is discarded, as on a 416.
- **HttpDownload**: an `ExpectedSha256Hex` that is not 64 hex digits can never match, and cost a whole download each
  time (under Resume, the progress too); it is an ArgumentException up front.
- **HttpDownload**: an empty or relative URL (with no base address on the client) failed in SendAsync as
  InvalidOperationException after the destination folder was created; it is an ArgumentException naming `url` first.
- **HttpDownload**: an empty destination path gave ParamName "path", from Path.GetFullPath; it is `destinationPath`.
- **HttpDownload**: a 206 with no Content-Range to a plain GET said "starting at byte  for"; the message says what
  came back.
- **CronSchedule**: "7/1" was out of range though 7 alone is Sunday. A step from a value over day-of-week runs to
  Saturday (so "1/2" is Monday, Wednesday, Friday, as round 13 settled); one from 7 is Sunday alone, and the doc now
  says where such a step ends.
- **CronScheduler**: a job added after Start waited out the loop's current wait, worked out before it existed, so a
  "* * * * *" job could first run a minute late. Add wakes the loop. (A round 91 test assumed the loop stayed parked
  through an Add and failed now and then; it waits for the woken pass to park again.)
- **CronScheduler.Remove(null)** returned false; it throws ArgumentNullException, as Add does.
- **TcpLineClient.Send** sent a lone surrogate as U+FFFD and reported success; it throws ArgumentException, as the
  hashing and key-derivation code does.
- **CryptoRandom.Bytes** and **KeyDerivation.GenerateSalt** past the largest byte array threw OutOfMemoryException,
  not the argument error.
- **PortCheck.IsReachable** read an empty host as unreachable, and **HostInfo.Resolve** as this machine; both throw
  ArgumentException.
- **SecretsFile.LoadOrCreate** checked the passphrase only when the file existed, so a missing one surfaced at the first
  save; it is checked first.

Checked and clean: cron with a time zone over every system zone's offset changes from 1990 to 2030 (25k random
trials against a minute-by-minute walk; this machine's data puts Samoa's jump on 2011-12-31, and it is included);
CronScheduler under concurrent RunDue, Add, Remove and Dispose from inside and outside jobs; Pbkdf2 against the BCL;
IsoWeek against ISOWeek over the whole DateTime range; CidrRange against a bitwise reference; TcpLineClient.ReadLine
against a reference splitter over six encodings; the durations and Unix times at their extremes.

Still suspected: BUGLIST R124-1 to R124-4. (A second concurrent non-Resume download of the same destination deletes
the first's partial where the OS allows it; Windows does not, and it was not probed elsewhere: R142-1.)

### Round 141: Text & small types, eighth pass (v2026.11.18.5)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 11 stays re-queued. The regression tests
are in `TextReview141Tests` (6, each failing on the old code, run by name under a 60 s timeout; the casing one fails
on .NET Framework 4.8, where the bug was). The two doc bugs have no test.

- **CaseConverter** on .NET Framework: its invariant casing lacks mappings .NET 8 has, so 'ς' and 'µ' stayed lower-case
  in CONSTANT_CASE ("λόγος" gave "ΛΌΓΟς"), 'ſ' too, and the Kelvin and Ohm signs and Cherokee kept their case in the
  lower styles. The netstandard2.0 build fills in .NET 8's simple mappings (111 pairs, Cherokee by range) where the
  runtime left the character unchanged and knows the target as a letter.
- **CaseConverter** doc: the letters whose capital lowers to another letter (final 'ς', 'ſ', the Greek symbol forms,
  'ẛ', U+1FBE) don't survive a trip through an upper-case style; the doc now says so.
- **TextAlign.Fit**: an escape sequence cut short was dropped whole, its line feed with it, so the character before it
  joined a U+FE0F after and the result was a column too wide. Its control characters are kept, as AnsiText.Strip does.
- **TextAlign.PadLeft / Center / Fit**: a symbol pad put before a U+FE0F that starts the text became its base and
  turned emoji-wide (PadLeft("\uFE0Fx", 4, '·') measured 5), and an ellipsis starting with one widened the character
  before it. The base-less selector is dropped in both places.
- **WordWrap.WrapToString** took a null newline as empty and ran the lines together; it throws ArgumentNullException.
- **UlidGenerator.NewUlid** moved the timestamp on before drawing the random bits, so after a Random that threw, the
  rest of that millisecond counted up from half-written bits. The bits are drawn first.
- **Tokenizer.Tokenize** took overlapping roles silently (an escape that is also a delimiter or quote, a quote that is
  also a delimiter: "a,b" with ',' as both gave "ab"); it throws ArgumentException.
- **CommandLineSplitter** doc said whitespace separates tokens; only space and tab do, as on Windows.

Checked and clean: CaseConverter's styles on .NET 8 against its own casing over the BMP (the .NET Framework gaps
above were found by diffing the two); otherwise TextAlign's widths, Tokenizer and CommandLineSplitter splitting, Slug,
Soundex, AnsiText and SnowflakeIdGenerator in the reviewers' probes.

Not a bug: GlobMatcher past its cache cap rebuilds a named character's mask each time, at the cost its doc states
(kept as BUGLIST R141-1 for a better algorithm).

Still suspected: BUGLIST R123-1 to R123-3 and R141-1.

### Round 140: Statistics, eighth pass (v2026.11.18.4)

Two fresh reviewers (correctness, robustness): 6 bugs, 1 severe, so section 3 stays re-queued. The regression tests
are in `StatisticsReview140Tests` (6, each failing on the old code, run by name under a 60 s timeout).

- **PermutationTest.TwoSample** (severe): the default mean difference summed raw values, so its rounding error was eps
  times the data's offset rather than of the statistic. Rearrangements holding exactly the observed values (1000.3 and
  1000.7 swapped for their twins) fell outside the tie tolerance and were not counted: an exact p of 1 read 0.7, and
  78 of 300 small random cases on offsets of 1e3 to 1e12 gave a wrong exact p. The default statistic now runs on the
  pooled data centred once, with compensated sums; the test grades 200 random cases against an exact BigInteger count.
- **LinearRegression.Fit** with y near 1e300 over x near 1e-300: the scale ratio overflowed and a zero slope read
  0 x infinity = NaN. The scale-back is ordered so a representable result is reached.
- **MultipleRegression.Fit**, the same data: the coefficients and standard errors were NaN, and the intercept, built
  from the unscaled coefficients, was NaN or infinite though it is finite. The intercept is formed in scaled units,
  and Predict sums each term in scaled units (the prediction at the centre was NaN). The slope itself is a rounding
  crumb past MaxValue there, which no double solver avoids (see BUGLIST R140-1).
- **MultipleRegression.Fit**: a coefficient truly past MaxValue made the intercept infinite (b0 - infinity x c) though
  the intercept is finite; fixed by the same scaled intercept.
- **RobustRegression.TheilSen** returned an infinite intercept from finite halves (y near 1e308); it refuses an
  overflow there as it does for the slope.
- **DistributionFit.FitNormal** on {0, 5e-324} said "All values are equal"; the spread rounds to 0 but the values
  differ, and it says so.

Checked and clean: the four special-function copies agree; Distributions and SpecialFunctions against mpmath (11.6k
cases); the t, F, Mann-Whitney, KS, ANOVA and correlation tests, the distribution fits and the intervals against scipy
or exact references, including data on offsets; GaugeRR against an exact crossed ANOVA over 300 studies.

Not bugs: the ChiSquare independence and GaugeRR degrees-of-freedom products cannot overflow int, since both take
rectangular arrays whose element count is under 2^31 (this closes BUGLIST R108-6). PermutationTest.Paired near-ties
between values differing only after decimal conversion are counted on purpose by the documented tolerance.

Still suspected: BUGLIST R115-1 to R115-7, R137-1, R138-1, R139-1 and R140-1.

### Round 139: WPF satellite, eighth pass (v2026.11.18.3)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 16 stays re-queued. The regression
tests are in `WpfReview139Tests` (4, each failing on the old code, run by name under a 60 s timeout).

- **NumericInputBehavior.StepValue** and **Coerce** read only a sign and a decimal point, but WPF shows a small bound
  double in exponent form ("1E-05"): a step read it as no number and went to 0, and losing focus kept it. Both read
  an exponent now.
- **NumericInputBehavior** refused every deletion from a box already holding text its rules refuse (a bound 3.14159
  at two decimals, "1E-05"), since each still left it unacceptable; deleting from such a box is allowed, and losing
  focus coerces it.
- **PlotPresenter.FitToView** under a zoom clamped to 1e308 stored infinite offsets, after which every pan and zoom
  was refused as non-finite; a fit that is not finite keeps the current view.
- **ValidationRuleSet.Matches** threw the regex parser's ArgumentException, which names no parameter; it names
  `pattern` now.

Not a bug: "15" refused in [-20, 10] (a reviewer read it as on the way to "-15"). Typing past the maximum is refused by
design (round 82 pins it, under the default minimum of -infinity); only under a negative maximum, where no positive
value is ever valid, is a sign typed in front allowed for.

Still suspected: BUGLIST R115-1 to R115-7, R137-1, R138-1 and R139-1.

### Round 138: IO, eighth pass (v2026.11.18.2)

Two fresh reviewers (correctness, robustness): 8 bugs, 2 severe, so section 8 stays re-queued. The regression tests
are in `IOReview138Tests` (11, each failing on the old code, run by name under a 60 s timeout, the zip rewrites on
.NET Framework 4.8 too; the UTF-8 name test is a guard).

- **FileWatcher.Rescan** (severe, found by both reviewers) re-checked a change against the stale listing with a plain
  File.Exists, which reads "pre." as "pre": a created "x." was forgotten, and "pre." deleted beside "pre" was dropped,
  on every restart and overflow rescan. It checks the exact form now.
- **ZipUtils.AddOrReplaceEntry** (severe) rewrote a name stored in a code page (flag bit 11 clear, the CP437 byte
  0x82) from its decoded U+FFFD as UTF-8, so the name was lost and two such names became one. The streamed copy
  dropped the archive comment and entry comments, and on netstandard2.0 (no ExternalAttributes) every Unix mode.
  It copies comments and attributes now, and leaves an archive whose names or comments are not stored as UTF-8, and
  every archive on netstandard2.0, to the in-memory rewrite. .NET Framework's update mode re-encodes every name too,
  so there code-page names are read and written through Latin-1, byte for byte; an archive mixing them with UTF-8
  names (or a new name past ASCII going into one) is refused with NotSupportedException, the file untouched.
- **ZipUtils.ExtractToDirectory** made the folder for a Unix-made entry "a\" (whose Name is the whole name) and then
  refused it as a file, part way through; a trailing backslash is a folder on Windows.
- **ZipUtils** and **TarUtils.CreateFromDirectory** counted the archive being written as a folder's content, so a
  folder holding only it was lost rather than stored empty.
- **FileWatcher**'s filter took a '*' in a name (allowed on Unix) as a literal match for the pattern's '*' and lost
  the backtrack ("a*b" missed "a*xb").
- **FileRetention**'s pattern match allocated a pattern × name table per file (1.5 GB over 300 files for a long
  pattern); it keeps two rows.
- **ZipUtils** on netstandard2.0 took every reparse point for a link, so a cloud-file placeholder was skipped; it reads
  the reparse tag, as DirectoryUtils does (no test: a placeholder needs a filter driver; kept suspected as R138-1).

Still suspected: BUGLIST R115-1 to R115-7, R137-1 and R138-1.

### Round 137: Signal analysis, eighth pass (v2026.11.18.1)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 2 stays re-queued. The regression
tests are in `SignalReview137Tests` (7, each failing on the old code, run by name under a 60 s timeout).

- **SavitzkyGolay** built a w × w basis for order w − 1 (120 s and w² doubles at w = 2001), though that order is the
  identity; it returns the input now. The basis was walked down the columns of a 2-D array, which made the build
  cache-bound; it is a column to an array now (order w − 2 at w = 2001 went from minutes to seconds). The build's
  O(w·m²) cost is documented.
- **Convolution** and **CrossCorrelate** rescued subnormal products only when the whole inputs' peaks multiplied below
  2^-969, so an element whose overlap held only tiny values beside a normal peak had each product rounded
  ([1, x, x, x] ∗ [y, y, y] with products of 0.4ε gave 0 for ε). Every tiny element is summed again scaled by its
  overlap's own peaks (found by both reviewers).
- **WelchPsd** scaled samples only when their peak was below 2^-500, so at 1e-150 a quiet bin's |X|² underflowed in the
  sum (bin 2 116% off, three bins 0, for a normal density); every finite peak is scaled now. **Multitaper** had the
  same rule (not reproduced: its tapers' leakage kept the bins normal) and is changed with it.
- **PhaseUnwrap.GroupDelay** accepted an infinite frequency, which passed the order check and gave a delay of 0 at it
  and its finite neighbour; NaN phases only spread NaN. Phases and frequencies must be finite.
- **Hampel** accepted nSigmas = ∞, whose threshold in a constant window was ∞·0 = NaN, so a point the documented rule
  flags was kept; it must be finite.
- **Window.EquivalentNoiseBandwidth** and **CoherentGain** returned NaN for a window holding ∞ or NaN, where every other
  unusable window is refused.
- **MedianFilter** and **SavitzkyGolay** threw OutOfMemoryException for a list longer than Array.MaxLength, and
  **Window.Apply** named Create's "length"; they are argument errors on "signal", "data" and "samples".

Still suspected: BUGLIST R115-1 to R115-7 and R137-1.

### Round 136: Objects, functional, grids, graphs, intervals, eighth pass (v2026.11.18.0)

Two fresh reviewers (correctness, robustness): 9 bugs, one severe, so section 14 stays re-queued. The regression
tests are in `ObjectsReview136Tests` (8, each failing on the old code, run by name under a 60 s timeout).

- **TypeUtils.DefaultValue** (severe: a silently wrong result) used Activator, which runs a struct's explicit
  parameterless constructor (C# 10): the "default" of a struct setting X = 42 had X = 42, and TryConvert's failure
  value with it. A constructor that threw escaped TryConvert, ConvertOrDefault and ObjectMapper.Map as
  TargetInvocationException, though none of them should throw on an unconvertible value. It is `default(T)` now.
- **TypeUtils** parsed a DateTime's own "O" text ("...Z") as local time, shifted by the machine's zone; it parses
  round-trip now, as PropertyPath and the binders do.
- **TypeUtils** and **PropertyPath** keys parsed enum names ignoring case only, so with members A and a, "a" became A
  (and a path "[a]" read and wrote the A entry). An exact name wins now.
- **FlattenObject** and **PropertyDiff** printed a key raw inside brackets: "[a]b]" did not parse, "[ x]" read key "x"
  and "['y']" key "y". A key holding ']', edge white space or a leading quote is quoted (UnflattenObject reads it).
- **Cell.ToString** used the current culture (U+2212 under sv-SE, bidi marks under fa-IR and ar-SA).
- **AttributeCache** threw InvalidCastException for a custom MemberInfo returning a plain object[]
  (NullReferenceException on .NET Framework, found by the net48 run of the new test).
- **ExpressionAccessor**'s object-based accessors threw InvalidCastException for a value or instance of the wrong
  type; they throw ArgumentException ("value" or "instance") now, as reflection does.

Still suspected: BUGLIST R136-1.

### Round 135: Logging, diagnostics, CLI, eighth pass (v2026.11.17.25)

Two fresh reviewers (correctness, robustness): 9 bugs, none severe, so section 12 stays re-queued. The regression
tests are in `LoggingReview135Tests` (9, each failing on the old code, run by name under a 60 s timeout).

- **ConsoleSpinner widths**: a flag counted 4 columns and a ZWJ sequence every member (the family emoji 6), so a
  message was cut short (`Fit` dropped an "X" after a flag that fitted); the whole of U+1F000-1FAFF counted wide,
  though U+1F700, U+1F800, U+1F10B and U+1F0A1 are narrow, as were U+303F and the Yijing hexagrams inside the CJK
  range; Hangul Jamo Extended-B counted 1 instead of 0. Only the East Asian Width W ranges of the emoji blocks count
  two now, a regional-indicator pair is one glyph and a ZWJ joins the next character at no width.
- **ConsoleSpinner writers**: only IOException and ObjectDisposedException were quiet, so a writer throwing
  NotSupportedException made the constructor and Dispose throw.
- **ConsoleTable** padded a styled cell by its escape characters (Bold("x") made a ten-wide column); SGR sequences
  take no width.
- **AnsiStyle** re-applied its style after a full reset only: a 22, 39 or 49 inside the text ended the outer bold or
  colour for the rest of it. What a partial reset leaves cancelled of the outer style is put back.
- **Logger**: a clock that threw escaped Log to the caller (the system clock stamps that event now); an undefined
  MinimumLevel was accepted and dropped every event.
- **FirstChanceMonitor.ThrottleWindow** accepted a negative window, which turned throttling off.
- **Benchmark.Run** threw OutOfMemoryException for int.MaxValue iterations (past the largest array); the limit is
  `Benchmark.MaxIterations`.
- **ScopedTimer.Dispose** tested, then set, a plain flag: 2 000 racing pairs reported 2 031 times.

Still suspected: BUGLIST R135-1.

### Round 134: Charts, eighth pass (v2026.11.17.24)

Two fresh reviewers (correctness, robustness): 18 bugs, none severe, so section 5 stays re-queued. The regression
tests are in `ChartsReview134Tests` (17, each failing on the old code, run by name under a 60 s timeout).

- **SvgChart and LinePlot** accepted an equal pair of explicit limits (XMin = XMax) and drew every point, those far
  outside it too, on the centre line; both reject it, as the other renderers do. LinePlot keeps drawing a reversed
  pair as a reversed axis (now documented).
- **SvgChart** never left room for half the first x tick label: with no y tick labels (and every MultiPanel panel past
  the first column under ShareY) a wide label started off the canvas.
- **BoxPlot.Compute** formed k·IQR from an IQR that overflowed to ∞: at reach 0 the fences were NaN, at a tiny reach
  infinite, so ±1.79e308 data had no outliers and whiskers past the quartiles.
- **BandChart** sampled the band once per pixel column, so with more samples than columns a spike between two column
  positions vanished from the envelope; each column now spans every sample drawn in it. The centre line to a sample
  whose pixel overflowed (1e300 on a 1e-300 range) was dropped; such an end is moved to the range limit it crosses.
- **ErrorBarChart** dropped a whisker whose ends mapped to ±∞ pixels; the whisker is clamped to the range first.
- **PlotFrame clipping** (LinePlot, SvgChart): the clip parameter underflowed to 0 (5e-301 over 1e300), so a segment
  collapsed to a point; an end cut at an edge is now put on it.
- **HeatMap, Colorbar, HexBin and Waterfall** mapped Min and Max to positions 0 and 1, so a map with stops at −1 … 1
  showed half its colours, against HeatMap's documentation. They map through the whole stop range with the new
  `Colormap.MapFraction` (with `StartPosition` and `EndPosition`); maps over [0, 1] are unchanged.
- **ScatterMatrix**: the group legend was a fixed 120 px column on a canvas sized for the panels, so long names and
  every entry below the last panel were cut off. Subnormal columns were binned through halved values that rounded
  ({1, 2, 3, 4}·ε counted 1, 0, 1, 2), and their 5% pad halved to 0.
- **TimingDiagram**: Clock with its end before its start returned an empty clock; a default(TimingMarker) failed
  naming "text"; a marker label near the right edge ran off the canvas; every sub-pixel unknown sliver drew up to 200
  hatch lines (20 000 changes in a tall lane wrote 67 M characters). Empty paths are no longer written.
- **HexBin**: a radius so small against the frame that a hexagon index passed 1e9 skipped points inside the frame as
  if far outside it (radius 1e-9 on 500 px gave no cells; Width 4e7 lost every point right of the limit). Compute and
  RenderSvg throw instead.
- **MultiPanel** ShareX / ShareY joined an empty panel's placeholder [0, 1] into the shared range.

Still suspected: BUGLIST R134-1.

### Round 133: Filters & control, eighth pass (v2026.11.17.23)

Two fresh reviewers (correctness, robustness): 10 bugs, none severe, so section 1 stays re-queued. The regression
tests are in `FiltersControlReview133Tests` (11, each failing on the old code, run by name under a 60 s timeout).

- **FirFilter.KaiserParameters** refused 8 dB, which kaiserord allows (it refuses only less).
- **Biquad and IirFilter responses** summed the denominator directly, which next to a pole near the unit circle lost
  its digits: an elliptic high-pass's pass-band edge read −0.184 dB (−0.209 through Response) where its sections give
  −0.4746 dB exactly. The denominator and Response now fall back to the roots where the sum cancels, as the numerator
  did, and the roots' discriminant is formed from exact (Dekker) products: rounded, it moved a pole at radius 1 − 4e-12
  by 4% of its distance from the circle. GroupDelay shares the same root code.
- **IIR and FIR Filter / FilterAligned** rescued an overflowed output only when every sample was finite, so a NaN at
  the end of the buffer turned earlier finite outputs (1.7e308) into ∞. Outputs a non-finite sample cannot reach are
  rescued whatever comes later.
- **IIR edge checks** compared against a rounded fs / 2, which refused 2ε at a rate of 5ε (Biquad, every design and
  every order estimate); they compare the ratio f / fs with ½, as the FIR checks do.
- **KalmanFilter1D** formed the new covariance as K·R, which lost P when P/R underflowed (P 1e-30 against R 1e300
  became 0, and a filter with no process noise ignored every later measurement).
- **PidController** tested windup and the step to the limit with products that underflowed at tiny scales (ki 1e-200,
  a limit of 5e-201): the integral wound up past the limit, and an I-only loop never moved. Both now test signs.
- **PidTuning.CohenCoon** formed 4τ, which overflowed for τ = 5e307 (derivative time ∞).
- **FopdtModel.StepResponse** returned NaN for a NaN time and 0 for an infinite step; both throw.
- **SlewRateLimiter** with an unlimited rise rate sent every update down the halved path, where a subnormal fall step
  rounded (3ε went 4ε); only a finite rate whose step overflowed is halved.

Still suspected: BUGLIST R133-1.

### Round 132: WinForms + tooling + docs, eighth pass (v2026.11.17.22)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 17 stays re-queued. Six were confirmed
by a reviewer's probe; two more were reviewer suspects, reproduced and fixed with them. The regression tests are in
`WinFormsReview132Tests` (6, each failing on the old code, run by name under a 60 s timeout).

- **PlotPictureBox** drew pixel boundaries away from ContentToView once MaxZoom was raised: GDI+'s nearest-neighbour
  stretch put a boundary meant for view x 100 at 99 at zoom 1000, 109 at 2e4 and 65 at 1e5, and at 1e6 showed one
  colour across the view. A magnified nearest-neighbour view now maps each view pixel to the source pixel under its
  centre, as ViewToContent does.
- **PlotPictureBox** dropped a wheel turn whose power overflowed (WheelFactor 1e200 over two notches) or underflowed
  to 0; the factor is held to the double range, so the turn zooms to the limit.
- **MenuBuilder.FindItem** matched a ToolStripTextBox or ToolStripComboBox by what the user typed into it, ahead of
  the item with that name; hosted controls are skipped. FindItem("") returned a top-level item with no text; a blank
  segment names nothing.
- **FormState.TryParse** took text Serialize never writes (" 1", "+2", "01") and an X near int.MaxValue whose Right
  wrapped to -2147483549. It takes exactly Serialize's form, and edges that stay in int range.
- **MenuShortcut.Parse** said it rejects only what a menu item rejects, but it also rejects keys no keystroke produces
  (Ctrl+LButton, Ctrl+LShiftKey: 60 shortcuts a menu item takes), so Format's text for them did not parse back. The
  documentation now says so.
- **The test runner's --list** exited 0 when the filter matched nothing (it returned before the no-match check); it
  exits 3, as a run does.

Still suspected: BUGLIST R132-1.

### Round 131: Windows satellite, eighth pass (v2026.11.17.21)

Two fresh reviewers (correctness, robustness): 12 bugs, 1 severe (the shortcut overwrite), so section 15 stays
re-queued. Eight were confirmed by a reviewer's probe; four more were reviewer suspects, reproduced and fixed with
them. The regression tests are in `WindowsReview131Tests` (10, each failing on the old code, run by name under a 60 s
timeout).

- **ShortcutUtils.Create (severe)** did not check the shortcut file's own path: IPersistFile cuts it at MAX_PATH, so a
  395-character path returned normally after overwriting the file its first 259 characters named (Read loaded that
  file too). A full path over 259 characters is refused, naming `shortcutPath`. A blank path threw COM's bare
  ArgumentException from Create and named "path" from Read; it is refused naming its parameter.
- **ShortcutUtils.Create** dropped an icon index given without an icon path (it read back as 0); the index is taken
  from the target, the shell's default icon file.
- **StorageDeviceInfo.GetUncPath** returned `\\server` for `\\?\UNC\server` and `\\` for `\\?\UNC\`: round 110's
  server-and-share check covered only the plain spelling.
- **DriveAndVolumeInfo.GetSpace** threw Win32Exception for `\\?\Volume{guid}` without its trailing backslash, which
  Describe accepts; it is added, as for `\\?\C:`.
- **RegistryUtils.SetValue** could not write back what GetValue reads: DWORD 0xFFFFFFFF reads into long as 4294967295,
  which threw (no ParamName) as a DWORD, and so did a ulong or a uint-backed enum with the high bit. Any integer, enum
  or whole number given DWord or QWord is now stored by its bits when it fits signed or unsigned; out of range is
  refused, and a fraction (2.5 was stored as 2) is refused rather than rounded. Any other kind mismatch now names
  `value`.
- **RegistryUtils.SetValue** stored a `List<string>` as its type name; a collection other than string[] or byte[] is
  refused.
- **RegistryUtils.GetValue<T>** read Version and IPAddress back as the default (it looked only for
  Parse(string, IFormatProvider)), and a Uri too (no Parse at all; it was also written unescaped). All three round-trip.
- **ScreenCapture.CaptureRegion** let a right or bottom edge wrap past int.MaxValue: (int.MaxValue, 0, 10, 10)
  returned a black image. It is refused.

Still suspected: BUGLIST R131-1.

### Round 130: Numerics core, eighth pass (v2026.11.17.20)

Two fresh reviewers (correctness, robustness) found 4 bugs; the net48 gate found 2 more: 6, none severe, so section 4
stays re-queued. The regression tests are in `NumericsCoreReview130Tests` (7: six failing on the old code, run by name
under a 60 s timeout, the last two on .NET Framework 4.8 only; and one guard that the exact distinct-x count still
refuses what it should).

- **Polynomial.Fit** tested rank on the QR diagonals with a tolerance of `n · 1e-13`, which grows with n while the
  diagonals' ratio does not: sin(3x) on 1e5 evenly spaced points at degree 30 (and 1e6 at degree 25, and 31 points at
  degree 30) was refused as "too few distinct x" though the points determine it to 3e-13. The distinct scaled x are
  now counted exactly, and the diagonal test (at 1e-15, n-independent) only refuses a fit doubles cannot resolve.
- **Uncertainty.Round** and **Format** in plain notation rounded the binary double for 0 to 15 decimals and the
  shortest decimal otherwise, against the documented rule: 1.005 ± 0.01 gave "1.00 ± 0.01" but 1.005e-9 ± 1e-11 gave
  "(1.01 ± 0.01)e-9", and 0.00145 rounded to 0.0014 but 0.0145 to 0.015. Every place now rounds the shortest decimal.
- **Integration.Simpson** re-summed one overflowing panel but not a running sum past double range:
  [6e307 × 5, -6e307 × 2] over 1 gave infinity for 1.6e308. It now re-sums on samples scaled by 2^-550, as Trapezoid
  does.
- **Interpolation.Lerp** (and Vector2's copy) gave NaN between equal infinite ends (Lerp(inf, inf, 0.5)); equal ends
  are returned as they are.
- **.NET Framework's double.Parse is not correctly rounded**: it misread about 1.5% of random 7-digit decimals with
  exponents from -320 to 300 (5.39205e252 and 6960980e-151 read one ulp low), and "R" leans on it. Uncertainty's fix
  above first failed there. **Rounding.RoundToSignificantDigits** returned a neighbour of the nearest double, and
  **EngineeringNotation** printed non-shortest digits and read its own full-precision output back an ulp off
  ("-4.230547953038732 m"). Both now find the shortest digits and read decimals back with exact integer arithmetic
  (private copies, as the drop-in law asks). Other sections that rely on Parse: BUGLIST R130-3.

Not bugs: `NextGamma` with a subnormal shape (Math.Pow(1, inf) is 1 on both frameworks); Angle with infinities and
`Primes.Sieve(int.MaxValue)` were fixed earlier. Still suspected: BUGLIST R130-1 and R130-2.

### Round 129: Binary formats & images, eighth pass (v2026.11.17.19)

Two fresh reviewers (correctness, robustness): 3 bugs, none severe, so section 6 stays re-queued. The regression tests
are in `BinaryImagesReview129Tests` (3, each failing on the old code, run by name under a 60 s timeout). The
correctness reviewer found nothing against independent encoders (PIL, a Python PNG encoder, BigInteger and exact
rational references) over hundreds of thousands of cases.

- **ImageBuffer** and **Bmp.Decode** checked `width * height * 4` in long, which overflows near int.MaxValue on both
  sides: `new ImageBuffer(int.MaxValue, int.MaxValue)` got a 4-byte buffer (GetPixel then threw IndexOutOfRange and
  PngWriter ran on), and a crafted BMP threw IndexOutOfRange or Overflow instead of InvalidDataException. The pixel
  count is compared now.
- **PngReader.Decode(Stream)** checked `width * height` of a 0xFFFFFFFF x 0xFFFFFFFF header in long, which overflowed
  and passed: the IDAT was read before a misleading "-1x-1" failure. It is refused at the header.
- **SvgUtils.EscapeText** and **EscapeAttribute** documented only `&`, `<`, `>` (and the quotes); they also escape CR
  (and tab and LF in attributes) and replace characters XML forbids with U+FFFD. The docs say so.

Not bugs: the ImageBuffer size error names `width` whichever side is large (a test pins it); a grey tRNS key with bits
above the bit depth never matches (the spec requires them zero). Still suspected: BUGLIST R129-1.

### Round 128: Concurrency, eighth pass (v2026.11.17.18)

Two fresh reviewers (correctness, robustness): 6 bugs, 1 severe (the pipeline hang), so section 10 stays re-queued. The
regression tests are in `ConcurrencyReview128Tests` (7, each failing on the old code, run by name under a 60 s timeout).

- **TokenBucketRateLimiter** stored the refilled count at every poll, so a refill too small to change a large count
  was lost with its time: 1e16 tokens at 1000 a second, polled each millisecond, never refilled, and 1e9 at 1e-3 a
  second gained 0.954 tokens in 1000 s. The base count moves only on a consume, when the bucket fills, or when the
  clock steps back (the refill up to the latest time seen is kept).
- **WhichExe** ignored a trailing dot, which the launcher reads as an empty extension: `cmd.exe.` was not found though
  it starts, and `cmd.` found cmd.EXE though it does not start.
- **RetryPolicy.DelayForAttempt** compared the cap with a double product: Linear, base 2^62 - 100 ticks, attempt 2,
  cap MaxValue - 1 gave the cap instead of 2^63 - 200 ticks. The exact checked product decides.
- **ConcurrentPipeline (severe)** did not see a continuation the consumer callback ran inline (a default
  TaskCompletionSource it completed) as the consumer: Dispose from it waited on its own thread for good, and a full
  Block TryEnqueue from it waited instead of throwing. The consumer loop's thread is now recognised.
- **AsyncEvent.InvokeParallelAsync** ran the synchronous part of each handler in turn on the caller's thread: three
  300 ms handlers took 900 ms and blocked the caller, and handlers waiting on each other hung. Each now starts on the
  pool (a single handler still runs directly).
- **TaskRace** started attempts in turn on the caller's thread: a 1 s synchronous first attempt won FirstToComplete
  over an instant second, and Hedged never hedged it. Attempts start on the pool. `ConcurrencyReview114Tests`'
  completion-order model now sets its sources only once every attempt has started.

Still suspected: BUGLIST R128-1 to R128-3.

### Round 127: Collections, eighth pass (v2026.11.17.17)

Two fresh reviewers (correctness, robustness): 5 bugs, none severe, so section 9 stays re-queued. The regression tests
are in `CollectionsReview127Tests` (5, each failing on the old code, run by name under a 60 s timeout; the Deque and
CircularBuffer ones fail only in the overflow-checked build, where the old index sum threw).

- **NearestBy** compared rounded distances, so keys far larger than the gap made a false tie and the farther element
  won: `[-1e17, 1e17]` near 1 gave the first. Distances are compared exactly (rounding errors by TwoSum).
- **CartesianProduct** read each input's Count afresh: an input that shrank mid-enumeration threw
  ArgumentOutOfRangeException for an "index" never passed, or ended the product early, and one that grew gave tuples of
  neither product. A changed input is InvalidOperationException, as Combinations and SlidingWindow already were.
- **Deque** and **CircularBuffer** computed a slot as `head + i - length`, which adds first: past a capacity of 2^30
  that overflowed, an OverflowException in a checked build. `head - (length - i)` cannot overflow.
- **BitSet** doubled its word array past the 2^25 words int.MaxValue needs: from 3 words it grew to 3 * 2^24, 128 MB
  that no index reaches. Growth stops at 2^25.

Not a bug: one reviewer's second NearestBy case (`[-3, 1e16]` near 5e15 - 2) has exact distances 5e15 + 1 and 5e15 + 2,
so the first element is right. Still suspected: BUGLIST R127-1 and R127-2.

### Round 126: Documents & config, eighth pass (v2026.11.17.16)

Two fresh reviewers (correctness, robustness): 8 bugs, and a ninth found while testing the duration fix; none severe,
so section 7 stays re-queued. The regression tests are in `DocumentsConfigReview126Tests` (8, each failing on the old
code, run by name under a 60 s timeout).

- **ConfigBinder and a property hidden with `new`** bound the base property too, so a derived `string X` over a base
  `int X` failed as "cannot convert 'hello' to Int32", and "5" set both. Only the most derived property is bound.
- **Durations (both copies of the grammar)** summed each term through a double, keeping 15 digits: "99999999.99999999s"
  rounded up to 10^15 ticks, though ticks are documented as truncated. The sum is exact (digits more than 400 places
  below a unit are cut).
- **Durations near the top of the range**: the double pre-check rounded "922337203685.4775807s" (TimeSpan.MaxValue) to
  2^63 and refused it, along with the few thousand ticks below it. The pre-check is loose and the exact sum decides.
- **EnvironmentVariables.RequireAll** reported a name holding '=' or NUL as Get's "name", not the caller's `names`.
- **Undefined enum values** were accepted: `(PdfPageSize)42` built a Letter page, `(PdfFont)42` Courier, `(CalloutKind)42`
  a note and `(MarkdownAlign)42` no alignment. Each is ArgumentOutOfRangeException now.
- **DocxWriter** stamped every ZIP entry with the local time of the build, so the same document gave different bytes
  each second and in each time zone. Entries carry 1980-01-01, the earliest time a ZIP entry can hold.
- **DocxWriter tables past 9,360 columns** got grid columns of width 0. The width is at least one twip.
- **HtmlReport.Table** with no headers dropped every cell and wrote empty rows; it throws ArgumentException (`headers`),
  as the other writers do.
- **MarkdownReport.Raw** took its list marker from a nested item too: after `Raw("- a\n  * b")` the next bullet list
  used '-' and joined the outer list. Only an outer item sets the marker.

Still suspected: BUGLIST R126-1 to R126-4.

### Round 125: Objects, functional, grids, graphs, intervals, seventh pass (v2026.11.17.15)

Two fresh reviewers (correctness, robustness): 9 bugs, none severe, so section 14 stays re-queued. The regression tests
are in `ObjectsGraphsReview125Tests` (9, each failing on the old code, run by name under a 60 s timeout). This ends
the seventh pass; the eighth goes 7, 9, 10, 6, 4, 15, 17, 1, 5, 12, 14, 2, 8, 16, 3, 11, 13.

- **DeepEquals and PropertyDiff on a class with no readable members** called any two instances equal, so two `Money`
  objects holding different private amounts matched. A class that overrides `Equals` now uses it, as a struct already
  did; one with neither members nor an `Equals` of its own still compares by its (empty) structure.
- **StringBuilder and Regex** keep their state in no property: builders of one length were equal whatever their text,
  and two patterns were equal. Both are leaves now: builders compare by their text (ordinal), regexes by type, pattern,
  options and timeout. `FlattenObject` writes a builder as one value instead of its Capacity, MaxCapacity and Length.
- **A boxed `Memory<object>` stored in its own backing array** was never seen as a cycle, since each visit copied the
  memory into a fresh array: DeepEquals and PropertyDiff threw at the 2,097,152-object limit and Flatten at its key
  budget. Each walk now keeps one copy per box.
- **ObjectMapper** with source properties `Name` and `NAME` filled a target's `Name` from whichever reflection listed
  last. An exact-case source name wins; otherwise the ordinally first of the case-insensitive matches.
- **PropertyPath.Set** of null into an `int` property or an `int[]` element stored 0. It throws ArgumentException
  (`value`), as a null into a non-nullable value type should.
- **PropertyPath** read "Kids[0]X" as "Kids[0].X". A name straight after ']' is a malformed path (ArgumentException,
  `path`).
- **TypeUtils.TryConvert** from decimal to float or double rounded silently (16777217m became 16777216f, though the
  int 16777217 is refused). A fraction must be spelled exactly by the result's shortest round-trip text, as a double
  going to decimal must; a whole number must be the result exactly, as a long must.
- **ExpressionAccessor's typed Getter and Setter** converted with an unchecked cast: a long of 4294967297 set into an
  int stored 1, and a NaN double read as an int gave int.MinValue. The cast is checked (OverflowException); a fraction
  still truncates, as a C# cast does.
- **Interval, RangeMap and RangeSet over strings** compared endpoints by the current culture, so `["ab", "a\u00ADb")` was
  empty (the culture ignores a soft hyphen) and `["a", "B")` was valid. String keys compare ordinally.

Not a bug: the A* heap's near-tie ordering gives path costs up to 1.3e-15 above Dijkstra's on 175 of 400 random grids;
the paths are optimal. Still suspected: BUGLIST R125-1 to R125-4.

### Round 124: Security, net, time, seventh pass (v2026.11.17.14)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 13 stays re-queued. The regression tests
are in `SecurityNetTimeReview7Tests` (5; four failing on the old code, run by name under a 60 s timeout, and one
checking that a rejected partial file with no destination is still deleted).

- **HttpDownload under Resume**: a destination that could not be resumed (longer than the file, so 416; the wrong start
  of it, so a checksum mismatch; a 206 with no Content-Range; an overrun) had its partial copy deleted, so every later
  call copied the same stale destination again and failed the same way, though the message said the next download
  starts over. With a destination there, the partial file is emptied instead, so the next call does start over.
- **CronScheduler after a short step back** counted every job from its last run, so "* * * * *" stopped for the whole
  of a two-hour correction. As in cron, only fixed-time jobs do; a wildcard job (minute or hour '*') runs by the clock
  as it reads. The round 91 test that an hourly job is not repeated now uses a daily one, since "0 * * * *" is a
  wildcard job.
- **AuthenticatedEncryption.Decrypt** threw ArgumentException for a blob shorter than a nonce and tag, and
  CryptographicException from 28 bytes up, where the class promises CryptographicException for altered input.
- **SecretsFile.FromBytes** took a key holding a CR from authenticated contents, though keys have no line breaks; it
  is malformed contents (CryptographicException), as an empty or duplicate key already was.

Still suspected: BUGLIST R124-1 to R124-4.

### Round 123: Text & small types, seventh pass (v2026.11.17.13)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 11 stays re-queued (both reviewers found
the first). The regression tests are in `TextReview123Tests` (4, all failing on the old code, run by name under a 60 s
timeout), with randomized idempotence and strip-agreement checks.

- **CaseConverter, letters with no one-letter capital**: 'ﬁ', 'ﬀ', 'ŉ', 'ǰ', 'ΐ' and the rest of Unicode's
  SpecialCasing lower-case letters stayed as they were in an upper-case style, so CONSTANT_CASE "ﬁle_name" gave "ﬁLE_NAME"
  and then "ﬁ_LE_NAME". They now take their full upper and title forms ("FILE_NAME", "File"), as 'ß' already did, and
  Title Case capitalizes past a leading 'ʼ' ('ŉ' titles to "ʼN").
- **CaseConverter, PascalCase after such a letter**: "bß_x" gave "BßX" and then "Bßx" (also "aﬀ_b"). After a word's one
  capital, such a letter is lower-case, so "BßX" reads back as bß | x.
- **NanoId**: the constructor accepted a `defaultSize` past the longest string, which `New()` then rejected naming "size".
  The constructor rejects it, naming defaultSize.
- **TextAlign.DisplayWidth** looked for U+FE0F past escape sequences holding a control character, which still acts and
  which `AnsiText.Strip` keeps: "❤" ESC[3 LF m U+FE0F measured 2 and its stripped text 1. Such an escape now ends the
  search.

Still suspected: R98-1 to R98-3, and BUGLIST R123-1 to R123-3.

### Round 122: Numerics core, seventh pass (v2026.11.17.12)

Two fresh reviewers (correctness, robustness): 12 bugs, none severe, so section 4 stays re-queued. The regression tests
are in `NumericsCoreReview122Tests` (9, all failing on the old code, run by name under a 60 s timeout), graded against
exact BigInteger sums and derivatives over randomized full-range data.

- **Lerp ends**: `Interpolation.Lerp`, `Remap` and `Vector2.Lerp` computed a + (b − a)·t, so t = 1 lost a small b next
  to a large a (`Lerp(1e16, 1, 1)` was 0). The ends are exact and an interior t stays between a and b.
- **EmpiricalDistribution.Quantile** ranked p·(n − 1), so p = 0.29 over 101 samples fell between ranks (28.999999999999996)
  where `Percentile` gives 29. A p that is the nearest double to k / (n − 1) is rank k.
- **Angle with ±∞** (the logged suspect R95-2) normalized to NaN everywhere; a `LerpDegrees` step that overflowed did too.
  They throw, naming degrees, from, to or t. NaN still passes through.
- **MovingAverage** kept one compensation term, which cannot hold three magnitudes: after 1e300 and 1e200 left a window
  of three, {1, 1, 1} averaged 0, and a window of 1000 stayed wrong until the next lap's rebuild. The running sum is
  exact (Shewchuk's partials, as `math.fsum`).
- **Means.Harmonic** was never held in [min, max] as the class promises: near-equal data landed an ulp above the largest
  value, and values near double.MaxValue gave ∞.
- **Undefined enums**: an `Extrapolation` cast from 42 behaved as Linear, an undefined `UncertaintyRule` took the PDG
  digit counts without PDG's 950–999 step, and an undefined `UncertaintyStyle` wrote PlusMinus. They throw (policy, rule,
  style).
- **MinMaxScaler.Scale** with a NaN data bound returned NaN for every value, where a NaN target bound throws.
- **LinearAlgebra.Multiply** of 100000x0 by 0x100000 threw OutOfMemoryException; a product past Array.MaxLength names b.
- **Integration.Trapezoid** (both overloads) summed finite panels past double range as ±∞, so opposite ones gave NaN
  (1819 of 20000 wide-range cases). They are summed again scaled by 2⁻¹¹⁰⁰: a signed ∞, or the value when they cancel.
- **Differentiation.Sample** gave NaN where a slope passed double range next to a tiny spacing (∞ − ∞ in every form; 511
  of 20000 cases). Such a derivative is formed as mantissa and exponent: a signed ∞ past double range.
- **Integration's summary** promised Simpson's rule over uneven spacing, which does not exist.

Still suspected: R95-1 and R95-3, and BUGLIST R122-1 to R122-5.

### Round 121: Charts, seventh pass (v2026.11.17.11)

Two fresh reviewers (correctness, robustness): 9 bugs, none severe, so section 5 stays re-queued. The regression tests
are in `ChartsReview121Tests` (10, all failing on the old code, run by name under a 60 s timeout).

- **Lines to far points**: a finite sample whose pixel position overflows (1e300 on a 1e-10 range, 1e307 on [0, 1])
  was treated as a gap, so the segment to it was dropped (SvgChart) or skipped (LinePlot). Such segments are clipped to
  the ranges in axis space first (`PlotFrame.ClipToRanges`), from the nearer end; the SVG pixel clip also works from the
  nearer end.
- **Raster band blending** ignored the destination's alpha: a band on a transparent canvas came out darkened (31,119,180
  at 48 became 6,22,34) and two half-transparent layers kept alpha 128. It is source-over compositing; opaque
  destinations are unchanged.
- **Callouts and arrows with huge offsets** (also the logged suspect R101-1): the leader of a callout 1e200 px to the
  side was clipped from its far end, where x0 + t·dx cancels, so nothing was drawn; and the arrow length overflowed. The
  line is clipped from the nearer end and the length is a scaled hypot.
- **SvgChart bar width** counted samples whose bars are never drawn (NaN or infinite y), so one NaN sample 0.01 from a
  bar shrank every bar.
- **default(PlotFrame)** was accepted by HexBin.Compute (R101-2), Annotations.ToSvg and Annotations.Draw, which drew or
  returned nothing; they name frame.
- **One-pixel colorbars** showed the top of the map vertically and the bottom horizontally (R101-6); both show the middle.
- **Tick rounding past 2^52 / 10^d**: Math.Round scales by 10^d, so time ticks at 1e15 + 0.5 came out 1e15 + 0.375 and a
  1e12 + 0.0005 tick was labelled without its fraction. Past that point only the exact fraction is rounded.
- **Waterfall value ticks** always halved the span, so a subnormal range (0..5e-324) gave 0/0 and the render threw.
  Halved only when the span overflows.
- **Undefined enums** (a cast PlotStyle, AxisKind, LineDash or AnnotationLayer) drew nothing, fell back silently or
  drew the wrong dash; they are rejected, naming style, options, panels, dash or layer.

Closed without a fix: R101-3 (BoxPlot ±∞ with WhiskerReach 0 behaves; probed this round), R101-4 (non-positive x on a
log axis already map to NaN and were never counted; the NaN-y case was the real one, fixed above), R101-5 (the
correctness reviewer judged `Linear(99, 101, 2, loose)` = [0, 200] right: two ticks force it). Still suspected
(BUGLIST R121-1 to R121-4).

### Round 120: WinForms + tooling + docs, seventh pass (v2026.11.17.10)

Two fresh reviewers (correctness, robustness): 10 bugs, none severe, so section 17 stays re-queued. The regression
tests are in `WinFormsReview120Tests` (10, all failing on the old code, run by name under a 60 s timeout; the
unformattable-cell test hung there on the grid's modal dialog).

- **One-column CSV**: an empty cell was a blank line, which CSV readers skip, so the row was lost (an empty header made
  the first row the header). A lone empty field is written `""`.
- **Formula escaping**: TryParse let "-Infinity", "-NaN", "-∞", an overflowing "-1e400" and a leading tab or CR through
  as numbers, so they reached the spreadsheet unescaped (also the logged suspect R105-5). Only finite signed numbers are
  spared.
- **Unformattable cells**: a combo value missing from its Items raised DataError while being exported, and the grid
  opened its modal error dialog once per cell; the export blocked, then wrote another item's text. Read with a handler;
  such a cell exports its value.
- **Non-string headers**: a header cell whose value is 42 exported as an empty header; it is formatted like the grid does.
- **SaveCsv with a long file name**: the temp name added 13 characters, so names of 243–255 characters failed with an
  IOException; a shorter temp name is used beside the target.
- **InvokeIfRequired after the UI thread exited**: Invoke returned without running the delegate, so the void overload
  did nothing and the generic one threw NullReferenceException. Both throw InvalidOperationException.
- **PlotPictureBox.Image**: a disposed image was stored before the throw (no ParamName), so every later paint failed.
  It is rejected before it is stored, naming value.
- **ZoomAbout limits**: the static overload took NaN or negative limits silently; it names minZoom or maxZoom.
- **verify.sh build line**: `tail -3` always cut the "N Warning(s)" line off; the counts come from the whole log.
- **Script locks**: checked-build.sh and net48.sh reported "another run is using it" (exit 2) when the lock's parent
  folder did not exist; they create it first.

Still suspected (BUGLIST R120-1 to R120-8). The corrected build line showed 35 warnings in the test projects, which the old line had hidden (BUGLIST R120-9).

### Round 119: Filters & control, seventh pass (v2026.11.17.9)

Two fresh reviewers (correctness, robustness): 9 bugs, none severe, so section 1 stays re-queued. The regression tests
are in `FiltersControlReview119Tests` (8, all failing on the old code, run by name under a 60 s timeout); the two
documentation fixes have no test.

- **PID integral when the error overflows**: setpoint 1e308 and measurement −1e308 made error·dt ∞ where it fits
  (2e298 at dt 1e-10), so the integral never moved; with limits, windup acted on the ∞ output and parked the integral at
  the limit (±1 gave 0.9999999999999999, not 0.02). The step is formed from the halved difference, an overflowed
  tentative output is summed in units of 2⁻⁶⁰⁰, and so is the integral at the limit. A round-3 test that expected 0 for
  this case (the true output is 3.4e8) was corrected.
- **Slew limiter when the gap and the step both overflow**: ∞ > ∞ is false, so Reset(−1.7e308) then Update(1.7e308, 2)
  at rate 1e308 jumped to the target (it is 3e307, limiting). Compared in halves, the step taken in two halves.
- **IIR steady state** (Reset(value), FiltFilt): the running product of the sections' DC gains overflowed (gains 2e200,
  2e200, 2e-300 gave NaN state for a cascade whose gain is 8e100) or underflowed (a start-up transient). The value is
  folded in and the product kept as m·2ᵉ.
- **IIR Response**: the complex product over the sections overflowed to (∞, NaN); kept as h·2ᵉ the same way.
- **Shelf at the slope limit**: the poles sit on the unit circle, which fell through to the gain check and blamed gainDb;
  it now names slope, and the message says the poles would reach the unit circle (it said "not monotonic", which slopes
  it accepts are not either).
- **Bessel overshoot docs**: "well under 1%" was 0.8–1.4% at orders 4–5 and 5% at order 8, 0.2·fs (matching SciPy); the
  docs give the measured figures.
- **Argument blame**: StepResponse.Analyze blamed riseLow for a bad riseHigh, and Analyze and FopdtModel.FitStep blamed
  values for a non-finite time.

Still suspected (BUGLIST R119-1).

### Round 118: Statistics, seventh pass (v2026.11.17.8)

Two fresh reviewers (correctness, robustness): 5 bugs, none severe, so section 3 stays re-queued. The regression tests
are in `NumericsReview118Tests` (10, all failing on the old code, run by name under a 60 s timeout). References are
700-digit mpmath: at 60 digits mpmath itself was wrong for shapes near 1e308.

- **F distribution when d1·x overflows**: FMasses formed d1·x + d2 = ∞, so FCdf(1e308; 2, 0.001) was 1 (it is 0.301)
  and others NaN, and FPdf was 0. The tails come from the ratio d2/(d1·x) without forming the product, and past
  1e-280 from the gamma limit with a cancellation-free ln Γ(a + b)/(Γ(a)·aᵇ).
- **The private F tails** in Anova and GaugeRR had the same overflow; each copy gains the same branch, and a test checks
  the three agree.
- **Tiny shapes**: the leading term 1 − yᵃ/(a·B(a, b)) took ln a + ln B(a, b), which keeps only eps·|ln a| absolutely,
  so FSurvival(1e-300; 1e-100, 1e-20) was −2.8e-14 (it is 1e-80); a tail at l = 0 was −0; and Anova's tail was 1
  when df1·f was subnormal. ln(a·B) is formed as lnΓ(1 + a) − (lnΓ(b + a) − lnΓ(b)), each part accurate to eps·a (the
  Stirling corrections' difference formed apart, since added in order it vanished into the larger one).
- **Quantiles past MaxValue**: the solver doubled its bracket to ∞ and bisected there, so FQuantile(0.5; 2, 0.001) was
  9e307 (it is ∞) and ChiSquareQuantile(0.99; 8e307) was ∞. The bracket stops at MaxValue, the midpoint no longer
  overflows, and a chi-square quantile within half an ulp of MaxValue (by the Laurent–Massart bound) rounds to it.
- **Subnormal data**: the t-tests, the F-test and the confidence intervals rounded on the subnormal grid before the
  statistic or the quantile multiplied (OneSampleT 2.99944 for 2.99935; ForMeanT of {0, 3ε} was (−11ε, 15ε), it is
  (−17.6ε, 20.6ε)). Data below 2⁻⁹⁰⁰ are scaled up by 2⁶⁰⁰ first, and the intervals scaled back at the end.

Still suspected (BUGLIST R118-1..4).

### Round 117: WPF satellite, seventh pass (v2026.11.17.7)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 16 stays re-queued. The regression tests
are in `WpfReview117Tests` (11: 9 fail on the old code, run by name under a 60 s timeout; two are guards, a
CollectionChanged handler still joining the load and Messenger delivery order against a naive model).

- **IncrementalCollection**: a handler that loaded more from IsLoading going false, LastError or HasMoreItems joined the
  load that had just finished: it got that page's count (or its failure, for a retry) and the loader never ran again.
  The properties settle, and the load counts as finished, before any change is raised.
- **Messenger.Unregister** for one type of a recipient with others scanned the type's whole index under the
  messenger's lock: unregistering 40000 recipients took 8.7 s, stalling every Send. Each registration holds its node in
  the type's linked index, so removal is constant time and the order is kept.
- **NumericRangeValidationRule** accepted a NaN bound, which then compared false and stopped applying (Minimum NaN,
  Maximum 10 passed "-1e300"). Setting one throws ArgumentOutOfRangeException; an empty range is documented.
- **NumericInputBehavior**: a NaN bound made the keystroke filter refuse "-5" while StepValue and Coerce produced it,
  and a NaN Maximum was no bound. NumericInputRules and the attached properties reject NaN, as RangeBase does.
- **HotkeyGesture.TryParse** let KeyConverter's case-insensitive fallback fold look-alikes: "Ctrl+" and a long s
  registered a system-wide Ctrl+S. Non-ASCII text is refused.
- **FileDropBehavior**: "*." (the Windows filter for no extension) matched only names ending in a dot, so nothing. It
  matches files with no extension.

Still suspected (BUGLIST R117-1..6).

### Round 116: Logging, diagnostics, CLI, seventh pass (v2026.11.17.6)

Two fresh reviewers (correctness, robustness): 9 bugs, none severe, so section 12 stays re-queued. The regression tests
are in `LoggingReview116Tests` (13: 9 fail on the old code, run by name under a 60 s timeout; four are guards, narrow
spinner widths, other control sequences still flattened, HTTP statuses, a child logger's Dispose).

- **ConsoleSpinner** counted small and vertical forms (FE10-FE19, FE50-FE6B), Hangul Jamo Extended-A, Tangut, the kana
  supplements and a symbol with an emoji selector (U+2764 U+FE0F) one column each, so a line of them was measured
  short and wrapped. They count two.
- **ConsoleSpinner** turned ESC into a space, so a message from AnsiStyle showed "[32m" as text, and the padding counted
  the sequence's columns. SGR sequences are kept, count nothing, and a cut styled line ends with a reset; other control
  sequences are still flattened.
- **ConsoleSpinner.Tick** drew without the guard the constructor and Message setter use: a closed pipe threw out of it.
  The background loop, which stopped on that exception, now stops on a flag the failed write sets.
- **ConsoleTable.AlignColumn** accepted an undefined alignment. It throws ArgumentOutOfRangeException.
- **ExceptionUtils.IsTransient** read an FTP reply as an HTTP status: 550 (file unavailable) was transient, 450 (busy)
  and 425 were not. The status is read by its enum type; FTP 5xx replies are permanent.
- **ExceptionUtils.IsTransient** skipped programmer errors (ArgumentException, NullReferenceException,
  InvalidOperationException), so a timeout they wrapped or sat beside made the whole tree transient. One anywhere in
  the tree makes it not; WebException, itself an InvalidOperationException, is still judged as a network failure.
- **Logger.AddSink** after Dispose, on the logger or a child from ForCategory, kept the sink and never disposed it (a
  FileSink's handle leaked). The disposed flag is shared with children, and AddSink throws ObjectDisposedException.
- **TextWriterSink.Dispose** with ownsWriter threw when the owned writer's own Dispose flushed into a broken stream.
- **FileSink**: a rotation blocked by a viewer followed by a refused reopen threw out of Emit after the event was
  written, so a caller that retried wrote it twice. The reopen is retried on the next event.

Rejected: a cancellation after every body succeeded (not a shape IsTransient sees). Still suspected (BUGLIST
R116-1..10).

### Round 115: Signal analysis, seventh pass (v2026.11.17.5)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe, so section 2 stays re-queued. The regression tests
are in `SignalReview115Tests` (8: 6 fail on the old code, run by name under a 60 s timeout; two are guards, a Hampel
filter against a brute-force one and Savitzky-Golay edges against a least-squares fit).

- **TimeDelayEstimate.CrossCorrelationPeak**: past 64 lags tied at the peak the FFT's noisy pick stood ([1] against 200
  ones gave lag 7 and 1.0000000000000002, not lag 0 and 1). Every tied lag is summed directly while that costs no more
  than the transform; past that the one nearest zero is. Ties go to the lag nearest zero, as documented now.
- **Goertzel.Estimate** reduced a frequency mod the rate only when 2π·f/fs overflowed: 1e17 Hz at 1 Hz, a multiple of
  the rate, gave 1.14 for DC's 8. Any frequency at or above the rate is reduced first; aliasing is documented.
- **CycleMeasurements**: Frequency and MeanFrequency were 1/Period, which overflows first: a 10-sample period at
  1e-308 Hz gave 0. Both are the rate over the period in samples.
- **Fft.Inverse** at Bluestein lengths rounded its result to the subnormal grid and then multiplied by 1/N (−0 for
  −0.58ε at N = 6). The 1/N is applied with the chirp, once.
- **Hampel**: the MAD merged the window's deviations outward to the middle rank, O(w) a sample (292 s for 400000
  samples with a half-window of 100000). It binary-searches the two sorted deviation runs.
- **SavitzkyGolay.Smooth** built and summed a weight row per edge position, O(w²·m) (19.9 s at w = 40001). Each edge
  window is projected once and the fit evaluated per position; a non-finite window keeps the per-row sums.

Rejected: PhaseUnwrap.Wrap drifting at huge angles (it matches NumPy). Still suspected (BUGLIST R115-1..7).

### Round 114: Concurrency, seventh pass (v2026.11.17.4)

Two fresh reviewers (correctness, robustness) and the .NET Framework gate: 9 bugs, 1 severe, so section 10 stays re-queued. The regression tests
are in `ConcurrencyReview114Tests` (14: 10 fail on the old code, run by name under a 60 s timeout, the last on .NET Framework 4.8; four are guards).

- **ConcurrentPipeline.TryEnqueue** (severe): under Block, the consumer callback or `onError` enqueuing into a full
  pipeline waited for a slot only the caller could free, forever, and CompleteAsync and Dispose hung behind it. It
  throws InvalidOperationException ("full") instead, as documented now; with room it still enqueues.
- **ConcurrentPipeline.CompleteAsync**: the "called from the consumer" mark was an AsyncLocal holding the pipeline, so
  a timer or Task.Run the consumer started inherited it and CompleteAsync returned at once (0 of 5 items processed).
  The mark is now per callback and lapses when the callback returns.
- **ConcurrentPipeline.Dispose**: the same mark made Dispose from such a task return while the callback still ran and
  leave the cancellation source and semaphores undisposed. It waits now; from the callback itself it still returns at
  once, and the resources are disposed when the consumer stops.
- **ConcurrentPipeline.CompleteAsync** after a drain and Dispose threw the semaphore's ObjectDisposedException; it
  succeeds.
- **ConcurrentPipeline.TryEnqueue** after Dispose under Block threw ObjectDisposedException with no object name (the
  disposed source's Token); it names the pipeline, as the drop policies did. A slot granted as Dispose runs is refused
  rather than buffered for a stopped consumer.
- **ConcurrentPipeline.Dispose** on .NET Framework disposed the semaphores under producers still blocked in them:
  a producer threw ArgumentNullException (4 runs in 25 of an existing test) or never woke. The semaphores are no
  longer disposed (without a wait handle they hold nothing), so every blocked producer gets ObjectDisposedException.
- **Retry.ExecuteAsync**: an operation failing after the caller cancelled went through ShouldRetry and a backoff and
  came out as the delay's TaskCanceledException, or waited out a delay that ignored the token. It stops at once: the
  operation's own OperationCanceledException is rethrown, and any other failure is a cancellation for the caller's token.
- **TaskRace.FirstSuccessful / FirstMatching / FirstToComplete** were quadratic: a WhenAny over the pending attempts
  and a List.Remove per round (40000 failing attempts took 11 s). Finished attempts are queued as they complete.
- **TaskRace.Hedged** copied every running attempt into a new WhenAny each round (8000 hedges took 3.9 s); it waits on
  the same queue and the timer.

Still suspected (BUGLIST R114-1..8).

### Round 113: Binary formats & images, seventh pass (v2026.11.17.3)

Two fresh reviewers (correctness, robustness): 5 bugs, 1 severe, so section 6 stays re-queued. The regression tests
are in `BinaryImagesReview113Tests` (6: 5 fail on the old code, run by name under a 60 s timeout; one is a guard).

- **PngReader.Decode(Stream)** (severe): the ancillary limit added up only each chunk's declared length, so an endless
  run of valid empty chunks (`00000000 tEXt CRC`) was never refused; each was kept, at about 27 bytes of memory per
  byte read (40 MB of them took 1.1 GB), until memory ran out. Empty IDAT chunks did the same against the image-data
  limit. Each chunk now counts its 12 bytes of framing too.
- **PngReader.Decode(Stream)** kept each `Read` as its own array, so a stream returning one byte at a time took about
  50 times the file (a 4 MB PNG allocated 207 MB), breaking the documented memory bound. Reads are copied into 64 KiB
  blocks.
- **Bmp.Decode** refused any side over 32768 as "implausible", so files `Bmp.Encode` writes (40000 x 1, 1 x 40000)
  did not read back. The side cap is gone; the pixel-count and truncation checks stay, and the stride is worked out in
  long.
- **Bmp.Decode** read a BI_BITFIELDS file with a 40-byte header and a pixel offset of 54 by taking the channel masks
  (at 54-65) as the first pixels; it is refused ("masks overlap the pixel data").
- **SvgDocument.Polyline** drew a null stroke black, where Line and Text throw; it is ArgumentNullException (`stroke`).

Doc fix: Base32's decode skips any Unicode whitespace (a pasted no-break space too), which its doc now says.

Rejected: Base58.Decode is quadratic at its default length cap, as its doc says (the overload caps it). Still
suspected (BUGLIST R113-1..3).

### Round 112: IO, seventh pass (v2026.11.17.2)

Two fresh reviewers (correctness, robustness): 6 bugs, none severe (both found the CsvDialect one), so section 8 stays
re-queued. The regression tests are in `IOReview112Tests` (8: 6 fail on the old code, run by name under a 60 s timeout;
two are guards).

- **CsvDialect** took any decimal separator but the delimiter, including characters numbers already hold: with '1',
  1.5 formatted as "115"; with '-', -2.5 as "-2-5"; with 'E', 1.5e-7 as "1E5E-07", none of which read back as
  FormatNumber promises. Letters, digits, signs, the quote, whitespace, controls and lone surrogates are refused
  (ArgumentException, `decimalSeparator`).
- **DirectoryUtils.Copy** treated an undefined `OverwritePolicy` (`(OverwritePolicy)42`) as "replace" and overwrote
  every existing file; it is ArgumentOutOfRangeException (`options`) before anything is copied.
- **ZipUtils.AddOrReplaceEntry** could not update a valid zip holding an entry with an empty name: copying it threw
  ArgumentException naming `entryName` for the caller's own name. Such a zip takes the in-memory path, which keeps it.
- **CsvBinder** read " 5 " as 5 but a field of spaces for a nullable property as an error; it is null, as an empty one is.
- **TarUtils.ExtractToDirectory** refused pre-POSIX (V7) tars, whose directories are empty old-style files named "d/",
  as "a file named like a directory", so nothing extracted; GNU tar and Python read them as directories, and now so
  does this. A V7 entry with data under such a name is still refused.
- **TarUtils.CreateFromDirectory** on Windows wrote every file as mode 0755 (TarWriter's default there), so each came
  out executable on Unix; files are 0644, folders stay 0755.

Test fix: the Windows suite's job kill-on-close test failed once in a full verify (a killed ping still listed after a
fixed 300 ms); JobObject starts children suspended, so none escapes. The test now waits up to 10 s for the pings it
started (an escaped one lives 30 s), counted by process id, not every ping on the machine.

Rejected: a manifest name "././a" keeps one "./" but still names the same file; CsvBinder writes a Uri unescaped, but
every probe read back an equal Uri; FileWatcher's buffer clamp to 4-64 KB is what its doc says. Still suspected
(BUGLIST R112-1..5).

### Round 111: Collections, seventh pass (v2026.11.17.1)

Two fresh reviewers (correctness, robustness): 13 bugs, 1 severe, plus one doc fix, so section 9 stays re-queued. The
regression tests are in `CollectionsReview111Tests` (22: 18 fail on the old code, run by name under a 60 s timeout; the
.NET Framework array-limit test fails on the old code under net48; three are guards).

- **Severe: CountMinSketch** told items apart by their 32-bit hash code, and a `long` hashes as its halves XORed: 2000
  keys `i * (2^32 + 1)` each over-counted by 1999, and a packed `(x << 32) | y` grid broke the error bound for every key.
  With the default comparer, 64-bit keys (long, ulong, double, DateTime, DateTimeOffset, TimeSpan and their nullables)
  hash from all 64 bits through the 64-bit MurmurHash3 finalizer; the doc says the bound needs distinct hash codes.
- **Aggregation.Summarize** lost about 7 digits of the standard deviation when the mean is large against the spread
  (`[1e10 + 4, 1e10 + 1, ...]` gave 2.0000002622604196, not 2): Welford runs on the distance from the first value.
- **LruCache**: eviction and Remove took the node off the list before the map, so a key whose hash throws on a later
  call left an orphan entry (the cache past its capacity); the map goes first and a failed eviction undoes the insert.
- **IndexedPriorityQueue**: every sift hashed each moved key, so a throwing hash mid-sift lost one key and duplicated
  another; entries are nodes that know their own slot, so sifting hashes nothing. UpdatePriority replaced the stored key
  with the caller's equal one; the first key is kept.
- **BiMap.RemoveByKey / RemoveByValue** left a one-sided pair when the second side's hash threw; it is undone, as Add does.
- **MultiDictionary.Remove / RemoveAll** changed the total and the list before removing the key; the key goes first.
- **TtlCache**: a `TimeSpan.MaxValue` ttl ("never expires") expired when the clock read `DateTimeOffset.MaxValue`; Remove
  with a clock that throws removed the entry and then failed.
- **CartesianProduct.Of** re-read the caller's list after checking it, so a slot set to null later threw
  NullReferenceException; the list is copied as it is checked.
- **Combinations and SlidingWindow** over a list that shrank mid-enumeration threw ArgumentOutOfRangeException for an
  "index" the caller never passed; it is InvalidOperationException, as the BCL's enumerators say.
- **BloomFilter / CountingBloomFilter** missed the target rate for a few items (3 at 0.01 measured 0.0128): the
  formulas' size grows to the smallest that meets the target by the exact rate.
- **.NET Framework array limits**: CountMinSketch, DisjointSet, FenwickTree, CircularBuffer and Deque accepted sizes
  between 0x7FEFFFFF (Framework's limit for elements wider than a byte) and 0x7FFFFFC7, which threw
  OutOfMemoryException there; they are ArgumentOutOfRangeException, and Deque and TopN grow to the Framework limit.
- Doc: **NearestBy** with an infinite key is the limit (+∞ the first largest element, −∞ the first).
- Hardening: **BiMap.Values** is read from the same map as Keys, so the two are always in step.

Still suspected (BUGLIST R111-1..2): composite keys (tuples) holding NaN or decimals of different scale still hash by
bits or scale on .NET Framework; TopN's and Deque's growth to the Framework limit is untested (it needs 16 GB).

### Round 110: Windows satellite, seventh pass (v2026.11.17.0)

Two fresh reviewers (correctness, robustness): 10 bugs, none severe, so section 15 stays re-queued (both reviewers
found the event-log one). The regression tests are in `WindowsReview110Tests` (14: 13 fail on the old code, run by
name under a 60 s timeout; one guards that bad text still reads as the default).

- **ClipboardUtils**: text and file lists holding a lone surrogate (legal in an NTFS name) were written and read through
  Encoding.Unicode, which turns it into U+FFFD; they are copied as raw code units, as SingleInstanceApp already did.
- **RegistryUtils**: values SetValue wrote did not read back through GetValue<T>, as its doc promised. A DateTime was
  written "G" (milliseconds and kind lost); DateTimeOffset, TimeSpan, Guid, Half, Int128, UInt128, BigInteger, DateOnly
  and TimeOnly read back as the default. Dates are written "O", TimeSpan "c", and non-convertible types parse with their
  invariant Parse. A null inside a string array threw with no ParamName; it names `value`.
- **EventLogWriter.IsSourceRegistered**: a source under the Security log threw SecurityException without admin rights
  (exactly when the answer is true); a denied key counts as registered.
- **SingleInstanceApp**: a launch of more than 1000 arguments was dropped, neither primary nor forwarded; the cap is
  32,768, past what a command line holds.
- **StorageDeviceInfo**: `\\.\PhysicalDrive0` and other device paths were refused as "network paths"; `GetUncPath(@"\\")`
  and `"\\server"` were returned as UNC paths; `IsNetworkDrive(@"\\?\Z:\x")` was false for a mapped Z:.
- **DriveAndVolumeInfo.GetSpace**: `\\?\C:` threw Win32Exception where Describe takes it as the drive.
- **JobObject.AssignProcess(IntPtr.Zero)**: threw Win32Exception; it is an ArgumentException.

Still suspected (BUGLIST R110-1..4): DpiInfo.Monitors and MonitorInfo disagree when per-monitor v2 is refused; a label
SID with no sub-authorities in Elevation.CurrentIntegrityLevel; DeleteKey of an over-long component throws where the
reads return absent; IsNetworkDrive on a real mapped drive is untested (none available).

### Round 109: Documents & config, seventh pass (v2026.11.16.25)

Two fresh reviewers (correctness, robustness): 20 bugs, one severe (settings data loss), so section 7 stays re-queued.
The regression tests are in `DocumentsConfigReview109Tests` (20); each fails on the old code, run by name under a 60 s
timeout.

- **SettingsStore (severe)**: a value the type rejects when loaded back (setters that check each other, applied in file
  order) was saved, and the next launch moved the file aside as corrupt, losing every setting. The save-time read-back
  now fails the save with InvalidOperationException.
- **SettingsStore.Update**: a nested Update or Save inside the change wrote the outer change half-done; when the outer
  change then threw, Value was rolled back but the file kept what the caller saw fail. Save, Update and Reset are
  refused while a change runs.
- **SettingsStore.LoadFrom**: a corrupt file reached through a symbolic link had the link moved aside (and the next save
  replaced it with a plain file); the target is moved. Invalid UTF-8 was loaded with U+FFFD and saved back that way; it
  is set aside as corrupt. A path that is a directory loaded as "no file yet" and failed at save; it throws
  ArgumentException.
- **ConfigBinder, EnvironmentVariables**: a comma list of a non-[Flags] enum's names was ORed into another member
  ("Red, Green" bound as Blue, "Off, On" as On); refused. A null value under a section (`Port:0`, `Server:Port`) counted
  as configured, failing the bind or creating the section; it is unset. A null key escaped as the dictionary's
  ArgumentNullException("key"). The two readers accepted different duration syntax ("1.5h" and "5 s" in one, "1m 30s"
  in the other); they share one grammar (ConfigBinder still allows a negative).
- **MarkdownReport**: an item like "--" or "**" completed a thematic break with its marker, and the item became a rule;
  it is escaped. Raw text whose list ended in a continuation line or an empty item was joined by the next list.
- **ReportBuilder.Figure, ReportFigure**: an SVG that was only an XML declaration was accepted, and ToHtml failed with
  ArgumentNullException("inArray"); refused.
- **ReportTemplates**: a null metadata key named the builder's internal parameter; an integer-only numberFormat ("X")
  threw a bare FormatException mid-build. Both throw ArgumentException naming the caller's parameter.
- **PdfWriter**: a size like 1e-11 passed validation and was written as "0 Tf" (sizes below 1e-7 are refused, and up to
  12 decimals are written); Code split a surrogate pair into two '?' at one char a line; Measure took a NaN or negative
  size.
- **DocxWriter**: a CR in Title or Author came back as LF (it is written as `&#13;`); Footer("") with page numbers began
  with a dangling separator.

Still suspected (BUGLIST R109-1..8): GithubSlug casing on .NET Framework for supplementary letters; DOCX embedding a
truncated PNG; a zero grid width past 9360 columns; flagPercent = ∞; a code language with a space; concurrent saves
from two processes; Code wrapping by UTF-16 count; NUL and lone surrogates in HtmlReport.

### Round 108: Statistics, sixth pass (v2026.11.16.24)

Two fresh reviewers (correctness, robustness): 11 bugs, none severe, so section 3 stays re-queued. This completes the
sixth pass; the seventh starts with section 7. The regression tests are in `StatisticsReview108Tests` (12); each fails
on the old code, run by name under a 60 s timeout.

- **Distributions.ChiSquarePdf**: the prefix (x/2)^(k/2)·e^(−x/2)/Γ(k/2) was formed before the division by x, so for
  k > 2 it underflowed at small normal x (pdf(1e-300, 3) was 0, true 3.99e-151; pdf(1e-160, 4) 1.1e-5 off). A prefix
  near underflow is taken in log space.
- **FCdf, FSurvival, FPdf, FQuantile** (and the copies in Anova and GaugeRR): y = d1·x/(d1·x + d2) underflowed under a
  huge d2, and the tails and density came back as 0 (FCdf(4.94e-24, 0.5, 1e300), true 1.16e-6) or lost digits. That case
  is the χ²(d1)/d1 limit, taken from the leading term or the incomplete gamma.
- **ConfidenceInterval, MeasurementAgreement.BlandAltman, ProcessCapability (overall σ)**: the deviations were summed
  without the two-pass correction Σd² − (Σd)²/n that HypothesisTests has had since round 66, so data on 1e6 with a spread
  of 1e-5 gave sd 7.5e-11 relative off.
- **DistributionFit** (FitNormal, and the starts of FitExponential and FitGamma): divided every value by max|v| (not a
  power of two), had no pivot and no correction: sd 5.8e-6 relative off on the same data.
- **Anova.OneWay**: the rounding floor followed the largest offset of any group, so a group near 0 next to a group at
  1e7 lost its within-group spread (SSW 0, F = ∞). Each group's within sum is taken about its own first value and floored
  by its own spread.
- **GaugeRR.Analyze**: the same floor zeroed the repeatability and operator effect of a part near 1 next to parts at
  1000. Repeatability is taken per cell, and operator and interaction effects from offsets within each part.
- **PermutationTest**: the tie tolerance scaled with the largest statistic of all rearrangements, so one huge one (a
  variance ratio near 1e20) made everything a tie (p = 1 for a true 9/21). The tolerance is per comparison.
- **PermutationTest.TwoSample / Paired, ProcessCapability.Compute**: an undefined Alternative or SigmaEstimator was run as
  a default; it throws ArgumentOutOfRangeException.
- **RobustRegression.TheilSen**: a pairwise slope past double.MaxValue gave an infinite slope with a NaN intercept, or a
  NaN line; it throws, and a slope·x past range is formed from halves.

Still suspected (BUGLIST R108-1..6): ±∞ control and agreement limits for data near double.MaxValue; FitWeibull's "all
values are equal" for close values; ChiSquareQuantile at tiny df above p = 0.5 and at df ≥ 1e17; the F upper tail at
d1 near 1e-308; the asymptotic two-sample KS p-value at very small n.

### Round 107: Filters & control, sixth pass (v2026.11.16.23)

Two fresh reviewers (correctness, robustness): 10 bugs, none severe, so section 1 stays re-queued. The regression tests
are in `FiltersControlReview107Tests` (9); each fails on the old code, run by name under a 60 s timeout.

- **IirFilter.Filter / FirFilter.Filter, FilterAligned**: one output past double.MaxValue redid the whole buffer at
  2^-64, so outputs that never overflowed lost their low bits or became 0 (a causal filter's early outputs depended on
  later samples). Only the outputs that overflowed are redone at the next scale; IirFilter.Filter streams the signal
  through a fresh copy, which rescales only where it must.
- **IirFilter.FiltFilt**: the backward pass carried an overflow to every sample, so 1e-300 thousands of samples before a
  burst came out as 0 or 1e169. FiltFilt is linear, so the small samples are filtered unscaled, the large ones rescaled,
  and the two added.
- **IirFilter.Process**: the streaming shift only grew, so after one burst near double.MaxValue every later small signal
  was flushed to 0 until Reset. The shift steps back down once the state is small again.
- **Response, MagnitudeDb, GroupDelay** (Biquad, IirFilter, FirFilter): the frequency was divided by the sample rate
  before reducing, so 2^60 Hz at 1 Hz was not DC and 1e300 Hz at 1e-10 Hz was NaN. The frequency is reduced first
  (IEEERemainder), so responses alias exactly.
- **Biquad** (LowPass, Notch, BandPass, Peaking, shelves): a Q of 1e12 to 1e17 at a mid-band frequency put the poles on
  the circle and the error named `frequency`; it names `q` (or `slope`) when a moderate section at that frequency is fine.
- **IirFilter.ButterworthOrder** (band-pass, band-stop): band edges a few ulps apart gave a selectivity just under 1 and
  order 1 (about −1 dB where −40 was asked); it throws, as the Chebyshev and elliptic estimates do.
- **FirFilter.KaiserParameters**: `sampleRate / 2` rounded at a subnormal rate, so the same width ratio gave a different
  tap count (16 at 1.5e-323 vs 12 at 3); the ratio is taken first.
- **PidTuning.Simc**: a model with no dead time and no τc named `closedLoopTimeConstant`, which was never passed; it
  names the model.
- **KalmanFilter1D.Reset** left LastGain from before the reset; it is 0, as before the first update.

Still suspected (BUGLIST R107-1..3): FopdtModel.StepResponse at a NaN time; PID ScaledOutput precision at extreme
output ranges; the FitStep error message.

### Round 106: Windows satellite, sixth pass (v2026.11.16.22)

Two fresh reviewers (correctness, robustness): 10 bugs, 1 severe, so section 15 stays re-queued. The regression tests
are in `WindowsReview106Tests` (9); each fails on the old code, run by name under a 60 s timeout.

- **RegistryUtils.DeleteKey** (severe): Win32 ends a key path at a NUL, so `DeleteKey(@"App\victim" + "\0other")`
  deleted all of `App\victim`, and `"\0"` got past the hive-root guard and named the hive itself. A NUL in a key path
  is refused by every write and reads as absent in every read.
- **RegistryUtils**: a NUL in a value name read, overwrote or deleted the value named up to it; writes refuse it and
  reads return the default.
- **RegistryUtils.SetValue** wrote numbers stored as text in the current culture, but GetValue<T> parses invariantly:
  under de-DE 1.5 read back as 15, and under sv-SE -5L had a Unicode minus. Text is written invariantly.
- **SingleInstanceApp**: the gate folded the id with ToUpperInvariant, but the pipe relied on the kernel's case table,
  which leaves U+017F, Georgian and titlecase digraphs alone, so "s"/"ſ" or "ǅ"/"Ǆ" ids shared a gate but not a pipe
  and the second launch was lost. Both names use one folded id.
- **SingleInstanceApp**: a forward that failed, then failed again after a take-over attempt, waited the whole timeout
  twice; the second forward gets what is left.
- **SingleInstance.TryAcquire**: names past the kernel's object-name limit were cut short, so two different long names
  shared a gate; names over `MaxNameLength` (32000) throw.
- **ShortcutUtils.Read**: an empty file (or one shorter than the link header) read as a shortcut with no target; it
  throws InvalidDataException like any other non-shortcut.
- **PrivilegeScope** (Enable, IsHeld, IsEnabled), **StorageDeviceInfo** (Describe, GetUncPath) and
  **SingleInstanceApp.Start** threw ArgumentException for a null argument; they throw ArgumentNullException.

Still suspected (BUGLIST R106-1..7): ids the kernel folds alike but ToUpperInvariant does not; DpiInfo.Monitors throwing
where MonitorInfo falls back; DpiInfo mapping an unknown awareness context to PerMonitor; ClipboardUtils.SetText cut at
a NUL; ShortcutUtils' 260-character fields; a truncated .lnk giving partial data; JobObject.Start("   ") and
AssignProcess(IntPtr.Zero) throwing Win32Exception.

### Round 105: WinForms, tooling, docs, sixth pass (v2026.11.16.21)

Two fresh reviewers (correctness, robustness): 10 bugs, none severe, so section 17 stays re-queued. The regression
tests are in `WinFormsReview105Tests` (9); each fails on the old code, run by name under a 60 s timeout. The two
checked-build.sh fixes were confirmed by running the old and new script lines in a scratch repo.

- **PlotPictureBox** never marked the wheel handled, so a scrolling parent panel scrolled while the plot zoomed.
- **PlotPictureBox**: a ViewChanged handler that re-fitted the view was undone, because ZoomAt and PanBy turned auto-fit
  off after the event returned; the flag is set before the event.
- **PlotPictureBox** took mouse capture after the MouseDown handlers ran, so one that released it (as a modal dialog
  does) left the plot holding capture with no button down; capture is taken first.
- **MenuItemsBuilder** (Submenu, an item with a bad shortcut, Item(ICommand)) and **ToolStripBuilder** (DropDown,
  Button(ICommand)) left the item being built undisposed, and its command subscription live, when its setup threw.
- **MenuBuilder.FindItem** read DropDownItems on the leaf it found, which created an empty drop-down; it reads them only
  when the item has some.
- **FormStatePersistence.EnsureOnScreen**: the working areas' union overflowed int for areas far apart, so a window
  straddling two adjacent monitors was moved; the extent is computed in long.
- **checked-build.sh**: a work-tree argument with a trailing slash put the lock inside the not-yet-made folder, so the
  run was refused as "in use"; an untracked root build file copied in by an earlier run stayed and was imported.
- **README**: the discovery table named `Identifiers.Ulid`, which is not a type (`UlidGenerator`). A test now checks
  that every type name in the discovery rows is declared and every member name is found in the sources.

Still suspected (BUGLIST R105-1..14): WaitCursorScope while the UI thread is blocked; InvokeIfRequired racing handle
destruction; SetSelectedEnum's leftover DropDown text; CommandBinding.Post after the UI thread exits; CSV export of
"-Infinity"/"-NaN"; a chorded right button ending a pan; ZoomAt by 1 above MaxZoom; FormStatePersistence's doc;
bump-version.ps1 (no cap on N, the BOM, a non-atomic write); test.ps1 (a trailing `-Configuration`, Kill on Windows
PowerShell 5.1); coverage.ps1 with no timeout; the incomplete-function generator dropping a reference row.

### Round 104: Signal analysis, sixth pass (v2026.11.16.20)

Two fresh reviewers (correctness, robustness): 17 bugs, none severe, so section 2 stays re-queued. The queue listed
section 2 twice; the second entry was a slip and is dropped (the seventh pass takes it up again). The regression tests
are in `SignalReview104Tests` (17); each fails on the old code, run by name under a 60 s timeout.

- **Convolution** rounded each subnormal product on its own (three 2^-537 by three 2^-538 gave all zeros for
  [0, ε, 2ε, ε, 0]); such elements are summed scaled up by powers of two and scaled back once.
- **Convolution**: one non-finite sample anywhere turned the overflow rescue off for every element; only elements whose
  overlap holds it are left alone.
- **SavitzkyGolay** did not pass subnormal polynomials through (3ε came back 4ε at the edges); a tiny window is
  recomputed scaled up by 2^600.
- **PeakInterpolation.Parabolic** lost the vertex offset for subnormal values (0 for 1/6); they are scaled up first.
- **Fft.Inverse** rounded a subnormal result twice, at the scale-back and again by 1/N (bin 3 had −10ε for −10.53ε); the
  power-of-two path applies 1/N with its scale-back.
- **PulseMeasurements.PulseWidth**: a rising crossing whose time rounded up to the next integer started the falling
  search a segment late (a runt at index 1000 measured 599.5 samples for 4e-14). Crossings are carried by segment index;
  EdgeTime, which had the same hand-off, too.
- **Hampel** with nσ = 0 kept every point when the MAD overflowed σ (0·∞ = NaN); and a MAD near 1.5e308 overflowed σ
  so real outliers were kept. Both sides are compared in quarters where either overflows.
- **Resample.Linear / ToRate** rounded a subnormal interpolation twice (halfway between ε and 2ε gave ε), and gave NaN
  between two equal infinities.
- **TimeDelayEstimate.CrossCorrelationPeak**: FFT rounding broke exact ties against the nearest-zero rule ([−1] against
  [−1, −1, 1, 2, 1, 2, −1] gave lag 1 at 1.0000000000000002). Lags within the FFT's error of the peak are summed
  directly (up to 64 of them), which also makes the value exact for integer signals.
- **EnvelopeFollower**: just above the 1e-5 series cutoff the coefficient 1 − e^(−x) lost five digits; below 1 it is
  2u/(1 + u) with u = tanh(x/2).
- **EnvelopeFollower.Process** and **Quantizer.Quantize(double[])** named the scalar overloads' arguments ("sample",
  "value") for a bad element; they name "samples" with the index.
- **CycleMeasurements**: the duty cycle was high time over period in seconds, both ∞ at 1e-306 Hz (NaN); it is in samples.
- **PhaseUnwrap.GroupDelay** with subnormal frequency spacing rounded 2π·Δf (a pure delay 4.7% off); Δf is scaled up first.
- **Goertzel** at a frequency whose f/fs overflowed returned NaN; it uses the alias f mod fs (exact).

Still suspected (BUGLIST R104-1..6): Savitzky-Golay edge cost at huge orders; ScaleB double rounding in WelchPsd and
Multitaper; Fft.Inverse subnormal double rounding for other lengths; FrequencyGrid.NearestBin halfway ties; Hampel and
MedianFilter cost at window ≈ n ≈ 1e6; Goertzel aliasing past f/fs = 2^53.

### Round 103: Logging, diagnostics, CLI, sixth pass (v2026.11.16.19)

Two fresh reviewers (correctness, robustness): 13 bugs, none severe, plus the open R69-1, so section 12 stays
re-queued (14). The gates also turned up a section 7 bug (below). The regression tests are in `LoggingReview103Tests`
(15); each fails on the old code, run by name under a 60 s timeout.

- **AsyncLogSink.Dispose** waited for the drain without limit, so an inner sink that hung hung Dispose (R69-1, open
  since round 29). A `drainTimeout` argument bounds the wait: past it, Dispose returns and the inner sink is closed when
  the drain ends. Null (the default) still waits until drained.

- **AsyncLogSink** wrote an event from another sink's consumer straight to its inner sink with no depth count, so two
  blocking sinks whose inner sinks log their own failures into the same logger recursed until the stack overflowed.
  The straight-through path is bounded per sink as the sink's own consumer is (a second nesting is dropped and counted).
- **AsyncLogSink**'s straight-through path skipped the Dispose check, so an event reached an inner sink already
  released; it throws ObjectDisposedException as the queued path does.
- **HealthCheck**: two checks at once reported a healthy probe as "previous run has not returned" when the other run's
  start was merely queued. Only an abandoned run (its token cancelled by a timeout or the check's cancellation) holds
  later checks off.
- **RateLimitedSink** could write a run's "(repeated N times)" summary before the run's first event, when that write
  was still in flight on another thread (or at Dispose). The summary is handed to that writer, which writes it after;
  an owned inner sink is disposed after the last write in flight.
- **AnsiStyle** looked only at an SGR sequence's first parameter for a reset, so ESC[1;0m ended the outer style early.
  Any 0 or empty parameter counts (colour arguments such as 38;5;0 do not). The round-69 test pinned ESC[10;0m as not
  a reset; it is one.
- **ScopedContext**: a scope disposed on another flow stayed current (and a push nested under it); Current and Push
  skip disposed scopes at the top.
- **ScopedContext** cached the full path on every node read, so logging in each of 10,000 nested scopes held 200 MB;
  only a short path is cached.
- **TextWriterSink**, **DelegateSink** and **RollingMemorySink** took events after Dispose; they throw
  ObjectDisposedException (RollingMemorySink's Snapshot still works).
- **FileSink**: one event its encoding refused (a lone surrogate with a throwing encoder) stayed in the StreamWriter,
  so every later write failed and Dispose threw. Lines are encoded before writing, a failed write drops the stream for
  the next event to reopen, and Dispose never throws.
- **ConsoleSpinner** drew a message's line breaks, so each frame scrolled a new line; tabs and other control characters
  broke the padding. They are drawn as spaces.
- **RouterSink.AddRoute** after Dispose was accepted and an owned sink leaked; it throws ObjectDisposedException.
- **SyslogSink** took an undefined transport (and used TCP); it throws naming "transport".
- **StructuredTextFormatter** took an undefined field (an empty column); it throws naming "fields".

Section 7, found by the gates: **ConfigBinder** failed to bind an empty `char?` setting in about one run in four of the
Config tests (R101-7, first seen on the checked build only). Logging the operands showed both sides of the char
branch's "nullable and empty" test true while the test was false: tier-1 PGO code on the .NET 8.0.3 runtime. Rewriting
the test did not help; the converter is marked NoOptimization (binding is not a hot path). The regression test is the
existing `Binder_ACharKeepsWhitespace` under the Config filter: 13 failures in 45 runs before, 0 in 15 with PGO off,
0 in 40 after. A one-test stress loop did not reproduce it (the profile needs the whole Config mix).

Test fix: `Hampel_WideWindowIsNotQuadratic` timed out at 3.26 s under the full verify's load (about 2 s alone; the old
code took 30 s); its limit is 10 s.

Still suspected (BUGLIST R103-1..7, beside R69-2..5): Benchmark at a 0 ns mean; spinner widths for VS16 emoji and after
a window shrink; a child logger's AddSink after the parent's Dispose; FileSink failing a delivered line when the reopen
after a rotation fails; ConsoleSpinner's constructor with a writer throwing other exceptions; HealthCheck's disposed
token source under an abandoned probe; DiskSpace with GetDrives throwing.

### Round 102: WPF satellite, sixth pass (v2026.11.16.18)

Two fresh reviewers (correctness, robustness): 13 bugs, none severe, so section 16 stays re-queued. The regression
tests are in `WpfReview102Tests` (14); each fails on the old code, run by name under a 60 s timeout.

- **ThemeManager** let the target dictionary be registered as a theme (or one that merges it, at any depth), so Apply
  merged the target into itself and the next missing-key lookup overflowed the stack. Register throws naming
  "dictionary"; Apply checks again (a dictionary can merge the target after registering) and changes nothing.
- **BindingFailureListener** read every live TraceSwitch's setting; one switch anywhere in the app whose value doesn't
  parse made the constructor throw, and the listener count, already raised, left every later listener with
  DataBindingSource Off. Such switches are skipped, and the count rises only once set up.
- **BindingFailureListener** let a FormatException out of a trace call with a malformed format; it records the raw
  format and its arguments.
- **DebugConverter.Log** took null and every conversion then threw NullReferenceException; the setter throws.
- **ValidationRuleSet.Dependents(null)** and **DependsOn** with a null name threw naming "key" (DependsOn after
  registering the names before it). They name "propertyName" and "otherProperties", and DependsOn checks first.
- **RuleBuilder.Range** took a NaN bound (ignored, so a NaN value passed); it throws naming "min" or "max".
- **GlobalHotkeys.Register(string)** named "text" for a null gesture; it names "gesture".
- **FileDialogOptions.Filter** took an odd number of '|' parts, which WpfDialogService rejected only when shown (no
  ParamName) and RecordingDialogService accepted, so tests passed where the app threw. The setter checks it.
- **PlotPresenter.FitMatrix** returned a NaN matrix for NaN or infinite sizes, or a subnormal content size; it returns
  Identity as for empty sizes.
- **FileDropBehavior** saved a bound or DynamicResource AllowDrop by storing the expression in another property: a
  binding threw "NonShareable ... already been used" out of SetCommand, and a resource reference came back as a local
  value (or ClearValue threw). The prior value is boxed, and a resource reference is restored as one.
- **NumericInputBehavior.Coerce** with no shown value in range gave "0.2" or "0.3" depending on the input; it gives
  Minimum rounded, as documented.
- **NumericInputBehavior** let Enter and Tab insert "\r\n" and "\t" unchecked with AcceptsReturn or AcceptsTab, as
  they go through editing commands; those commands are refused.
- **Messenger**: after Unregister and a re-register, the delivery order depended on whether a Send had pruned the stale
  index entry in between. Unregister drops the entry, so a re-registration always goes last.

Still suspected (BUGLIST R102-1..6): IncrementalCollection with a re-entrant ResetAsync; CountToVisibilityConverter with
a null enumerator or a one-shot sequence; GlobalHotkeys with a null callback for a held gesture; RecordingDialogService
recording live caller objects; ThemeManager's dispatcher on a non-pumping STA thread; NumericRangeValidationRule with a
NaN bound.

### Round 101: Charts, sixth pass (v2026.11.16.17)

Two fresh reviewers (correctness, robustness): 9 bugs, none severe, so section 5 stays re-queued. The regression tests
are in `ChartsReview101Tests` (11); each fails on the old code, run by name under a 60 s timeout.

- **Histogram.Bin** over a range wider than double.MaxValue built its edges as `lo + i·width`, which overflowed past the
  middle: edges of +∞, and the maximum counted in a middle bin. The edges are computed halved, held monotone and in
  [lo, hi].
- **ErrorBarChart, BoxPlot, PlotFrame** (and so LinePlot and SvgChart) halved the mapping only when `max − min`
  overflowed; `v − min` overflows too for a value far outside an explicit finite range. Its ±∞ pixel dropped the error
  bar, the upper whisker, or the line segment (SvgChart drew a dot). BandChart's copy of the mapping got the same fix.
- **SvgChart** padded a bar axis by half a slot past the end bars, so bars at 0 and 1.7e308 gave an infinite axis and
  AxisTicks threw naming its own "min". The padding is held finite.
- **SvgChart** took a NaN or +∞ `MinMargin*` and failed inside PlotFrame, naming "width". It throws naming "options".
- **HeatMap.Render and MultiPanel.Compose** passed an image past the largest byte array to ImageBuffer, which named its
  own "width". They check the cap and name "options" and "gap".
- **TimingDiagram** replaced an explicit `End` at or before every change with the first change + 1, silently. The
  window now opens before End.
- **TimingDiagram.FormatSeconds** chose the unit with ties-to-even rounding but printed with the format's own, so
  9.999995e-10 came out "1000 ps". The unit is chosen from the printed value.

Still suspected (BUGLIST R101-1..7): a raster Callout's arrowhead with a huge dx; HexBin.Compute with a default
PlotFrame; BoxPlot with infinite data; bar width on a log axis; a 2-tick loose axis; a 1 × 1 colorbar; and an
intermittent checked-build failure of `Binder_ACharKeepsWhitespace` (about 1 run in 40 of the Config tests), where
`TryConvertScalar("", char?)` returned false although its code cannot. It never failed with `DOTNET_TieredPGO=0` or in
the unchecked build: a suspected tier-1 PGO miscompile in the .NET 8.0.3 runtime the gates ran on.

### Round 100: Documents & config, sixth pass (v2026.11.16.16)

Two fresh reviewers (correctness, robustness): 10 bugs, 2 severe, so section 7 stays re-queued. The regression tests
are in `DocumentsConfigReview100Tests` (12); each fails on the old code, run by name under a 60 s timeout.

- **Severe: SettingsStore** with a get-only `ReadOnlyCollection<T>` or `ReadOnlyDictionary<K,V>` saved fine, then every
  `LoadFrom` threw NotSupportedException (the refill setter made System.Text.Json read a type it can't build). Such a
  property is now skipped on load and keeps its default, as documented.
- **Severe (security): SettingsStore**'s save moved a temp file over the settings file, so the file's own ACL was
  replaced by the folder's inherited one (an owner-only file gained SYSTEM and Administrators). It now uses
  `File.Replace` on Windows, and copies the Unix mode elsewhere.
- **SettingsStore** saved a value no load could read back (an interface-typed property holding an object), and every
  launch after it crashed in `LoadFrom`. The save now reads its JSON back first and throws NotSupportedException
  without touching the file.
- **SettingsStore** wrote a `char` (or `List<char>`, `char[]`) holding a lone surrogate as U+FFFD silently; strings
  were already refused. A strict char converter refuses it too.
- **PdfWriter** sized an automatic table column to its text, but `(w + 2·pad) − 2·pad` can land one ulp below `w`, so
  about 7% of one-word cells split mid-word ('flq' at Helvetica 15.6). The column grows until it gives `w` back.
- **ConfigBinder** passed a null list item to conversion: "cannot convert ''" for an int list, a null entry in a string
  list. A null item is unset, as a null key is (a later item past it is a gap).
- **ConfigBinder** skipped an empty index ("Hosts:"), dropping its value without a word. It is reported.
- **ConfigBinder** was quadratic on a self-containing type (every section scanned every key once per property: 8,000
  keys took 29 s). Each section now scans only the keys under it.
- **ConfigBinder and ConfigLayers** kept one of "Port" and "PORT" by enumeration order, silently. With different
  values the binder reports it and `ConfigLayers.Add` throws ArgumentException; the same value twice is fine.
- **EnvironmentVariables.Get("X\0junk")** returned X's value (the OS stops at NUL). A name with NUL, or with '=' past
  its first character, throws ArgumentException.

Still suspected (BUGLIST R100-1..5): GitHub slugs for a final sigma and U+0130; "infinity" and near-MaxValue floats on
.NET Framework; several instances saving one settings file at once; SettingsStore's rollback when restoring attributes
fails after the move; stale-temp cleanup on a share with a skewed clock.

### Round 99: Binary formats & images, sixth pass (v2026.11.16.15)

Two fresh reviewers (correctness, robustness): 3 bugs, 1 severe, so section 6 stays re-queued. The regression tests
are in `BinaryImagesReview99Tests` (5); each fails on the old code, run by name under a 60 s timeout.

- **Severe: PngReader.Decode(Stream)** treated tRNS, whose first letter is lower-case, as an ignorable ancillary chunk
  and passed it on empty: palette images with transparency decoded fully opaque with no error, and valid greyscale
  and RGB images with tRNS were refused ("Bad tRNS length"). `Decode(byte[])` and `Load` were right. Both reviewers
  found it; of 1,500 random PNGs, the stream path got 541 wrong, every one with tRNS. tRNS is now kept (up to 256 bytes).
- **PngReader.Decode(Stream)** buffered a PLTE, IEND or unknown critical chunk whole before refusing it: one declaring
  400 MB allocated 381 MiB, and with a right CRC the final copy doubled it. IHDR, PLTE, tRNS and IEND are refused at
  their header when longer than they can be, and an unknown critical chunk at its header. As hardening, both paths now
  refuse an IEND with data, which the byte path ignored.
- **SvgDocument**'s size error said "must be at least 0.0000005", yet refused 5e-7 (a double just below it, which
  rounds to 0 at six decimals). It now says the size must be more than 0.0000005.

Still suspected (BUGLIST R99-1..2): Base58.Encode with no output-length guard (OutOfMemoryException rather than
ArgumentException on a huge input); a 32-bit bitfields BMP with a 40-byte header whose masks overlap the pixel data
giving a misleading error.

### Round 98: Text & small types, sixth pass (v2026.11.16.14)

Two fresh reviewers (correctness, robustness): 4 bugs, none severe, so section 11 stays re-queued (four bugs). The
regression tests are in `TextReview98Tests` (8); each regression fails on the old code, run by name under a 60 s
timeout. `DisplayWidth_WithoutTheSelectorSymbolsKeepTheirWidth` and `Glob_LongSegmentsMatchANaiveMatcher` are guards.

- **TextAlign.Fit, PadRight and Center** kept an ESC whose sequence the next character cut short (another ESC, a
  non-ASCII character, CAN or SUB). Once the cut, or the dropping of an unfinished escape at the end, removed that
  character, the ESC swallowed the padding or ellipsis after it: `PadRight("ab" ESC ESC, 6)` measured 2,
  `Fit("a" ESC "中", 2)` measured 1. A fuzz of 200,000 cases found 1,308 wrong widths. Both reviewers found it. The cut
  leaves such sequences out, and the unfinished escape is dropped together with the cut-short ones just before it.
- **TextAlign.DisplayWidth** missed U+FE0F after an escape sequence: "❤" ESC[0m U+FE0F measured 1, and 2 once stripped.
- **TextAlign.DisplayWidth** let U+FE0F widen BMP symbols only, so 🌡️ and 🕉️ measured 1 beside ❤️'s 2, and
  `Fit("🌡️xyz", 2)` took three terminal columns. Text-presentation symbols in the emoji blocks U+1F000–1FAFF now
  measure 2 with the selector.
- **GlobMatcher** built each character's mask position by position, O(m), and past the cache's cap rebuilt it for every
  named character: `"*" + "[一-龥]"×20,000 + "x*"` against 100,000 CJK characters took 15 s. Masks are now unions of
  the segment's token groups (the ? tokens, equal literals, each distinct class).

Still suspected (BUGLIST R98-1..3): NGramSimilarity counting n-grams in UTF-16 units, so a gram can split a surrogate
pair; TextAlign.Fit not validating its ellipsis as it does its pad; TextDiff's largest allowed table being one 1 GB
array, which a 32-bit or capped process can fail to allocate before the documented limit.

### Round 97: Concurrency, sixth pass (v2026.11.16.13)

Two fresh reviewers (correctness, robustness): 3 bugs, 1 severe (a hang), so section 10 stays re-queued. The
regression tests are in `ConcurrencyReview97Tests` (7); each regression fails on the old code, run by name under a
60 s timeout. `WithTimeout_ATaskGivenUpOnAndFaultedLaterIsObserved` is a guard.

- **Severe: ConcurrentPipeline.CompleteAsync** called from the consumer callback (a consumer that stops on a sentinel
  item) waited for the drain, which waited on the caller: it hung, and so did every later CompleteAsync and Dispose.
  From the consumer (tracked by an AsyncLocal, so it holds across the callback's awaits) it marks the pipeline
  completed and returns; the rest of the buffer drains after the callback. Dispose from the consumer, documented as
  not allowed, now cancels without waiting as it does from onError.
- **ConcurrentPipeline.TryEnqueue** under DropNewest and DropOldest never checked for Dispose, though documented to
  throw ObjectDisposedException: a full buffer dropped or evicted and returned, and a call from onError after Dispose
  returned true for an item nothing would process. Both reviewers found it.
- **TaskExtensions.WithTimeout and WithCancellation** added a fault observer to the task on every give-up, so polling
  a long-lived task with short timeouts kept one continuation per call (200,000 polls kept 52 MB). Each task is
  observed once.

Still suspected (BUGLIST R97-1..3): WithCancellation returning a finished task's result under an already cancelled
token (the BCL's WaitAsync does the same; AsyncManualResetEvent documents the opposite rule); WhichExe on Unix
ignoring the execute bit and on Windows resolving a drive-relative `C:tool`; a second concurrent Dispose of
ConcurrentPipeline or PeriodicWorker returning before the loop stops; ProcessRunner reader threads kept alive by a
grandchild holding the inherited pipes.

### Round 96: Collections, sixth pass (v2026.11.16.12)

Two fresh reviewers (correctness, robustness): 6 bugs, 1 severe (a hang), so section 9 stays re-queued. The
regression tests are in `CollectionsReview96Tests` (13); each regression fails on the old code, run by name under a
60 s timeout. `Samplers_AFewZeroDrawsAreRedrawn` and the two wrapped-Clear tests are guards.

- **Severe: ReservoirSampler.Add and SamplingPlans.WeightedWithoutReplacement** redrew a zero uniform until it was
  nonzero, which never ended on a Random that only returns 0. After 64 zeros in a row (2^-3392 for a sound source)
  they throw InvalidOperationException.
- **Aggregation.Summarize** reported the mean as sum / n, whose rounding left [min, max]: three copies of 0.1 had a
  mean of 0.10000000000000002, and 1e7 copies 0.09999999998389754. It reports the running (Welford) mean it already
  kept, clamped to [min, max].
- **MultisetEqualityComparer.Equals** kept int counts, which wrapped: 2^32 nulls equalled an empty sequence (and
  2^32 copies of one item would too). Counts are longs.
- **CountMinSketch.Add and Estimate** passed a null item to the caller's comparer, so StringComparer.Ordinal threw
  ArgumentNullException for a parameter "obj". A null item hashes to 0, as under the default comparer.
- **Deque.Clear and CircularBuffer.Clear** wiped the whole backing array, so clearing one item cost the largest size
  ever held (117 ms after 5e7 items, where `Queue<T>` takes none). They clear the occupied slots.
- **DataTableLite.Row:** `default(Row)` threw NullReferenceException from Count and both indexers; it reads as a row
  with no cells.

Still suspected (BUGLIST R96-1..3): TtlCache entries that never expire counting as expired when an injected clock
returns DateTimeOffset.MaxValue; the samplers' skip lengths quantised by a seeded Random's 31-bit NextDouble after
about k·2^31 items; CountMinSketch not checking width × depth before allocating.

### Round 95: Numerics core, sixth pass (v2026.11.16.11)

Two fresh reviewers (correctness, robustness): 8 bugs, none severe, so section 4 stays re-queued. The correctness
reviewer did not reach AkimaSpline, LookupTable, ScatteredInterpolation, StreamingQuantile, Differentiation,
Uncertainty, Vector2 or EngineeringNotation; the next pass should start there. The regression tests are in
`NumericsCoreReview95Tests` (13); each regression fails on the old code, run by name under a 60 s timeout.
`Fraction_SubnormalResultsMatchACorrectlyRoundedReference` and `NextInt64_ASmallRangeKeepsItsSequence` are guards.

- **RandomUtils.NextPoisson** could return int.MinValue: a zero uniform draw made the PTRS acceptance test compare
  log 0 = −∞ and accept any count, up to 1e15 when the other draw sat at the edge of the hat. Such draws are redrawn.
- **DeterministicRandom.NextInt64(min, max)** used plain modulo reduction, documented as a negligible bias: for a
  range of 3·2^62 the first third of the range came up half the time. A draw past the last whole multiple of the
  range is redrawn; every other draw gives the value it gave before, so small ranges keep their sequences.
- **LinearRegression.Fit and ZScore.Standardize** lost data spread over a few ulps: the mean rounded onto the
  data's own grid, so the deviations were off-centre (`{1, 1 + 2^-52}` fitted half the slope with R² 0.5, and
  scored 0 and 1.414). Deviations are the offsets from the first point less their mean.
- **Polynomial.RealRoots:** a quadratic whose coefficients span more than the double range lost a root to
  underflow (1e300x² − 1e-300 gave [−0]); x is now scaled to balance a against c, and the roots come back through
  the mantissas. A cubic's monic coefficient could underflow the same way (1e300x³ − 1e-300 gave [0], not 1e-200);
  the monic shift also scales x up where that keeps every coefficient normal.
- **WeightedAverage.Compute:** a weight far below the largest flushed to 0 when the weights were scaled, though its
  product with a huge value dominated ({1e308, 0} weighted {1e-300, 1e300} gave 0, not 1e-292). Products are formed
  from the mantissas, and the mean is taken about the value nearest 0, not the first one, which cancelled there.
- **Fraction.ToDouble** rounded a subnormal result twice ((3·2^59 − 1)/2^1134 gave 1e-323, not 5e-324). It is
  rounded once, to a multiple of 2^-1074.

Hardening without a public repro: the ScaleB copies in LinearAlgebra, Polynomial, WeightedAverage and ZScore rounded
twice when a step went subnormal and was scaled again; a result below the normal range is now reached in one
multiplication. Still suspected (BUGLIST R95-1..3): NextGamma's `Pow(1, ∞)` on .NET Framework, `Angle`'s
normalisation of an infinite angle (NaN, not an exception), and the sizes at which `Primes.Sieve` and
`Polynomial.Fit` run out of memory instead of rejecting the argument.

### Round 94: IO, sixth pass (v2026.11.16.10)

Two fresh reviewers (correctness, robustness): 4 bugs, 1 severe, so section 8 stays re-queued. FileRetention's
wildcard matching agreed with the BCL's Win32 matcher on 300,000 random pairs, and seven other crafted tar metadata
sequences gave the guard and TarReader the same reading. The regression tests are in `IOReview94Tests` (6); each
regression fails on the old code, run by name under a 60 s timeout, and `Tar_GnuLongNamesStillRead` is a guard.

- **TarUtils metadata guard (severe):** a GNU long-name header followed by a PAX header put the guard out of step
  with TarReader, which reads that PAX header as an ordinary entry and ignores its size; the guard applied the size
  to the next header, so a later 1.5 GB "x" header got through and a 5 KB archive made `ListEntries` (or
  `ExtractToDirectory`) allocate gigabytes. GNU and PAX metadata headers before one entry are refused (GNU's own K
  then L is still read). A PAX `size=` with more than 18 digits of leading zeros, which TarReader reads, was refused.
- **TempDirectory.Dispose** stopped at the first file it could not delete (one still open), leaving everything after
  it on disk. Each entry is tried; only those that cannot go are left.
- **ZipUtils and TarUtils extraction:** a file entry whose path is an existing folder threw a bare
  UnauthorizedAccessException naming nothing. It throws IOException naming the entry and the path.

Still suspected (BUGLIST R94-1..4): tar's self-exclusion through a short name or junction (it fails on a sharing
violation rather than packing itself), a folder holding only the archive left out, AtomicFile's ".bak" past the
255-character name limit (the write fails; the original is kept), and zip directory entries ending in a backslash.

### Round 93: Objects, functional, grids, graphs, intervals, sixth pass (v2026.11.16.9)

Two fresh reviewers (correctness, robustness): 3 bugs, none severe, so section 14 stays re-queued. RangeMap,
RangeSet and IntervalTree against naive references, the grid searches and ShortestPath against Bellman-Ford, and the
walks' stacks and rollbacks held up. The regression tests are in `ObjectsGraphsReview93Tests` (6); the two code
regressions fail on the old code, run by name under a 60 s timeout, and the other four are guards for documented
behaviour.

- **FlattenObject** counted its 2^29-character key budget when a child was walked, after every child's full path had
  been built: one 2,000-character key above a million-item array allocated 4 GB before the documented
  InvalidOperationException (more items or a longer key ran out of memory first). Each child's path is counted as
  it is made.
- **Graph.Edges** and **Neighbors** returned the graph's own lists, so a caller that cast and changed one put
  `Edges`, `EdgeCount` and the adjacency lists out of step. They return read-only views (which still follow edges
  added later).
- **UnflattenObject** documented that indexed children become a list, but the root is always a dictionary: a
  flattened root list comes back keyed "0", "1". Documented. (`FloatTolerance` now also says that values of
  different types are never equal and decimals compare exactly; that was the behaviour, not a bug.)

Still suspected (BUGLIST R93-3): A*'s relative 1e-12 tie window. A* steps cost 1 or √2, so distinct f-values differ
by at least about 1/(3n) for paths of n steps, far wider than the window at any grid that fits in memory.

### Round 92: Security, net, time, sixth pass (v2026.11.16.8)

Two fresh reviewers (correctness, robustness) on the round 91 code: 3 bugs, none severe, so section 13 stays
re-queued (3 is the threshold). The round 91 changes otherwise held up: the 400-year cron search against a naive
reference in fixed offsets and four DST zones, and the strict TCP decoding across reads, at the end, in UTF-16 and
around a byte order mark. The regression tests are in `SecurityNetTimeReview6Tests` (4) and, for the scheduler,
`SecurityNetTimeReview5Tests` (1); each fails on the old code, run by name under a 60 s timeout.
`Tcp_ACrLfWithNoBadBytesIsStillOneBreak` is a guard.

- **CronScheduler:** the round 91 clock-back check worked a job's next time out again even when it was already due,
  so a few milliseconds of jitter back around an Add skipped that run. A due job keeps its run.
- **TcpLineClient:** with a strict encoding, bad bytes between a CR and its LF marked the next line, and the LF was
  skipped as the end of the CR LF, so the good line after them was dropped. That LF ends the bad line.
- **RelativeTime** counted years as days / 365, gaining one every four years (2,000 calendar years read "2001
  years", the calendar's whole span "10005"). It counts whole calendar years (a Feb 29 anniversary is Feb 28).

### Round 91: Security, net, time, fifth pass (v2026.11.16.7)

Two fresh reviewers (correctness, robustness): 11 bugs (two found by both), 4 severe. Section 13 stays re-queued,
and the fifth pass is complete. The regression tests are in `SecurityNetTimeReview5Tests` (17); every regression
fails on the old code, run by name under a 60 s timeout. `Scheduler_AShortStepBackDoesNotRepeatARun`,
`Cron_AnImpossibleScheduleStillThrowsQuickly` and `Secrets_TheHandBuiltFormatLoads` are guards for the new behaviour.

- **CronScheduler, clock corrected back (severe):** the stored next times came from the fast clock, so after a
  correction no job ran until real time caught up (a year, for a clock a year fast). RunDue notices the clock going
  back (against its last reading, or Start's) and works each job's next time out again; a step back under three
  hours counts from the last run, so a run already made is not repeated. A clock that threw during Add after Start,
  or a reading past the last occurrence (a glitch to 9999), left the job's next time null for good; the first is
  reported as the scheduler's and retried on the next pass, the second is recovered when the clock comes back.
- **CidrRange.Contains (severe):** an IPv4-mapped IPv6 address (how a dual-mode socket reports an IPv4 peer) never
  matched an IPv4 block, so IPv4 deny lists let those peers through. It is tested as its IPv4 address, as IpUtils
  does. `Parse(null)` threw FormatException; it throws ArgumentNullException.
- **TcpLineClient, strict encodings (2 severe):** after a bad byte the decoder was reset and the bytes decoded again
  one at a time, which lost a valid character split across reads, assumed UTF-8 when skipping (one lone UTF-16
  surrogate misaligned every later line) and threw once per bad byte. A copy of the encoding counts bad bytes
  instead of throwing and keeps its own carry and alignment; the line holding them throws once, at its end, and an
  overlong line with bad bytes is reported once, as too long.
- **HttpDownload** wrote a body longer than its declared total to disk without limit, then retried (and kept it
  under Resume). Nothing past the total is written: the partial file is deleted and HttpDownloadException thrown.
- **SecretsFile.FromBytes:** contents that authenticate but are malformed (another tool sharing the passphrase)
  threw ArgumentOutOfRangeException or FormatException, and empty or duplicate keys were taken. They throw the
  documented CryptographicException, and invalid UTF-8 is no longer replaced silently.
- **CronSchedule** searched only 8 years, so valid schedules such as Feb 29 on a weekend (`0 0 29 2 */6`, up to 16
  years apart) or on a Sunday (28 or more) threw "no occurrence". It searches the 400-year Gregorian cycle, jumping
  over days that cannot match, so it is no slower for frequent schedules and an impossible one still fails fast.
- **Iso8601Duration** rejected ISO's decimal comma (`PT1,5S`); the seconds fraction takes a comma or a point.

### Round 90: Collections, fifth pass (v2026.11.16.6)

Two fresh reviewers (correctness, robustness): 16 bugs, 1 severe (multiset hash collisions). Section 9 stays
re-queued. The regression tests are in `CollectionsReview90Tests` (16); every regression fails on the old code, run
by name under a 60 s timeout (the NaN/decimal keys and the ToString digits only on .NET Framework 4.8, proven with
net48.sh). `Summarize_MatchesANaiveReference` is a differential guard for the new streaming Summarize.

- **MultisetEqualityComparer (severe)** summed the raw element hashes, so int multisets with one sum collided
  (280,840 3-subsets gave 352 hashes; a HashSet of them took 29 s). Each element hash is finalised (Murmur3 fmix)
  before the sum, and the count is mixed in. Equals checks lengths first when both are collections.
- **SequenceEqualityComparer** was `hash*31 + h`, so every `[i, -31i]` collided; **DataTableLite.Distinct** had
  the same row hash (40,000 rows took 8 s). Both use Murmur3 steps.
- **.NET Framework keys:** equal decimals with different scales and NaNs with other payloads hashed apart, so one key
  counted twice in DataTableLite GroupBy/Distinct/InnerJoin, Counter, OrderedSet, OrderedDictionary, BiMap, caches,
  Aggregation, sampling strata and the sequence comparers. Those key types now hash by value (a decimal after
  dropping its trailing zeros, not through double, whose conversion rounds some equal decimals apart).
- **DataTableLite.Columns** returned the internal array (cast back and written, it desynced the name index); it is
  a read-only view. **ToString** printed doubles with 15 digits on .NET Framework; it round-trips.
- **CountMinSketch:** a depth past Array.MaxLength threw OutOfMemoryException; it is an argument error (`depth`).
- **BitSet.EnumerateSetBits** took its version at the first MoveNext, missing a change made after GetEnumerator.
- **TtlCache:** Remove returned true for an expired, unpruned entry; expiry saturated to "never" when the local
  (offset) time was near year 9999 though the UTC expiry fit; the prune threshold never came down after Clear,
  Remove or a prune, so write-once keys piled up to the old peak.
- **Null keys:** Summarize, Pivot and Stratified threw ArgumentNullException("key") when a selector returned null;
  they throw ArgumentException naming the selector.
- **OrderedDictionary / LruCache:** an update replaced the stored key instance; the first is kept, as Dictionary does.
- **Trie** kept a dictionary per node (about 250 bytes a character); nodes keep one child inline until a second.
  An Add that ran out of memory left a partial branch, so StartsWith answered true for prefixes never stored; the
  branch is built first and linked last.
- **Aggregation.Summarize** kept every value of every group (20 million items held 384 MB) and could not pass 2^31
  values in a group. It streams: Welford's mean and deviations on values scaled by a running power of two, so the
  overflow and underflow cases of earlier rounds keep their spread; a group past int.MaxValue throws
  OverflowException.

### Round 89: Text & small types, fifth pass (v2026.11.16.5)

Two fresh reviewers (correctness, robustness): 15 bugs, 1 severe (n-gram hash collisions). Section 11 stays
re-queued. The regression tests are in `TextReview89Tests` (7), `TextRobustnessReview89Tests` (6) and
`EnumReview89Tests` (3); every one fails on the old code, run by name under a 60 s timeout (three of them only on
.NET Framework 4.8, where they were proven with net48.sh).

- **NGramSimilarity (severe):** grams were told apart by a rolling hash mod 2^64, and a Thue–Morse block and its
  complement share one for any base, so such text gave thousands of distinct grams with one hash (a million
  characters took 43 s; 2 million, 3.5 minutes). Repetitive text compared every repeat in full, O(length·n). Grams now
  get exact class ids by rank doubling with counting sorts, O((|a| + |b|) log n) whatever the text; checked against a
  naive Substring reference over 4,000 seeded trials.
- **TextDiff** compared whole lines in every LCS cell, so long near-identical lines cost their length each time
  (2,000 lines of 10,000 characters took 6 s). Lines are mapped to int ids once.
- **GlobMatcher:** each distinct input character got its own cached mask, rebuilt once the 8 MB cache was cleared, so
  5,000 distinct CJK characters against a long segment took 13 s. Characters the pattern never names share one mask.
- **StringSimilarity.TopMatches** kept and sorted every qualifying candidate; it keeps a bounded heap of `count`.
- **EnumFlags** threw OverflowException on a ulong enum with bit 63 set; it works on the bits of any width.
- **QueryString.Format** wrote U+FFFD for an unpaired surrogate on .NET 8 (Parse didn't give it back) and threw
  UriFormatException on .NET Framework; it throws ArgumentException (ParamName `pairs`).
- **TextAlign:** every character in U+1F000–U+1FAFF counted two columns, narrow symbols too; U+3248–U+324F (circled
  numbers on black squares, ambiguous width) counted two. Zero-width marks came from the runtime's categories, so
  marks newer than Unicode 8 counted one on .NET Framework. Both now come from fixed Unicode 15 tables.
- **Soundex** coded 'ſ' as S and 'ı' as I on .NET Framework (upper-casing); letters outside A–Z act as vowels on
  every runtime.
- **Slug** didn't fold 'ẞ' (capital sharp s) to "ss".
- **EnumExtensions.GetDescription** went through ToString, so an alias could lose its [Description] (and .NET
  Framework picked a different alias); the first declared field with a description wins, else the first name.
  **TryParseName** with ignoreCase took the first case-insensitive match even when another name matched exactly.
- **WordWrap / Truncate** broke words at no-break spaces (U+00A0, U+2007, U+202F).
- **Docs:** CaseConverter and NaturalComparer now say their letters and casing come from the runtime's tables, which
  differ on .NET Framework.

### Round 88: WinForms + tooling + docs, fifth pass (v2026.11.16.4)

Two fresh reviewers (correctness, robustness): 17 bugs, none severe, after merging the one both raised (README's
scripts list lacked bump-version.ps1). Section 17 stays re-queued. The WinForms regression tests are in
`WinFormsReview88Tests` (10 tests, every one failing on the old code, run by name under a 60 s timeout). The tooling
fixes were probed by hand, each against the old script too: bump-version on padded, legacy and unknown values; a
build in a folder named `pm a,b=c` (CS8101 before, `/_/` paths after); the checked-build and net48 locks refusing a
second run; test.ps1's runner call with spaces, quotes and trailing backslashes in arguments.

- **bump-version.ps1** didn't trim the stored value, so `<Version> 2026.11.16.3 </Version>` failed the match and reset
  the counter to `.0`, reusing a version. It trims, and an unrecognised value (neither `YYYY.M.D.N` nor the old
  `0.N.0`) is an error instead of a silent reset.
- **MenuShortcut:** TryParse accepted LShiftKey, RShiftKey, LControlKey, RControlKey, LMenu and RMenu as the key,
  which Windows never delivers as one. Format wrote "None" for Keys.None and "Ctrl+None" for a bare modifier; it
  writes "" and "Ctrl".
- **MenuBuilder / ToolStripBuilder:** an ICommand item with a null command was made (and left undisposed) before the
  check, so a bad shortcut hid the ArgumentNullException. MenuBuilder.ApplyTo left the earlier menus on the strip when
  a later one threw; ToolStripBuilder.ApplyTo left the items it had made undisposed and still subscribed to their
  commands. Both are all or nothing now. CommandBinding unsubscribed dead items inside its lock, so a command whose
  remove accessor waited on another binding thread deadlocked; it unsubscribes after the lock.
- **AutoSizeColumns** in ColumnHeader mode with the headers hidden threw InvalidOperationException and applied no cap;
  it applies just the cap. **BindEnum** threw ArgumentException on a combo with a DataSource; it clears it first.
- **FormStatePersistence.EnsureOnScreen** used empty and inverted working areas, so a lost window came back 0×0 (or
  −100 wide) on one, and an area whose Right passed int.MaxValue wrapped. Such areas are ignored.
- **PlotPictureBox** kept its drag start when capture was taken away (Alt+Tab mid-drag), so a later move with the
  button down jumped the view; losing capture ends the drag.
- **Directory.Build.props:** a clone path with ',' or '=' broke every Release build (CS8101); PathMap doubles them
  (a ';' still can't be mapped, which the comment says).
- **Scripts:** checked-build.sh and net48.sh shared one fixed scratch folder with no lock, so two runs rebuilt it
  under each other; both take a lock folder now. checked-build.sh ignored a failed copy and didn't carry uncommitted
  root build files (Directory.Build.props). net48.sh wrote folder names into the csproj unescaped. test.ps1 ran each
  suite with no time limit; it kills one after 20 minutes and exits 124. Four reference generators wrote their output
  in place, so an interrupted run left half a file; they write a temp file and rename it.
- **ConsumerSmoke** built only for net8.0 against the core. It builds for net48 (the netstandard2.0 build on .NET
  Framework) and net8.0-windows with the three satellites (Satellites.cs), and verify.sh runs all three.
- **README:** the scripts list lacked bump-version.ps1, and gen-distribution-references.py needs scipy as well as mpmath.

### Round 87: Statistics, fifth pass (v2026.11.16.3)

Two fresh reviewers (correctness, robustness): 15 findings. One bug was reported from both sides (the independence
test's subnormal marginals). One was not reproduced: "Math.Log is inaccurate for subnormals on .NET Framework" came
from the reviewer's net48 harness, where `double.Parse("1.4866e-320")` gives 3008·2^-1074 instead of 3009 (Math.Log
itself is exact there). That leaves **13 bugs, none severe**, so section 3 stays re-queued. The regression tests are in
`StatisticsReview87Tests` (13 tests, every one failing on the old code, run by name under a timeout; the two new APIs
were stubbed to throw in that run).

- **Quantiles far in the lower tail at many degrees of freedom:** Newton approached the root from above by about
  sd/|z| a step and ran out of iterations (χ² with 1e9 df at p = 1e-300 came back with a CDF of 2e-172). The solver
  now notices steady linear progress and bisects, geometrically once the bracket spans a factor of 4.
- **Confidences next to 1:** (1 + c)/2 rounded to 1, so the mean, t, proportion, standard-deviation and regression
  intervals threw for c = 1 − 2^-53. They take the lower-tail quantile of (1 − c)/2 and negate it. The upper χ²
  quantile uses the new `Distributions.ChiSquareInverseSurvival(q, k)`, which solves the upper tail directly.
- **Anova** counted observations in an int: 2,100 groups of 2^20 wrapped it negative. It is a long.
- **FitWeibull** stopped its shape bracket at 1e12, though the equal-values guard admits shapes near 1e14. It now goes
  to 1e17 and refuses the sample past that.
- **ChiSquareGoodnessOfFit** compared overflowing totals (∞ with ∞) and accepted mismatched counts. Totals that
  overflow are re-summed, scaled by a power of two.
- **ChiSquareIndependence:** with an overflowing total, rescaling turned a subnormal row into 0, and it was refused.
  Marginals are checked before the rescale. An expected count that underflowed gave ∞ for a χ² of 1; such a term is
  formed from its ratios.
- **FitGamma:** the series for ln k − ψ(k) stopped at the 1/(132k¹⁰) term, 1e-10 relative at k = 6. Digamma and
  trigamma now shift to 10 and carry two more terms; the shape matches mpmath's root to 1e-13.
- **ChangePoint.Cusum and DetectShift** summed the raw values. A 2-ulp shift on 1e8 was lost (index 1, magnitude 0),
  and constant data gave rounding noise. They now work in offsets from the first value.
- **Chi-square and F at subnormal x:** x/2 and d1·x rounded, 8e-5 relative in the CDFs and densities. Below the
  normal range the leading term is taken in logs, or the F ratio is scaled up exactly.
- **ProcessCapability:** for σ past 3e307, 6σ overflowed (∞/∞ made Cp NaN) and so did 3σ (Cpk came out 0). Each is
  divided by σ first then.
- **Uniform:** UniformPdf(NaN) returned 1/(b − a), because NaN fails both range comparisons. The uniform quantile the
  docs promised was missing; it is `Distributions.UniformQuantile(p, a, b)`.
- **ChiSquareQuantile and FQuantile** returned an arbitrary subnormal when the true quantile is below the smallest
  double. They return 0, as the class documents for underflow.

### Round 86: Signal analysis, fifth pass (v2026.11.16.2)

Two fresh reviewers (correctness, robustness): 19 findings, two of them the same bug from both sides (the unscaled
power-of-two FFT, the parameter name on lengths past the FFT limit), so **17 bugs, none severe**. Section 2 stays
re-queued. The regression tests are in `SignalReview86Tests` (14 tests, every one failing on the old code, run by name
under a 60 s timeout). Three fixes have no test because they need arrays of 8 to 16 GB: the radix-2 loop's `len <<= 1`
overflow at 2^30 points, Welch's `start + step` int overflow on a huge record, and the ParamName of Hilbert, Multitaper
and Welch lengths past the FFT limit (they threw from inside Fft with its parameter name).

- **Fft:** the power-of-two path never scaled huge inputs down as Bluestein does, so [1e308, −1e308, 1e308, −1e308] had
  a NaN DC bin; it scales by 2^-512 (or up by 2^600 for subnormal data) and back. Scaling by a real factor multiplied
  as a complex product, so Inverse of (∞, 0) gave ∞ + NaN·i; every scale is now applied to the parts.
- **PulseMeasurements:** each high-reference crossing without a low one searched back to sample 0, so rise time over
  many partial edges was quadratic (400k of them took minutes); the backward search stops at the last one searched.
- **Quantizer:** a subnormal step was rounded (6ε for 6.25ε at 4 bits over 100ε) and so was every code; the top center
  passed max, and a range of a few ε was refused. The range is scaled by 2^600 then. A code near a bin edge was taken
  from a rounded quotient (an ulp below 0.25 took code 5 at 3 bits over [−1, 1]); near an edge it is settled exactly.
- **PhaseUnwrap:** delta + π was rounded before the floor, so a jump a few ulps below 5π landed below −π; the remainder
  is computed with the product's rounding error and decides the turn count itself.
- **MedianFilter and Hampel:** −0 and +0 compare equal, so a leaving −0 could remove a +0 still in the window and the
  median of {−1, +0, 1} came back −0; the sorted window orders −0 first.
- **Hilbert** didn't scale a subnormal signal up (two roundings: −2ε for −1.475ε), and its header named only Fft though
  it calls PhaseUnwrap; a test now checks every Signal file's header names the Signal types it uses.
- **EnvelopeFollower**'s overflow fallback Value + (g·level − g·Value) overflowed once g > 0.5. **Spectrum.Amplitude** of
  a (Max, Max) bin was ∞. **Window.CoherentGain** divided each entry first, so [ε, ε] had gain 0.
  **CycleMeasurements.Measure** returned early for under 3 samples before refusing NaN or ∞. **Resample** rippled a
  constant signal by an ulp (a·(1 − t) + b·t); it interpolates in difference form.

### Round 85: Numerics core, fifth pass (v2026.11.16.1)

Two fresh reviewers (correctness, robustness): 24 findings, two of them the same bug from both sides (TryParse of an
overflowing number, Normalized of a NaN vector), plus one found while testing a fix (the derivative over a span past
double.MaxValue), so **23 bugs, 1 severe** (C1). Section 4 stays re-queued. The regression tests are in
`NumericsCoreReview85Tests` (24 tests, every one failing on the old code, run by name under a 60 s timeout; the overflowing
parse failed only on .NET 8, where it gave infinity, and the exponent-form uncertainty text failed on both frameworks,
differently).

- **Severe: Polynomial.RealRoots** lost a double root of an exact integer cubic ((x−15)²(x−19) gave 19 twice): the Newton
  polish at a double root jumped to the other root. Polish now stops on any step larger than 1e-5 of the root. RealRoots
  throws InvalidOperationException on NaN or infinite coefficients (it threw ArithmeticException from Math.Sign or
  returned −∞), and Fit takes distinct subnormal x (the halved scale was 0) without turning 0·∞ into NaN.
- **Integration.AdaptiveSimpson** trusted the first five samples, so an integrand that aliased them (sin²(πx) on [0, 4])
  came back as 3e-31. It now always splits into at least 32 pieces (65 samples) before accepting an estimate.
- **MonotoneCubic and AkimaSpline** scaled x to keep a huge coordinate in range and flushed tiny ones to 0 beside it (NaN
  curve, wrong knot values); x is no longer scaled, widths are scaled per segment, and evaluation is in difference form,
  so flat segments come back exactly (they overshot by an ulp, against the no-overshoot guarantee); MonotoneCubic also
  clamps each segment to its knots' range.
- **Means.Arithmetic** could land outside [min, max] ({0.1, 0.1, 0.1} gave 0.10000000000000002); it is clamped.
  **MinMaxScaler** maps the data extremes exactly onto the target bounds and stays inside them, and refuses infinite or
  NaN target bounds.
- **UnitConvert:** degree, gradian and turn conversions are exact (90 deg was 99.99999999999999 grad); TryParse refuses
  a number that overflows to infinity on every framework.
- **Uncertainty:** Format's exponent form printed 17 or more digits with "F" (zeros after 15 on .NET Framework, binary
  noise on .NET 8); it now rounds the shortest round-trip digits in decimal. Round read the lead digits through a +1e-9
  fudge that moved values just under a PDG boundary into the wrong rule; it reads them exactly.
- **Differentiation** gave a nonzero derivative of constant data, and NaN at the ends of points spread past
  double.MaxValue; it uses difference forms and Newton divided differences, and halves such positions.
- **RootFinding:** Bisection's failure result (a+b)/2 overflowed near double.MaxValue; all four methods refuse
  maxIterations below 1 (ArgumentOutOfRangeException).
- **MovingAverage** stayed scaled by 2⁻³² after huge samples left the window, flushing tiny ones to 0. **ToleranceCheck**
  reported the first sample past double.MaxValue as the worst, not the furthest. **Vector2:** ApproximatelyEquals was
  false for identical infinite vectors; Normalized of a vector with a NaN component gave (1, 0), not NaN.
  **LinearAlgebra** threw IndexOutOfRangeException for arrays not indexed from zero (ArgumentException now).
  **ScatteredInterpolation** accepted non-finite query points and grid bounds.

### Round 84: Binary formats & images, fifth pass (v2026.11.16.0)

Two fresh reviewers (correctness, robustness): 10 findings with no overlap, so **10 bugs, 1 severe** (R1). Section 6
stays re-queued. The regression tests are in `BinaryImagesReview84Tests` (14 tests: 10 failed on the old code, run by
name under a 60 s timeout, one of them only on .NET Framework 4.8 where it differed; three are guards that pass on the
old code: clipped segments through and beside the rectangle, Base58 against a textbook conversion, and ancillary-chunk
CRCs while streaming). One finding was suspected from reading the code (Load of a file over 2 GB); it is fixed without a
test, since the test would need a 2 GB file.

- **Severe: PngReader.Decode(Stream)** copied the whole source before looking at it, so non-PNG input ("GIF89a" and
  zeros, or an endless stream) was read up to 2 GB (92 s, 4.3 GB allocated) before being rejected. Now the signature,
  IHDR first, each chunk's type letters and CRC are checked as they arrive; ancillary chunks are checked and skipped
  rather than kept, up to `PngReader.MaxAncillaryBytes` (256 MiB) in all; and the image data is refused once it passes
  twice what IHDR implies, plus 1 MiB.
- **PngReader memory:** the image plus 4.7 times the file (byte[]) or 9.5 times (Stream), from growing MemoryStreams and
  ToArray, against a doc saying the image plus the compressed data. The data is gathered into exact-size arrays (now
  under the image plus 1.5 times the file, 3.5 times from a stream) and the doc says so.
- **PngReader chunk rules:** tRNS in colour types 4 and 6, tRNS of the wrong length, more tRNS entries than PLTE, and a
  PLTE longer than the bit depth allows were accepted; they are rejected. Bytes after the zlib checksum are now named in
  the mismatch message.
- **Encoder length limits:** Base32, CrockfordBase32, Base85, Base64Url and QuotedPrintable threw OutOfMemoryException
  (after up to 80 s, some allocating 2 GB first) for output past the longest string. Each works out the exact length as
  a long and throws ArgumentException(data), as Hex does; Base64Url encodes in slices rather than one 2 GB string.
- **Base58** was about five times slower than it needed to be (100 KB took about 7 s): BigInteger divided by 58^10 as a
  long missed its one-limb fast path. It now works over 32-bit limbs, five digits per pass (about 1.5 s), and
  `Decode(text, maxLength)` refuses long untrusted text before any work.
- **ImageBuffer.DrawLineClipped** compared inexact edge crossings with exact ends: a segment touching the rectangle only
  at an endpoint on an edge drew nothing, one ending 5e-24 outside drew a pixel. Each end's side is decided exactly.
- **SvgUtils.EstimateTextWidth** counted a flag (two regional indicators) as two em, and its zero-width classes came from
  the framework's Unicode tables, so 440 code points measured differently on .NET Framework 4.8 and .NET 8. Zero width
  and the joined-symbol test now use fixed Unicode 15.0 tables.
- **Bmp.Decode** turned an out-of-range palette index into black and accepted a pixel offset inside the palette; both
  throw InvalidDataException. **PngReader.Load and Bmp.Load** of a file over 2 GB threw IOException; they throw
  InvalidDataException like Decode (PngReader streams such a file).

### Round 83: Filters & control, fifth pass (v2026.11.15.0)

Two fresh reviewers (correctness, robustness): 13 findings with no overlap, and one more found while testing a fix (the
section-scaling grid missed narrow resonances), so **14 bugs, none severe**. Section 1 stays re-queued (14 bugs). The
regression tests are in `FiltersControlReview83Tests` (12 tests: 11 failed on the old code, run by name under a 60 s
timeout; one guards the corrected doc, an infinite sample staying in the streaming state). Two findings were suspected
from reading the code (int overflow in FilterAligned's centre index and in DefaultPadLength, both needing arrays or
section counts too large to test); both are fixed by widening to long.

- **IirFilter section scaling:** every partial product of the cascade was aimed at the unscaled peak, so intermediate
  signals sat about 1/|K| times the output (1e140 for a 40th-order low-pass at 1e-4·fs) and a large input overflowed
  between sections. The peak grid also missed narrow resonances (a Chebyshev high-pass at 0.49·fs left a partial product
  24 dB above the output); it now adds points up to Nyquist and around each section's pole angle.
- **IirFilter overflow near double.MaxValue:** a finite input whose state would overflow (a constant 1e308 through a
  low-pass) left Process NaN for good; the streaming state is now held in units of a power of two and rescaled when that
  happens, and Filter and FiltFilt rerun at a smaller scale. FiltFilt's odd extension overflowed for full-scale swings.
  The Process doc now says an infinite or NaN sample stays in the state until Reset.
- **IirFilter.GroupDelay** next to zeros on the unit circle cancelled to nonsense (a high-pass near DC gave −8e15
  samples); there it is summed per root. **Filter and FiltFilt** flushed subnormal states from sample 0 when any later
  sample was large, so they were not causal; the flush starts at the first large sample, as in Process.
- **FirFilter:** the running sum passed double.MaxValue though the output fits (a constant 1.7e308 through a low-pass
  whose positive taps sum to 1.15); it reruns at a smaller scale.
- **PidController:** with P and D overflowing to opposite infinities the tentative output was NaN, neither windup test
  fired and the integral took the whole step past the limit.
- **Deadband and DeadbandFilter:** a distance that rounded to exactly the half-width read as inside (−1 about 1e17 is
  1e17 + 1 away); a tie is decided on the exact difference. **FirstOrderLag.FromCutoff** past about 2.9e307 gave a
  pass-through (2π·cutoff overflowed). **SchmittTrigger** blamed lowThreshold for a NaN highThreshold.
- **Test fix (WPF):** `NotifyTaskCompletion_ThrowingHandlerDoesNotStopTheRest` pumped the dispatcher twice, but since
  round 82 the task is awaited off the dispatcher and the raise posted back, so under load the post arrived later and
  the test failed in the full verify. It pumps until `Completion` (passed 30 runs in a row).

### Round 82: WPF satellite, fifth pass (v2026.11.14.3)

Two fresh reviewers (correctness, robustness): 21 findings, one pair overlapping, so **20 bugs, none severe**. Section
16 stays re-queued (20 bugs). The regression tests are in `WpfReview82Tests` (24 tests: 22 failed on the old code, run
by name under a 60 s timeout; two guard the new code, raising on the UI thread and the Messenger index not holding
recipients). Three findings were suspected from reading the code (the failure list's cost at its cap, a Command on a
ContentElement, the theme manager off a UI thread); all three were confirmed by their tests and fixed.

- **BindingDiagnostics:** starting a listener ran `PresentationTraceSources.Refresh()`, which runs `Trace.Refresh()`:
  every TraceSource and Switch in the app went back to its configured level and listeners added in code were dropped.
  The trace state is saved before and restored after. At the cap each failure shifted the whole list (a queue now).
  `BindingFailureException(null)` threw NullReferenceException.
- **IncrementalCollection:** a caller arriving while an abandoned load wound down waited for it with no regard for its
  own token; the last waiter to cancel follows the cancelled load to its end, the others stop waiting at once.
- **ValidationRules:** one throwing rule left every property after it with stale errors (SetAndValidate and
  ValidateAll run them all, then rethrow). A string Range compared in the current culture ("ch" sorts after "h" in
  Czech); it is ordinal. MinLength and MaxLength took a negative length. NumericRangeValidationRule let "1,5" through
  as 15 under en-US. RegexValidationRule's default pattern rejected multi-line text.
- **NotifyTaskCompletion:** completion resumed on the creating dispatcher, so one that had shut down never completed
  `Completion`; it now completes off the dispatcher and raises through it, skipped once it has shut down.
- **Messenger:** Send walked every recipient of every message type under the lock; an index per message type makes
  it cost only that type's recipients, in first-registration order.
- **FileDropBehavior:** Copy was shown when the source allowed only Move, and a drop reported every allowed effect,
  Move included (a source may then delete its originals). Clearing the command made AllowDrop local, so the element
  stopped inheriting it. A Command on a ContentElement or UIElement3D got no handlers yet made the outer target step
  aside. With no filter the source's own array, null entries and all, went to the command.
- **NumericInputBehavior, PlotPresenter, ValidationObservableObject, ValueConverters, ThemeManager:** a paste was
  checked whole though MaxLength truncates it ("1." into "5" became 15 past a maximum of 9). MaxZoom below MinZoom
  is read as MinZoom (either order in XAML); ZoomAbout rejects NaN and crossed bounds with ParamName. SetErrors took
  a null error. CountToVisibility showed `false`. ThemeManager on an MTA thread made a dispatcher nothing pumped, so
  following the system theme silently did nothing; it uses the application's, or throws.

### Round 81: Concurrency, fifth pass (v2026.11.14.2)

Two fresh reviewers (correctness, robustness): 17 findings, of which two pairs overlapped, so **15 bugs, 1 severe**
(R1). Section 10 stays re-queued. The regression tests are in `ConcurrencyReview81Tests` (16 tests: 15 failed on the
old code, run by name under a 60 s timeout, and one guards the new line splitting). One older test called the private
`OnLine` by reflection and now calls `Capture`. Two suspected findings (a failed kill reported as done, reader threads
held by a grandchild after a timeout) are in BUGLIST.md.

- **ProcessRunner (severe):** a stdin encoder that throws (a strict UTF-8 meeting a lone surrogate) threw on the writer
  thread and took the host down; the input is encoded before the child starts, so the caller gets the exception.
- **ProcessRunner:** with no timeout, a grandchild streaming text with no line break looked quiet, so the run ended
  early and the unfinished line was lost; the readers take text in chunks, count any text as arriving, and keep a
  partial line (split on \r, \n and \r\n as ReadLine does). EnsureSuccess formatted the exit code in the current
  culture. On .NET Framework under a UTF-8 console the child's stdin starts with a BOM; netstandard2.0 cannot avoid it,
  so it is documented.
- **AsyncEvent:** InvokeParallelAsync turned the caller's cancel into an AggregateException of TaskCanceledExceptions;
  it throws OperationCanceledException, or, with real failures too, an AggregateException holding both. A handler's
  own OperationCanceledException was replaced by a new TaskCanceledException. With no handlers and a cancelled token
  the parallel form threw and the sequential one returned; both throw.
- **TaskExtensions, TaskRace:** WhenAllOrFirstException never observed the faults after the first, and Hedged did not
  observe an attempt that faulted as the caller cancelled (UnobservedTaskException). Hedged left one timer behind per
  failed attempt until the whole race ended.
- **Retry, ConcurrentPipeline:** a DelayAsync returning null threw NullReferenceException and lost the operation's
  error; it throws InvalidOperationException with that error inside. A producer blocked by the Block policy when the
  pipeline was disposed got OperationCanceledException; it gets ObjectDisposedException, as documented.
- **ArrayPoolScope, WhichExe, Jitter:** clearOnReturn did nothing on .NET 8 for an array longer than the pool's largest
  bucket (the pool drops it uncleared); the scope clears it itself. With PATHEXT unset the fallback order was
  .EXE;.CMD;.BAT;.COM, not cmd's .COM;.EXE;.BAT;.CMD. Equal jitter rounded half the delay down, so an odd tick count
  fell below delay/2.

### Round 80: Charts, fifth pass (v2026.11.14.1)

Two fresh reviewers (correctness, robustness): 24 findings, of which three pairs overlapped, so **21 bugs, none
severe**. Section 5 stays re-queued. The regression tests are in `ChartsReview80Tests` (24 tests). Each failed on the
old code, run by name under a 60 s timeout; the hexbin count test calls the new private increment by reflection (2^31
points take minutes). Two older tests encoded the old behaviour and were updated: a tight 0.13..0.97 axis now gets
0.2, 0.4, 0.6, 0.8 (the smallest nice step within the limit, as documented) instead of only 0.5, and a clock whose
period is below its start time's resolution throws instead of returning collapsed edges.

- **AxisTicks:** tight mode chose its step by the bracketing tick count, so [3.4, 6.68] with two ticks got none; it
  counts the ticks inside the range. A tight range under 1e-250 was widened to ±1e-250 and returned with ticks outside
  the request (non-positive ones on a log axis); tight mode keeps the caller's bounds and in-range ticks (the ends
  themselves when none fit), and only spans under 1e-300 are opened. Exponent labels used 15 significant digits, so
  ticks of 1 + k·1e-15 all printed "1E+0"; labels are built from the tick's exact decimal.
- **SvgChart, MultiPanel:** a non-positive log limit named "explicitMin" (a private parameter), and reversed explicit
  limits were swapped without a word; both are rejected as "options". Sharing an axis across panels of different kinds
  wrote a linear range into a log panel; it is rejected as "panels".
- **LinePlot, Histogram, PlotFrame:** non-finite LinePlot limits named "xMin" or silently became [0, 1]; Histogram.Render
  named "min"/"max" for bad options; all are "options". A PlotFrame whose left + width or top + height overflowed to
  ∞ was accepted; it throws.
- **HexBin:** a cell's count wrapped past int.MaxValue points; it throws ArgumentException naming points.
- **Waterfall:** infinite Width, Height or FontSize and tick limits under 2 failed deep inside with foreign names;
  reversed ValueMin/ValueMax were swapped; a step below the start's resolution (1e17 with step 1) collapsed the axes and
  labelled a span the data do not have. All throw, naming options. The flat colour-bar label used "G4" (1.235E+04, and
  "-0" on .NET 8 only); it is round-trip and unsigned at zero.
- **TimingDiagram:** Clock took an infinite period (an empty clock) and, at a start far above the period, rounded edges
  onto each other (800 of 20000 kept); both throw naming period. FromSamples took an infinite rate or a start time
  that collapsed the sample times, and reported a NaN low threshold as the high one. Segments took NaN or reversed
  windows. An equal-time replacement could leave a "change" to the same value in Changes; it is dropped.
- **ScatterMatrix:** a null name or a null or empty palette failed with foreign names; Pearson ignored a length
  mismatch; a small negative r printed "r = -0.00" (.NET 8 only); ticks on a panel edge were labelled, so neighbouring
  panels printed two "0" labels 8 px apart, and only interior ticks are labelled now.
- **Annotations, BoxPlot, BandChart:** a callout's label side was re-decided after the clamp, so the label ran off the
  plot; it is decided once. BoxPlot's weighted interpolation could put Q1 above Q3 for neighbouring values (2574 of
  200000 near-equal datasets); quartiles use a + f·(b − a), clamped to the pair. BandChart never drew a lone centre
  sample; it is a dot, as in LinePlot.

### Round 79: Windows satellite, fifth pass (v2026.11.14.0)

Two fresh reviewers (correctness, robustness): 14 distinct findings, all real, plus one from a reviewer's unprobed
note that was checked and confirmed (the clipboard text bound), so **15 bugs, none severe**. Section 15 stays
re-queued. The regression tests are in `WindowsReview79Tests` (17 tests). Each failed on the old code, run by name
under a 60 s timeout; the stalled-primary test is a guard for the synchronous forward and fails at HEAD only because
the old gate name was case-sensitive. Three older tests built the gate name by hand and now upper-case the app id.

- **SingleInstanceApp:** the gate mutex name kept the app id's case while the pipe name ignores it, so "App" and "app"
  both became primary and the second's launches went to the first; the gate name now upper-cases the id. Launches
  queued behind a slow handler had no bound (only the before-subscribe queue did), so a stuck handler held every
  acknowledged launch; at most 100 wait, and the rest are refused (ForwardedToPrimary false). The synchronous forward
  blocked on async pipe reads and writes (GetAwaiter().GetResult()); it now uses synchronous I/O cancelled by a timer
  (CancelIoEx) at the deadline.
- **SingleInstance:** a bare namespace prefix (`Global\`) threw IOException and a session that does not exist threw
  DirectoryNotFoundException; both are ArgumentException naming the name.
- **ScreenCapture, DpiInfo:** ScreenImage named width for a bad height. DpiInfo.Scale with a DPI of 0 or less returned
  0 or a negative size; it throws. MonitorDpi.ToString used the current culture (tr-TR printed "%200").
- **ResourceSampler:** Sample after Dispose kept sampling a caller's process (and an owned one threw the
  process-exited error); it throws ObjectDisposedException, and Dispose is idempotent.
- **DriveAndVolumeInfo:** GetSpace passed a NUL to the API, which ended the path there (C:\ + NUL + junk reported C:),
  and an empty or blank path; both throw ArgumentException. A long `\\.\` device path was rewritten to the UNC form and
  failed.
- **EventLogWriter:** a NUL in a source or log name opened the name before it (Application + NUL + junk was reported
  registered); names with a NUL or backslash are rejected, and null is ArgumentNullException, as elsewhere in the
  satellite. A NUL in a message cut the event text short; it is written as U+2400.
- **ShortcutUtils:** a NUL in any path or field cut it short (writing a shortcut at the cut path), an empty target wrote
  a shortcut to nothing and an empty path threw DirectoryNotFoundException; all are ArgumentException. A corrupt .lnk
  threw a bare COMException (E_FAIL); it is InvalidDataException.
- **PrivilegeScope, FileAssociation:** a NUL in a privilege name or extension answered for the name before it.
- **ClipboardUtils:** clipboard text was bounded by int.MaxValue characters, not the string limit (0x3FFFFFDF), so a
  2.1-4.3 GB block threw OutOfMemoryException; the text's length is found first (up to the NUL) and bounded by the
  real limit.

### Round 78: Logging, diagnostics, CLI, fifth pass (v2026.11.13.21)

Two fresh reviewers (correctness, robustness): 19 distinct findings (two found by both), all real, so **19 bugs, none
severe**. Section 12 stays re-queued. The regression tests are in `LoggingDiagnosticsCliReview78Tests` (22 tests).
Each fix's test failed on the old code, run by name under a 60 s timeout; one is a differential guard that also passes
at HEAD (the one-pass delimiter removal against repeated Replace over random values). One older test pinned the old
form of a compound ANSI reset and was updated.

- **Logging:** ScopedContext tokens disposed out of order brought back a disposed scope (push A, push B, dispose A,
  dispose B left A current); only the innermost live scope now pops, to its nearest live ancestor. Each scope stored
  the whole combined path, so 20,000 nested scopes held 1.9 GB; the path is now built on first read. The Compact
  formatter read an exception's Message unguarded, so a Message that throws took the console sink down.
  StructuredTextFormatter removed delimiters by repeating Replace until none was left, quadratic for a multi-character
  delimiter (a 400,000-character value took 40 s); it is one pass now. FilterSink, RouterSink and RateLimitedSink
  accepted events after Dispose (RateLimitedSink counted repeats it would never summarize); they throw
  ObjectDisposedException, as the other sinks do, and RateLimitedSink no longer disposes its inner sink twice.
  FileSink's cleanup of extra backups took the primary file for a backup when the search matched it, and threw
  ArgumentOutOfRangeException. AsyncLogSink disposed from its own consumer thread lost a failing inner Dispose as an
  unobserved task exception; it goes to onError. SyslogSink over UDP reset its back-off when a lookup failed and it
  fell back to the last good address, so the back-off never grew.
- **Diagnostics:** MetricsRegistry's mean under the sample cap, when the sum overflowed, added v/n in sorted order and
  lost the small terms (Max, Max, -Max, -Max, 6 gave 0, and 1.2 past the cap); both paths now use the scaled sum.
  FirstChanceMonitor dropped an exception whose Message throws, uncounted and unreported, and its record's ToString
  threw; the per-type "(other messages)" signatures were not counted toward the cap, so many exception types grew the
  table without bound (past the cap, every type now shares one signature). HealthCheck faulted the whole run, losing
  every probe's result, when an exception's Message threw; an async probe that blocks before its first await took one
  more pool thread on every run (40 after 40 runs); like a synchronous probe, it is not started again while the last
  start is still running. Benchmark.Compare reported every candidate as 1.0 against a baseline that measured 0 ns
  (now infinity, or 1.0 for another 0). EnvironmentReport merged case-variant variable names (PATH and path) off
  Windows.
- **CLI:** AnsiStyle put its style after a compound reset with colour (ESC[0;32m), so the outer colour overrode the
  text's own 32; it now emits the reset, the style, then the rest. ConsoleTable left NEL, LS, PS, VT and FF in cells,
  which broke rows on screen. ConsoleSpinner counted every astral character as two columns (U+1D400, a narrow letter,
  too), BMP emoji (U+2705, U+26A1) as one, Hangul medial and final jamo and skin-tone modifiers as columns of their
  own. ExitException with a null message printed the BCL's default "Exception of type ... was thrown."

### Round 77: Objects, functional, grids, graphs, intervals, fifth pass (v2026.11.13.20)

Two fresh reviewers (correctness, robustness): 29 distinct findings, all confirmed, so **29 bugs, 1 severe** (a
pending Task property hung every graph walker). Section 14 stays re-queued. The regression tests are in
`ObjectsGraphsReview77Tests` (35 tests). Each fix's test failed on the old code; the "-0" text and random double keys
fail only on .NET Framework 4.8, and were proven there. Four tests are differential guards that also pass at HEAD
(A* against Dijkstra, FloodFill and RangeMap against naive references, RangeMap's enumeration check).

- **Graph walkers (DeepEquals, PropertyDiff, FlattenObject):** a Task, ValueTask, Lazy, Stream, TextReader,
  TextWriter, WaitHandle, CancellationToken, Thread or delegate was walked: a pending Task's Result blocked forever
  (severe), a Lazy ran its factory, a Stream's ReadTimeout threw. They are now leaves, compared with their own Equals.
  A lazily cached endless chain (`Next => _next ??= new Node()`) and an endless lazy sequence never ended; past
  2,097,152 objects or 4,194,304 items a walk now throws InvalidOperationException. A chain whose type grows each step
  (`W<T>.Next => new W<W<T>>()`) overflowed the stack; types now repeat by generic definition. A dictionary that has
  only the generic interfaces (ExpandoObject) was walked as a sequence of pairs, so order mattered; it now compares,
  diffs and flattens by key. Dictionary keys printed in a form that did not round-trip (a DateTime lost its
  milliseconds, so two keys collided; a double lost digits on .NET Framework, and -0.0 printed as "0"); keys now use
  round-trip text that PropertyPath reads back. With fields included, a `new` field hiding a base property (or the
  reverse) was walked as two members of one name, so Flatten threw; the most derived one now wins. A set comparer's
  exception came out wrapped in TargetInvocationException.
- **PropertyDiff:** **Behaviour change:** a pair of objects reached along several paths is reported once, at the
  first path; a diamond chain 40 levels deep took exponential time. Difference.ToString was culture-sensitive.
- **PropertyPath:** `D[1,5]` read key 15 of a double or decimal dictionary (thousands separators were allowed). TimeSpan,
  DateTimeOffset, Version and Uri keys did not convert. An array with a nonzero lower bound was indexed from 0. An
  indexer's own exception was swallowed by TryGet. An out-of-range index named "index" rather than "path".
  **Behaviour change:** an empty segment (`a..b`, `.a`, `a.`, `a.[0]`) is now ArgumentException; it used to be read
  as a member with an empty name.
- **TypeUtils:** **Behaviour change:** TryConvert no longer loses information silently. A double went to decimal
  rounded to 15 digits (0.1 + 0.2 became 0.3) and 1e-30 became 0; an integer went to double or float inexactly (2^53 +
  1); a nonzero double or text ("1e-400") underflowed to zero. All of these now fail. A date converted to string lost
  its fraction of a second (it now uses "O"). Text "-0" lost its sign on .NET Framework. Half could not convert.
  FriendlyName printed a rank-1 multi-dimensional array (`int[*]`) as `int[]`.
- **Smaller:** ExpressionAccessor's typed setter threw on a null `int?` into an `int` member (it now stores 0), and
  picked a base field over a derived property. ActivatorUtils chose a ValueType constructor over `int?` for an int.
  Edge, Option, Result and Either printed 1.5 as "1,5" under a comma culture. Result.Try threw on an exception whose
  Message is null. Memoize recursed until the stack overflowed when a function called itself with the argument it was
  computing; it now throws InvalidOperationException, as Lazy does.
- **Grids and intervals:** Dijkstra treated a diagonal step whose cost overflowed to infinity as a wall. A* expanded
  nearly the whole grid on an open map: equal priorities were not broken by the heuristic, and 8-connected sums of
  sqrt(2) differed by rounding noise; ties now go to the cell nearer the goal, within a relative 1e-12. FloodFill on a
  T[,] over 2^24 cells switched to a HashSet (1.4 GB for a 4097 x 4097 grid); grids now always use a bitmap.
  RangeMap inserted into a flat list, so 300,000 descending inserts took 14 s; it now uses blocks of entries and
  stays near-linear.

### Round 76: Documents & config, fifth pass (v2026.11.13.19)

Two fresh reviewers (correctness, robustness): 24 distinct findings, all confirmed (four of them by reading the code
rather than a probe), so **24 bugs, none severe**. Section 7 stays re-queued. The regression tests are in
`DocumentsConfigReview5Tests` (25 tests), and each failed on the old code. The WithPrefix collision can only happen on
Unix, so its test drives a new internal overload; at HEAD it fails only because that overload is missing. One fix has
no failing test: SettingsStore's guarded temp-file delete, since no delete could be made to fail after a failed save.

- **SettingsStore:** a type System.Text.Json cannot load at all (an abstract property type, a ConcurrentBag) made
  LoadFrom move a valid file aside as corrupt on every launch. It now throws. Get-only queues, stacks, concurrent
  collections and lists declared as `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` or
  `IReadOnlyDictionary<K,V>` were saved but never loaded, so the next save erased them. They are now refilled from
  the object the property holds. A stack came back reversed on every load. A property with a private setter was saved
  but never loaded; it now loads through that setter. Update's rollback could restore a lossy copy, and a restore that
  threw replaced the caller's exception. Now the copy is used only if it saves exactly as before; otherwise Value is
  left alone and saving is refused until Reset, and the caller's exception always propagates. Update ran the change
  before refusing to save an unreadable file, and a failed save left the change in place; Reset replaced Value before
  its write could fail. The backup name of a corrupt file could pass 255 characters (or UTF-8 bytes), so the file
  could not be moved aside. **Behaviour change:** a string holding a lone surrogate now makes Save throw
  ArgumentException; it used to be written as U+FFFD. A save now keeps the Hidden and System attributes, fails at once
  on a read-only file instead of retrying for half a second, removes temp files a crashed save left more than an hour
  ago, and no longer lets a failed temp delete hide the real error.
- **ConfigBinder:** a section's own value beside its keys ("Server=localhost" with "Server:Port") and a list's whole
  value beside its indexed keys were dropped without a word; both are now errors. **Behaviour change:** a property
  with a private setter is read-only, so configuring it is an error (it used to be set). **Behaviour change:** a null
  value is unset and keeps the default, for every type; it used to clear a string and be a conversion error for an
  int.
- **EnvironmentVariables:** Get(null) now throws ArgumentNullException. RequireAll reports a null or empty element
  as "names", not "name". **Behaviour change:** "y" and "n" are no longer booleans; they were never documented and
  ConfigBinder refuses them. On Unix, WithPrefix let `APP_x` and `app_x` strip to one key and silently kept one of
  them; that now throws InvalidOperationException naming both.
- **PdfWriter:** an indented first word was never width-checked, so the line could run off the page; the indent now
  shrinks to fit, and a hard-split first word keeps it. An image scaled to the full usable height could leave the
  first page blank. A code block's background bar could cross the bottom margin. Corrupt compressed PNG data threw
  InvalidDataException; it is now an ArgumentException naming "png".
- **Smaller:** MarkdownReport's Code, Link text and Image alt text kept line breaks (CR, LF, U+2028, U+2029), so a
  blank line ended the paragraph and left literal backticks or brackets behind. They are now spaces, as in Bold and
  Italic. ReportBuilder's Markdown output gave an all-punctuation heading the anchor "section"; GitHub gives it "",
  so the link pointed nowhere and a real "Section" heading got the wrong number. HTML keeps "section", since an id
  can't be empty. DocxWriter's image alt text lost its tabs and line breaks to XML attribute normalization.

### Round 75: IO, fifth pass (v2026.11.13.18)

Two fresh reviewers (correctness, robustness): 26 findings, 24 confirmed, so **24 bugs, 1 severe**. Section 8 stays
re-queued. The regression tests are in `IOReview75Tests` (23 tests). Each failed on the old code: on .NET 8, or on
.NET Framework for the ByteSize carry, the CsvBinder overflow, DosMatch and IsLink. The exception is the TarUtils
backslash test, which can only fail on Unix. Three fixes have no failing test. The DotEnv doc now lists `\r`. ZipUtils
now replaces the target instead of deleting it and then moving: a move that failed after the delete lost the only
copy, but no move could be made to fail between those two steps. The FixedWidth limit is tested only for the
exception, since the old code's failure was an OutOfMemoryException. Refuted: FileWatcher.Start leaving the OS watcher
running when the first snapshot throws, because the snapshot tolerates IO errors and cannot throw them. Also refuted:
a PAX header followed by a GNU long-name header slipping past the tar guard (TarReader refuses that order).

- **TempDirectory (severe):** Dispose used the BCL's recursive delete, which overflowed the stack on a deep tree and
  so ended the process (past about 4,500 levels on a 1 MB stack, or 1,500 on 64 KB). The delete is now iterative,
  bottom-up, clears ReadOnly as it goes and removes a link without following it.
- **Deep trees:** DirectoryUtils.Delete and Copy checked every ancestor for a link swap on every item, so the cost
  was files x depth: 40 s to delete a chain 800 deep. Each folder now checks only itself; Delete goes back to checking
  every ancestor once a retry has waited. The netstandard DosMatch was recursive and ended the process on a pattern
  of 30,000 '*'; it is now an iterative DP with the same rules as .NET Core. That also fixes "*." and "a?" not
  matching "a.".
- **Memory:** ZipUtils.AddOrReplaceEntry rewrote the archive in Update mode, which holds every entry in memory: 400 MB
  to add 3 bytes to a 400 MB zip. It now streams each entry across, keeping stored entries stored, and their times
  and attributes. An entry this runtime cannot read falls back to the old in-memory rewrite. StreamUtils.ReadExactly
  allocated the whole count before reading anything, so a 4-byte stream claiming 2 GB cost 2 GB. It now grows with
  the data, and a count above 0x7FFFFFC7 throws ArgumentOutOfRangeException.
- **CsvBinder:** float, double and decimal now parse invariantly with no thousands separators. **Behaviour change:**
  "1,5" was read as 15 and is now refused. A float too large for its type is now ±Infinity on .NET Framework too
  (it used to throw there). **Behaviour change:** a DateTimeOffset without an offset is now UTC, not the machine's
  offset.
- **FileWatcher:** a name ending in a dot or a space lost its deletions and ignored StableFor, because the snapshot
  normalized the path. Such names are now kept as listed.
- **Names and paths:** FileRetention's '?' did not match what the listing matched before a dot or at the end of a
  name. It now follows the BCL's Win32 rules exactly. **Behaviour change:** TempFile refuses an extension holding ':',
  a separator or an invalid character, which could escape the temp folder or create an alternate data stream.
  ChecksumManifest refuses "./", which it used to write as a line its own Parse rejected. SafeFileName kept a lone
  surrogate, and a multi-byte replacement could break its UTF-8 byte cap. TarUtils on Unix turned a backslash in an
  entry name into a directory separator.
- **Smaller:** JsonFile.TryLoad swallowed the unsupported-type error that its doc says propagates. **Behaviour
  change:** ByteSize.Format's decimals is now 0 to 99; above that it printed garbage on .NET Framework, and there it
  could also print "1024.00 TiB" instead of carrying. LineReader's default limit (0x7FFFFFC7) could never be reached,
  so it is now 0x3FFFFFDF, the longest string, and a larger limit throws. A partial BOM's bytes also skipped the limit
  check. FixedWidth.Format threw OutOfMemoryException for widths too long for a string; it now throws
  ArgumentOutOfRangeException. The netstandard IsLink took every reparse point for a link, so .NET Framework skipped
  OneDrive placeholders. It now reads the reparse tag. **New:** `CopyOptions.CancellationToken`, checked before each
  file.

### Round 74: Text & small types, fourth pass (v2026.11.13.17)

Two fresh reviewers (correctness, robustness): 21 findings, 20 confirmed, so **20 bugs, none severe**. Section 11 stays
re-queued, and with it the fourth pass is done: every section is re-queued for a fifth. The regression tests are in
`TextReview4Tests` (28 tests); 24 failed on the old code, and the other four are differential guards for the rewritten
matchers (glob against a regex, n-grams against substrings, Jaro against the window scan, the diff against an LCS
table). Three doc fixes (AnsiText.VisibleLength, TemplateFormatter, Levenshtein's time) have no test. Not changed:
Guard.InRange with min > max names "max", Guard's own parameter, as .NET does; blaming the caller's value was
fixed in an earlier round.

- **SnowflakeIdGenerator:** past 4,096 ids in a millisecond it borrowed the next one without waiting, so the timestamp
  ran ahead of the clock without limit (2M ids put it 310 ms ahead) and a restarted generator re-issued ids.
  **Behaviour change:** it now waits for the clock (yielding, not sleeping) and throws InvalidOperationException if the
  clock does not move for a second.
- **Quadratic and memory blow-ups:** GlobMatcher backtracked in O(n·m) ("*" + 20,000 'a's + "b" against 200,000 'a's
  took 14 s; now a bit-parallel scan); NGramSimilarity built a substring per gram (now a rolling hash); Jaro scanned
  the whole window per character (now O(n + m)); TextDiff kept an int per table cell (now one bit, and an
  ArgumentException past 2^33 cells, about 92,000 lines each).
- **GlobMatcher:** case-insensitive matching was one-sided ('ſ' matched "S" but not "s"; now the transitive closure
  of the runtime's invariant mappings); a reversed range such as `[z-a]` was swapped into `[a-z]` (**behaviour
  change:** now empty, as in POSIX); `[]-a]` was read as three members rather than the range ']' to 'a'.
- **CaseConverter:** "A1B" split as A1 | B, so CONSTANT_CASE did not read back (a capital after a digit now starts a
  word only when a lower-case letter follows); a word starting with 'ß', and titlecase letters, broke idempotence; in
  decomposed text the accent hid the lower-case letter that ends an acronym ("XMLÉcole").
- **Widths and escapes:** TextAlign counted conjoining jamo (a decomposed "한" was 4), missed Tangut, Khitan, Nushu and
  the kana supplements (now 2) and counted "❤️" (emoji selector) as 1. AnsiText.Strip and TextAlign took a control
  character inside an escape as a parameter: they now read sequences as a VT terminal does (the control character
  is kept, CAN, SUB and non-ASCII characters end the sequence).
- **Smaller:** Slug dropped Ħ, ŧ, ŋ, ĸ, Ĳ, ŀ, ŉ, ǆ and other Latin letters (now FormKD plus a fold table generated
  from the Unicode names); a ULID random-part overflow carried into the timestamp (now InvalidOperationException, as
  the spec says); NanoId turned a broken injected Random into IndexOutOfRangeException; CommandLineBuilder.Join named
  "argument" for a null element; a lone surrogate was accepted as TextAlign's pad and Mask's mask character.

### Round 73: WPF satellite, fourth pass (v2026.11.13.16)

Two fresh reviewers (correctness, robustness): 24 findings, 22 after merging the two both found, plus two found while
fixing (ResetAsync's generation overflowing in a checked build, and a double-dispose race in BindingFailureListener),
all confirmed, so **24 bugs, 1 severe**. Section 16 stays re-queued. The regression tests are in `WpfReview4Tests` (23
tests). 22 failed on the old code (the generation one only in an overflow-checked build). The concurrent-dispose stress
test did not reproduce the race in 500 runs, so that fix is unproven by test. Coerce's range doc (wording only) has no
test. Nothing is left open.

- **NumericInputBehavior (severe):** a box whose Text was bound took the user's culture (on a German machine "1,5"),
  but the binding converted with WPF's en-US default Language, so the value became 15. A bound box now uses the
  binding's ConverterCulture, or else its Language even when that is the default; an unbound box is unchanged.
  **Behaviour change:** set `xml:lang` (or ConverterCulture) for local separators in bound boxes. Also: Insert turned on
  overtype mode, which replaced characters the checks never saw (now blocked); dropped text went in unchecked (text
  drops are now refused; files are left alone); a paste from a source that advertised text and failed to render it
  threw a COMException; a culture with an empty minus sign refused every keystroke; stepping by 1 from 2^53 never moved.
- **FileDropBehavior:** with nested drop targets the outer one handled the tunnelling Preview events first, so the
  inner one never saw the drop or refused files it accepts. The innermost target now decides.
- **Validation:** GetErrors(null or "") returned every property's errors; INotifyDataErrorInfo says that means
  object-level errors. **Behaviour change:** they now return only the "" bucket, and the new `GetAllErrors()` returns
  everything (the existing GetErrors test was updated). A RegexValidationRule or Matches pattern such as "a)|(b" escaped the
  whole-value anchor; it is now refused. A pattern ending in a (?x) comment was refused as invalid.
- **ThemeManager:** an undefined AppTheme was applied as Light (now ArgumentOutOfRangeException). A theme registered
  by Uri that App.xaml had already merged was merged a second time, so the first copy's keys outlived the switch.
- **GlobalHotkeys:** Key.LineFeed parsed but could not register (no virtual key). Ids ran past 0xBFFF, out of the
  application range. Unregister and Dispose after the dispatcher shut down called UnregisterHotKey(NULL, id), which
  could remove another hotkey of the thread.
- **IncrementalCollection:** a caller joining a shared load while the last waiter's cancellation was in flight was
  cancelled without asking; a loader's own OperationCanceledException (a timeout) was reported as a cancellation, not
  a failure; a CollectionChanged handler that threw during ResetAsync left IsLoading stuck and the old load running;
  the generation counter overflowed in a checked build.
- **Smaller:** AsyncRelayCommand's null task threw NullReferenceException (now InvalidOperationException); the dialog
  service's default owner threw on a secondary UI thread (now no owner); BindingFailureListener dropped TraceData events
  (all critical ones, and warnings), kept every failure and unfinished Write for its whole life (new `MaxFailures`,
  default 10,000, and `DroppedFailures`; unfinished text is capped at 64K chars), and a double dispose on two threads
  restored the trace level too early.

### Round 72: Concurrency, fourth pass (v2026.11.13.15)

Two fresh reviewers (correctness, robustness): 26 findings, 24 after merging the two both found, all confirmed, so
**24 bugs, 1 severe**. Section 10 stays re-queued. The regression tests are in `ConcurrencyReview4Tests` (26 tests);
all failed on the old code. Two fixes are wording only (the Task.Delay limit in PeriodicWorker and Hedged, and
ArrayPoolScope's disposal doc) and have no test. Nothing is left open.

- **ProcessRunner (severe):** after a decoding failure (a strict encoding meeting a bad byte) the reader stopped and left
  the pipe full, so a child writing more than a pipe buffer blocked until the timeout, or forever with none; the pipe is
  now drained. With no timeout, a grandchild holding the inherited pipes kept the run waiting for it; the run now reads
  until the pipes close or go quiet for a second. Lines read after the run returned were still buffered. A child that
  exited by itself as the timeout fired was reported with exit code -1. An empty file name threw
  InvalidOperationException, and an environment name holding '=' became another variable (names with '=' or NUL, and
  values with NUL, now throw).
- **Cancellation:** ConcurrentPipeline's Dispose still handed every buffered item to a consumer that ignored the token;
  TaskRace with an already-cancelled token could succeed, a cancelled FirstMatching reported "no acceptable result" and a
  cancelled FirstToComplete threw the attempt's failure; AsyncEvent's parallel invoke ran every handler on a cancelled
  token, and the sequential one reported a handler that honoured the cancellation as a failure too; WaitAsync on a set
  event ignored a cancelled token (the BCL events don't).
- **Exceptions:** WithTimeout and WithCancellation left the abandoned task's later fault unobserved; WhenAllOrFirstException
  and FireAndForget unwrapped with GetBaseException, not as await does; ParallelUtils dropped the bodies' failures when
  the source threw (it now throws an AggregateException holding the source's exception first; round 54's test, which
  expected the source's alone, was updated); AsyncLazy cached a null task; WithTimeout took small negative timeouts (−0.5 ms timed out at once,
  −1.5 ms never).
- **Numbers:** Retry's delay went through milliseconds and lost a tick; the token bucket's rounding tolerance grew with
  the count (int.MaxValue was granted two tokens short); CircuitBreaker's failure count went negative past int.MaxValue
  and its generation threw in an overflow-checked build; AtomicLong and AtomicDouble formatted in the current culture.
- **Smaller:** WhichExe returned a relative path for a relative PATH entry.

### Round 71: Collections, fourth pass (v2026.11.13.14)

Two fresh reviewers (correctness, robustness): 20 findings, 19 after merging the one both found, all confirmed, so
**19 bugs, none severe**. Section 9 stays re-queued (≥ 3 bugs). The regression tests are in `CollectionsReview4Tests`
(23 tests); all failed on the old code, the CircularBuffer, Deque and Fenwick ones against the overflow-checked build.
TopN's growth cap needs a 2 GB heap, so it was confirmed by a probe. Nothing is left open.

- **Bloom filters:** double hashing `(h1 + i·h2) mod m` cycled in small or even-sized filters (one item at a 1% target
  gave 25% false positives); each probe now goes through the murmur3 finalizer. The string overloads turned every lone
  surrogate into U+FFFD, so "key" + U+D800 and "key" + U+DBFF were one item (and removing one removed the other); lone
  surrogates are now encoded as their own three bytes (WTF-8), well-formed strings as UTF-8.
- **Aggregation.Summarize:** after rescaling an overflowed sum it kept the ∞ sum ([1e308, 1e308, −1e308]), and squared
  deviations of tiny values underflowed (1e-200 and 2e-200 had a deviation of 0). Values below 2^-400 are now scaled too,
  in two halves.
- **Counters past int:** BitSet.Count wrapped at 2^31 set bits and MultiDictionary.Total past int.MaxValue values (both
  are now long); CircularBuffer and Deque version counters and FenwickTree sums threw in an overflow-checked build.
- **Sizes past Array.MaxLength** threw OutOfMemoryException in CircularBuffer, Deque, CountMinSketch,
  CountingBloomFilter and TopN growth; they are now argument or operation errors.
- **DataTableLite:** strings sorted by the culture cached in Comparer.Default (now ordinal); a caller's comparer was
  handed null cells; AddRow read an infinite sequence forever; InnerJoin's column check was quadratic.
- **Smaller:** NearestBy returned the last of a run of equal keys below the key, and checked the key before a null list;
  weighted sampling without replacement tied weights that underflowed against a 1e308 neighbour (keys are now in log
  space); SequenceEqualityComparer passed null elements to the element comparer, so equal sequences could hash
  differently; BitSet enumeration missed a modification; Trie.Remove allocated the whole path for an absent key.

### Round 70: Signal analysis, fourth pass (v2026.11.13.13)

Two fresh reviewers (correctness, robustness): 30 findings, 22 after merging the eight both found, all confirmed, plus a
Quantizer reconstruction overflow found while fixing, so **23 bugs, none severe**. Section 2 stays re-queued (≥ 3 bugs).
The regression tests are in `SignalReview4Tests` (26 tests). All 24 regression tests failed on the old code on .NET 8;
the other two check the new sliding Median and Hampel windows against naive references. Nothing is left open.

- **Overflow and underflow for representable answers:** Goertzel (1000 samples of 1e303 at DC gave NaN), WelchPsd and
  Multitaper (amplitude 1e155 at 1e10 Hz gave ∞, 1e-170 at 1e-100 Hz gave 0), Savitzky-Golay edge rows (a 1.7e308
  constant gave ∞), Convolve ([1e200, 1e200] ∗ [1e200, −1e200] gave NaN in the middle), cycle statistics at extreme
  rates (a standard deviation of 0 at 1e200 Hz), GroupDelay over ±1e308 Hz (0), CrossCorrelationPeak's value for
  opposite scales, and Hilbert's real part for double.MaxValue (∞). Inputs are scaled by powers of two, which is exact,
  and scaled back last.
- **Subnormal inputs lost bits:** radix-2 FFT (8% off), cycle measurement (a ±ε square wave had no cycles: 0/0
  crossings) and pulse measurements (a rise time of 1 for 1.4667). They are measured scaled up by 2^600.
- **Quantizer** refused ±1e308, although its step is representable; with the range accepted, a 1-bit ±MaxValue
  quantizer reconstructed ∞. Both are halved where they would overflow.
- **Unwrap** with a tolerance above π corrected only jumps past it, and only to within it ([0, 10] at 4 stayed
  [0, 3.72]); it now follows NumPy's `discont` and MATLAB's `tol` (corrected at the tolerance, into (−π, π]).
- **RiseTime and FallTime** threw for a record starting partway up an edge although a full edge followed; they now take
  the first edge that crosses both references.
- **MedianFilter and Hampel** sorted the window again for every sample (16 s and 30 s for 20000 samples with a window as
  long); the window is now kept sorted as it slides, and Hampel's MAD is read by one merge.
- **Length checks:** Convolve and CrossCorrelate overflowed int for long virtual lists; ToRate checked int.MaxValue, not
  Array.MaxLength; a too-long non-power-of-two FFT allocated gigabytes before refusing.
- **Smaller:** Decimate gave NaN for [MaxValue, MaxValue, −∞] (−∞); Linear's last point could round below the last
  input; EnvelopeFollower froze once τ·fs passed about 2^53; SpectralPeak accepted an undefined PeakMethod.

### Round 69: Logging, diagnostics, CLI, fourth pass (v2026.11.13.12)

Two fresh reviewers (correctness, robustness): 30 findings, 26 after merging the four both found, all confirmed, plus a
ConsoleTable bug found while fixing and three of round 29's unfixed items, so **30 bugs, 1 severe**. Section 12 stays
re-queued. The regression tests are in `LoggingReview4Tests` (29 tests). All 29 failed on the old code: 27 on .NET 8,
and the IPv6 syslog, AssertInvariant path and ScopedTimer tests on .NET Framework 4.8, where those bugs live. The
checked build caught an unchecked cast in the new colour check before release. Still open (BUGLIST.md): AsyncLogSink's
Dispose has no drain timeout, and four suspected robustness items.

- **Severe: RateLimitedSink deadlocked.** It held its lock while calling the inner sink, so an inner sink that waited
  on another logging thread (a blocking AsyncLogSink with a full queue whose consumer logs back) never returned. The
  inner sink is now called after the lock is released.
- **SyslogSink:** TCP on .NET Framework was IPv4-only ("::1" never connected); the client is now dual-stack. A collector
  that stopped reading cost each emit the 5 s send timeout under the lock, and the retry hid the failure (FailedSends
  stayed 0); a send timeout now counts, backs off and isn't retried. A connect raced by Dispose threw
  NullReferenceException out of Emit on .NET Framework. A null event returned silently.
- **Formatters:** StructuredTextFormatter's single Replace left a delimiter with a space in it (" | ", ", ", "  ") inside
  values, and let NEL, LS and PS split a record. JsonLine wrote unpaired surrogates raw (lost as U+FFFD in UTF-8). An
  exception whose ToString throws took every formatter and SyslogSink down.
- **Sinks:** `Emit(null)` did something different in every sink; all now throw ArgumentNullException naming logEvent.
  The RateLimitedSink summary was stamped with the flush time, not the last repeat. FileSink named no parameter for an
  empty or invalid path on .NET Framework, and never deleted backups numbered past maxFiles. AsyncLogSink accepted an
  undefined overflow policy.
- **HealthCheck:** an undefined status (−1) made the report Healthy; ProbeResult now refuses it. A hung synchronous
  probe took one more pool thread on every check; while one run is outstanding, later checks report the failure status.
- **MetricsRegistry:** samples were kept without limit (20M took 400 MB) and Snapshot sorted under the lock (3 s of
  blocked Increment). Timers now keep up to a cap (default 2^20) with reservoir sampling past it (count, min, max and
  mean stay exact), and sort outside the lock.
- **FirstChanceMonitor:** signatures kept whole messages (600 MB for 300 large ones); they are cut to 500 characters, and
  the signature cap no longer races or takes every dictionary lock.
- **ExceptionUtils:** IsTransient called HTTP 4xx failures (other than 408 and 429) transient; Data values were formatted
  in the current culture.
- **ScopedTimer** timed with the wall clock (15.6 ms steps on .NET Framework, negative across a clock step); it now
  uses Stopwatch. **AssertInvariant** lost the violation for a path with '<', '>' or '|' on .NET Framework.
  **EnvironmentReport.Capture** named the BCL's parameter for a null variable name.
- **CLI:** undefined AnsiColor values made bogus SGR codes; a compound reset (ESC[0;1m) inside styled text ended the
  outer style. Sparkline accepted NaN, infinite and inverted bounds. ProgressBar showed a full bar or 100% before the
  work was done (it now floors). ConsoleSpinner cut lines inside surrogate pairs and measured UTF-16 units, not
  columns, and threw from its constructor and Message setter on a broken writer. ConsoleTable let line breaks and tabs
  in cells break the table. The README called ConsoleTable a box-drawing table.

### Round 68: Binary formats & images, fourth pass (v2026.11.13.11)

Two fresh reviewers (correctness, robustness): 9 findings plus a slow Base58 encode the robustness reviewer noted, all
confirmed, so **10 bugs, 1 severe**. Section 6 stays re-queued. The regression tests are in
`BinaryImagesReview4Tests` (14 tests); 13 failed on the old code, and the 14th is a differential guard (Base58 against
a naive one-digit-per-division encoder). Three older tests changed with the behaviour: a 40M × 1 RGBA16 row is now
allowed (so its 8-byte stream fails as truncated), and the Crockford and SVG examples used non-canonical or
just-below-half inputs. The checked build caught an unchecked length cast in the new stream reader before release.

- **Severe: tall, narrow PNGs decoded at about 4 µs a row.** Each row took two deflate reads, so 1 × 10⁷ (a 19 KB
  file) took 43 s. Rows are now served from a 64 KiB block.
- **PNG limits:** data past the last scanline was inflated and checksummed to the end (a 1 MB file could inflate 1 GB);
  the drain now stops after as much again as the image, at least 64 KiB, and ignores the rest unchecked, as libpng
  does. A row's size was compared in bits with int.MaxValue, so 2^25 × 1 RGBA16 or 10⁸ × 1 RGBA8 (within MaxPixels)
  was refused. Decode(Stream) copied the whole source (past IEND, failing at 2 GB with an IOException); it now reads
  chunk by chunk, stops at IEND and refuses a stream over 2 GB as invalid data.
- **SVG numbers:** "0.######" rounded to 15 significant digits first (0.7551384999999999 gave 0.755139,
  4503599627370497 gave 4503599627370500) and differed between .NET Framework and .NET past about 7e11. SvgUtils and
  SvgDocument now round the exact binary value to six decimals, halves away from zero, on both frameworks.
- **Text width:** combining marks outside U+0300–036F, zero-width and format characters, variation selectors, astral
  marks and tags counted as an average letter; NBSP counted as a letter; every part of a ZWJ emoji sequence and each
  skin-tone modifier counted an em. A width that overflows to ∞ (a font size near MaxValue) is refused, naming
  fontSize.
- **Canonical decoding:** Crockford Base32 accepted non-zero unused bits in the last symbol ("01" and "00" both gave
  0x00), and Base85 accepted any spelling of a short final group ("!#" and "!!"); both now have one spelling per
  value. Base85's documentation called it btoa-style, but it has no z/y shorthand, delimiters or whitespace skipping;
  it now says so.
- **Base58:** encoding divided the whole number once per digit, about seven times slower than decoding (17.6 s for
  100 KB); it now takes ten digits per division.

### Round 67: Filters & control, fourth pass (v2026.11.13.10)

Two fresh reviewers (correctness, robustness): 15 findings, 13 after merging the two that both found, all confirmed,
plus round 30's unfixed empty FiltFilt, so **14 bugs, 1 severe**. Section 1 stays re-queued. The
regression tests are in `FiltersControlReview4Tests` (24 tests); 23 failed on the old code, and the 24th is a
differential guard (Cohen-Coon's rewritten form against the textbook one on 200 random models).

- **Severe: PidController stalled short of saturation.** Conditional integration dropped the whole integral step
  whenever it would cross a limit, so with a coarse step the loop never reached the limit (an I-only controller stayed
  at 0 for good; a PI loop on a first-order plant stuck at y = 0.15 for a 0.9 setpoint). The integral now stops at the
  value that puts the output exactly at the limit, which also covers an error·dt that overflows.
- **Elliptic designs:** the degree equation summed the 8-term nome series at a nome near 1, so high orders or high
  selectivity missed the passband spec (−3.087 dB for a 3 dB ripple at n = 20); 1 − m and 1 − k1² were formed by
  subtraction and lost their digits when the attenuation is close to the ripple. The complementary nome is used below
  ratio 1, complements are carried through Jacobi and the inverse sc, and sn near K is reflected. The prototype now
  matches mpmath to about 1e-15 on 24 specs. A design whose poles land within rounding of the unit circle names the
  spec (stopAttenuationDb), not the mid-band cutoff.
- **MagnitudeDb** underflowed deep in the stop band (−∞ for −8000 dB) and the numerator cancelled next to its zeros
  at Nyquist. It is summed per section in logs, the numerator from its roots with the point taken from the exact
  offset to ±1.
- **Subnormal state:** a decaying tail ran in subnormal arithmetic (about 20 times slower). Once the signal reaches
  2^-969, state and outputs below the smallest normal are flushed to 0; a tiny signal keeps its exact arithmetic.
- **Huge and subnormal sample rates:** Response, GroupDelay, the prewarps, the shelves and FIR edges multiplied π·f
  or halved fs before taking the ratio (overflow past 5e307, rounding at a subnormal fs). Ratios come first.
- **FIR:** tiny cutoffs quantised the taps (62% off at 2e-323); counts past Array.MaxLength were an
  OutOfMemoryException in Design and KaiserParameters and are now refused by name (a .NET Framework 2 GB guard too).
- **Arguments:** a NaN or infinite frequency gave NaN responses (now refused, naming frequency); FiltFilt's padded
  length wrapped in int (now refused, naming padLength); FiltFilt of an empty signal with no pad threw
  IndexOutOfRangeException (now empty).
- **StepResponse:** interpolation across times near ±MaxValue overflowed (rise time −∞), and samples far larger than
  the step made the crossing fraction ∞/∞ (settling NaN).
- **FopdtModel and tuning:** StepResponse lost every digit just past the dead time (1 − e^(−x)) and overflowed K·step;
  FitStep's τ and θ overflowed into a misleading "rises too abruptly"; Ziegler-Nichols overflowed K·θ, and Cohen-Coon
  overflowed θ/τ and K·θ (it is now written in whichever ratio is at most 1).

### Round 66: Statistics, fourth pass (v2026.11.13.9)

Two fresh reviewers (correctness, robustness): 22 findings, all confirmed, and one more found while testing (the
t-test's standard deviation), so **23 bugs, none severe** (12 normal, 11 minor). Section 3 stays re-queued. The
regression tests are in `StatisticsReview4Tests` (22 tests, one of them .NET Framework only). Every one failed on the
old code, the .NET Framework one under net48 (OutOfMemoryException instead of ArgumentException). Most are lost digits
for data on a large offset, or tight data, where the method kept a rounding error it could have avoided.

- **Data on an offset:**
  - Anova.OneWay's round-off floor scaled with the data's magnitude, not its spread, so real variation 1e9 + m·2⁻²³
    was zeroed. GaugeRR summed raw readings without centring and had the same floor; both centre now.
  - MultipleRegression.Fit did not centre the predictors beside the intercept column (slope 7e-7 relative off at
    x = 1.7e9 + i/16). Columns and y are centred; the intercept, its SE and Predict are recovered from the centre.
  - The t tests subtracted two separately rounded means (t 3e-5 off for nearly equal means); the difference is now
    summed about one pivot. Their standard deviation kept the mean's rounding squared; it uses the corrected two-pass
    sum.
- **Tight data:** FitGamma's shape kept the mean's rounding in s (shape 2.9% off at CV 1e-7) and its log-likelihood
  cancelled for a large shape (476 for 504.9); FitLogNormal's sigma lost digits to the logs' own rounding.
- **KS one-sample:** the p-value was Stephens' asymptotic at every n, badly wrong for small n and large D. It is now
  exact (Marsaglia-Tsang-Wang to n = 140, the Smirnov tail for D ≥ ½ or tiny p), graded against SciPy's `kstwo` on
  101 cases.
- **Extremes:**
  - ChiSquareIndependence returned NaN for counts near MaxValue or subnormal; goodness-of-fit overflowed its
    differences.
  - FPdf was wrong by orders of magnitude for a numerator df past 1e16, and FQuantile's upper tail with it.
  - FitGamma on subnormal-mean data returned scale 0 and log-likelihood +∞ (so FitAll ranked it best); its CDF
    overflowed `2·v` near MaxValue.
  - ChangePoint.Cusum overflowed on large finite values and accepted NaN and ∞.
  - LogNormalPdf returned NaN when x·σ·√2π underflowed; NormalQuantile skipped its refinement for p below 1e-310;
    Gamma returned 0 where the result is subnormal (x in about (−178, −171)).
  - FUpperTailProbability accepted a subnormal df and returned NaN.
- **Limits:** GaugeRR's long-format balance check wrapped in int. PermutationTest had no cap on permutations and held
  the null distribution twice. Trend's Sen slope arrays could pass .NET Framework's 2 GB array limit.
- **Copies and docs:** the LnGamma copies in Anova, DistributionFit, Distributions and GaugeRR reflected with an
  unreduced sin(πx) (3.5e-9 relative off near −20); they reduce the argument as SpecialFunctions does. The TestResult
  docs omitted the F test and the KS and U statistics.

### Round 65: Windows satellite, fourth pass (v2026.11.13.8)

Two fresh reviewers (correctness, robustness): 27 findings, two of them duplicates, so **25 bugs, 1 severe**. Section 15
stays re-queued. BUGLIST 56-1 (`Thread.Sleep`) is closed with it. The regression tests are in `WindowsReview4Tests`
(22 tests). Every fix's test failed on the old code, except the end-to-end clipboard test. That one self-skips where
the clipboard cannot be opened (access denied in the session that ran the proof); the parser it calls is proven by
four `DecodeDropFiles` tests.

- **Severe, ClipboardUtils.GetFileDropList:** a CF_HDROP block from any program went to DragQueryFile, which follows
  the header's offset without bounds. A crafted block killed the reading process with an uncatchable access violation.
  The block is now copied (bounded by GlobalSize, the clipboard closed before parsing) and parsed by the new public
  `DecodeDropFiles`, which stays within the block. GetImage copies the same way.
- **SingleInstanceApp:**
  - Arguments and the working directory went over the pipe as UTF-8, so an unpaired surrogate (legal in a file name)
    arrived as U+FFFD, naming another file. Strings now travel as raw UTF-16. The byte limits are doubled, so the same
    text fits, and the pipe name carries a protocol version.
  - An app id with a control character made a broken gate.
  - Any listener failure other than IO or access ended listening for good. It now backs off and retries.
  - A launch that lost the takeover race was dropped instead of forwarded.
- **SingleInstance:** a stray backslash, or a namespace prefix in the wrong case, threw DirectoryNotFoundException
  from the mutex. It is now an ArgumentException; the kernel matches `Local\`, `Global\` and `Session\<n>\`
  case-sensitively.
- **EventLogWriter:** a negative category was written as 65535 and up, and an unknown kind went through. An over-long
  name made IsSourceRegistered throw.
- **JobObject.Start:** `""` as the working directory failed. A NUL in it was not rejected. If GetProcessById threw, the
  suspended child was left in the job.
- **PrivilegeScope and PowerScheme:** a restore that threw could not be retried. An unknown privilege reported
  ParamName "name".
- **ResourceSampler:** it was not IDisposable, so ForCurrentProcess leaked a Process. CPU could exceed 100%. Measure
  slept in Thread.Sleep and now waits on a CancellationToken.
- **DriveAndVolumeInfo.GetSpace:** a file path or a directory over MAX_PATH failed.
- **RegistryUtils:** a bool read from REG_SZ "1" or "0" returned the default. A leading backslash in the subkey read
  as absent but failed on write. The REG_EXPAND_SZ expansion is now documented.
- **ScreenCapture:** the size caps used int.MaxValue / 4, so a buffer over Array.MaxLength got through.
- **ClipboardUtils.SetImage:** it accepted sizes that GetImage refuses to read back.
- **Small ones:**
  - default ScreenImage, ShortcutInfo and FixedDrive threw from ToString.
  - OsVersionInfo passed an NTSTATUS as a Win32 error.
  - MonitorInfo fell back to scaled bounds without trying per-monitor v1.
  - IdleTime's 49.7-day limit is now documented.
- **Also fixed:** a race in round 64's Scheduler test (the loop could run the job before RunDue did). It failed once in
  the checked build.

### Round 64: Security, net, time, fourth pass (v2026.11.13.7)

Two fresh reviewers (correctness, robustness): 16 findings, no duplicates, **16 bugs, none severe** (one is a docs
fix). Section 13 stays re-queued (≥ 3 bugs). The regression tests are in `SecurityNetTimeReview4Tests` (19 tests; two
are guards that valid multibyte text and mid-range Unix times are unchanged). Every fix's test failed on the old code.

- **TcpLineClient:** with a strict encoding, a decode error left the old buffer in place, so later calls returned the
  previous lines again; at the end of the stream that looped forever (never null), and mid-stream the lines around the
  bad bytes were lost. Now the bad line throws `DecoderFallbackException` once and is dropped; the lines around it are
  kept.
- **SecretsFile and HttpDownload:** a save or download over an existing file gave it the temp file's inherited ACL and
  attributes (an owner-only secrets file became readable by Administrators and SYSTEM). Both now use `File.Replace`,
  which also stops the netstandard2.0 download deleting the old file before the move.
- **HttpDownload:** local file errors (a full disk, a parent path that is a file) were retried as network failures.
- **CronScheduler:** `NextRun`/`LastRun` could be read torn (a summer time with the winter offset); `RunDue` on a UI
  thread ran jobs on its synchronization context, so a later `Dispose` there deadlocked.
- **CronSchedule:** the time-zone overload in the calendar's last minute threw ArgumentOutOfRangeException instead of
  InvalidOperationException.
- **UnixTime:** Local and Unspecified times within a day of the calendar's ends were clamped (9999-12-31 20:00 PST gave
  the same seconds as MaxValue).
- **CryptoRandom:** NumericCode and Password had no length cap (OutOfMemoryException); now `MaxCodeLength`.
- **AuthenticatedEncryption:** DecryptToString turned invalid UTF-8 into U+FFFD (the encrypt side was already strict).
- **Small ones:** `::1%1` was not loopback; 360–364 days read "12 months ago"; `" -5m"` did not parse; TimeOfDayRange
  dropped seconds from ToString; SecretsFile threw ArgumentException for a null key or passphrase; PasswordHasher.Verify
  did not document its FormatException.

### Round 63: Documents and config, fourth pass (v2026.11.13.6)

Two fresh reviewers (correctness, robustness): 31 findings, two of them duplicates, so **29 bugs, 5 severe**. Section 7
stays re-queued. The regression tests are in `DocumentsConfigReview4Tests` (33 tests; one more guards that wrapping is
unchanged). Every fix's test failed on the old code; the deep-nesting one crashed the process.

- **Severe, SettingsStore:**
  - A file that was locked at load (defaults used) was overwritten with those defaults by the next Save or Update. Saving
    is now refused until Reset.
  - A get-only array or read-only collection made every load fail, so the valid file was moved aside as corrupt.
  - A get-only `ICollection<T>` (an interface) was never refilled, and the next save erased it.
  - A get-only Uri or Version made every Save and Load throw ("doesn't support populating"); a null get-only object
    failed to load.
- **Severe, ConfigBinder:** a key about 2,000 sections deep overflowed the stack. Nesting is capped at `MaxDepth` (64)
  and reported.
- **SettingsStore, also:** a symbolic link was replaced by a plain file (now written through to its target); a file name
  near 255 characters could never be saved (the temp name was 14 characters longer); a brief reader made Save fail at
  once (it now waits about half a second).
- **ConfigBinder, also:** a failed list item was also reported as a gap; `Hosts:0:Name` on a list of strings and
  `Port:0` under an int were dropped silently; a space or tab char could not be bound; a DateTimeOffset without an
  offset took the machine's zone (now UTC); IntPtr and UIntPtr never converted.
- **EnvironmentVariables:** GetDouble("1e400") was ∞ on .NET Core but an error on .NET Framework (now an error on both);
  RequireAll(null) blamed "source".
- **Markdown:** two lists in a row merged into one loose list (the second now uses the other marker), and the report's
  table of contents swallowed a list after it; a line break inside Bold or Italic broke the span and could start a list;
  entities in a link destination were decoded by the renderer.
- **PdfWriter:** wrapping was quadratic in a line's words (24 s for a tiny-font paragraph); a font taller than the page
  left blank pages; a size below 0.0005 pt was written as "0 Tf"; Image blamed "width" for a bad height; a bad list level
  left the earlier items written.
- **ReportBuilder and templates:** ToPdf took font sizes its 1.9× title could not use (failing as "size"); a figure's PNG
  and a DocxWriter image followed later changes to the caller's buffer; PercentChange overflowed to ∞ for ±1e308.
- **DocxWriter and HtmlReport:** a rows or items enumerable that threw part-way left a half-written table or list (an
  unclosed element; Word refused the file). PdfWriter and DocxWriter now document their 2 GB in-memory limit.

### Round 62: Charts, fourth pass (v2026.11.13.5)

Two fresh reviewers (correctness, robustness): 27 findings, three of them duplicates, so **24 bugs, 1 severe** (13
normal). Section 5 stays re-queued. The regression tests are in `ChartsReview4Tests` (24 tests). Every one failed on
the old code; the Annotations band failed only in the checked build. Almost all the bugs are spans and limits near
±MaxValue, or validation that let NaN and ∞ through to an inner method.

- **Severe: AxisTicks.Linear** with two ticks over ±MaxValue: the raw step overflowed to ∞, so the search walked all
  2³¹ candidate exponents (about 4 minutes), then threw IndexOutOfRangeException. SvgChart and a narrow TimingDiagram
  reached it too.
- **TimingDiagram:**
  - Widths it documents as valid failed as "maxTicks".
  - The unknown-value hatch was O(segments × LaneHeight), so 30 narrow slivers wrote 220M characters.
  - Change times one ulp apart made every x 0/0.
  - An explicit End < Start was silently replaced, and NaN or ∞ bounds failed in AxisTicks.
  - FormatSeconds printed 1e-16 as "0 ps" and 1e300 as 301 digits.
  - Clock and FromSamples blamed "changes" for their own overflow, or for a NaN startTime.
- **Histogram.Bin:**
  - An underflowed bin width made the settle walk O(n × bins). It now bisects after four steps.
  - Constant data past 4.5e15 gave zero-width bins.
  - Explicit min = max = 5 counted 4.6 and 5.4.
- **Opened limits at ±MaxValue** overflowed to ∞ in SvgChart, HexBin and Waterfall. **BoxPlot** and **ErrorBarChart**
  collapsed onto a few rows when the data's span overflowed.
- **ScatterMatrix:**
  - Cells below 18 px drew negative heights.
  - Pearson of subnormal columns gave r = −0.667 for exactly anti-correlated data. The columns are now scaled to unit
    magnitude first.
- **Validation:**
  - HeatMap, HexBin (reversed or NaN limits, ∞ Width), SvgChart (NaN limits, ∞ Width, Height or FontSize),
    MultiPanel (∞ panels, a figure summing past MaxValue) and Annotations.ToSvg (∞ fontSize) are now checked and
    name "options" or "fontSize".
  - BoxPlot.Render named Compute's "whiskerReach" and "data".
  - A Series whose lists changed length after construction indexed past the end. It is now rejected at render
    time.
- **Annotations.Draw** cast pixel coordinates beyond the int range without a clamp.
- **LinePlot:** a lone point in Line style (one point, or one between NaN gaps) drew nothing.

### Round 61: Numerics core, fourth pass (v2026.11.13.4)

Two fresh reviewers (correctness, robustness): 24 findings, **24 bugs, none severe** (13 normal), so section 4 stays
re-queued. Regression tests are in `NumericsCoreReview4Tests` (25 tests, all failing on the old code). Almost all are
extreme-range bugs: overflow, subnormals, or one rounding too many.

- **AdaptiveSimpson** spent its whole 10M-evaluation budget on a constant 1e308 (the Simpson weights overflowed and
  ∞ − ∞ never met the tolerance), and integrated 1 over [0, 3ε] to 0. **Simpson** over a subnormal dx did the same.
- **LinearAlgebra.Solve / Inverse** returned all-NaN for a 70×70 Wilkinson matrix of 1e288s: rows near the overflow
  threshold now keep n + 24 bits of headroom for pivot growth, and an elimination that overflows anyway throws.
- **RunningStatistics.Sum** after the running total overflowed came from the mean, off by 1e292 for a true sum of 61;
  it comes from a scaled double-double copy now (plus an unscaled sum of tiny values).
- **Angle:** differences of finite angles near ±MaxValue were NaN, 3.6e17° (a multiple of 360) swallowed a 10° offset,
  and small negative angles collapsed to 0 or snapped to the ulp of 360.
- **Splines:** MonotoneCubic's Fritsch-Carlson limiter and both splines' secants overflowed (a 1e10 rise over 1e-300)
  to NaN; Akima's derivative was 0·∞ for flat data on tiny x with huge y. **LookupTable** missed a knot's own y.
- **Means.Geometric** was hundreds of ulps off near 1e±300 and fell below the harmonic mean; it sums mantissa logs and
  exponents separately now. **WeightedAverage** and **ExponentialMovingAverage** drifted off equal or constant input.
- **Polynomial:** the cubic lost a double root next to the simple root ((x + 8.078125)²(x + 8.09375)); Fit's design
  matrix past Array.MaxLength threw OutOfMemoryException (now ArgumentOutOfRangeException).
- **ScatteredInterpolation:** NaN when every sample was beyond MaxValue away, results an ulp outside the samples'
  range, a last grid coordinate past max, ±∞ sample values accepted, and a grid past Array.MaxLength threw OOM.
- **Others:** Histogram accepted bins narrower than the ulp at 1e16; WeightedChoice was biased for subnormal weights;
  Percentile rounded the rank twice (p = 29 of 101 points missed sorted[29]); Differentiation.Sample accepted ±∞ x.

### Round 60: WinForms, tooling and docs, fourth pass (v2026.11.13.3)

Two fresh reviewers (correctness, robustness): 23 findings, one raised by both, so **22 bugs, none severe** (4
normal), and section 17 stays re-queued. Regression tests are in `WinFormsReview4Tests` (13 tests, all failing on the
old code); the tooling fixes were probed by hand. Three section-14 bugs found after round 59 were released here too
(`ObjectsReview60Tests`, 5 tests, 4 failing on the old code, 1 a guard).

- **bump-version.ps1** built today's key with the culture's calendar: under th-TH the year was 2569, so a version
  could repeat or go backwards. It uses the Gregorian date parts now (checked under en-US, th-TH and ar-SA).
- **BeginInvokeIfRequired:** a control with no handle of its own posts to its parent, so the action still ran after the
  control was disposed (a tab page closed meanwhile). The posted action checks again before it runs.
- **CommandBinding:** every bound item stayed subscribed to its command, which kept the item and its parameter alive
  for as long as the command lived. A subscription now holds the item weakly, drops itself when the item dies or is
  disposed, and is pruned at each bind. A command raised from a worker before any WinForms control existed on the
  thread set Enabled off the UI thread; binding on an STA thread with no context installs one.
- **DataGridView export:** an image column (or a byte[] column shown as one) exported "System.Drawing.Bitmap" and
  decoded a new, undisposed Bitmap per cell; such cells are empty fields now. `AutoSizeColumns` with an undefined mode
  threw WinForms' exception naming "autoSizeColumnMode"; it is ArgumentOutOfRangeException("mode").
- **MenuShortcut / MenuBuilder:** TryParse accepted KeyCode, Packet, NoName and ProcessKey as keys, and rejected
  "Ctrl+-"; FindItem did not ignore the mnemonics in the path, contrary to its doc. ToolStripBuilder's second Build was
  empty; each Build and ApplyTo now makes fresh items.
- **PlotPictureBox:** at deep zoom a float offset far from 0 swallowed small pans (the pan is double inside now);
  ViewChanged fired for a zoom clamped to no change or a zero pan, which also ended auto-fit; smooth scaling faded the
  image's border (bicubic read transparent black past the edge; it mirrors now).
- **FormStatePersistence.EnsureOnScreen** accepted a window with a 1 px sliver of caption on screen; it needs half the
  caption now.
- **Scripts:** checked-build.sh left the old file of a staged rename in its worktree; coverage.ps1 broke on a path with
  a space under Windows PowerShell 5.1 and reported success when reportgenerator failed; net48.sh wrote the repo path
  unescaped into the generated csproj; verify.sh could run a stale test exe from another TFM folder.
- **Packaging:** the DLLs embedded the build machine's absolute PDB path; Release builds map the repo root to `/_/`.
- **Docs:** the distribution table was called "40-digit"; it holds 20 digits computed at 40. The README said net48.sh
  could run every folder with no arguments.
- **Section 14 (59-1 to 59-3):** `FriendlyName` overflowed the stack about 2,400 generic levels deep and ended the
  process; it throws InsufficientExecutionStackException now. A stored lazy sequence making new objects on each pass
  hung DeepEquals, PropertyDiff and FlattenObject. Unflatten's "Empty path." for a leaf root now says why.
- **Gates:** the full verify (core 3221, Windows 115, WPF 162, WinForms 104, MathChecks 40), the checked build (3221)
  and the Objects tests on .NET Framework 4.8 (159) are green.


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

### Round 30 — 2026-10-25 — Filters & control, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 1 with 2 severe).
The correctness reviewer matched 1,512 grid and 2,997 random IIR designs, every FIR design, filtering, order
estimates and the tuning tables against SciPy and the published references; no math errors. **9 confirmed bugs**
(three found by both reviewers), none severe, each with a failing-first regression test in
`FiltersControlReview2Tests` (14 tests, all failing on the old code).

- **IirFilter steady state:** `Reset(value)` and `FiltFilt` treated any section with |1 + a1 + a2| < 1e-14 as a
  pole at z = 1 and started it (and every later section) from zero. Cutoffs near 1e-8·fs fall under that with all
  poles inside the circle, so a high-pass or band-pass output 3 for a settled constant 3, and a 1e-5 Hz low-pass
  at 1 kHz output ~0 instead of 1. Only a non-finite gain now stops it; a zero numerator gives gain 0.
- **SchmittTrigger:** equal thresholds toggled on every sample at the threshold; it now turns off only below it.
- **PidController:** an overflowing error·dt put ∞ into the integral for good (NaN output with kp = 0), and with
  ki = 0 the unused integral still accumulated so 0·∞ made every later output NaN. The integral only commits when
  ki ≠ 0 and the new value is finite.
- **FirstOrderLag / KalmanFilter1D:** `input − Value`, `P + R` and `measurement − estimate` overflowed for far-apart
  finite values (∞ or NaN, kept for good); both now interpolate and clamp, and the Kalman gain and covariance
  are computed without overflow.
- **StepResponse.Analyze / FopdtModel.FitStep:** normalising a representable step from −1e308 overflowed (overshoot
  ∞, settling NaN); now split when the difference overflows.
- **Biquad.Peaking / LowShelf / HighShelf:** a tiny Q or slope overflowed into a plain `ArgumentException`
  ("Coefficients must be finite"); now `ArgumentOutOfRangeException` naming q or slope.
- **Parameter names:** `FitStep` with a tiny input step blamed "gain" (now inputStep); `FirstOrderLag.FromCutoff(1e-320)`
  blamed "timeConstant" (now cutoffHz); `StepResponse.Analyze` with a huge interval blamed "values" (now sampleInterval);
  SIMC/lambda with an extreme model blamed "kp" (now model).
- **ZieglerNichols / CohenCoon:** an extreme dead time silently returned Ti = ∞ (no integral action); now rejected
  naming model.
- **Not fixed:** rise time from the first 10% crossing rather than the last before 90% (unverified against MATLAB);
  `ToSections` zero pairing could in theory give a one-pole section two zeros (never seen in 4,500 designs);
  denormal slowdown; `Response` accuracy at extreme frequencies; the stability check uses zpk rather than the
  quantized coefficients; NaN frequency in `Response`/`GroupDelay`; `FiltFilt` of an empty signal with pad 0 throws.
- **Checked build:** same 7 baseline failures, none new.
- **Re-queued:** yes (9 bugs).

### Round 31 — 2026-10-25 — Signal analysis, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 2 with 1 severe).
**23 confirmed bugs, 2 severe**, each with a failing-first regression test in `SignalReview2Tests` (20 tests; 19
fail on the old code, and one checks that Savitzky-Golay edges still fit the polynomial).

- **PulseMeasurements (severe):** state levels were the record's min and max, so overshoot became the top: a
  ringing step's fall time was 75 samples instead of ~4, and its rise time threw. They are now the IEEE 181
  histogram modes of the lower and upper halves of the range.
- **PulseMeasurements (severe):** rise/fall paired the first low-reference crossing in the record with the first
  high-reference one, so a glitch long before the edge gave 5.3 instead of 1.6. The low crossing is now the last
  one before the high crossing. References of 0 and 1 were unreachable (they threw) and now count arriving at the
  level. A NaN gave NaN levels if it came first and was ignored elsewhere; non-finite samples are now rejected.
  Levels, mid level and crossing fractions no longer overflow near ±double.MaxValue.
- **ZeroCrossing:** every zero sample counted as a crossing (`{1,−1,0,0,0,0,0,1}` gave six), and a touch
  (`{1,0,1}`) counted. A run of zeros now counts once, at its middle, only if the sign changes across it. ±1e308
  pairs and infinities no longer give 0 or NaN positions.
- **MedianFilter:** NaN sorted first and acted as −∞; it is now left out, as in Hampel. Even-count midpoints in
  MedianFilter and Hampel overflowed near double.MaxValue.
- **SavitzkyGolay:** all w coefficient rows were kept (835 MB at w = 10001); now only the centre row is kept and
  each edge row is computed into one reused buffer.
- **TimeDelayEstimate:** inputs around 1e154 overflowed and around 1e-165 underflowed, giving lag 0; inputs are
  now scaled to a peak of 1, with the correlation value scaled back.
- **Overflow and underflow:** `Resample.ToRate` with an infinite step (IndexOutOfRange), `Decimate`'s block sum,
  `CycleMeasurements` level and band, `PhaseUnwrap` (one huge jump made everything after it −∞; now rejected),
  `FrequencyGrid` bin frequencies at huge rates, `WelchPsd` normalisation (underflowed to 0), `Fft.Inverse`
  (`[1e308, 1e308]` gave ∞ + NaN·i; retried prescaled), `Window.CoherentGain` (∞).
- **Validation:** `Quantizer` rejects non-finite ranges and ranges whose step is ∞ or 0; `Window.Create` rejects
  an undefined type at length 1; `EquivalentNoiseBandwidth` of an all-zero window throws (was NaN); NaN or ∞
  `targetFrequency` (Goertzel), NaN ratio (AmplitudeToDb/PowerToDb), ∞ sample rate (SpectralPeak, Multitaper),
  ∞ floor (AmplitudeDb).
- **Parameter names:** WelchPsd's invalid window ("window", was "type"), TimeDelayEstimate empty input and
  too-long input, Fft Bluestein too long ("input"/"real"/"spectrum"), GroupDelay length mismatch ("frequencies").
- **Not fixed:** Parabolic `2·center` overflow; a Hampel window of a ±∞ pair; WelchPsd cost at very high
  overlap; median/Hampel O(n·w·log w); EnvelopeFollower frozen when τ·fs > 1e16; NearestBin banker's rounding;
  CycleMeasurements first-crossing timing (documented).
- **Checked build:** same 7 baseline failures, none new.
- **Re-queued:** yes (23 bugs, 2 severe).

### Round 32 — 2026-10-25 — Text & small types, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 11 with 1 severe).
The correctness reviewer fuzzed the command-line builder/splitter round trip (200k lists), natural-sort ordering,
Jaro-Winkler, Soundex, Roman numerals, TextDiff minimality and ULID/Snowflake ordering; all correct. The
robustness reviewer found no hangs, and Snowflake and ULID held up under contention. **12 confirmed bugs**, none
severe, plus 2 doc fixes, each code fix with a failing-first regression test in `TextReview2Tests` (11 tests, all
failing on the old code).

- **AnsiText / TextAlign:** an OSC string interrupted by an ESC other than ST swallowed all the text after it
  (`Strip` gave "" and the width 0); an ESC inside a CSI or other escape was eaten as its final byte
  (`"ESC ESC [31mX"` gave "[31mX"). An ESC now cancels the sequence and starts a new one, in both files.
- **Slug:** ß, æ, ø, ł, đ, ð, þ, œ and ı have no decomposition and were dropped ("Straße" → "stra-e",
  "Øresund" → "resund"); they now fold (ss, ae, o, l, d, d, th, oe, i).
- **PercentEncoding.Encode:** '+' in `safe` with `spaceAsPlus` stayed literal and decoded as a space; it is now
  escaped, like '%'.
- **NanoId:** an alphabet with a repeated character (silently biased) or a surrogate half (invalid UTF-16 ids) is
  rejected.
- **StringSimilarity.TopMatches:** a NaN `minScore` returned nothing while `BestMatch` filtered nothing; both now
  filter nothing.
- **CaseConverter:** a first letter outside the BMP (Deseret) was not capitalised.
- **GlobMatcher:** with `ignoreCase`, literals compared upper cases while classes tried three forms, so 'ſ'
  matched "s" but not "[s]" and the Kelvin sign "[k]" but not "k"; literals now use the same forms.
- **UlidGenerator.DecodeTimestamp:** accepted 'ſ' (U+017F) as 'S'; only ASCII is decoded now.
- **TextAlign.Fit / PadLeft / PadRight / Center:** a wide or zero-width pad character broke "exactly width
  columns" ('中' gave 9 for 5); it is now rejected naming pad.
- **Truncate.WithEllipsisOnWord:** leading whitespace stayed before the ellipsis ("   abcdef", 4 gave "  …").
- **Guard.InRange:** min > max blamed the value; now an `ArgumentException` naming max.
- **Docs:** CaseConverter's "idempotent" claim now notes that adjacent one-letter words read back as an acronym
  ("x_y_z" → "xYZ" → "xYz"); MiddleEllipsis's example showed a split the code never makes.
- **Not fixed:** NGramSimilarity materialises every gram (400 MB for n = 10,000 on 20k chars); emoji widths for
  text-presentation symbols and flags; the 8-bit C1 CSI (U+009B); `AnsiText.VisibleLength` counts UTF-16 units;
  a combining mark at the very start of `ToWords` is dropped; Dedent treats \f and \v lines as blank; RomanNumerals
  and 'ı' on .NET Framework casing (untested).
- **Checked build:** same 7 baseline failures, none new.
- **Verify:** one ToolBelt.Windows test failed once while other builds were loading the machine, then passed 101/101 on three
  reruns (flaky, unidentified; this round changed nothing in ToolBelt.Windows).
- **Re-queued:** yes (12 bugs).

### Round 33 — 2026-10-25 — WPF satellite, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 16 with 1 severe).
None of round 16's 21 fixes had regressed. **17 confirmed bugs**, none severe, plus 1 doc fix, each with a
failing-first regression test in `WpfReview2Tests` (17 tests, all failing on the old code).

- **NumericInputBehavior:** clamping to a bound with more decimals than shown formatted back out of range (Min 0.25,
  one decimal: "0" became "0.2"; Max 3.5, none: "9" became "4"); the bounds now move inward to the shown precision.
  Stepping past double.MaxValue or a Minimum of +∞ gave "Infinity"; a DecimalPlaces of a billion made losing focus
  throw FormatException; more than 15 decimals showed binary noise ("0.10000000000000000555") rather than the zeros
  the comment promised. StepBy's doc said it selects the result; it leaves the caret at the end.
- **RegexValidationRule / RuleBuilder.Matches:** `$` matched before a final "\n", so "123\n" passed a whole-value
  "[0-9]+"; now `\z`. `Range(5, 1)` now names max.
- **NotifyTaskCompletion:** `ConfigureAwait(false)` raised PropertyChanged on the thread pool; it now resumes on the
  creating context, as AsyncRelayCommand does since round 16.
- **GlobalHotkeys:** Unregister from another thread returned true but left the hotkey registered system-wide; Dispose
  from another thread threw half-way with every hotkey still registered and no way to retry. Both, and Register,
  now throw InvalidOperationException before changing anything. A hostile WM_HOTKEY wParam above 32 bits threw
  OverflowException out of the message loop; `default(HotkeyGesture)` registered VK 0; a failure other than "already
  registered" no longer claims another app took the hotkey.
- **HotkeyGesture.TryParse:** KeyConverter's Enum.Parse fallback took "Ctrl+65" as Ctrl+V and "Ctrl+999" as an
  undefined key; numbers, comma lists and the ImeProcessed/DeadCharProcessed pseudo-keys are rejected. Parse(null)
  throws ArgumentNullException.
- **ThemeManager.Apply:** a registered dictionary the app had already merged (App.xaml) got a second copy, which
  outlived every later switch, so Apply(Dark) changed nothing while reporting Dark; it is now adopted in place.
- **FileDropBehavior:** filtering called Directory.Exists per file on every DragOver (200 ms per event for 5,000
  files, for the whole drag); it is now done once per drag. ".tar.gz" never matched (Path.GetExtension).
- **PlotPresenter:** MinZoom/MaxZoom accept only positive finite numbers; a fit to a scale WPF cannot invert keeps the
  current view (ViewToContent threw). `BitmapFromRgba`'s size check overflowed (32768² × 4 matched an empty array),
  and a NaN, zero or negative dpi is rejected.
- **CountToVisibilityConverter:** zero of nint, Half, Int128, BigInteger (any INumberBase) and UnsetValue are Collapsed.
- **EnumToBooleanConverter:** a numeric parameter ("99") wrote an undefined enum value back; only names are parsed.
- **BindingFailureListener.WriteLine:** an id beyond int threw OverflowException.
- **Not fixed:** ThemeManager's follow-system Apply runs in a dispatcher callback with no try/catch (a missing or broken
  dictionary throws on an OS theme switch; not reproduced); Normalize leaves "-" or "." in the box; RecordingDialogService
  Strict mode throws before recording the call; ZoomAbout on a mirrored view; IncrementalCollection records a loader's
  own OperationCanceledException; a pattern with an unbalanced ")" escapes the anchors; EnumToBoolean names are
  case-sensitive.
- **Verify:** all green (WPF 134).
- **Re-queued:** yes (17 bugs).

### Round 34 — 2026-10-25 — WinForms + tooling + docs, second pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 17 with 1 severe).
None of round 17's 24 fixes had regressed. **12 confirmed bugs**, none severe, plus 2 doc fixes and a hygiene fix, each
bug with a failing-first regression test in `WinFormsReview2Tests` (12 tests, all failing on the old code).

- **DataGridViewExtensions.SaveCsv:** an invalid delimiter was rejected only after the StreamWriter had truncated the
  file (round 17 fixed the same for a null grid).
- **PlotPictureBox:** `ZoomAt`/`ZoomAbout` took a NaN or infinite point (and an infinite zoom), and the next paint threw
  in WM_PAINT; `PanBy(3e38)` twice gave an infinite offset. Far from the origin (offsets past ~2^31, e.g. deep zoom)
  GDI+ threw OverflowException on every paint; it now draws only the whole source pixels in view, and fills their
  blocks when each spans millions of view pixels. `Paint` handlers ran before the image was drawn, so the overlays the
  class doc suggests were hidden. ResetView ignored MinZoom/MaxZoom, and a zoom-in on a view above a lowered MaxZoom
  zoomed out; ResetView now clamps, a clamp never reverses the zoom direction, and the doc says MinZoom wins.
- **ControlExtensions:** for a control with no handle (a ShowDialog form closed but not disposed), InvokeRequired is
  false, so a worker ran the action itself and created child windows on the worker thread. On a non-STA thread the
  synchronous helpers now throw InvalidOperationException and BeginInvokeIfRequired drops the action.
- **MenuItemsBuilder.Item / ToolStripBuilder.Button (ICommand):** `Item("&Save", cmd, "Ctrl+S")` compiled but passed
  "Ctrl+S" as the command parameter with no shortcut (and `Button(text, cmd, image)` passed the image); the optional
  arguments now come in the Action overload's order, with `parameter` last. **Breaking** for positional parameters.
- **MenuBuilder.FindItem:** "File/" matched a separator (empty text); separators are never matched.
- **MenuShortcut:** TryParse took non-ASCII digits ("Ctrl+٣" gave Keys 0x20663); Parse(null) threw FormatException. The
  doc's "exactly what ShortcutKeys accepts" now names the mouse buttons it deliberately rejects. Check's doc said the
  callback runs after each click; it also runs when Checked is set.
- **TestRunner / scripts/test.ps1:** a filter matching nothing printed ALL GREEN and exited 0, so a typo passed. The
  runner exits 3 (`TestRunner.NoMatch`) and test.ps1 fails only when no suite matched.
- **Hygiene:** `scripts/__pycache__/*.pyc` was committed; removed and ignored.
- **Rejected:** "packaging contradicts design law 2" — the law's "never packaged" is about third-party satellites
  (MathChecks), not the src projects.
- **Not fixed:** a lost mouse capture mid-drag is not handled (OnMouseCaptureChanged; not reproduced with real
  input); WaitCursorScope's counter is not exception-safe if the UseWaitCursor setter throws (not reproduced);
  CommandBinding sets Enabled on the thread raising CanExecuteChanged (undocumented); EnsureOnScreen can leave 1 px of
  caption visible; TryParse takes "Ctrl+Ctrl+S" and rejects "Ctrl+-"; a null command orphans a menu item before the
  throw; the nupkgs carry placeholder metadata (description, license); under coverage, ChartsReview2Tests'
  wide-dash annotation test hit its time budget once.
- **Verify:** all green (WinForms 82). Checked build skipped (core unchanged).
- **Re-queued:** yes (12 bugs). This completes the second pass; the third pass starts with section 7.

### Round 35 — 2026-10-25 — Documents & config, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 26 with 20 bugs, 6
severe). **17 confirmed bugs, 2 severe**, each with a regression test in `DocumentsConfigReview3Tests` (17 tests; 16
fail on the old code, the duration one only on .NET Framework).

- **SettingsStore (severe):** get-only properties (`List<string> Recent { get; } = new()`, dictionaries, nested
  objects) were saved but skipped on load, so the next save erased them. A type-info modifier now clears and refills
  a get-only collection and populates a get-only object.
- **ConfigBinder (severe):** a configured get-only scalar, and a section for a null get-only class property, were
  dropped silently despite the doc's "never dropped silently"; both are errors now. Also errors now: a list given one
  value ("Hosts=a,b") or a non-numeric index ("Hosts:first"), a section under an `object` property, and float/double
  overflow ("1e400" was ∞ on net8 but an error on net48). A property of the class's own type (Node.Next) was skipped
  by the cycle guard; it binds now, since each level needs a longer configured key.
- **ConfigLayers:** TryGet/SourceOf rebuilt the whole merged view per call (20k keys × 2 layers: 43 s). Sources are
  copied case-insensitively on Add (later changes to the dictionary aren't seen, now documented) and a lookup walks
  the layers.
- **EnvironmentVariables:** the longest duration ("922337203685477ms") threw OverflowException on .NET Framework
  instead of parsing; ticks are computed directly.
- **PdfWriter:** Wrap dropped leading spaces (Lines, Paragraph, cells, bullets); a table at a tiny font size
  overflowed the lines-per-page count and put one line per page; a very thin image got a singular (zero) matrix.
- **ReportBuilder:** ToPdf(fontSize: 1e308) failed inside Heading with ParamName "size"; it is checked up front. The
  Markdown TOC indented by absolute level, so a TOC starting at level 3 became a code block; it nests by depth.
- **MarkdownReport:** a list item containing a blank line split the list (continuation lines are now indented);
  Bold/Italic with edge spaces rendered literal asterisks (spaces now go outside the markers); Bold("") gave "****" (a
  thematic break) and Code("") two backticks; both give "".
- **Not fixed:** env ParseBool takes y/n while the binder doesn't, and duration formats differ slightly; SettingsStore
  ignores public fields (now documented); ToDocx of a corrupt PNG, DocxWriter.Image height cap, ImagePng's
  InvalidDataException contract, a null value in a later ConfigLayers layer overriding a real one, "Infinity%" from
  ReportTemplates, and SettingsStore's empty-file/invalid-UTF-8 handling (not reproduced or by design).
- **Verify:** all green (core 2793). Checked build: the same 7 baseline failures.
- **Re-queued:** yes (17 bugs, 2 severe).

### Round 36 — 2026-10-25 — Numerics core, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 18 with 28 bugs, 5
severe). **24 confirmed bugs, 2 severe**, each with a regression test in `NumericsCoreReview3Tests` (17 tests, all
failing on the old code).

- **LinearAlgebra (severe):** elimination overflowed on rows near double.MaxValue (Solve of {{1e308, −1e308}, {1e308,
  1e308}} gave [0, 0], a determinant of 2e306 came out ∞). Rows beyond 2⁹⁶⁰ or below 2⁻⁹⁶⁰ are scaled by a power of
  two first. Also: a pivot near MaxValue overflowed the determinant's running mantissa; a column-scaled matrix
  ({{1e-20, 1e20}, {2e-20, 3e20}}, determinant 1) was called singular (a pivot is negligible only against both its row
  and its column now); the non-square ArgumentException has ParamName "a".
- **Polynomial (severe):** CubicRoots divided by a tiny leading coefficient and overflowed (1e-10x³ + 1e300 gave [−0],
  not −2.15e103; 1e-320x³ + x² − 1 lost ±1). x is scaled down just enough to keep the monic coefficients finite. Fit
  overflowed for y near MaxValue (a constant 1e308 fitted as ∞); y is scaled by a power of two.
- **RootFinding:** Newton and Secant reported convergence on a small residual alone (1e-15·(x − 5) "converged" at 0,
  e^−x at 28); the step must vanish, or f change sign nearby. Brent with tolerance 0 never stopped on a bracket of two
  adjacent doubles.
- **Splines:** MonotoneCubic and Akima flushed small values beside huge ones to 0 (a fixed 2⁻⁶⁰⁰ scale; now just
  enough, and the given y is returned at the knots); a 1e9 rise over 1e-300 overflowed to NaN (tiny ranges are now
  scaled up); Akima's weighted mean overflowed for steep slopes.
- **RunningStatistics:** the sum of squared deviations underflowed (1e-200 and 2e-200 had a deviation of 0) and
  overflowed (deviation 1e200 gave ∞). It is kept as a mantissa and a power of two.
- **Interpolation, LookupTable, Percentile:** SmoothStep(−1e308, 1e308, x) was NaN; LookupTable lost precision at
  1e-200 (dy·dv underflowed) and gave NaN extrapolating a flat end to ±∞; Percentile of [−∞, +∞] was NaN.
- **Overflow near MaxValue:** Differentiation.Sample (uniform differences and uneven spacing products), Simpson's
  trailing odd interval, the inverse-distance weighted sum, and C↔F conversion of 9e307.
- **Smaller:** EngineeringNotation.TryParse("1e400") returned false with value ∞ (now 0); on .NET Framework, Rounding
  and EngineeringNotation fell back to G17 rather than the shortest round-trip digits; Trapezoid accepted a NaN x;
  inverse-distance accepted infinite coordinates; ParamName fixes in WeightedChoice, ToleranceCheck, LookupTable,
  ToGrid ("columns"), and StreamingQuantiles' indexer throws ArgumentOutOfRangeException.
- **Not fixed:** Fraction.ToDouble double-rounds in the subnormal range; Uncertainty.Format shows binary digits past
  15 decimals on .NET 8; a tiny Histogram range makes Add slow (bounded); .NET Framework's own Parse inaccuracy;
  MinMaxScaler.ToRange(−∞, ∞) and NaN polynomial coefficients are left as they are.
- **Verify:** all green (core 2810). One Windows test failed once in a verify run and passed on two reruns (unrelated
  to Numerics). Checked build: the same 7 baseline failures.
- **Re-queued:** yes (24 bugs, 2 severe).

### Round 37 — 2026-10-25 — Charts, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 19 with 28 bugs, 5
severe). **21 confirmed bugs, 2 severe**, plus one doc fix, each with a regression test in `ChartsReview3Tests` (11
tests, all failing on the old code).

- **AxisTicks (severe):** time axes below a millisecond got no ticks: the step table stopped at 1 ms, so
  Time(0.0001, 0.0002) had no tight ticks and a loose [0, 0.001] axis, and SvgChart and Waterfall time labels were lost.
  Below a second the 1-2-5 linear steps continue the table. Also: one axis mixed plain and E-notation labels (the
  format now follows the largest tick), and Time with two ticks across zero picked too coarse a step (Time(−1, 1, 2) now steps by 1 s).
- **Histogram (severe):** one explicit bound past all the data was centred away (Bin({4.7, 4.8}, min: 5) moved min to
  4.5 and counted both). The bound stays and the other side opens beyond it. Decimal edges were off by an ulp (0.3
  fell in the bin below its own); edges are lo + span·i/bins when that cannot overflow.
- **Raster charts:** one limit past the data (LinePlot MinY = 100 over 0..3, BoxPlot, BandChart, ErrorBarChart) drew
  the data as if in range; the other side of the axis now opens past the limit. Reversed or empty explicit ranges in
  BoxPlot, BandChart and ErrorBarChart, and NaN or ∞ BoxPlot limits, are rejected (LinePlot's reversed axes stay
  allowed). A negative Margin put the plot outside the image in all five raster charts; it throws, naming options.
- **Spans overflowing to ∞:** finite data near ±1e308 gave NaN coordinates in Waterfall lines, TimingDiagram, and
  ScatterMatrix ticks, points and diagonal histograms (spans are halved); an Annotations line of width 1e308 overflowed
  its SVG dash pattern (capped).
- **Option validation, naming options:** ScatterMatrix CellSize, Gap, PointRadius, PointOpacity and Bins (int.MaxValue
  ran out of memory); HexBin Radius (∞ and 1e-300 drew nothing); Waterfall ValueMin/ValueMax non-finite, axis ends
  that overflow, and an overflowing ColorbarWidth; TimingDiagram LaneGap (capped at 1e6) and null Markers; SvgChart
  MaxXTicks/MaxYTicks below 2; null FontFamily in Waterfall and MultiPanel (and MultiPanel FontSize).
- **Smaller:** a ScatterMatrix null group label threw a bare NullReferenceException; a FormatTime returning null failed
  as "text" (now an empty label); FormatSeconds of NaN or ∞ gave "NaN ps"/"Infinity s" (throws); BoxPlot quartiles of
  [−∞, +∞] were NaN. The ScatterMatrix doc said "lower-triangle panels" (it draws every off-diagonal panel), and
  MultiPanel's FontSize and FontFamily now say they style only the figure title.
- **Not fixed:** time labels at ±1e308 seconds are very long (correct, just unwieldy).
- **Verify:** all green (core 2821). Checked build: the same 7 baseline failures.
- **Re-queued:** yes (21 bugs, 2 severe).

### Round 38 — 2026-10-25 — Objects, functional, grids, graphs, intervals, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 20 with 19 bugs, 4
severe). **19 confirmed bugs, 3 severe**, plus one doc fix, each with a regression test in `ObjectsGraphsReview3Tests`
(13 tests, all failing on the old code).

- **Computed self-typed properties (severe ×2):** a property returning a new object of its owner's type on every read
  (a struct's Perpendicular, a class's Reciprocal) never met the same reference twice: DeepEquals and PropertyDiff hung,
  and Flatten grew past 6 GB. ObjectMembers.Read now flags such members (a struct, or a reference that a second read
  does not return again), and the walks follow at most three of them along a path: DeepEquals assumes equal beyond,
  PropertyDiff stops, and Flatten records the value as-is. Linked lists (identity-stable Next) are unaffected.
- **TryConvert to an enum (severe):** out-of-range numbers wrapped (2^32 + 1 to DayOfWeek was Monday; 256 and −1 to a
  byte enum were 0 and 255). They are range-checked against the enum's underlying type. Also: "1e400" was ∞ on .NET 8
  and a failure on .NET Framework (it fails on both; "Infinity" still parses); text did not convert to Guid, TimeSpan,
  DateTimeOffset, Version or Uri, nor Guid to text; a by-ref-like target (Span) threw from TryConvert and DefaultValue.
- **Overridden accessors:** a property overridden with only a setter (or only a getter) lost the accessor it inherits:
  DeepEquals, PropertyDiff and Flatten skipped it, ExpressionAccessor and PropertyPath threw, and ObjectMapper skipped
  it. All resolve the inherited public accessor; a private getter no longer counts as readable. ObjectMapper with an
  interface source now includes the properties of the interfaces it extends.
- **PropertyDiff and DeepEquals:** a pair found equal while a cycle cut its walk short was memoized, so a second path to
  it hid real differences (only P.V was reported, not C2.Up.V); a pair is memoized only when no cycle from inside it led
  above it. DeepEquals of two dictionaries with different key comparers depended on argument order.
- **IPAddress and EndPoint** are values: their ScopeId/Address getters threw a wrapped SocketException out of
  DeepEquals, PropertyDiff and Flatten. A throwing getter now surfaces its own exception, not TargetInvocationException.
- **Shortest paths:** a finite path whose total overflows to ∞ (edges of 1e308) was reported unreachable, in
  ShortestPath.Dijkstra and GridPathfinding.Dijkstra/AStar. Only an infinite edge or cost is no edge.
- **Validation:** Interval and RangeMap accepted NaN endpoints (which acted as −∞); Graph.AddEdge(from, null) named
  "vertex" and left `from` added, and null vertices elsewhere named "key"; an invalid Connectivity was treated as Four
  in FloodFill, ConnectedComponents2D and GridPathfinding; a grid with non-zero lower bounds threw
  IndexOutOfRangeException; a wrong-typed array element in PropertyPath.Set threw InvalidCastException; a typed
  ExpressionAccessor with no conversion threw InvalidOperationException (now ArgumentException naming TValue);
  Unflatten with a null key threw FormatException.
- **Smaller:** Interval.ToString followed the current culture ("[1,5, 2,5)"); PropertyPath now indexes an
  `IDictionary<string, object>` such as ExpandoObject.
- **Doc fix:** PropertyPath.Set on a struct root writes into the box it is given (as PropertyInfo.SetValue does), now
  documented rather than rejected.
- **Not fixed:** a cycle between computed objects of two different types (A.ToB.ToA…) is still unbounded.
- **Verify:** all green (core 2834). Checked build: the same 7 baseline failures.
- **Re-queued:** yes (19 bugs, 3 severe).

### Round 39 — 2026-10-25 — Signal analysis, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 31 with 23 bugs, 2
severe). **13 confirmed bugs, 1 severe** (two found by both reviewers), each with a regression test in
`SignalReview3Tests` (12 tests, all failing on the old code).

- **Savitzky-Golay at high orders (severe):** the fit used a monomial basis, which loses every digit as the order grows:
  order 40 on 41 points was off by 0.24 at the edges, order 60 on 101 points was wrong at the centre (3.48 for 5.15),
  and order w − 1 was not the identity (off by 34 on data in [0, 1]). The weights are now rows of Q·Qᵀ for an orthonormal
  basis built by Arnoldi (orthogonalised twice), exact to about 1e-12 at every order; ordinary orders are unchanged.
- **FFT and Hilbert overflow:** Bluestein's convolution (every non-power-of-two length) overflowed to NaN about n times
  sooner than radix-2 ([1e308, 0, 0] gave NaN in every bin; 1000 samples of ±1e304 failed the round trip). The input is
  scaled to at most 1 by a power of two (exact) and scaled back. Hilbert's spectrum overflowed the same way for a large
  tone even at a power-of-two length; it scales likewise.
- **Hilbert with a non-finite sample:** one NaN or ∞ made the whole analytic signal NaN, and InstantaneousFrequency then
  threw naming "phase". Samples must be finite (ArgumentException naming samples), as in the other measurements.
- **Huge-rate scaling:** Multitaper's 1/(fs·K) underflowed to 0 at fs = 1e308 (every density 0); SpectralPeak's
  k·fs/N overflowed to ∞; FrequencyGrid.NearestBin's f·N overflowed and gave N/2 for an in-range frequency; DbFs read −∞
  (reserved for silence) or +∞ when rms/fullScale under- or overflowed. Each falls back to the order that stays in range.
- **CycleMeasurements:** a level whose hysteresis band edge was exactly the capture's minimum or maximum (level 0.9 on a
  0/1 square wave) could never arm or commit an edge and reported no cycles. Reaching the band edge now counts (with a
  band; with none, touching the level is still not a crossing).
- **Smaller:** PulseMeasurements on a denormal range ({0, ε}) halved the range to 0 and threw IndexOutOfRangeException;
  Resample.Linear blended an exact input point with a NaN or ∞ neighbour (0·∞ = NaN), as ToRate no longer does;
  SpectralPeak returned a NaN vertex for ∞ neighbours; Window.Apply on an empty list threw naming "length".
- **Verify:** all green (core 2846). Checked build: the same 7 baseline failures.
- **Re-queued:** yes (13 bugs, 1 severe).

### Round 40 — 2026-10-25 — IO, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 24 with 22 bugs, 2
severe). **19 confirmed bugs, 4 severe** (one found by both reviewers). 15 have regression tests in `IOReview3Tests`
(20 tests, all failing on the old code; the Dispose test crashed the old test process). Four are fixed without a test
because they can't be triggered on demand here: a timing race, a flush failure, a .NET Framework-only exception, and a
Win32 error code.

- **DirectoryUtils.Copy lost the file it was replacing (severe):** the destination was deleted before File.Copy, so a
  copy that then failed (source locked, disk full) left neither version. Each file is now copied to a sibling temp
  file and moved over the target. That still breaks a hard link at the target instead of writing through it.
- **FileWatcher.Dispose could end the process (severe):** after its 5 s wait for an in-flight tick timed out, Dispose
  disposed the wait handle. The timer signalled it when the tick ended, which threw ObjectDisposedException on the
  timer thread. The handle is now left to the timer when the wait times out.
- **Tar metadata bomb (severe):** TarReader allocates a PAX/GNU metadata entry's declared size (up to 2 GB) before
  reading it, so a 1.5 KB archive could demand 1.5 GB. A forward-only guard stream now follows the headers (including
  PAX size overrides) and rejects metadata entries over 1 MB with InvalidDataException.
- **FileWatcher read the caller's options live (severe):** setting Filter to null after construction threw from Start,
  or on the OS watcher's thread, which ended the process. The options are copied in the constructor, and OnRaw now
  routes every non-fatal exception to Error.
- **Archive create destroyed an existing archive:** Tar and Zip CreateFromDirectory opened the archive with
  FileMode.Create, so a failed run deleted a good archive. Both now write a temp file and move it into place after the
  writers' final flush. In tar the "completed" flag used to be set before that flush.
- **Archive fidelity:** extraction now restores tar and zip modification times (directories last, deepest first); tar
  also keeps Unix permission bits less group/other write. Zip creation stamps each entry with the file's time instead
  of now, and keeps empty directories as `name/` entries, as tar does. Tar directory entries carry their times.
  ReadEntryText now honours and drops a byte-order mark.
- **Reserved device names:** on Windows a tar or zip entry named `CON`, `aux.txt`, `x/COM1.log` and so on was written
  to the device and reported as success. Such entries are now rejected. A tar that ends inside a header threw
  EndOfStreamException; it now throws InvalidDataException like any other damage. On .NET Framework, a zip entry name
  with `<`, `|` or `"` threw ArgumentException; it now throws IOException.
- **FileWatcher coalescing and rescans:** for a→b, c→a then deleting (or overwriting) b, the Deleted reported for a
  overwrote the pending c→a. That lost c's disappearance and reported a deleted although a existed. The snapshot used
  Directory.GetFiles, which reads `?` as "one character or none", so with filter `a?.txt` every rescan reported a.txt.
  Snapshots now list everything and filter with the watcher's own matcher. Rescans are serialized, and a diff that
  went stale while the listing was taken is checked against the disk.
- **Search patterns with a directory part:** `..\*` made Directory.GetFiles list the parent folder. DirectoryUtils.
  EnumerateFiles, DirectorySnapshot.Take and the FileWatcher filter now reject `/`, `\`, NUL and rooted patterns.
- **Smaller:** ChecksumManifest.Verify and CompareDirectories ignored the fileFilter the manifest was created with and
  reported the excluded files as added; both now take one. With PreserveTimestamps = false, Windows copies still kept
  the source's time. EnsureEmpty accepted negative retries. AtomicFile deleted the temp file after a File.Replace
  failure that had already removed the original; that temp file was the only copy of the data, and it is now moved
  into place or kept.
- **Verify:** all green (core 2866). Checked build: the same 7 baseline failures.
- **Re-queued:** yes (19 bugs, 4 severe).

### Round 41 — 2026-10-25 — Windows satellite, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 22 with 26 bugs, 1
severe). **12 confirmed bugs, 2 severe.** 11 have regression tests in `WindowsReview3Tests` (11 tests, all failing on
the old code). The JobObject lifetime bug has no test because it depends on GC timing. The hive-wipe test uses an
invalid hive, so it can never delete anything; on the old code it shows only that the subkey guard was missing. The
wipe itself was confirmed from .NET's source and on a scratch key.

- **RegistryUtils.DeleteKey(hive, @"\") deleted the whole hive (severe):** RegistryKey refuses an empty subkey, but
  its guard runs before it trims backslashes, so `\` reached RegDeleteTree as the hive itself. A subkey that is empty
  or only backslashes is now rejected (ArgumentException naming subKey) before the hive is opened.
- **SingleInstanceApp: a second launch could hang for good (severe):** after the primary sent its id, the message was
  written with a synchronous write and Flush. Flush on a pipe waits for the reader, outside the forward timeout. If
  the primary stopped reading (suspended, starved, at a breakpoint), the launch never returned. The write is now
  async and bounded by the timeout, and the Flush is gone because the acknowledgement already proves delivery.
- **SingleInstanceApp message budget:** the client counted length headers against the 1 MiB limit and the primary
  does not, so a launch the primary would accept (1000 arguments of 1045 characters) was refused and lost. The client
  now applies the primary's budget. The doc no longer claims another account cannot block launches: pipe names are
  machine-wide.
- **DpiInfo:** SystemDpi and ForWindow answered as the caller or window sees it, so a DPI-unaware thread or window
  read 96 on a 200% display. ForWindow promises the monitor's DPI. Both now query per-monitor-v2 (ForWindow through
  MonitorFromWindow and GetDpiForMonitor). AwarenessScope with an undefined value named the private parameter "a".
- **RegistryUtils values:**
  - A high-bit DWORD read into an int-backed enum was widened, overflowed, and silently read as the default. An enum
    as wide as the stored value now takes its bits.
  - SetValue rejected `0x80000000u` as a DWORD (and `ulong.MaxValue` as a QWORD), and stored an unknown-kind uint as
    text, although GetValue reads DWORDs as unsigned. A uint or ulong is now stored as the DWORD or QWORD with the
    same bits.
  - DeleteValue checked existence with GetValue, which is null for kinds .NET does not read (REG_RESOURCE_LIST,
    REG_LINK), so it reported such a value missing and left it in place.
- **RegistryUtils arguments:** an undefined hive threw IndexOutOfRangeException from inside RegistryKey and is now
  ArgumentOutOfRangeException naming hive. A key segment over 255 characters threw naming "name": reads now return
  the default as documented, and writes name subKey.
- **Smaller:** JobObject.AssignProcess(Process) let the Process become unreachable while its raw handle was being
  assigned, so a collection could close it (GC.KeepAlive). ResourceSampler.Measure with an interval over about 24.8
  days threw from Thread.Sleep naming "timeout"; it is now checked up front.
- **Verify:** all green (core 2866, Windows 112). Checked build: the same 7 baseline failures.
- **Re-queued:** yes (12 bugs, 2 severe).

### Round 42 — 2026-10-25 — Concurrency, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 21 with 25 bugs, 1
severe). **8 confirmed bugs, 1 severe.** 7 have regression tests in `ConcurrencyReview3Tests` (8 tests, all failing on
the old code). The WhichExe bug happens only on .NET Framework, and the tests run on net8.0, so it has no test; it was
confirmed on net48.

- **ProcessRunner: a child inherited the caller's stdin (severe):** stdin was redirected only when `StandardInput` was
  set, although the doc said "Null leaves stdin unused". The child read the caller's data (`sort` printed it back). A
  child that reads stdin blocked on an interactive console until the timeout, or for good when there was none. Stdin
  is now always redirected, and closed at once when there is nothing to send.
- **TokenBucketRateLimiter under-granted:** refills summed in small steps fell short by rounding. Ten 100 ms steps at
  1/s left 0.9999999999999999 tokens, so the due token was refused, and polling every 10 ms at 10/s granted 90 a
  second instead of 100. The compare now has a tolerance of one part in a billion.
- **PeriodicWorker:** a delay that threw its own OperationCanceledException (the worker not disposed) stopped the loop
  without reporting it. Only a cancellation of the worker ends the loop quietly now; any other is reported, as a
  failing delay is.
- **AsyncEvent:** InvokeParallelAsync unwrapped an AggregateException that a handler threw itself, so parallel and
  sequential invokes reported the same failure differently. Both now report the handler's own exception.
- **Jitter:** a NaN from the random source passed the range check and gave a delay of about -10.7 million days, which
  failed far away in Task.Delay. It is now rejected naming random, as 1.0 is.
- **RetryPolicy:** a null DelayAsync replaced the operation's exception with a NullReferenceException, and a null
  ShouldRetry threw inside the exception filter, where the runtime swallows it, so the policy silently never retried.
  Both are now rejected up front.
- **CreateLinkedTimeout:** a timeout past the runtime's limit threw naming "delay", a parameter the caller never saw.
  It now names timeout and says what the limit is.
- **WhichExe:** on .NET Framework, a name with invalid path characters ("a|b") threw from Path.HasExtension instead of
  returning null. It now returns null on both runtimes.
- **Verify:** all green (core 2874, Windows 112). Checked build: the same 7 baseline failures.
- **Re-queued:** yes (1 severe).

### Round 43 — 2026-10-25 — Logging, diagnostics, CLI, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 29 with 17 bugs, 1
severe). **18 confirmed bugs, 1 severe.** All have regression tests in `LoggingReview3Tests` (19 tests, all failing on
the old code). DiskSpace's invalid-character case on .NET Framework has no test, since the tests run on net8.0.

- **AsyncLogSink: Dispose from the consumer deadlocked (severe):** an inner sink or onError that disposed the sink (a
  fatal event shutting logging down) waited for the consumer thread, which was itself. Disposing from the consumer now
  completes the queue and closes the sink after the consumer drains it, without waiting; onError runs as consumer code.
- **RateLimitedSink:** a clock stepping back left the window "current" until the clock caught up, muting every repeat
  for that long. A window that starts in the future is now a new window. Dispose leaked an owned inner sink when the
  final summary threw; the inner sink is now always disposed.
- **RollingMemorySink:** a large capacity was allocated up front; `int.MaxValue` threw OutOfMemoryException. The
  queue now grows on demand.
- **SyslogSink:** an over-long host name threw ArgumentException out of Emit instead of counting a failed send.
- **HealthCheck:** an undefined failureStatus was accepted and reported as itself. Only Degraded or Unhealthy is
  allowed.
- **ConsoleApp:** an ExitException or ArgumentException wrapped in an AggregateException (a `.Result` in the body) exited
  70 instead of its code or 2. An exception whose Message or ToString threw, or an error writer that threw anything but
  IOException, replaced the exit code with a crash. All now keep the code.
- **MetricsRegistry:** counters wrapped from long.MaxValue to negative. They now saturate (unchecked add, then clamp).
- **FirstChanceMonitor:** a signature's count wrapped from int.MaxValue to negative. It and Suppressed now saturate.
- **DiskSpace.Preflight:** a bad path threw naming "path", a parameter the caller never saw. It now names directory.
- **Prompt:** Confirm and Choose threw NullReferenceException for a null question. MaxAttempts below 1 said "after 0
  attempts" when one was made.
- **FileSink:** a relative path followed the current directory, so rotation after a directory change wrote and moved
  files in the new directory. The path is now fixed when the sink is created.
- **StructuredTextFormatter:** a value whose end ran into the following delimiter ("x-" then "--") formed a delimiter
  and the line split one place early. Such a tail is now blanked. The fields list was kept by reference, so clearing
  the caller's list emptied every line; it is now copied.
- **ExceptionUtils.IsTransient:** an aggregate's answer depended on member order. A permanent I/O failure in any member
  now makes the whole non-transient.
- **Sparkline:** a max given below the data (or a min above it) drew the wrong level; the range now collapses to the
  given bound and values past it draw at the top or bottom.
- **Verify:** all green (core 2893, Windows 112). One Windows test failed in 2 of about 35 full runs, both under the
  verify script, whose output kept only the summary line, so the test is not yet named; this round changed nothing in
  the Windows package. Verify now keeps each suite's full output so the next failure names it. Checked build: the same 7 baseline
  failures.
- **Re-queued:** yes (18 bugs, 1 severe).

### Round 44 — 2026-10-25 — Collections, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 25 with 11 bugs, 1
severe). **13 confirmed bugs, 1 severe.** 11 have regression tests in `CollectionsReview3Tests` (14 tests, all failing
on the old code). The Deque and heap overflows need 1-2 GB arrays, so they were confirmed with a probe, before and
after the fix, rather than a test.

- **BloomFilter and CountingBloomFilter: 2.5x to 20x the false-positive rate (severe):** the two FNV-1a hashes used for
  double hashing had tied low bits (FNV-1a's low bits depend only on the input's low bits, and the offsets differ in
  bit 0), so with an even bit count every item probed only odd or only even bits. At n = 1000, p = 0.001 the rate was
  about 0.018. A murmur3 finalizer now mixes each hash, and the rate meets the target. Filters are not persisted, so
  nothing stored is invalidated.
- **Aggregation.Summarize:** values at or near double.MaxValue still gave a mean of ∞ and a deviation of NaN: log2 of
  them rounds to 1024 and 2^1024 is ∞. The scale is now capped at 2^1023.
- **Deque.PushFront:** `_head - 1 + length` overflowed int above a 2^30 capacity, threw, and left the deque broken.
- **BinaryHeap, TopN, IndexedPriorityQueue:** SiftDown's `2 * i + 1` overflowed past 2^30 elements and indexed out of
  range. A leaf now stops the loop before the children are computed.
- **ReservoirSampler:** once its acceptance weight fell below 2^-53 (about 1e16 items at capacity 1), log(1 - w) was 0,
  the skip was -∞, and every later item replaced the sample. A series keeps log(1 - w) accurate for small w. This was
  an unconfirmed note in round 25.
- **OrderedSet, OrderedDictionary, LruCache:** a key whose hash threw on insert left an orphan node in the order list:
  the count said 0 while enumeration gave the key, and LruCache later evicted the orphan and went over capacity. The
  map is now written first.
- **BiMap:** a value whose hash threw left the key mapped with no reverse entry. The forward entry is rolled back.
- **IndexedPriorityQueue:** a key whose hash threw left a heap slot that Dequeue later returned. The index is now
  written first.
- **Deque and CircularBuffer:** emptying the collection before the first MoveNext went unnoticed (the version was only
  checked after a yield). It is now checked before each step and at the end.
- **CartesianProduct.Of(first, second, ...):** a null argument threw ArgumentException naming "sequences". It now
  throws ArgumentNullException naming first or second (ArgumentException naming rest for a null in rest).
- **SamplingPlans.WeightedWithoutReplacement:** a length mismatch threw with no ParamName; it names weights.
- **DataTableLite:** a bad column ordinal threw IndexOutOfRangeException, a bad row named "index", `Select` with a null
  name blamed "column", and a join name collision blamed other instead of rightPrefix. All now name the right
  parameter, with ArgumentOutOfRangeException for out-of-range indexes.
- **Not a bug:** the reviewer's expected finite deviation for [Max, Max, -Max]; the true value, √(4/3)·Max, overflows.
- **Verify:** all green (core 2907, Windows 112). Checked build: the same 7 baseline failures (the first run caught a
  `(uint)` range check that throws OverflowException when checked; replaced with plain comparisons).
- **Re-queued:** yes (13 bugs, 1 severe).

### Round 45 — 2026-10-25 — Statistics, third pass
Reviewers: one for correctness and one for robustness, fresh to the section (re-queued from round 23 with 22 bugs).
**25 confirmed bugs, 1 severe** (24 from the reviewers, with the Uniform overflow reported by both, plus a NormalPdf
overflow found while testing). All have regression tests in `StatisticsReview3Tests` (27 tests, all failing on the old
code). Most came from sums and squares formed before rescaling, so they are fixed by scaling by a power of two first.
Normal-range data are scaled by 1, so their results are bit-identical.

- **MultipleRegression (severe):** a response or predictor near 1e±160 made the sums of squares and (XᵀX)⁻¹ underflow
  or overflow. R² came out 0 or NaN, standard errors 0 or ∞, and p-values NaN. Each column and y is now scaled by its
  largest magnitude, the fit is solved in those units, and the coefficients, errors and intervals are scaled back.
  TheilSen's length mismatch now names y.
- **Sums near double.MaxValue:**
  - **OneSampleT, TwoSampleT and PairedT:** sums of values near double.MaxValue overflowed. The data are now
    pre-scaled, and a mean difference that overflows is formed from halves. PairedT now validates second and names it.
  - **Pearson, LinearRegression and ControlChart.Individuals:** the same overflow, fixed the same way.
  - **ChangePoint.Cusum and DetectShift:** the same overflow, fixed the same way.
  - **PermutationTest.Paired:** the null statistics overflowed to NaN for differences near double.MaxValue.
- **Squares near 1e±160:**
  - **ConfidenceInterval.ForMean, ForMeanT, ForStandardDeviation and ForVariance:** squares near 1e±160 underflowed or
    overflowed (intervals of 0 or ∞). They are now built from a scaled standard deviation, and constant data give a
    point interval.
  - **ProcessCapability:** the same underflow and overflow, fixed the same way.
  - **MeasurementAgreement.BlandAltman:** the same underflow and overflow, fixed the same way. Its length mismatch now
    names methodB.
- **Anova.OneWay and GaugeRR:** a sum of squares of zero times an overflowing s² gave NaN; zero now stays zero. Data on
  a large common offset (1e9 + 0.1 …) lost their effects to rounding. The means are now offsets from the first value,
  and F is exact again.
- **Distributions:**
  - **Incomplete beta (Distributions and SpecialFunctions):** at a = 1e20 both x and the branch split rounded to 1.
    FCdf(0.5, 1e20, 1000) returned the wrong tail and FSurvival went negative. The branch is now chosen from whichever
    of x and 1 − x is smaller.
  - **Incomplete beta, tiny shapes:** values could step just outside [0, 1]. They are now clamped.
  - **Incomplete gamma at a subnormal shape:** P was NaN, because 1/a overflowed. Q is now computed directly.
  - **Subnormal degrees of freedom:** these are now rejected, because halving them loses the shape and the tails were
    NaN.
  - **UniformPdf and UniformCdf, NormalCdf, NormalSurvival and NormalPdf:** b − a, x − mean and sd·√(2π) overflowed for
    bounds near ±MaxValue. Each is now formed from halves or divided in turn.
  - **FPdf:** at large df the direct form cancelled terms of size ~df and lost about df·eps (1.6e-6 relative at df =
    1e10). It now uses Loader's saddle-point binomial form, as R's df does.
- **SpecialFunctions:**
  - **Gamma:** Gamma(5) was 23.99999999999996, and exp(lnΓ) carried lnΓ's absolute error as a relative one (1.5e-13 at
    170). It now uses the recurrence down to (1, 2], and Γ(1 + x)/x below 0.2.
  - **Gamma reflection:** sin(π·x) rounded away x's distance from the pole, so Γ(−20.000001) was off by 3.5e-9
    relative. A reduced SinPi now feeds Gamma and lnΓ.
  - **Infinite shapes:** Beta(∞, b) and RegularizedBetaI with an infinite shape gave NaN. They now give the limits; both
    shapes infinite gives NaN, as documented.
- **DistributionFit:**
  - **FitWeibull:** at a tiny shape, the scale's power underflowed to 0 or overflowed. It now falls back to logs.
  - **FitExponential:** a subnormal mean threw naming a "rate" parameter the caller never passed. It now names sample.
- **PermutationTest argument names:** these now report the right parameter (a, b, first, second).
- **Verify:** all green (core 2934, WPF 134, WinForms 82, tooling 38). Windows was 111/112: the job-object test
  `Job_ActiveProcessLimitAndKillOnCloseSurvive` failed once under the full run and passed three times on its own, so it
  is a load flake. The checked build has the same 7 baseline failures. Two timing tests (async sinks, process output)
  starved during a one-core run and pass on their own.
- **Re-queued:** yes (25 bugs, 1 severe).

### Round 46: flaky-test root causes, and the huge-shape incomplete beta (v0.46.0)

No review subagents this round. Three tests flaked in round 45's verify, and each one was traced to its cause rather
than retried. A running bug list (`BUGLIST.md`) now tracks every suspected and confirmed bug until its fix is released.

- **ConcurrentPipeline and AsyncLogSink (B1):** the consumer resumed on the thread pool after each wait, so producers
  blocked for space on pool threads starved it. With the pool capped at 2 threads, 2 blocking producers moved 0 of 200
  items in 60 s; uncapped, each item waited for thread injection. The consumer now runs on a dedicated background
  thread (200 items in under 0.1 s). This was the "flaky" `AsyncSinks_ForwardingBothWaysDoesNotDeadlock`.
- **ProcessRunner (B3):** output was read with `BeginOutputReadLine` and stdin written with `Task.Run`, both on the
  pool. Two `Run`s on a pool capped at 2 threads both timed out, with 0 and 4 KB of 660 KB captured. The readers and the
  writer are now dedicated background threads (3.2 s, all captured). This was the "flaky"
  `HandlesLargeOutputWithoutDeadlock`.
- **JobObject (B5):** the docs promised reliable orphan prevention, but assigning after `Process.Start` leaves a window.
  With a 200 ms stall before `AssignProcess` (a preemption or GC pause), cmd's ping survived kill-on-close in 10 of 10
  runs. The new `JobObject.Start` creates the process suspended, assigns it, then resumes it. A refused process
  (over the active-process limit) is terminated before it runs. The docs now describe the window.
- **Job test (B2):** after a refused assign, `ActiveProcesses` read 2 because a console program's conhost starts inside
  the job and counts, even under a limit of 1. This is not a JobObject fault. The test now checks the refusal instead
  (the refused process survives the job), and `SetActiveProcessLimit` documents the count.
- **Incomplete beta at huge shapes (B4; Distributions, SpecialFunctions, Anova, GaugeRR):**
  - The continued fraction multiplied m(b − m) and (a + m)(a + b + m) out before x scaled them down. Both overflowed
    at shapes near 1e300, so `RegularizedBetaI(5e-324, 2, MaxValue)` and `FCdf(0.5, 1e10, 1e300)` were NaN. Each term
    is now a product of bounded ratios.
  - When a + b overflows, every λ form was NaN (`RegularizedBetaI(0.5, MaxValue, MaxValue)`). The smaller shape is
    then above ~1e292, so I is a step at the mode, decided by x·b against (1 − x)·a.
  - At subnormal shapes the prefix underflowed before its division by a, so `RegularizedBetaI(5e-324, 5e-324, 5e-324)`
    was 0, not ½. It now divides inside the exponential when the prefix would underflow, and skips the fraction when
    the whole underflows.
  - `FQuantile(0.5, 1e10, 1e300)` was 2.5e-34 and `FQuantile(0.5, 1e300, 1e300)` was 0, both searched against NaN
    CDFs. They are now 0.9999999999333333 and 1 − 2e-16.
- **Stale drop-in copies (B6):** `Anova.cs` and `GaugeRR.cs` had missed round 45's incomplete gamma and beta fixes.
  Their `BetaI(0.3, 1e-100, 1e-20)` gave 1.0000000000000173 and a negative tail, and `GammaPQ(5e-324, 0.5)` gave NaN.
  They are synced, and a new test checks, through reflection, that all four copies give bit-identical results.
- **Left open (B7, confirmed):** `FCdf(0.5, 5, 1e300)` is off by 1.0e-13 relative. With a small and b huge, the beta
  prefix cancels terms of about 1700. This is next, in the differential harness.
- **Tests:** `Threading/FlakeRootCauseTests` (3), `Numerics/HugeShapeTests` (6), and Windows `FlakeRootCauseTests`
  (3). Each one failed on the old code; the Windows tests did not compile, since there was no `Start`.
- **Verify:** all green (core 2943, Windows 115, WPF 134, WinForms 82, tooling 38). The checked build has the same 7
  baseline failures. The verify scripts now use `cw run` in place of the retired low.cmd.

### Round 47: differential harness for the incomplete beta and gamma (v0.47.0)

A cross-section round on section 3's numerics. Its fixes are counted in section 3's totals.

- **Harness:** `tests/ToolBelt.MathChecks/IncompleteFunctionsGradedTests` grades `RegularizedBetaI` at 6880 points and
  `RegularizedGammaP`/`Q` at 848 values. Shapes run from 1e-300 to 1e20, with x at multiples of the mean and at fixed
  points, plus mirror points (fl(1 − x), b, a) that reach the complement path. The references are 50-digit mpmath
  values from `scripts/gen-incomplete-references.py`, computed without cancellation: all-positive series, a
  complement formed at rising precisions until two agree to 1e-30, or quadrature over the smaller tail. Errors are
  graded in units of eps·(1 + κ), where κ is the condition number from 50-digit central differences, and over 16
  fails. Rows over 16 eps that lack a κ go to a need file for the generator's `kappa` step.
- **B7 (huge other shape):** `FCdf(0.5, 5, 1e300)` was off by 1.0e-13. The prefix's p·ln x and p·ln(a + b) were each
  about 1700 and cancelled. They are now merged as p·ln(x·(a + b)).
- **B8 (P near 1 at tiny a):** `RegularizedGammaP(1e-300, 1e-10)` was 0.9999999999999791, 94 eps from 1 at κ ≈ 0. The
  series prefix carried lnΓ(a)·eps. P is now 1 − Q from the accurate small-shape Q.
- **B9a (the small tail of a tiny shape):** with b < 1 above the split, or a < 1 below it, the small tail was formed as
  1 − the complement. `I_0.7(1, 1e-20)` gave 3.6e-15 where the true value is 1.2e-20, and `I_0.99(50, 1e-300)` gave
  2.8e-14 where the true value is 5.6e-301. A new `SmallShapeTail` sums it directly, by series or by series + BGRAT.
- **B9b (the min-shape factor in the prefix):** 1/B(a, b) and 1/Γ(a) carried ln(min shape) inside the exponential.
  ln(1e-300) = −690.8 costs about 250 eps. So `I_0.01(1e-300, 50)` was 1 − 2.8e-14, `Q(1e-300, 2)` and
  `ChiSquarePdf(1, 2e-300)` were 2.3e-14 off, and BGRAT's terms were off in the same way. The factor is now applied
  outside the exponential (`GammaFront`, `LnPBeta`, `LnGamma1p`).
- **All four drop-in copies** (Distributions, SpecialFunctions, Anova, GaugeRR) are changed. The copies-agree test
  gained the new cases.
- **Tests:** `Numerics/DifferentialHarnessTests` (4) failed on the old code, and asserts that did not fail there were
  removed. `HugeShapeTests`' copies cases were extended. The graded harness is all green: 0 of 7728 values over 16
  units.
- **Verify:** all green (core 2947, Windows 115, WPF 134, WinForms 82, MathChecks 40). The checked build has the same 7
  baseline failures.

### Round 48: Security, net, time, third pass (v0.48.0)

Reviewers: one for correctness and one for robustness, fresh to the section. The robustness reviewer also ran the
section on .NET Framework 4.8 and in an overflow-checked build. **16 confirmed bugs**, two of them severe. Each has a
regression test in `SecurityNetTimeReview3Tests` (18 tests). 15 of them fail on the old code; the other 3 fail only in
the checked build or on .NET Framework, where probes confirmed them.

- **Severe: lone surrogates made different passwords equal.** `KeyDerivation.Pbkdf2(string)`, and so
  `PasswordHasher`, encoded with the lenient UTF-8 encoder, which turns every lone surrogate into U+FFFD:
  `Verify("pw\uD800", Hash("pw\uDBFF"))` was true. They now encode strictly and throw `ArgumentException`
  (`password`). `Hashing`, `Hmac` (`text`) and `AuthenticatedEncryption.Encrypt(string)` (`plaintext`, which
  decrypted to U+FFFD) do the same. A `SecretsFile` passphrase threw `EncoderFallbackException` with no ParamName,
  and now throws `ArgumentException` (`passphrase`) up front, as `Set` does.
- **Severe: the checked build.** Narrowing casts threw `OverflowException` on ordinary input:
  - `IpUtils.NetmaskFromPrefix(24)`;
  - `CidrRange` with any prefix that is not a multiple of 8, `BroadcastAddress` and `Addresses()`;
  - `Pbkdf2` past block 255 (a key over 8160 bytes).

  These were 6 of the 7 "baseline" checked-build failures carried since round 21. The other baseline failures, and
  the same trick elsewhere, are fixed too: `(uint)index >= (uint)count` threw for a negative index in `Guard.Index`,
  `CircularBuffer`, `Deque`, `FenwickTree`, `DisjointSet` and `ConsoleTable.AlignColumn`, and so did
  `Palettes.FromHex`. The checked build now has no failures.
- **IpUtils.IsLoopback:** an IPv4-mapped address was not unwrapped. `::ffff:127.0.0.2` was false, and on .NET
  Framework so was `::ffff:127.0.0.1`.
- **CidrRange.TryParse:** it accepted `[::1]:80/128` (the port was silently dropped) and scope ids, with
  `fe80::1%eth0/64` and `::ffff:010.0.0.1/104` taken differently on each runtime. IPv6 text must now be hex groups
  with an optional decimal dotted quad.
- **PortCheck and TcpLineClient on .NET Framework:** a plain `TcpClient` is IPv4-only there, so `"::1"` threw
  `ArgumentException`. `IsReachable` let it escape, and `TcpLineClient` leaked the socket. Both now use a dual-mode
  IPv6 socket, and a connect that throws synchronously is handled.
- **HttpDownload on .NET Framework:** cancelling during the body threw `IOException` (the disposed response aborted
  the read). It is now `OperationCanceledException`.
- **CronSchedule:**
  - The zone overload started its search a day in, so from `DateTimeOffset.MinValue`, "0 12 * * *" skipped
    0001-01-01. It now starts at the first minute whose wall time is in the calendar.
  - `GetNextOccurrences(after, int.MaxValue)` pre-sized its list and threw `OutOfMemoryException`.
- **CronScheduler:**
  - Two jobs that called `Dispose` in the same minute each waited for the other, forever. Jobs already inside
    `Dispose` are no longer waited for.
  - `Add` after `Start` threw for a schedule with no occurrence. It now gets a null `NextRun` and the failure goes
    to `onError`, as at `Start`.
  - `Start` and `Add` after `Dispose` silently did nothing. They now throw `ObjectDisposedException`.
- **ParamNames:**
  - `BusinessDays.AddBusinessDays` past the calendar's ends blamed `value` (from `AddDays`), not `count`.
  - `UnixTime.FromUnixSeconds` and `FromUnixMilliseconds` blamed `value`, not `seconds` and `milliseconds`.
  - `KeyDerivation.GenerateSalt(-1)` blamed `count`, not `length`.
- **Checked and found correct:** AES-GCM tag, size and tamper checks; the `SecretsFile` header bounds and buffer
  zeroing; constant-time compares; unbiased `CryptoRandom`; `PasswordHasher.Parse`; unsatisfiable cron expressions in
  four zones; the duration parsers and formatters at the extremes; the HttpDownload idle timeout and Content-Range
  checks; the round 13 and 27 fixes.
- **Checked build:** no failures (was 7).
- **Re-queued:** yes (16 bugs, 2 severe).

### Round 49: WPF satellite, third pass (v0.49.0)

Reviewers: one for correctness and one for robustness, fresh to the section. The robustness reviewer also ran the
section in an overflow-checked build. **20 confirmed bugs**, none severe. 19 have a regression test in
`WpfReview3Tests` (17 tests, all failing on the old code). The checked-build overflow fails the existing
`ThemeManagerTests.DefaultPalette_HasFrozenBrushesThatDiffer` when the old code is built checked.

- **EnumToBooleanConverter:** a comma list such as `"Green,Blue"` parsed for an enum without `[Flags]`, so the
  converter wrote back the undefined `(Color)3`, and `Convert` compared against it. Lists are now accepted only for
  `[Flags]` enums.
- **ValidationObservableObject:**
  - `SetErrors` raised `ErrorsChanged` even when the list was unchanged, so re-validating a valid property on every
    keystroke raised events for nothing.
  - `HasErrors` was announced on every change, even going from one error to two. It is now raised only when it
    flips, and `ClearAllErrors` raises it once.
- **NumericInputBehavior:**
  - Deletions were never checked. Delete, Backspace, Cut and the word deletions on `"10.5"` gave `"105"`, past a
    maximum of 100. They are now checked like typing. Inside a multi-character minus sign, the whole sign is
    removed.
  - `-0.001` at two places was formatted as `"-0.00"`. The value is now rounded before the zero test.
  - A negative `DecimalPlaces` allowed a separator, which `Coerce` treats as 0 places.
- **PlotPresenter:** `PanBy`, `ZoomAt` and `ZoomAbout` accepted NaN and infinite input. This left a view that is not
  invertible, so every later `ViewToContent` returned NaN. They now throw `ArgumentOutOfRangeException` (`delta` or
  `viewPoint`), and a pan that would overflow is ignored.
- **Messenger:**
  - A value-type recipient was boxed into an orphaned box. Its registration vanished at the next GC, and it could
    never be unregistered. It now throws `ArgumentException` (`recipient`).
  - The docs now say delivery matches the type argument, not the runtime type.
- **GlobalHotkeys:**
  - The `HotkeyGesture` constructor accepted undefined modifier bits, undefined keys and `ImeProcessed`. This made
    gestures that `Parse` would reject, and `ToString` printed `"Ctrl+5000"`. The constructor now validates as
    `Parse` does.
  - Registering a gesture that the same instance already holds blamed "another application" (`Win32Exception`). It
    now throws `InvalidOperationException`, and `TryRegister` returns false.
  - After the dispatcher shut down, `TryRegister` registered a hotkey that could never fire. It now throws
    `ObjectDisposedException`.
- **ThemeManager:**
  - With `FollowSystem` on, a broken dictionary or a throwing `ThemeChanged` handler made an OS theme switch an
    unhandled dispatcher exception. The current theme now stays, and the new `SystemThemeApplyFailed` event reports
    the error (or `Trace` does, if there is no handler).
  - The built-in palette threw `OverflowException` in a checked build.
- **WpfDialogService:** an owner window not yet shown, or already closed, threw `InvalidOperationException` from
  the file and folder dialogs. A window with no handle now owns no dialog.
- **BindingFailureListener:** `PresentationTraceSources.Refresh()` reset every other WPF trace source (routed
  events, animation and so on) to Off. Their levels are now saved and restored.
- **IncrementalCollection:**
  - When callers share one load, one caller's token cancelled the load for all of them. Now each token cancels
    only that caller's wait, and the load is cancelled once every waiting caller has cancelled.
  - If a loader's cancellation callback threw during `ResetAsync`, the list was left half reset: items kept and
    `IsLoading` true for good. The reset now completes, and then the exception propagates.
- **NotifyTaskCompletion:** a throwing `PropertyChanged` handler stopped all the remaining notifications (9 of 10
  names were never raised) and the other handlers. Every handler now gets every name, and the exception is rethrown
  afterwards on the synchronization context.
- **FileDropBehavior:**
  - The once-per-drag filter cache was keyed only on the data object. A source that refilled the same object with
    other files got the previous verdict. The key now includes the paths, and the cache is cleared on drop and on
    drag leave.
  - A drop source that advertised files but failed to render them made `GetData` throw a `COMException`, which
    escaped the drag handlers. It is now treated as no files.
- **Checked and found correct:** the round 16 and 33 fixes; command threading; `RecordingDialogService`
  strictness; `ValidationRules`; `BindingProxy`; the multi-value converters; the hotkey id range.
- **Checked build:** the WPF suite in a checked build is green (151 tests). The core is unchanged this round.
- **Re-queued:** yes (20 bugs).

### Round 50: Text & small types, third pass (v0.50.0)

Reviewers: one for correctness and one for robustness, fresh to the section. Both also ran the section on .NET
Framework 4.8 against the netstandard2.0 build, and the robustness reviewer ran it in an overflow-checked build.
**17 confirmed bugs**, none severe (two found by both reviewers). Each has a regression test in `TextReview3Tests`
(16 tests). 14 fail on the old code on net8. The other two fail only where the bug lives: the long query string
on net48 and the Roman overflow in a checked build. The net48 seed collisions were confirmed with the reviewer's
probe instead (3 and 4 duplicates in 150k before, 0 in three runs after). The section's tests are green on net48
(309).

- **SnowflakeIdGenerator:**
  - The sequence was advanced before the "past the last timestamp" exception, so the call after it returned a
    smaller id that repeated an earlier one. State now changes only after the range check.
  - A clock 0.5 ms before the epoch truncated to 0 and was accepted. It now throws.
  - The clock went through a double, so x.9999 ms rounded up into the next millisecond (an id stamped in the
    future). Milliseconds are now floored from the ticks.
- **UlidGenerator:** the same two clock bugs (before the Unix epoch, rounding up). On .NET Framework, its default
  random source was seeded from `Guid.GetHashCode()`, giving only 2^31 streams, so separate generators repeated
  ULIDs (4 in 150k). It now draws from the OS generator.
- **NanoId:** the same seeding on .NET Framework repeated ids across instances (3 in 150k). It now draws from the
  OS generator.
- **Slug:**
  - Noncharacters made `Normalize` throw: U+FFFE on net8, and U+FFFF, U+FDD0..FDEF and the astral ones on .NET
    Framework. They now become separators, and anything else the normalizer refuses does too.
  - `separator` was not checked. 'É' gave "aÉb", which slugified again to "aeb", and '/' gave a path. It must now be
    '-', '_', '.' or '~' (`ArgumentException`).
- **QueryString.Format:** on .NET Framework, a key or value over 65,519 characters threw `UriFormatException`. Text
  is now escaped in pieces.
- **HammingDistance.BitDistance:** returns `long`. An `int` wrapped negative past 2^31 differing bits.
- **RomanNumerals:** input longer than the longest numeral (15 characters) is rejected before summing. A huge
  "MMMM..." threw `OverflowException` from `TryFromRoman` in a checked build.
- **GlobMatcher:** with `ignoreCase`, a bracket class disagreed with the same literal: `[K]` (Kelvin sign) didn't
  match "k" but `K` did, and the same for ſ, µ and the digraphs. It disagreed on 35 BMP characters. The class now
  also tries the members whose case forms are the character, from the runtime's own tables.
- **AnsiText / TextAlign:** DCS, SOS, PM and APC strings (ESC P, X, ^, _) were taken as two-character escapes, so
  their payload (a kitty graphics image, say) leaked into `Strip` and the widths. They now run to ST or BEL like OSC.
- **TextAlign:**
  - U+FE10..FE19, U+FE50..FE6B and U+A960..A97C (East Asian Wide) counted as one column.
  - Padding after an unfinished escape at the end of the text was swallowed by it, so `Fit`/`PadRight`/`Center`
    measured 2 or 4 instead of the width, and a cut lost its ellipsis. The unfinished escape is now dropped when
    something goes after it.
- **PercentEncoding.Encode:** non-ASCII characters in `safe` were still escaped. They now stay as they are, and
  `Decode` takes them back.
- **Soundex:** a letter outside the BMP was skipped instead of acting like a vowel ("B𐐨B" gave B000, not B100).
- **CaseConverter:**
  - Digraphs were uppercased instead of titlecased ("ǆemal" gave "Ǆemal", not "ǅemal"), and on .NET Framework they
    were not lowered at all.
  - Letters without an invariant case broke idempotence and dropped word boundaries. `ToConstantCase("STRAßE")`
    gave "STR_Aß_E", `ToSnakeCase("İSTANBUL")` gave "İstanbul", and `ToPascalCase("ana_ışık")` gave "Anaışık".
    Now 'İ' lowers to 'i', 'ı' uppers to 'I', 'ß' uppers to "SS" ("Ss" at the start of a word), and a letter with no
    case continues its word.
- **Checked and found correct:**
  - the round 11 and 32 fixes
  - round trips under fuzz: CommandLine, PercentEncoding, QueryString, ShortGuid, Roman and TextDiff
  - GlobMatcher against a regex reference
  - ULID monotonicity under clock jitter
  - the similarity metrics
  - NaturalComparer ordering laws
  - per-thread culture independence
  - Tokenizer spans
  - Guard
  - the Enum helpers
- **Doc:** the TextDiff summary no longer claims an exact rebuild where its remarks say \r\n is normalised.
- **Checked build:** the core suite in a checked build matches the empty baseline.
- **Re-queued:** yes (17 bugs).

### Round 51: WinForms, tooling and docs, third pass (v0.51.0)

Reviewers: one for correctness and one for robustness, fresh to the section. The scope now includes `CLAUDE.md`,
`docs/status.md` and the new `scripts/verify.sh`, `checked-build.sh` and `net48.sh`. **18 confirmed bugs, 3 severe**
(five found by both reviewers). The library fixes have regression tests in `WinFormsReview3Tests` (9 tests); 7 fail
on the old code. The escaping test uses a new parameter, so it can't build there, and the `AutoSizeColumns` test
covers a doc-only fix. The script and generator fixes were proven by repro in a scratch worktree of HEAD.

- **checked-build.sh (severe):** it ignored the suite's exit code and only looked for FAIL lines, so a stack overflow,
  a crash or the timeout reported success. It now fails unless the run exits 0 with an ALL GREEN summary, and it
  says so on a timeout. Proven with a recursing test: HEAD exits 0, the fix exits 1.
- **checked-build.sh (severe):** given any existing checkout (`.`, or one of your own worktrees) it ran `checkout -f`
  and `clean -fd`, throwing away uncommitted work. It now reuses only a folder holding the `.toolbelt-checked-wt`
  marker it writes when it creates the worktree, and refuses any other.
- **PlotPictureBox (severe):** `ZoomAbout` checked its input offset but not the one it computed, so a deep zoom (a
  huge `MaxZoom`, or zooming far from the origin) stored an infinite `Offset`. After that every wheel event and drag
  threw inside the window procedure. A zoom that would overflow `float` now leaves the view unchanged. `FitToView`
  with a huge `MinZoom` also centred to ±∞; the offset is now held to the float range.
- **checked-build.sh:**
  - Changed files with spaces or non-ASCII names were split or quoted by git and silently left out. The list is
    now read NUL-separated.
  - After a temp cleanup removed the worktree folder, git still had it registered and `worktree add` refused. The
    script now runs `git worktree prune` first.
  - A relative worktree path was taken from the repo root, not from where you ran the script.
- **verify.sh:**
  - MathChecks had no timeout and no `< /dev/null`, so a hang stalled the run. It is now capped at 15 minutes like
    the suites, and built with `UseSharedCompilation=false`.
  - A relative log folder was created inside the repo.
- **net48.sh and the test runner:** a misspelled folder compiled no tests and reported "ALL GREEN 0 passed". The
  script now checks each folder, and the runner exits 3 whenever it selects no test, not only under a filter.
- **MenuBuilder / ToolStripBuilder:** a command's `CanExecuteChanged` raised on a worker set `Enabled` on that worker,
  so `EnabledChanged` handlers ran off the UI thread and a cross-thread exception landed in the view-model's raise.
  It is now marshalled to the strip's UI thread, or to the binding thread's WinForms context before the strip has a
  handle.
- **DataGridViewExtensions:**
  - `SaveCsv` truncated the target before reading any cell, so a throwing `CellFormatting` handler left a partial
    file in place of the previous export. It now writes beside the target and moves it over at the end.
  - CSV formula injection: a cell like `=HYPERLINK(...)` or `@SUM(...)` runs when the file is opened in Excel. The
    new opt-in `escapeFormulas` on `ToCsv` and `SaveCsv` prefixes `'` to fields starting with `= + - @`, a tab or a
    CR (numbers such as `-5` are left alone). The docs now warn about it.
  - The `AutoSizeColumns` doc promised a cap at `maxWidth`, but WinForms never sets a width below the column's
    `MinimumWidth`. The doc now says so.
- **gen-incomplete-references.py:** in `kappa` mode one failing row aborted the whole pass, and nothing was written
  until the end, so hours of computed condition numbers were lost. A failing row is now reported and skipped, each
  result is saved as it is computed, and writes go through a temp file.
- **Packaging:** the nupkgs had "Package Description" and no licence. Each project now has a description, and the
  packages carry the MIT licence expression and the repository URL.
- **Docs:**
  - CLAUDE.md listed `scripts\test.ps1` under "Run from Git Bash", where it can't run. It now gives the
    `powershell -File` form.
  - The README claimed 1,400+ tests (now 3,300+). Its Layout was missing `gen-incomplete-references.py` and the
    MathChecks cross-check files.
  - docs/status.md said `Check.Close` fails when either side is NaN. It fails only when exactly one side is.
- **Checked and found correct:**
  - the generators reproduce the checked-in data byte for byte
  - ConsumerSmoke
  - the FormStatePersistence fuzz
  - the MenuShortcut round-trip
  - `test.ps1`, `build.ps1` and `coverage.ps1`
  - ControlExtensions disposed and handle-less paths
  - WaitCursorScope across threads
  - weak command bindings under GC
  - the package contents
- **Checked build:** the core suite in a checked build is green.
- **Re-queued:** yes (18 bugs, 3 severe).

### Round 52: Binary formats & images, third pass (v0.52.0)

Reviewers: one for correctness and one for robustness, fresh to the section. **17 confirmed bugs, 1 severe**, plus
one found while fixing them (rounding near a half, below). Regression tests are in `BinaryImagesReview3Tests` (25
tests); 22 fail on the old code. Of the other three, one covers a doc-only fix (VarInt), and two guard the rewrites:
ordinary segments against exact clipping, and maximally compressed PNG data still decoding.

- **SvgDocument (severe):** a NaN or infinite number threw halfway through writing an element, leaving a broken
  element such as `<line x1="0" y1="` in the document, so every later `ToString` was invalid XML. Each element is
  now built whole and appended only once every number has formatted. The error names the real parameter (`y1`,
  `cx`, `strokeWidth`, `points`) instead of `value`.
- **PathBuilder:** the same partial write (`M1 ` left after `MoveTo(1, NaN)`), and the same wrong parameter name.
- **SvgDocument:** a size under 0.0000005 was written as `width="0"`, which no viewer renders. It is now rejected.
- **SvgUtils:** `StrokeAttributes` accepted a negative width (an SVG error), and `EstimateTextWidth` returned NaN or
  infinity for an infinite font size. Both are now rejected.
- **ImageBuffer.DrawLineClipped:** far endpoints drew the wrong pixels. `x0 + t·dx` cancelled (pixels off at 1e17,
  one pixel of a 1000-pixel row lit at 1e200), and `x1 - x0` overflowed to infinity near 1e308, so nothing was
  drawn. Liang–Barsky's `t` can't be fixed in doubles: for endpoints at ±1e20 every `t` rounds to 0.5 and it picks
  the wrong edge. The clipper now works along the major axis. Each clipped end is fixed exactly by an endpoint or an
  edge, and its other coordinate is read off the line in exact arithmetic on the doubles' binary expansions
  (BigInteger) once coordinates pass 2^40. It is graded against an exact rational Liang–Barsky over 600 far lines
  and 3,000 ordinary ones (stress-tested at 60,000).
- **ImageBuffer.DrawLineClipped (found while fixing):** an end lying within about 1e-12 of a half rounded the wrong
  way, because the double nearest 19.4999… is 19.5 and then rounds half-to-even to 20. A result that close to a half
  is now rounded from the exact value.
- **PngReader:**
  - decompressed data past the last scanline was reported as a "checksum mismatch"; libpng and Pillow accept it. It
    is now checksummed and ignored.
  - Lenient where the format is strict: CINFO above 7, PLTE in a greyscale image, a PLTE length of 0, over 768 or not
    a multiple of 3, a chunk type that isn't four letters, a missing IEND (a cut-off file decoded), a second IHDR
    (it silently re-described the pixels), any chunk before IHDR, PLTE or tRNS repeated, out of order or after the
    data, and IDAT chunks that aren't consecutive. All are now rejected.
  - Decoding inflated the whole image data into a growing buffer, so memory peaked near three times the scanlines
    (2.2 GB for 10000 × 10000 16-bit RGBA). It now inflates a scanline at a time straight into the image. Data too
    short to fill the image (deflate expands at most 1032-fold) fails before the image is allocated.
  - On .NET 8 a corrupt deflate stream surfaced the runtime's "unsupported compression method" message. It now says
    the PNG data is corrupt.
- **PngWriter:** the filtered scanlines and the compressed stream were built in memory, so an image near 2 GB threw
  OutOfMemoryException or ArgumentException after the 33-byte header was already written (`Save` left a broken
  file). Rows are now compressed as they are read and written in 64 KiB IDAT chunks.
- **Bmp.Encode:** a size between the largest array and `int.MaxValue` threw OutOfMemoryException instead of
  ArgumentException.
- **Hex.Encode:** 1 GiB of input overflowed `data.Length * 2` (OverflowException). Input past the longest string is now
  an ArgumentException naming `data`.
- **Base32 / Base64Url:** non-canonical input decoded: `MZ` like `MY`, `QR` like `QQ` (non-zero unused bits), and
  Base32 partial padding (`MY=`). Each byte string now has exactly one accepted spelling. Graded by an exhaustive
  differential: a string is accepted iff re-encoding its bytes gives it back.
- **Crc32, Crc16, Adler32:** an offset outside the array was reported as `count`. It is now `offset`.
- **VarInt (doc):** the doc said over-long input is rejected, but a zero-padded `80 00` decodes like `00`, as
  Protocol Buffers, DWARF and WebAssembly allow. The doc now says so.
- **Tests on .NET Framework:** `QuotedPrintableTests` used span `SequenceEqual`, so the Binary tests didn't compile
  on net48.
- **Checked and found correct:** half-to-even rounding of clipped ends is consistent with `DrawLine`, and Base58's
  quadratic decode is documented.
- **Checked build:** the core suite in a checked build is green (3006).
- **.NET Framework 4.8:** the Binary tests (145, which include every new test) are green. The Visualization test
  folder still doesn't compile on net48 (spans, `ZipArchive`, LINQ overloads in older tests); that is on the bug list
  for the net48 cross-cutting round.
- **Re-queued:** yes (18 bugs, 1 severe).

### Round 53: Filters & control, third pass (v0.53.0)

Reviewers: one for correctness and one for robustness, fresh to the section. **17 confirmed bugs, 1 severe**, plus
three found while fixing them (below). Regression tests are in `FiltersControlReview3Tests` (20 tests); 18 fail on
the old code, one more (elliptic, below) fails on the old code on .NET Framework 4.8, and one guards the new
conditioning check (accepted designs keep their DC gain).

- **IirFilter section order (severe):** sections were ordered by pole radius and the whole gain put on the first, so
  intermediate signals could swing by many orders of magnitude. Ordinary high-order band-stops were wrong: a
  Butterworth of order 36 (1.66-300 Hz at 1 kHz) came out 3e4 times its own output in error, a Chebyshev I of order 30
  3e13 times, a Butterworth of order 17 1.75 times. Each step now picks the section that keeps the partial response's
  shape closest to its share of the final one over the passband, and every partial product is scaled to the final
  peak (L∞), the last section taking the rest of the gain. Graded against the same sections run in double-double, on
  the five reported designs (now ≤ 2e-13) and 120 random designs across the families. The ordering was prototyped
  first against an mpmath forward filter.
- **IirFilter at an edge near 0 or Nyquist:** the poles were inside the unit circle but their rounded coefficients
  were not: `ButterworthLowPass(2, 1, 1e9)` had a section pole at exactly z = 1 (DC gain ∞; a constant 3 came out as
  3e-17), and 1e-5 Hz at 1 kHz had its DC gain 1.2% and its corner 0.6% off. Designs now reject a section whose
  coefficients' rounding moves the DC or Nyquist response or the pole radius by more than about 1e-4, naming the
  edge (`cutoff`, `low`, `high`). A 0.1 Hz DC blocker at 48 kHz still designs. The round 30 tiny-cutoff tests now
  check the rejection and run at the smallest cutoffs that design.
- **Biquad factories:** the same check, naming `frequency`. `LowPass(1e9, 1)` had its pole on the circle.
- **Biquad Peaking / LowShelf / HighShelf:** an extreme gain on its own (±600 dB at 1 kHz of 48 kHz) put the poles on
  the circle; |H| at DC came out NaN or 0. Rejected, naming `gainDb` when the same filter at 0 dB holds. ±300 dB
  still designs, to the expected gain.
- **Elliptic on .NET Framework:** `Complex.Asin` there is 9e-8 off at j·1e5, which moved small-ripple designs (1e-12 to
  1e-8 dB) by up to 1e-3 relative in the transition band. The arc-sc descent stays on the imaginary axis, so it now
  runs in real arithmetic with `asinh`. Graded against SciPy on both runtimes.
- **Order estimation with a tiny ripple:** the discrimination overflowed (ripple 1e-305 dB), reported as "transition
  band too narrow". Butterworth, Chebyshev and elliptic orders now work from its logarithm (446 and 250, checked
  against mpmath). A ripple whose 10^(r/10) − 1 underflows to 0 is rejected naming `passRippleDb`, and Chebyshev I
  and elliptic designs name `rippleDb` / `passRippleDb` instead of "numerically unstable".
- **Asinh / Acosh (with the above):** `log(x + √(x² ± 1))` lost digits near 0 (asinh) and 1 (acosh) and overflowed
  past 1e154. Both now use log1p forms and log(2x) for large x.
- **FiltFilt:** the odd extension `2·x0 − x[k]` overflowed: a constant 1e308 came out NaN. Now `x0 + (x0 − x[k])`.
- **FirFilter:** the window's cosines rounded differently at n and taps − 1 − n, so the taps were not exactly
  symmetric and the phase not exactly linear as documented. The taps are mirrored.
- **PidController:**
  - a zero gain times an overflowed error or change (0 · ∞) made the output NaN;
  - terms that overflowed in opposite directions (P = +∞, D = −∞) gave NaN where the true output is finite;
  - (found while fixing) an error past double.MaxValue times a tiny gain gave ∞ instead of 3.4e8.
  The output is now summed again in units of 2^-600 when it isn't finite. Both limits at +∞ (or both at −∞) pinned
  every output to infinity; they are rejected.
- **PidGains:** Ki = Kp/Ti and Kd = Kp·Td overflowed (which PidController then rejects) or underflowed to 0 (silently
  no integral or derivative action). Rejected naming `integralTime` / `derivativeTime`.
- **PidTuning.Simc / Lambda:** λ + θ overflowed, so Kp silently came out 0. The sum is halved when it overflows, and
  the divisions taken one at a time when K(λ + θ) overflows or underflows.
- **PidTuning.ZieglerNicholsUltimate:** Pu/2 underflowing was reported as `integralTime` (not a parameter of this
  method), and Pu/8 underflowing silently dropped the derivative. Now `ultimatePeriod`, or `ultimateGain` when Kp
  underflows.
- **FopdtModel.FitStep:** an output change past double.MaxValue was "did not change"; an underflowed gain blamed
  `gain`; non-finite initial or final values and the other failures named no parameter. The fit halves values whose
  difference overflows, and every failure names `values`, `times`, `inputStep`, `initialValue` or `finalValue`.
- **Deadband.Apply:** `center + offset − halfWidth` overflowed (1.5e308 about −1.5e308 gave ∞) and cancelled (3 about
  1e17 gave 1, not 4). The value itself is shifted now; graded against decimal arithmetic.
- **FirstOrderLag:** the expm1 used a series below 1e-5 and Exp(x) − 1 above, losing about 2500 ulps just past the
  switch. Kahan's form is used at every x.
- **Parameter names:** KalmanFilter1D's constructor named Reset's `estimate`; SchmittTrigger, PidController's
  limits, StepResponse, the IIR and FIR band checks, the Biquad constructor, order-spec edge checks, and (found while
  fixing) the IIR "pole not inside the unit circle" check named no parameter.
- **Tests on .NET Framework (found while fixing):** `FilterReviewTests` used `double.IsFinite`, so the Signal tests
  didn't compile on net48.
- **Checked and found correct:** the reported cancellation in `Biquad.LowPass` / `HighPass`'s (1 ∓ cos)/2: it is
  computed from the same rounded cos as the poles, so the DC (or Nyquist) gain is exactly 1; it reached 0 only where
  the pole was already on the circle, which the new check rejects. The sin² rewrite was tried and reverted.
- **Known gap:** in derivative-on-error mode a previous error that overflowed (±∞) still leaves the scaled retry ∞.
- **Checked build:** the core suite in a checked build is green (3026).
- **.NET Framework 4.8:** the Signal and Control tests (274, which include every new test) are green.
- **Re-queued:** yes (20 bugs, 1 severe).

### Round 54: Throwing callbacks, cross-cutting (v0.54.0)

The first cross-cutting round: the whole library, for user code that throws inside ours (comparers, hashes, clocks,
delays, predicates, event handlers, error sinks, and callbacks registered on tokens we cancel). Reviewers: one for
correctness and one for robustness, fresh. **36 confirmed bugs, 2 severe** (a callback's exception ending the
process). Regression tests are in `Threading/CallbackReview54Tests` (32 core tests), `HttpDownloadTests` (1) and
`ToolBelt.Wpf.Tests/CallbackReview54Tests` (8); 37 fail on the old code (one of them by ending the process). The
other 4 guard the rewritten sorts and the unchanged command paths.

- **HealthCheck (severe):** a probe's timeout was enforced by `CancelAfter`. If the probe registered a token callback
  that threw, the exception hit the timer thread and ended the process. Separately, the timer could lose the race to
  the timeout's delay: the source was then disposed with the timer pending, and the abandoned probe's token was never
  cancelled (15 to 40 of 200 probes on the old code). The timeout path now cancels explicitly and drops the
  callback's exception.
- **FileWatcher (severe):** a Changed handler that threw `InsufficientMemoryException`, which derives from
  `OutOfMemoryException` but is recoverable (a `MemoryFailPoint` refusing), escaped onto the timer or OS watcher
  thread and ended the process. Only an exact `OutOfMemoryException` is left to propagate; the rest go to `Error`.
- **CronScheduler:**
  - a clock that threw once stopped the loop for good, unreported;
  - so did a delay that threw;
  - a token callback a running job registered that threw out of Dispose skipped the wait.
  `onError` is now `Action<ScheduledJob?, Exception>`, and a null job means a scheduler failure. The loop tries again
  a second later, and Dispose reports the cancel's AggregateException there instead of throwing it.
- **PeriodicWorker, ConcurrentPipeline Dispose:** the same token-callback throw escaped Dispose and skipped the wait.
  It goes to `onError`.
- **ParallelUtils.ForEachAsync:**
  - in fail-fast mode a body's throwing token callback escaped unobserved;
  - when the source threw, the callback's exception replaced the source's, and the started bodies were not waited
    for.
  The callback's exception is now collected with the bodies' failures.
- **TaskRace.FirstToComplete, Hedged:** a loser's throwing token callback replaced the winner's result. It is dropped
  like the loser's outcome.
- **TaskExtensions.FireAndForget:** an `onError` that threw faulted the continuation, which then surfaced as an
  UnobservedTaskException at a later GC. It is dropped, as the other error sinks do.
- **BinaryHeap, TopN, IndexedPriorityQueue:** a comparer that threw mid-sift left the heap out of order or lost an
  item. In TopN at capacity it left a larger item at the root, so later Adds were measured against the wrong
  threshold for good. In IndexedPriorityQueue it lost the dequeued key. Every comparison now happens before the heap
  changes: the target slot is found first, then the path is shifted. Each is graded against a model over 200 to 300
  randomized trips.
- **TopN.ToArray, DataTableLite.OrderBy:** `Array.Sort` and `List.Sort` wrapped a comparer's exception in an
  InvalidOperationException. ToArray is now a heapsort of a copy and OrderBy a stable bottom-up merge sort, so the
  exception comes out unchanged. Stability and order are graded against LINQ.
- **Graph.AddEdge:** a hash that threw on `to` left `from` added, or half an undirected edge listed. The vertices
  added are removed again.
- **OrderedSet, OrderedDictionary.Remove:** a hash that threw during the map removal left the order list and the map
  disagreeing (Count ≠ enumeration). The map is removed from first.
- **FileChangeCoalescer.TakeQuiet:** an `isReady` that threw lost the changes already taken. Every call now comes
  before anything changes.
- **Retry:** `ShouldRetry` ran in an exception filter, which swallows an exception thrown inside it (taken as
  false), so a predicate's own bug vanished and the operation's exception came out. It now runs in the catch.
- **CircuitBreaker:** a clock that threw while a failure was recorded replaced the operation's exception, and the
  failure went uncounted. The opening is dated "the next time the clock is read".
- **RateLimitedSink:**
  - an inner sink that threw left the window unstarted, so a flood of a failing message was never suppressed;
  - when both the summary and the event failed, only one exception was reported (now an AggregateException).
- **HttpDownload:** an `OnProgress` that threw an IOException, TimeoutException or OperationCanceledException (a
  common way to abort) was taken for a network failure and restarted the download. It is carried out of the attempt
  and rethrown, and the partial file is removed.
- **ConsoleSpinner:** a writer that broke (a closed pipe) faulted the animation loop, unobserved when Stop was never
  called. The animation ends quietly.
- **ProcessRunner:** after a timeout or cancel, the reader threads kept calling `OnOutputLine` after Run had
  returned (a grandchild can hold the pipe). No call starts after the run returns.
- **SettingsStore:**
  - `Update` with a change that threw partway left Value half-changed, and the next Save wrote it. Value is
    restored from a snapshot;
  - a get-only collection whose Add threw had its exception wrapped in a TargetInvocationException. An
    OutOfMemoryException was then taken for a corrupt file, and the file was moved aside. It is unwrapped.
- **WPF ValidationObservableObject:** an ErrorsChanged handler that threw stopped ClearAllErrors part way and
  skipped the HasErrors change. Every notification is now raised; one failure is rethrown, several are aggregated.
- **WPF AsyncRelayCommand:** a CanExecuteChanged handler that threw after the operation failed replaced the
  operation's exception. Both are now reported in an AggregateException.
- **WPF IncrementalCollection:**
  - a PropertyChanged handler that threw on IsLoading = true left IsLoading stuck, so no later load ran;
  - one that threw on IsLoading = false replaced the load's exception. Both are now reported in an
    AggregateException.
- **Checked and left as is:** Messenger stops delivery at a throwing handler, as its docs say.
- **Found while testing (not this round):** on .NET Framework, `Benchmark.AllocatedBytesPerOperation` reports 987
  B/op for a 4096-byte allocation. Its fallback is `GC.GetTotalMemory`, which drops at each collection. Recorded as
  BUGLIST 54-1 for the net48 round. Also: the Threading, Collections, Logging, Time and IO test folders don't compile
  on net48 (UnixEpoch, Index, TarWriter, System.Text.Json), which is BUGLIST 52-1 widened.
- **Checked build:** the core suite in a checked build is green (3059).
- **.NET Framework 4.8:** this round's tests plus the existing tests for every changed type (303) ran there. All pass
  except 54-1.

### Round 55: Cancellation and dispose races, cross-cutting (v0.55.0)

The second cross-cutting round: the whole library, for what a cancelled token and a Dispose racing the work do. Does
the OperationCanceledException carry the caller's token? Does a cancelled token stop the work? Does a disposed object
say so, or report success, keep working, or wait behind a blocked call? Reviewers: one for correctness and one for
robustness, fresh. **17 confirmed bugs, 2 severe** (the two Ctrl+C handlers, where a throwing token callback escaped
the CancelKeyPress handler: an unhandled exception there ends the process. The handler throwing is reproduced; the
crash under a real signal is inferred). Regression tests are in `Threading/CancelDisposeReview55Tests` (18 core
tests) and `ToolBelt.Wpf.Tests/CancelDisposeReview55Tests` (3); 19 fail on the old code and 2 guard the pipeline's
successful drains. BUGLIST 52-2 was a test bug, fixed here.

- **The wrong token:** AsyncLock and Bulkhead waited on a token linked with their own dispose token, and a caller who
  cancelled got an exception carrying the linked token. HttpDownload's body read is cancelled through an idle-timer
  token linked to the caller's; that cancellation came out as a TaskCanceledException with the idle token (the
  re-raise skipped every OperationCanceledException). WPF IncrementalCollection's sole or last waiter, which cancels
  the shared load, got the load's internal token. Each now re-raises with the caller's token.
- **WPF IncrementalCollection:** a token already cancelled still started a fetch, and a loader that ignored its token
  appended the page and returned success. It now returns a cancelled task before anything starts.
- **JsonLines.ReadAsync (net8):** `ReadLineAsync(token)` returns a line already in the reader's buffer without
  looking at the token, so a cancel at record 3 of 100 still yielded all 100. The token is checked per record.
- **ConcurrentPipeline.CompleteAsync:** a Dispose racing the drain stopped the consumer with items buffered, and
  CompleteAsync still reported success (0 of 10 processed, 20 of 20 runs). It now throws ObjectDisposedException
  unless the buffer emptied (a Dispose after an empty drain is still a success).
- **ConsoleApp.RunAsync and CreateInterruptSource (severe):** the Ctrl+C handlers caught only
  ObjectDisposedException from `Cancel()`, so a token callback the body registered that threw raised an
  AggregateException out of the CancelKeyPress handler. RunAsync now writes the failure to its error writer and the
  run ends Interrupted. The interrupt source drops it. Tested by raising the handlers through reflection.
- **ProcessRunner:**
  - an `OnOutputLine` call already under way when the run ended kept running after Run returned, and its exception,
    raised after the run had read the first-error slot, was lost (20 of 20 runs). Calls are now counted in before
    the done flag is read, and the run waits up to a second for the ones under way. A call that runs longer still
    outlives the run, which is documented;
  - a child that exited on time but left a grandchild holding the output pipes had its exit code replaced by -1. It
    keeps the child's own code (still TimedOut, as documented). A grandchild still running once its parent has
    exited is not in the process tree any more, so it is not killed; the docs now say so and point at a job object.
- **AsyncLogSink, SyslogSink:** Emit after Dispose threw the pipeline's InvalidOperationException ("completed"), or,
  in SyslogSink, returned silently, where FileSink throws ObjectDisposedException. Both throw ObjectDisposedException.
- **SyslogSink.Dispose:** waited behind an Emit blocked in a connect (up to the connect timeout) or a full send
  buffer (up to the 5 s send timeout), since both run under the sink's lock. Dispose now closes the socket in use
  first, which fails the blocked call, and a retry after Dispose began does not reconnect.
- **CountingStream, TeeStream:** with the inner streams left open, the wrapper kept reading, writing, counting and
  teeing after Dispose, and its `Can*` properties stayed true. It is now closed: operations throw
  ObjectDisposedException and `Can*` are false. A second TeeStream Dispose no longer flushes again.
- **FileWatcher.Dispose:** a Flush on another thread that had passed its disposed check could raise Changed after
  Dispose returned. Raise rechecks under its gate, and Dispose waits for the gate (bounded at 5 s, as for the tick,
  for a handler that marshals synchronously to the disposing thread).
- **BUGLIST 52-2 (test bug):** `Http_CallerCancellation_IsNotRetried` started a 300 ms timer before the call, and the
  raw test server counts a request only after reading its whole head on a pool thread. On a starved core the cancel
  came first and the test saw 0 requests (a retry would show 2 or more). The test now cancels from the server once it
  has the request, and waits 200 ms for a retry that should not come.
- **Checked and left as is:** the async primitives (AsyncManualResetEvent, AsyncCountdownEvent, PauseTokenSource,
  KeyedLock), TaskExtensions, TaskRace, ParallelUtils, Retry, CronScheduler's Start/Dispose race, HealthCheck,
  SingleInstanceApp, ConsoleSpinner, WPF AsyncRelayCommand and NotifyTaskCompletion. PeriodicWorker's Dispose from
  the work callback deadlocks, as documented. Not reached by the robustness reviewer this round: Messenger,
  GlobalHotkeys, PlotPresenter, SingleInstance, ClipboardUtils, JobObject, ResourceSampler, EventLogWriter,
  TcpLineClient, MetricsRegistry, FirstChanceMonitor, ObjectPool, Memoize (the section passes cover them).
- **Found while testing:** on .NET Framework the HttpDownload test first cancelled from `OnProgress`, which never
  fired there because the framework's response stream did not return the 5-byte partial body. The cancel now comes
  from the server. More test files that don't compile on net48 (`IOReview2Tests`, `IOReview3Tests`, `IOReviewTests`,
  `LoggingReview2Tests`, `LoggingReview3Tests`, `SyslogAndFirstChanceTests`, `FlakeRootCauseTests`,
  `SecurityNetTimeReviewTests`, `SecurityNetTimeReview2Tests`) are added to BUGLIST 52-1.
- **Checked build:** the core suite in a checked build is green (3077).
- **.NET Framework 4.8:** this round's tests plus the existing tests for every changed type that compile there (216)
  all pass.

### Round 56: Non-finite and extreme numerics, cross-cutting (v0.56.0)

The third cross-cutting round: the whole library, for NaN, ±∞, subnormals, values near ±MaxValue and sizes past the
largest array. Reviewers: one for correctness and one for robustness, fresh. **34 confirmed bugs, 2 severe** (both
hangs), counting BUGLIST 53-1 and 53-2, fixed here. Regression tests are in `Numerics/NumericsExtremeReview56Tests`
(57 tests, all failing on the old code); `ChartsReview2Tests.AxisTicks_TimeBeyondLongRangeHasSaneLabels` is amended
for the new huge-time labels and fails on the old code too.

- **Pow2Scale hang (severe):** the power-of-two scaling helper copied into 11 statistics and quality files looped
  forever when a value was at or near MaxValue (its exponent passed 1023, so the scale became ∞ and the loop never
  ended). LinearRegression, Correlation, ConfidenceInterval, ChangePoint, Anova, HypothesisTests,
  MultipleRegression, ControlChart, GaugeRR, MeasurementAgreement and ProcessCapability never returned on such data.
  The exponent is capped at 1023 in every copy.
- **AxisTicks.Linear hang (severe):** a flat range near ±MaxValue widened to an infinite span, and the tick loop never
  ended; every chart that draws a linear axis inherited it. The normalisation now works in halves where the span
  overflows.
- **AxisTicks, more:** `Time` on a sub-second range rejected valid input naming "seconds" (it now gives the linear
  ticks), and near MaxValue its multiples overflowed to ∞ or past the long range (linear ticks in seconds there);
  `maxTicks` is capped at 10,000 (it did unbounded work); `FormatTime` throws for NaN and ±∞ and, from 2^53 s, where
  whole seconds stop being exact, prints exponent-form seconds instead of 300-digit hours.
- **Overflow and underflow in the arithmetic:** Differentiation (even-spacing samples with a huge dx, uneven samples
  with large y, Derivative and Richardson at ±MaxValue, which now take a one-sided step where x ± h is infinite and
  throw naming h where the step itself overflows), Integration (the trapezoid width and panel near MaxValue and in
  the subnormal range), LinearRegression (subnormal x, y near MaxValue: slope and intercept in scaled units),
  Goertzel (omega and the magnitude), PeakInterpolation.Parabolic, Fft's Bluestein path on subnormal input (scaled
  up first), ProcessCapability's Cp near MaxValue, PidController's derivative after an overflowed error (53-1).
- **Midpoints in the medians:** `(a + b) / 2` in Hampel, MedianFilter, Trend (Sen's slope) and RobustRegression
  (Theil-Sen) overflowed near MaxValue and lost the low bit for subnormals; they use the overflow-safe midpoint.
- **Ranges and inputs that were accepted and gave nonsense:** RootFinding's bisection and Brent with an infinite
  bracket end (reported ∞ or an endpoint as the root; brackets must be finite now), GridPathfinding.Dijkstra with a
  NaN cost (now throws), HexBin with an infinite radius, Numerics.Histogram on a subnormal range (unequal bins) and
  on a range too narrow for its bin count (shared bounds; now throws naming binCount, before allocating).
- **IirFilter:** an extreme ripple or attenuation was blamed on the cutoff or stop edge; the spec parameter is named
  (53-2 and the related EdgeTooClose and NaN-pole routes).
- **Limits instead of late failures:** PdfWriter font size ≤ 10,000 points (larger wrote numbers no reader accepts),
  DirectoryUtils' retryDelay checked before any file is touched, TextAlign widths up to the longest string, and sizes
  past Array.MaxLength throw ArgumentOutOfRangeException instead of OutOfMemoryException (DisjointSet, FenwickTree,
  NanoId, Bootstrap, Numerics.Histogram, LinearAlgebra.Identity, MovingAverage, Window, Resample.Linear,
  Visualization.Histogram, whose Render also rejects more bins than the plot has pixel columns).
- **ParamName sweep:** 51 ArgumentExceptions across Intervals, IO, Numerics, PropertyPath, IirFilter and the charts
  named no parameter.
- **Checked and left as is:** MonotoneCubic gives a zero tangent for subnormal secants but stays monotone;
  `Interval<double>` hashing of −0.0 on .NET Framework was not verified (Equals treats them as equal).
- **Found while testing:** `PauseAndRaceTests.FirstMatching_AndFirstToComplete` failed in the checked build on a
  starved core (the 150 ms and 300 ms timers fired out of order). The slower attempt now waits for the faster one.
  `Thread.Sleep` in DirectoryUtils' retry and ResourceSampler is against the house rules: BUGLIST 56-1.
- **Checked build:** the core suite in a checked build is green (3134).
- **.NET Framework 4.8:** this round's tests plus the existing review tests for the changed areas that compile there
  (271) all pass.

### Round 57: the whole suite on .NET Framework 4.8 and in the checked build, cross-cutting (v2026.11.13.0)

The fourth cross-cutting round: no reviewers; the whole core suite was made to compile on .NET Framework 4.8 and run
there against the netstandard2.0 build, for the first time in one go, and the whole core suite ran in the checked
build. **4 library bugs, none severe**, counting BUGLIST 54-1; BUGLIST 52-1 (the suite did not compile there) is
closed. Regression tests are in `IO/IONetFrameworkReview57Tests` (7 tests; 6 fail on the old code on .NET
Framework, and the 7th checks the fix does not refuse a copy beside the source); `BenchmarkTests.MeasuresAllocation`,
`IOReview2Tests.Zip_EntryNamesWithAStreamOrNulAreRefused` and `IOReview2Tests.Copy_IntoTheSourceThroughAJunctionIsRefusedNotEndless`
also fail there on the old code.

- **DirectoryUtils.Copy through a link (netstandard2.0):** links are resolved only on net6+, so on .NET Framework
  `Copy(src, j\c)` with a junction j → src was not refused and copied the tree into itself once. Where a link lies
  along either path, a uniquely named mark (deleted on close) in the destination's nearest existing folder is looked
  for in the source's tree; finding it refuses the copy.
- **ZipUtils on .NET Framework:** its ZipArchive puts every name it reads through the path checks, so an archive
  holding "x.txt:hidden", a NUL or '|' threw ArgumentException (blaming no argument) from ExtractToDirectory,
  ListEntries, ReadEntryBytes and AddOrReplaceEntry, where .NET Core reads the name and extraction refuses it with
  IOException. All four throw IOException there now.
- **Benchmark allocation (54-1):** the netstandard2.0 build measured with `GC.GetTotalMemory(false)`, the heap's net
  change, which a collection takes back to 0 (`MeasuresAllocation` got 0 B/op for 4096). It now uses the per-thread
  counter where the runtime has it (found by reflection) and the application domain's monitored total on .NET
  Framework (that switch, once on, stays on for the process; documented).
- **The suite on .NET Framework (52-1):** net8-only APIs in tests replaced with portable ones (index-from-end, span
  `SequenceEqual`, `MatchCollection` LINQ, two-argument `FirstOrDefault`, `DateTimeOffset.UnixEpoch`,
  `MemoryStream.Write(byte[])`, `TcpListener` in `using`, `OperatingSystem.IsWindows` via a new
  `Framework/TestPlatform`), and tests of net8-only types (tar, JSON files, settings and secrets stores,
  AuthenticatedEncryption, ArrayPoolScope, `ReadOnlyMemory`, `Span` reflection) guarded with `#if !NETFRAMEWORK`.
  `net48.sh` references the framework assemblies the tests use (zip, HttpClient, Microsoft.CSharp).
- **Tests that were wrong on .NET Framework:** the zip name tests built their archives with ZipArchive, which refuses
  those names there (they now patch the real name over a placeholder); the Word automation test released the
  `dynamic` application after Quit through a dynamic call that binds against the dead object (0x80010114); a
  throwing `Exception.Data` value must be serializable there; and `AtomicFile_LongFileNameStillWorks` goes past
  MAX_PATH, which .NET Framework refuses for every API unless long paths are enabled, so it runs there only when
  plain `File.WriteAllText` accepts the path.
- **Found while testing:** the test runner rethrew a test's exception with `throw ex.InnerException`, losing the
  stack trace; it keeps it now.
- **Checked build:** the whole core suite in the checked build is green (3141).
- **.NET Framework 4.8:** the whole core suite there: 3079 tests, all green, in 168 s.

### Round 58: IO, fourth pass (v2026.11.13.1)

Two fresh reviewers (correctness, robustness). **39 bugs, 7 severe**, counting the DirectoryUtils part of BUGLIST 56-1.
Regression tests are in `IO/IOReview58Tests` (39 tests; 36 fail on the old code on .NET 8 or .NET Framework 4.8, and
`AddOrReplaceEntry_StillReplacesAndCreates`, `HexDump_LengthIsExact` and the ParamName check are guards) and
`IO/IOReview58ApiTests` (5 tests of new parameters, which do not compile against the old code).

- **Severe: a folder swapped for a junction mid-operation.** `DirectoryUtils.Delete` checked each folder for a link
  once, then retried a locked file for a while: a folder replaced by a junction in that window lost the junction
  target's files. `Copy` listed the source tree up front and checked each destination folder once, so a swap on
  either side read files from, or wrote them to, a folder outside the tree. Every file now re-checks that no folder
  between the root and it has become a link, and a delete whose tree changed stops.
- **Severe: the tar metadata guard read what TarReader did not.** A size field with a leading NUL read as 0 to the
  guard, so a 2 MB PAX header passed the metadata cap; a PAX record whose stated length spanned a newline hid a
  `size=` record that TarReader (which splits at newlines) applied. The guard now parses size fields the way
  TarReader does (base-256 only for 0x80, octal after trimming NULs and spaces, overflow refused) and refuses any PAX
  record it could read differently: a length past the data or not ending at its newline, a `size=` with spaces or
  signs, or a duplicate.
- **Severe: `ZipUtils.AddOrReplaceEntry` lost the archive on a failure.** It updated in place, so an exception part way
  (an undefined `CompressionLevel`, a full disk) left a truncated zip. It now rewrites a copy beside the zip and moves
  it over the original when complete; undefined levels are refused up front there and in `CreateFromDirectory`.
- **Severe: `FileRetention.Prune` deleted the wrong file.** On .NET Framework a listed "a." was deleted as "a", the
  file the policy kept.
- **Names ending in a dot or a space** (Win32 drops them, so "a." is opened as "a"): `DirectoryUtils` Copy, Delete,
  EnumerateFiles, Summarize and GetRelativePath; `ZipUtils` and `TarUtils.CreateFromDirectory`; `TempDirectory`'s
  dispose (on .NET Framework its recursive delete could not reach "r.", found while testing); `ChecksumManifest`;
  `DirectorySnapshot`/`FileWatcher`. Each uses the `\?\` form for such a name.
- **Archive names Windows does not allow:** tar and zip extraction refuse `? * < > " |`, control characters and a name
  ending in a dot or space (they failed part way, or "a." overwrote "a"); a tar regular-file entry named "dir/" is
  refused before anything is made.
- **Archive limits:** `ExtractToDirectory` (zip and tar) takes `maxTotalBytes`, a cap on the sum of entry sizes (each
  entry was capped, but not the total). Extracting with overwrite now replaces a read-only file.
- **.NET Framework patterns:** `EnumerateFiles("*.txt")` matched 8.3 short names (a.txtx), and a pattern ending in ".."
  threw; names are now matched against the long name with .NET Core's rules.
- **DirectoryUtils:** `Delete` and `EnsureEmpty` take a `CancellationToken`, and the retry waits on it instead of
  `Thread.Sleep` (BUGLIST 56-1, DirectoryUtils part).
- **FileRetention.ShiftNumbered:** cleanup stopped at the first missing number, so with .3 absent .4 stayed for good;
  a folder in the way failed after some copies were renamed. It now lists the copies once, checks first, and deletes
  every copy at or past `keep`.
- **LineReader.ReadLines** takes `maxLineBytes`: a stream with no newline became one line, as large as the input.
- **HexDump:** input whose dump cannot fit in a string threw OutOfMemoryException after building gigabytes; it is
  refused up front, and the builder is sized exactly.
- **SafeFileName:** the 255 cap was in UTF-16 characters; 255 CJK characters are 765 UTF-8 bytes, too long on Linux
  and macOS. It caps UTF-8 bytes now, never splitting a surrogate pair.
- **DotEnv:** a lone CR (classic Mac line ends) did not end a line.
- **CsvBinder:** a property hidden with `new` threw AmbiguousMatchException; `Name` and `NAME` bound to whichever
  reflection returned. The most derived property wins, an exact-case match beats a case-insensitive one, and two
  case-insensitive matches are refused. −0 lost its sign on .NET Framework (formatted "0", "-0" parsed as +0), here
  and in `CsvDialect`.
- **FixedWidth:** Format could split a surrogate pair at a column edge and Parse took a boundary inside one; Parse
  trimmed tabs and other whitespace from a field, not just the padding spaces. Widths are documented as UTF-16 units.
- **Docs:** `JsonFile.TryLoad` says which exceptions still propagate; `DirectoryUtils.Summarize` says links are not
  followed.
- **Checked build:** the core suite in the checked build is green (3185).
- **.NET Framework 4.8:** the IO tests (246) all pass.

### Round 59: Objects, functional, grids, graphs, intervals, fourth pass (v2026.11.13.2)

Two fresh reviewers (correctness, robustness), plus one bug found while testing on .NET Framework. **27 bugs, 3
severe.** Regression tests are in `Objects/ObjectsGraphsReview59Tests` (32 tests; 26 fail on the old code on .NET 8 and
3 more only on .NET Framework 4.8; the subclass chain and stored-tree tests are guards). `AttributeCacheTests` asserted
the shared instance this round removes; it now asserts equal copies.

- **Severe: chains of made objects hung the walkers.** DeepEquals, PropertyDiff and FlattenObject stopped a getter that
  returns a new object of its owner's type, but not Ping.Next returning a new Pong whose Next returns a new Ping, two
  structs returning each other, or a getter returning a new list of new objects. `ObjectMembers.Read` now reads a
  non-leaf getter twice and classes the value (the same object, a struct copy, or made fresh); a walk's `Trail` counts
  repeats of a made type along one path and stops descending after 3. Stored data never repeats, so deep stored trees
  are walked in full. The second read is documented.
- **Severe: a boxed struct that reaches itself** through an object-typed field hung all three walkers: the cycle check
  skipped boxes. Boxes are tracked now.
- **Severe (found while testing): .NET Framework member order.** Its reflection listed a type's fields in one order on
  the first `GetFields` (after a `GetField` by name) and another once the cache filled, so the two sides of a compare
  were paired V against X, a false difference. Members are now put in a fixed order (most derived type first, then
  declaration order).
- **Walkers:** a default `ImmutableArray` or `ArraySegment` (which throw when enumerated) counts as null; a property
  hidden by `new` is read once, the most derived one (two differences at one path; Flatten wrote the base value).
- **FlattenObject:** two values that flatten to one path (a name holding the separator, or Hashtable keys 1 and "1")
  overwrote each other silently; they throw now. An empty collection or memberless object is recorded, so Unflatten
  rebuilds it.
- **PropertyPath:** a getter's or setter's own exception was wrapped or, in TryGet, swallowed; it propagates now. A
  Span-typed segment is an ArgumentException (TryGet false). A quoted index key runs to its matching quote, so
  `["a]b"]` works and inner quotes are kept.
- **ObjectMapper:** a by-ref-like (Span) property is skipped (it threw NotSupportedException); accessor exceptions are
  unwrapped.
- **TypeUtils.TryConvert:** a double or float converts to text that round-trips (.NET Framework kept 15 digits); a
  non-whole value to an integral type, and a finite double outside float's range, fail rather than round or become
  infinity; an IConvertible throwing NotSupportedException is a failure. `GetTypesWithAttribute` found nothing for an
  attribute applied twice. `FriendlyName` names pointers and by-refs (`int*`, `List<int>&`).
- **ExpressionAccessor:** a Span, ref-returning or pointer member, or any member of a by-ref-like type, cached a
  delegate that threw InvalidProgramException on every call; it is an ArgumentException naming it now.
- **ActivatorUtils:** `ScanAndCreate` skipped struct implementations and passed onError the TargetInvocationException
  wrapper; `TryCreate` threw for TypedReference, ArgIterator and RuntimeArgumentHandle on .NET Framework (which does
  not mark them by-ref-like). The same check is in every by-ref-like test in Objects.
- **AttributeCache:** returned the cached attribute itself, so a caller setting a property changed later lookups;
  each call now gets its own (shallow) copy.
- **Graphs:** `DepthFirst(null)` threw from inside the walk, not ArgumentNullException("start"); Kruskal summed a +∞
  edge beside a −∞ one to NaN; a +∞ edge now counts as no edge.
- **Intervals:** a Half or NFloat NaN endpoint was accepted; `RangeMap.Set` with a value comparer that throws left the
  map half-merged (it is now undone).
- **Checked build:** the core suite in the checked build is green (3217).
- **.NET Framework 4.8:** the Objects, Graphs and Intervals tests (201) all pass.
