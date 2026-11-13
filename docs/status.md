# Status

Where ToolBelt stands. Updated with every chunk of work; the rules are in [CLAUDE.md](../CLAUDE.md). The review's
full history is in [REVIEW-PLAN.md](../REVIEW-PLAN.md) (round log) and the git tags.

## Next

1. **Cross-cutting rounds** (planned 2026-11-04), each across the whole library rather than one section. Throwing
   callbacks was round 54; still to do:
   - cancellation and dispose races;
   - non-finite and extreme numerics (NaN, ±∞, subnormals, 1e±300);
   - the whole suite on .NET Framework 4.8 and in the overflow-checked build.
1. **Then:** every section re-queued by its last pass goes round again until a pass finds ≤ 1 minor bug. As of
   round 53 all 17 sections are re-queued.

## Built

- **The library** (2026-09-25 to 2026-10-05): 29+ divisions in the core plus the Windows, WPF and WinForms
  satellites. The README's Layout is the inventory.
- **The review** (2026-10-07 onward): 54 rounds, released as v0.1.0 to v0.54.0. Three full passes over all 17
  sections, two rounds on flaky tests and the incomplete beta/gamma harness, and the first cross-cutting round
  (round 54, throwing callbacks: 36 bugs, 2 severe, both a callback's exception ending the process).
- **Checks every round passes:** the full verify (core 3059 tests, Windows 115, WPF 159, WinForms 91,
  MathChecks 40 at v0.54.0), the overflow-checked build with no failures (since round 48), and the special
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
- Memory: a few core image tests (PNG and BMP size limits) each allocate an image buffer of about 2 GB, near the
  largest array; the peak hasn't been measured. Nothing else needs more than a few GB. No GPU.

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
- `Check.Close` passes NaN against NaN but must fail when only one side is NaN (fixed in round 18); write any new
  tolerance helper the same way.
- Timing-sensitive tests (pause/race, job objects) flake under load. Widen margins or remove the race; don't retry.
- When a fix rewrites an algorithm (streaming instead of buffering, a new clipper), run the whole suite before
  writing up: old guard tests catch what the rewrite gave up. Streaming PNG decode into a pre-allocated image broke
  the "tiny file claiming a huge image" guard, and only the full verify showed it.
- Check a reviewer's numerical "fix" against the whole computation before applying it: the suggested sin² form for
  Biquad's (1 − cos)/2 was more accurate alone but broke the exact unity DC gain the rounded poles were matched to.
- Grade a filter cascade against the same sections run in double-double, not against a second double design: that
  isolates the cascade's own rounding, which is what section ordering changes.
- A fail-first test must reach the code it targets: a serializer can build its own instance of a test type first,
  so make only the target instance misbehave (round 54's get-only collection test passed at HEAD until then).
- A race that loses only sometimes (a timer against a delay) needs many instances in one test: 200 probes failed
  every run on the old code where one probe passed.
- `net48.sh` takes whole folders, and several folders hold net8-only tests. Until the net48 round, run a round's
  files there through a scratch copy that includes single files.
- Grade geometry against exact rational arithmetic (BigInteger on the doubles' binary expansions), not against a
  second floating-point formula. A double reference hid both the wrong-edge pick and the near-half rounding.

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
- `scripts/checked-build.sh` only reuses a worktree holding its `.toolbelt-checked-wt` marker (added in round 51);
  a scratch worktree made before then is refused until it is removed or the marker is added by hand.
- A script's exit code must come from what it ran, not from grepping the output: a crashed run prints no FAIL line.
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
