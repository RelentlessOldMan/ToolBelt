# Status

Where ToolBelt stands. Updated with every chunk of work; the rules are in [CLAUDE.md](../CLAUDE.md). The review's
full history is in [REVIEW-PLAN.md](../REVIEW-PLAN.md) (round log) and the git tags.

## Next

1. **Round 51: section 17** (WinForms + tooling + docs), third pass. Its scope now includes `CLAUDE.md`, this file
   and the new `scripts/verify.sh`, `scripts/checked-build.sh` and `scripts/net48.sh`.
1. **Rounds 52-53: sections 6 and 1**, third pass. That finishes the third pass.
1. **Cross-cutting rounds** (planned 2026-11-04), each across the whole library rather than one section:
   - throwing callbacks and handlers (user code that throws inside ours);
   - cancellation and dispose races;
   - non-finite and extreme numerics (NaN, ±∞, subnormals, 1e±300);
   - the whole suite on .NET Framework 4.8 and in the overflow-checked build.
1. **Then:** every section re-queued by its last pass goes round again until a pass finds ≤ 1 minor bug. As of
   round 50 all 17 sections are re-queued.

## Built

- **The library** (2026-09-25 to 2026-10-05): 29+ divisions in the core plus the Windows, WPF and WinForms
  satellites. The README's Layout is the inventory.
- **The review** (2026-10-07 onward): 50 rounds, released as v0.1.0 to v0.50.0. Two full passes over all 17
  sections, the third pass up to section 11, and two rounds on flaky tests and the incomplete beta/gamma harness.
  Section 11 (round 50) found 17 bugs, none severe.
- **Checks every round passes:** the full verify (core 2981 tests, Windows 115, WPF 151, WinForms 82,
  MathChecks 40 at v0.50.0), the overflow-checked build with no failures (since round 48), and the special
  functions against mpmath (graded in condition-number units).
- **Unverified:** the whole core suite on .NET Framework 4.8 has never been run in one go; rounds only ran the
  section under review there. That is one of the cross-cutting rounds.

## Not built

- No NuGet publishing: packages are attached to GitHub releases only.
- No contributions: issues and PRs are off.

## Job sizes

- `scripts/verify.sh`: about 3 minutes (measured 2026-11-12: build 50 s, core suite 76 s, the rest under 30 s). Some tests start
  processes, use the clipboard or time themselves, so a busy machine can make them flaky (see Lessons).
- `scripts/checked-build.sh`: about 2.5 minutes, a full non-incremental build of the core plus the core suite.
- `scripts/net48.sh` for one section: under a minute (Text, Identifiers, Enums, Guards: 309 tests in 2 s).
- Memory: no job here needs more than a few GB. No GPU.

## Lessons

Written as rules a future session can apply.

**Testing and reviews**
- Reference values: hand-computed literals are wrong often enough that tests grade against independent references
  (SciPy, mpmath, Math.NET, naive implementations). SciPy 1.12's `savgol_coeffs` is itself wrong at high orders;
  mpmath `betainc`/`gammainc` stall at huge parameters (use series at high precision or `quad`).
- A fix can break a cross-check in another suite. Run MathChecks every round.
- Perf regression tests must be sized so the old code clearly fails (5-10x the limit), including on one slow core.
- A stack-overflow fail-first test kills the test process (exit 127, "Stack overflow."). Count that as the failure.
- Some fail-first tests only fail where the bug lives: on .NET Framework, in the checked build, or under one-core
  affinity. Say so in the test's comment.
- `Check.Close` must fail when either side is NaN; write helpers that do.
- Timing-sensitive tests (pause/race, job objects) flake under load. Widen margins or remove the race; don't retry.

**.NET and C#**
- .NET Framework's invariant casing doesn't map the ǅ/ǈ/ǋ/ǲ digraphs, the Kelvin sign, ſ or µ; net8 does.
  `Uri.EscapeDataString` there throws past 65,519 characters, and `new Random()` there is seeded from the clock.
- The overflow-checked build needs `--no-incremental`, and can't be built in place with a different
  `BaseIntermediateOutputPath` (duplicate attributes). `(uint)x >= (uint)n` bounds checks throw under checked
  arithmetic; wrap them in `unchecked`.
- Finite-overflow fixes: compute plainly, and only when the result is non-finite with finite inputs redo it scaled,
  so ordinary results stay bit-identical. Check "not finite", not `IsInfinity` (∞·0 = NaN).
- Significant-digit rounding: round the shortest round-trip ("R") digits and parse once. On .NET Framework "R" can
  drop a digit, so fall back to G17 when it doesn't round-trip.
- A `params IReadOnlyList<T>[]` overload beats `IReadOnlyList<IReadOnlyList<T>>` for a single `List<List<T>>`.
- `SemaphoreSlim` can grant a slot instead of honouring a cancellation that races a `Release`: after a cancellable
  `WaitAsync` used for dispose, recheck the disposed flag and hand the slot back.
- WPF `Matrix.HasInverse` treats |det| < ~2.2e-15 as singular. WPF tests need an STA thread
  (`FileDropBehaviorTests.RunSta`); dispatcher tests pump with `Dispatcher.PushFrame`.
- WinForms `MenuBuilder.Menu(...)` defers its items until `Build()`.
- Windows clipboard calls need an STA thread and a retry on `OpenClipboard`. Shell links round-trip only up to 260
  characters. Junction tests use `cmd /c mklink /J` (fully qualify `System.Diagnostics.Process`; the tests have a
  `Process` namespace).

**Tooling**
- One build or test project at a time on Windows: overlapping builds deadlock on `obj` locks. The scripts disable
  the MSBuild server so a lingering one can't make a shell look hung.
- PowerShell scripts that forward arguments parse `$args` by hand: a `[CmdletBinding()]` param block eats `--` and
  turns `-v` into `-Verbose`.
- From Git Bash, `$TEMP` is a Windows path and globs under it don't expand; use `$(cygpath -u "$TEMP")`.
- Edit scripts: write Python files with the Write tool, not Bash heredocs (they mangle quotes, backslashes and
  non-ASCII). The Write tool turns a single-backslash `\uXXXX` into the literal character, so double the backslash
  for C# escapes. Python text mode on Windows writes CRLF; the repo is LF.
- Statistics keeps four copies of the special functions (drop-in law). After editing one, splice the change into
  the others; a test checks they agree.
