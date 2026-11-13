# Status

Where ToolBelt stands. Updated with every chunk of work; the rules are in [CLAUDE.md](../CLAUDE.md). The review's
full history is in [REVIEW-PLAN.md](../REVIEW-PLAN.md) (round log) and the git tags.

## Next

1. **The fourth pass:** every section re-queued by its last pass goes round again until a pass finds ≤ 1 minor bug.
   As of round 53 all 17 sections are re-queued; the order is in REVIEW-PLAN.md. The cross-cutting rounds (54 to 57)
   are done. Round 58 (IO) found 39 bugs, 7 severe, round 59 (section 14) 27 bugs, 3 severe, and round 60 (section 17)
   22 bugs, none severe, round 61 (section 4) 24 bugs, none severe, and round 62 (section 5) 24 bugs, 1 severe, so
   all five stay re-queued; section 7 is next.
1. **BUGLIST 56-1** (`Thread.Sleep` in library code): the DirectoryUtils part is fixed (round 58); the Windows parts go
   with section 15.

## Built

- **The library** (2026-09-25 to 2026-10-05): 29+ divisions in the core plus the Windows, WPF and WinForms
  satellites. The README's Layout is the inventory.
- **The review** (2026-10-07 onward): 62 rounds, released as v0.1.0 to v0.56.0 and v2026.11.13.0 onward. Three full passes over all 17
  sections, two rounds on flaky tests and the incomplete beta/gamma harness, and four cross-cutting rounds (round 54,
  throwing callbacks: 36 bugs, 2 severe; round 55, cancellation and dispose races: 17 bugs, 2 severe, both a Ctrl+C
  handler letting a token callback's exception escape; round 56, non-finite and extreme numerics: 34 bugs, 2 severe,
  both hangs on data near MaxValue; round 57, the whole suite on .NET Framework 4.8: 4 bugs, none severe). The fourth
  pass began with IO (round 58: 39 bugs, 7 severe, mostly junction swaps mid-operation, the tar guard and names
  ending in a dot).
- **Checks every round passes:** the full verify (core 3270 tests, Windows 115, WPF 162, WinForms 104,
  MathChecks 40 at v2026.11.13.5), the overflow-checked build with no failures (since round 48), and the special
  functions against mpmath (graded in condition-number units).
- **.NET Framework 4.8:** since round 57 the whole core suite compiles and passes there (3079 tests; net8-only
  types' tests are guarded out).

## Not built

- No NuGet publishing: packages are attached to GitHub releases only.
- No contributions: issues and PRs are off.

## Job sizes

- `scripts/verify.sh`: about 3 minutes (measured 2026-11-12: build 50 s, core suite 76 s, the rest under 30 s). Some tests start
  processes, use the clipboard or time themselves, so a busy machine can make them flaky (see Lessons).
- `scripts/checked-build.sh`: about 2.5 minutes, a full non-incremental build of the core plus the core suite.
- `scripts/net48.sh` for one section: under a minute (Text, Identifiers, Enums, Guards: 309 tests in 2 s). Every
  folder: about 4 minutes (build about 1 minute, tests 168 s).
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
- To make a name ending in a dot or space in a test, prefix `\\?\` to a path that is already full: `Path.GetFullPath`
  itself drops the dot, so the test silently writes the plain name twice.
- .NET Framework's reflection can return a type's members in a different order on its first `GetFields` or
  `GetProperties` than later. Never pair two reads by index unless the list is sorted; a test of it needs a type no
  other test touches.
- A swap-race test (a folder replaced by a junction mid-operation) must swap after the check it targets, e.g. from a
  filter callback on a file inside that folder; swapping earlier is caught by the old once-per-folder check.
- Some fail-first tests only fail where the bug lives: on .NET Framework, in the checked build, or under one-core
  affinity. Say so in the test's comment.
- .NET Framework's own `MakeGenericType` overflows the stack building a type about 8,000 generic levels deep, so a
  deep-nesting test runs at 5,000 there.
- Windows PowerShell 5.1 passes a native argument's embedded quotes raw and has no reliable escape: don't build a
  quoted command line inside one argument (coverlet's `--targetargs`); run an exe that needs no arguments instead.
- To check a build leaks no local paths, grep the DLL for the repo folder's name: the PDB path is the usual leak, and
  `PathMap` in Directory.Build.props maps it to `/_/` for Release builds.
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
- `net48.sh` takes whole folders, and every folder compiles there since round 57. Keep it so: a test of a
  net8-only type goes under `#if !NETFRAMEWORK`, and a test uses portable APIs (`TestPlatform.IsWindows`, LINQ
  `SequenceEqual`, no index-from-end). A test folder that uses another's helpers needs both on the command line.
- On .NET Framework, `ZipArchive` refuses to write names with ':' or NUL and throws reading them: build such archives
  by patching the name bytes over a placeholder.
- A cancellation test should check `CancellationToken == caller's token`, not just the exception type: four types
  passed type-only tests for many rounds while handing out an internal linked token.
- Start a test's cancel from the event it races (the server has the request), not from a timer started before the
  call: on a starved core the timer wins before anything happens (BUGLIST 52-2).
- A test's trigger can behave differently on .NET Framework: its response stream held back a partial body, so a
  cancel from `OnProgress` never fired there and the test failed for the wrong reason.
- A helper copied into many files carries its bug into all of them: Pow2Scale's uncapped exponent hung 11 types.
  When a copied helper is fixed, grep for every copy.
- Probe every numeric entry point at ±MaxValue, a flat range there, and the subnormal range: a scaling loop or a
  span that overflows to ∞ hangs rather than throws, and `(a + b) / 2` overflows or loses the low bit.
- Grade geometry against exact rational arithmetic (BigInteger on the doubles' binary expansions), not against a
  second floating-point formula. A double reference hid both the wrong-edge pick and the near-half rounding.
- A sum that must survive huge values cancelling needs a renormalised double-double (TwoSum, then hi + lo folded
  back): one Neumaier compensation term carries the cancelled rounding error and loses whatever small value follows.
- Divide after multiplying when a step can be subnormal (`s * dx / 3`, not `s * (dx / 3)`), and test the "obvious"
  branches with values just past a tolerance: a near-double root, a near-singular pivot, an ulp-wide bin.

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
