# Bug list

A running list of every suspected and confirmed bug that is not yet fixed and released, so none is dropped between
rounds. Every confirmed bug gets fixed. Priority only orders the work: **P1** wrong results, crashes, hangs or security
issues on ordinary inputs; **P2** the same on extreme inputs (overflow, subnormals, huge sizes); **P3** naming, messages,
docs. A bug leaves this list when its fix is released; the round log in REVIEW-PLAN.md keeps the history.

Status: `suspected` (not yet reproduced), `confirmed` (failing test or probe), `fixed` (in the working tree, unreleased).

| ID | Pri | Section | Status | Summary | Found by |
|----|-----|---------|--------|---------|----------|
| R69-2 | P2 | 12 | suspected | `DiskSpace` reads `IsReady` on every drive before matching the path, so an offline drive could stall the call. | review r69 |
| R69-3 | P2 | 12 | suspected | After Ctrl+C, `Prompt` silently takes the default (`ReadLine()` returns null on Windows). | review r69 |
| R69-4 | P2 | 12 | suspected | A `Logger` clock that throws escapes `Log`. | review r69 |
| R69-5 | P2 | 12 | suspected | `Benchmark` with `int.MaxValue` iterations throws OutOfMemoryException, not an argument error. | review r69 |
| R81-1 | P2 | 10 | suspected | `ProcessRunner.KillTree` reports the kill as done when `Kill` threw (access denied, say) with the child still running. | review r81 |
| R81-2 | P2 | 10 | suspected | After a timeout, a grandchild holding the inherited pipes keeps both reader threads blocked until it exits (a thread pair per run, worst on netstandard2.0 with no tree kill). | review r81 |
| R93-3 | P2 | 14 | suspected | GridPathfinding's MinHeap ties keys within 1e-12 relative, which is not a strict ordering; A* on near-tied keys might close a node a few ulps too high. Not reproduced: steps cost 1 or √2, so distinct f-values differ by about 1/(3n) for n-step paths, far wider than the window at any grid that fits in memory. | review r93 |
| R94-1 | P2 | 8 | suspected | `TarUtils.CreateFromDirectory` excludes the archive by its path string, so an archive inside the source reached through a short (8.3) name or a junction is not recognised: the run fails on a sharing violation (or packs an old copy of the archive). Compare by file identity. | review r94 |
| R94-2 | P3 | 8 | suspected | `TarUtils.CreateFromDirectory` leaves out a folder whose only file is the archive being written. | review r94 |
| R94-3 | P3 | 8 | suspected | `AtomicFile` with a backup fails on names of 252 characters or more ("<name>.bak" passes the 255-character limit); the original is kept, but the write is refused. Undocumented. | review r94 |
| R94-4 | P3 | 8 | suspected | A zip made on Unix with a directory entry ending in a backslash is extracted as a file; on Unix, extraction treats a backslash as a separator while creation keeps it as a name character. | review r94 |
| R95-1 | P3 | 4 | suspected | `RandomUtils.NextGamma` with a subnormal shape below 1 and a zero draw computes `Math.Pow(1, ∞)`, which is 1 on .NET Core but may be NaN on .NET Framework 4.8. Not run there. | review r95 |
| R95-2 | P3 | 4 | suspected | `Angle` normalisation of ±∞ returns NaN rather than throwing; whether the contract allows it was not judged. | review r95 |
| R95-3 | P3 | 4 | suspected | `Primes.Sieve(int.MaxValue)` and `Polynomial.Fit` at its n·m cap run out of memory (OutOfMemoryException) instead of rejecting the size; neither limit is documented. | review r95 |
| R96-1 | P3 | 9 | suspected | `TtlCache`: an entry that never expires (or whose expiry was capped at `DateTimeOffset.MaxValue`) counts as expired when an injected clock returns `MaxValue`. | review r96 |
| R96-2 | P3 | 9 | suspected | `ReservoirSampler` and `WeightedWithoutReplacement`: a seeded `Random`'s NextDouble has 31-bit resolution, which quantises skip lengths after about k·2^31 items. Effect looks negligible; not measured. | review r96 |
| R96-3 | P3 | 9 | suspected | `CountMinSketch` does not check width × depth before allocating, so a huge size runs out of memory rather than being rejected. | review r96 |
| R97-1 | P3 | 10 | suspected | `TaskExtensions.WithCancellation` returns a finished task's result when the token is already cancelled (as `Task.WaitAsync` does), where AsyncManualResetEvent and AsyncCountdownEvent document "a cancelled token wins". Contract not judged. | review r97 |
| R97-2 | P3 | 10 | suspected | `WhichExe`: on Unix it ignores the execute bit and empty PATH entries; on Windows a drive-relative name (`C:tool`) resolves against the current directory of that drive. Not probed. | review r97 |
| R97-3 | P3 | 10 | suspected | A second concurrent `Dispose` of ConcurrentPipeline or PeriodicWorker returns before the loop has stopped; ProcessRunner's reader threads stay alive while a grandchild holds the inherited pipes. | review r97 |
| R98-1 | P3 | 11 | suspected | `NGramSimilarity` counts n-grams in UTF-16 code units, so a gram can split a surrogate pair; the doc says "character n-grams" without naming the unit. | review r98 |
| R98-2 | P3 | 11 | suspected | `TextAlign.Fit` does not validate `ellipsis` as it does `pad`: a lone surrogate or escape fragment in it gives invalid UTF-16 or a mis-measured result. | review r98 |
| R98-3 | P3 | 11 | suspected | `TextDiff`'s largest allowed table is one 1 GB `ulong[]`, which a 32-bit or memory-capped process can fail to allocate (OutOfMemoryException) before the documented ArgumentException limit. Not probed. | review r98 |
| R99-1 | P3 | 6 | suspected | `Base58.Encode` has no output-length guard, so a huge input likely ends in OutOfMemoryException rather than ArgumentException. Not probed. | review r99 |
| R99-2 | P3 | 6 | suspected | A 32-bit bitfields BMP with a 40-byte header: the channel masks can overlap the pixel data, giving a misleading error rather than a clear one. | review r99 |
| R100-1 | P3 | 7 | suspected | `ReportBuilder.GithubSlug` lowercases invariantly; GitHub's slugger may differ for a word-final sigma and for U+0130. Not checked against GitHub. | review r100 |
| R100-2 | P3 | 7 | suspected | On .NET Framework, "infinity" (lower case), "∞" and floats near MaxValue may parse differently from net8 in ConfigBinder and EnvironmentVariables. Not run on net48. | review r100 |
| R100-3 | P3 | 7 | suspected | Several SettingsStore instances saving one file at once: 8 threads gave UnauthorizedAccessException once the move retry ran out (no corruption). Documented "save from one place". | review r100 |
| R100-4 | P3 | 7 | suspected | SettingsStore.Update: if the replace succeeds but restoring attributes then throws, Value is rolled back while the disk holds the new data. | review r100 |
| R100-5 | P3 | 7 | suspected | SettingsStore's stale-temp cleanup judges age by LastWriteTimeUtc; on a share with a clock skewed by over an hour it could delete a live temp. | review r100 |
| R101-1 | P3 | 5 | suspected | A raster Callout with a huge dx may lose its arrowhead (the length overflows; a scaled hypot would not). Not reproduced. | review r101 |
| R101-2 | P3 | 5 | suspected | `HexBin.Compute` accepts `default(PlotFrame)` (zero size) rather than refusing it. | review r101 |
| R101-3 | P3 | 5 | suspected | BoxPlot with ±∞ in the data: the IQR is ∞, so with WhiskerReach = 0 the fences are 0·∞ = NaN and an infinite point may not be listed as an outlier. Not probed. | review r101 |
| R101-4 | P3 | 5 | suspected | SvgChart bar width: BarSpacing uses every finite x, but on a log x axis non-positive x are not drawn, so the width may come from points that are not shown. | review r101 |
| R101-5 | P3 | 5 | suspected | `AxisTicks.Linear(99, 101, maxTicks: 2, loose: true)` gives [0, 200]: the data fills 1% of the axis (follows the tick-count guarantee, but poor). | review r101 |
| R101-6 | P3 | 5 | suspected | A 1 × 1 colorbar is black horizontally (low end) and white vertically (high end): the degenerate size picks different ends. | review r101 |
| R102-1 | P3 | 16 | suspected | IncrementalCollection: a CollectionChanged handler that calls ResetAsync during ResetAsync's clear (one handler only) loads a page that the outer reset then overwrites (HasMoreItems, IsLoading). Not reproduced. | review r102 |
| R102-2 | P3 | 16 | suspected | CountToVisibilityConverter.HasAny: an IEnumerable whose GetEnumerator returns null throws NullReferenceException; a one-shot sequence loses its first element to the check. | review r102 |
| R102-3 | P3 | 16 | suspected | GlobalHotkeys.Register with a null callback for a gesture this instance already holds throws InvalidOperationException, not ArgumentNullException (Holds is checked first). | review r102 |
| R102-4 | P3 | 16 | suspected | RecordingDialogService records the caller's mutable FileDialogOptions and string[] rather than snapshots, and Calls is the live list. | review r102 |
| R102-5 | P3 | 16 | suspected | ThemeManager on an STA thread that never pumps: with FollowSystem on, queued BeginInvoke operations pile up (the non-STA fix's symptom). | review r102 |
| R102-6 | P3 | 16 | suspected | NumericRangeValidationRule: a NaN Minimum or Maximum silently turns that bound's check off (other range types refuse NaN bounds). | review r102 |
| R103-1 | P3 | 12 | suspected | Benchmark: an operation whose mean measures 0 ns reports 0 ops/s ("never completes"); RelativeToBaseline treats 0 ns as infinitely fast. Not reproduced (an empty iteration measured 21 us). | review r103 |
| R103-2 | P3 | 12 | suspected | ConsoleSpinner: a text-default symbol with VS16 (U+2764 U+FE0F) counts one column though terminals draw two; after the window narrows, the pad to the last width wraps the line. | review r103 |
| R103-3 | P3 | 12 | suspected | Logger: after the parent's Dispose, a child from ForCategory can still AddSink; the sink gets events and is never disposed. | review r103 |
| R103-4 | P3 | 12 | suspected | FileSink: when the reopen after a rotation fails, Emit throws though its line was written and flushed. | review r103 |
| R103-5 | P3 | 12 | suspected | ConsoleSpinner: the constructor's first draw ignores only IOException and ObjectDisposedException; any other writer exception escapes after the token source and loop are set up, leaking both. | review r103 |
| R103-6 | P3 | 12 | suspected | HealthCheck: an abandoned probe's token source is disposed when its check returns; a probe reading token.WaitHandle later gets ObjectDisposedException. | review r103 |
| R103-7 | P3 | 12 | suspected | DiskSpace: DriveInfo.GetDrives exceptions (an unreadable mount table off Windows) are not caught. | review r103 |
| R104-1 | P3 | 2 | suspected | SavitzkyGolay: each edge sample recomputes a fit row in O(w·m), so edges cost O(w²·m); w = 10001 at order 100 is about 1e10 operations. | review r104 |
| R104-2 | P3 | 2 | suspected | WelchPsd, Multitaper: ScaleB below 2^-1000 multiplies by 2^-1000 and then the rest; a first step that lands subnormal rounds twice (one subnormal ulp). | review r104 |
| R104-3 | P3 | 2 | suspected | Fft.Inverse for lengths that are not a power of two multiplies by a rounded 1/N after the Bluestein transform's scale-back, so a subnormal result can round twice. | review r104 |
| R104-4 | P3 | 2 | suspected | FrequencyGrid.NearestBin: Math.Round's ties-to-even sends exact halfway frequencies to alternating sides (2.5 to 2, 3.5 to 4). | review r104 |
| R104-5 | P3 | 2 | suspected | Hampel, MedianFilter: O(n·w) inserts and MAD merges; quadratic for n ≈ w ≈ 1e6. | review r104 |
| R104-6 | P3 | 2 | suspected | Goertzel: past f/fs = 2^53 the alias f mod fs keeps no meaningful phase per sample (1e20 Hz at 1 Hz returns an arbitrary magnitude); refusing that range may be better. | review r104 |
| R105-1 | P3 | 17 | suspected | WaitCursorScope: the wait cursor may never show while the UI thread is blocked (no message is pumped to apply it), and a worker's scope sets it from another thread. | review r105 |
| R105-2 | P3 | 17 | suspected | ControlExtensions.InvokeIfRequired: the handle can be destroyed between the InvokeRequired check and Invoke. | review r105 |
| R105-3 | P3 | 17 | suspected | SetSelectedEnum: with ComboBoxStyle.DropDown, the edit text typed earlier stays after the selection changes. | review r105 |
| R105-4 | P3 | 17 | suspected | CommandBinding: posting a CanExecuteChanged update throws once the UI thread has exited. | review r105 |
| R105-5 | P3 | 17 | suspected | DataGridView CSV export: "-Infinity" and "-NaN" cells are left unescaped (a leading "-" is a formula prefix to spreadsheets). | review r105 |
| R105-6 | P3 | 17 | suspected | PlotPictureBox: pressing the right button during a left-button pan ends the pan. | review r105 |
| R105-7 | P3 | 17 | suspected | PlotPictureBox.ZoomAt with factor 1 while the zoom is above MaxZoom (a limit lowered since) still changes the zoom. | review r105 |
| R105-8 | P3 | 17 | suspected | FormStatePersistence: the documentation of what is restored is looser than the code. | review r105 |
| R105-9 | P3 | 17 | suspected | bump-version.ps1: no cap on the day counter N. | review r105 |
| R105-10 | P3 | 17 | suspected | bump-version.ps1 drops Directory.Build.props' BOM and does not write it atomically. | review r105 |
| R105-11 | P3 | 17 | suspected | test.ps1: a trailing -Configuration with no value is passed through to the runner. | review r105 |
| R105-12 | P3 | 17 | suspected | test.ps1: Kill() on Windows PowerShell 5.1 kills only the runner, not processes it started. | review r105 |
| R105-13 | P3 | 17 | suspected | coverage.ps1 has no timeout; a hung run blocks forever, and killing it can leave an instrumented ToolBelt.dll in the test bin folder. | review r105 |
| R105-14 | P3 | 17 | suspected | gen-incomplete-references.py: a reference row that now fails is dropped (the previous good row is lost) and the script still exits 0. | review r105 |
