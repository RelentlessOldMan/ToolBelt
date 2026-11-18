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
| R105-6 | P3 | 17 | suspected | PlotPictureBox: pressing the right button during a left-button pan ends the pan. | review r105 |
| R105-7 | P3 | 17 | suspected | PlotPictureBox.ZoomAt with factor 1 while the zoom is above MaxZoom (a limit lowered since) still changes the zoom. | review r105 |
| R105-8 | P3 | 17 | suspected | FormStatePersistence: the documentation of what is restored is looser than the code. | review r105 |
| R105-9 | P3 | 17 | suspected | bump-version.ps1: no cap on the day counter N. | review r105 |
| R105-10 | P3 | 17 | suspected | bump-version.ps1 drops Directory.Build.props' BOM and does not write it atomically. | review r105 |
| R105-11 | P3 | 17 | suspected | test.ps1: a trailing -Configuration with no value is passed through to the runner. | review r105 |
| R105-12 | P3 | 17 | suspected | test.ps1: Kill() on Windows PowerShell 5.1 kills only the runner, not processes it started. | review r105 |
| R105-13 | P3 | 17 | suspected | coverage.ps1 has no timeout; a hung run blocks forever, and killing it can leave an instrumented ToolBelt.dll in the test bin folder. | review r105 |
| R105-14 | P3 | 17 | suspected | gen-incomplete-references.py: a reference row that now fails is dropped (the previous good row is lost) and the script still exits 0. | review r105 |
| R106-1 | P3 | 15 | suspected | SingleInstanceApp: two ids the kernel's case table folds alike but ToUpperInvariant does not (and the reverse, e.g. U+A7C0/U+A7C1) may share a pipe but not a gate, so both become primary. | review r106 |
| R106-2 | P3 | 15 | suspected | DpiInfo.Monitors throws where MonitorInfo falls back when the monitor API fails. | review r106 |
| R106-3 | P3 | 15 | suspected | DpiInfo maps an unknown (-1) awareness context to PerMonitor rather than Unaware/unknown. | review r106 |
| R106-4 | P3 | 15 | suspected | ClipboardUtils.SetText: text with a NUL is cut at it by CF_UNICODETEXT readers, silently. | review r106 |
| R106-5 | P3 | 15 | suspected | ShortcutUtils: fields at or past the 260-character limits may be cut or refused inconsistently between Create and Read. | review r106 |
| R106-6 | P3 | 15 | suspected | ShortcutUtils.Read of a truncated .lnk (past the header) can return partial data rather than throw. | review r106 |
| R106-7 | P3 | 15 | suspected | JobObject: Start("   ") and AssignProcess(IntPtr.Zero) throw Win32Exception rather than ArgumentException. | review r106 |
| R107-1 | P3 | 1 | suspected | FopdtModel.StepResponse at a NaN time returns a value rather than NaN or a throw. | review r107 |
| R107-2 | P3 | 1 | suspected | PidController ScaledOutput may lose precision when the output range is extreme (near double.MaxValue wide). | review r107 |
| R107-3 | P3 | 1 | suspected | FopdtModel.FitStep's error message for an unusable step does not say which input is at fault. | review r107 |
| R108-1 | P3 | 3 | suspected | ControlChart.Individuals and BlandAltman limits can be ±∞ for finite data near double.MaxValue. | review r108 |
| R108-2 | P3 | 3 | suspected | FitWeibull reports "All values are equal" for distinct values only a few ulps apart. | review r108 |
| R108-3 | P3 | 3 | suspected | ChiSquareQuantile at tiny df and p > 0.5 returns 1e-323 where 0 may be right; at df ≥ 1e17 the round trip loses digits. | review r108 |
| R108-4 | P3 | 3 | suspected | F upper tail at d1 near 1e-308 under a huge d2: −Expm1 of the leading term may lose relative precision. | review r108 |
| R108-5 | P3 | 3 | suspected | KolmogorovSmirnovTwoSample uses the asymptotic p even at n = 3 (0.033 where the exact p is 0.1); documented as asymptotic. | review r108 |
| R108-6 | P3 | 3 | suspected | GaugeRR uses int products for degrees of freedom; very large studies could overflow them. | review r108 |
| R109-1 | P3 | 7 | suspected | ReportBuilder's heading slug lowers with ToLowerInvariant, which on .NET Framework leaves supplementary letters (Deseret) as they are: anchors may differ between builds. | review r109 |
| R109-2 | P3 | 7 | suspected | DocxWriter checks only a PNG's 24-byte header, so a truncated PNG is embedded and Word reports damage (ToPdf rejects it). | review r109 |
| R109-3 | P3 | 7 | suspected | DocxWriter table grid width 9360/columns is 0 past 9360 columns. | review r109 |
| R109-4 | P3 | 7 | suspected | ReportTemplates.Comparison accepts flagPercent = ∞, which turns flagging off silently. | review r109 |
| R109-5 | P3 | 7 | suspected | HtmlReport.CodeBlock with a language holding a space writes two CSS classes. | review r109 |
| R109-6 | P3 | 7 | suspected | Two processes saving one SettingsStore file at once can still lose one writer's update after the retries. | review r109 |
| R109-7 | P3 | 7 | suspected | PdfWriter.Code wraps by UTF-16 count, so a line with emoji wraps early. | review r109 |
| R109-8 | P3 | 7 | suspected | HtmlReport passes U+0000 and lone surrogates through into the HTML text. | review r109 |
| R110-1 | P3 | 15 | suspected | DpiInfo.Monitors throws when the per-monitor-v2 context is refused, where MonitorInfo falls back to v1 or returns an empty list. | review r110 |
| R110-2 | P3 | 15 | suspected | Elevation.CurrentIntegrityLevel would read sub-authority -1 for a label SID with none. | review r110 |
| R110-3 | P3 | 15 | suspected | RegistryUtils.DeleteKey of a path with a 300-character component throws ArgumentException where KeyExists, GetValue and DeleteValue report absent. | review r110 |
| R110-4 | P3 | 15 | suspected | IsNetworkDrive on a device spelling of a real mapped drive is untested (no mapped drive available to the reviewers). | review r110 |
| R111-1 | P3 | 9 | suspected | Composite keys (tuples, object keys) holding a NaN or decimals of different scale still hash by bits or scale on .NET Framework, so equal keys count separately; the value-hashed comparers cover only plain double, float and decimal. | review r111 |
| R111-2 | P3 | 9 | suspected | Deque and TopN growth to .NET Framework's 0x7FEFFFFF limit is untested (it needs a 16 GB array). | review r111 |
| R112-1 | P3 | 8 | suspected | JsonFile's JSON Lines readers count lines in an int, so an error past line 2^31 reports a negative line number. | review r112 |
| R112-2 | P3 | 8 | suspected | ZipUtils' streaming rewrite copies each entry's LastWriteTime unguarded; a date outside 1980-2107 would throw ArgumentOutOfRangeException instead of falling back. | review r112 |
| R112-3 | P3 | 8 | suspected | Clearing ReadOnly before a delete may follow a symlink and change its target's attributes. | review r112 |
| R112-4 | P3 | 8 | suspected | Zip entry lookups by name pick one of several same-named entries. | review r112 |
| R112-5 | P3 | 8 | suspected | The tar metadata guard may keep a stale size when two PAX headers come before one entry. | review r112 |
| R113-1 | P3 | 6 | suspected | Base32 and Crockford Decode pre-size their output from the input length, so a huge whitespace- or hyphen-only string allocates about 0.6 bytes per character for nothing. | review r113 |
| R113-2 | P3 | 6 | suspected | ImageBuffer.DrawLineClipped rounds the clipped endpoints half-to-even, so pixels inside the clip can differ from the unclipped DrawLine ((0,0)-(4,1) clipped at x=2). | review r113 |
| R113-3 | P3 | 6 | suspected | Bmp.Decode reads BI_BITFIELDS masks at 54 for non-standard header sizes 41-51. | review r113 |
| R114-1 | P3 | 10 | suspected | PauseToken's wait returns success for an already-cancelled token while unpaused. | review r114 |
| R114-2 | P3 | 10 | suspected | AsyncEvent.InvokeParallelAsync runs handlers that finish synchronously one after another, not in parallel. | review r114 |
| R114-3 | P3 | 10 | suspected | ProcessRunner adds a WhenAll continuation per poll while waiting for output to drain. | review r114 |
| R114-4 | P3 | 10 | suspected | AsyncLazy callers block for the synchronous part of the factory. | review r114 |
| R114-5 | P3 | 10 | suspected | A custom Retry DelayAsync that throws synchronously replaces the operation's exception. | review r114 |
| R114-6 | P3 | 10 | suspected | ProcessRunner.RunAsync can report a timeout and a cancel landing together as a cancel. | review r114 |
| R114-7 | P3 | 10 | suspected | TokenBucket's capacity check does not match the tolerance it grants with. | review r114 |
| R114-8 | P3 | 10 | suspected | WhichExe resolves a drive-relative name like `C:tool.exe` against that drive's current directory. | review r114 |
| R115-1 | P3 | 2 | suspected | Resample.ToRate rounds its output length half to even (5 samples to 2, 7 to 4). | review r115 |
| R115-2 | P3 | 2 | suspected | FrequencyGrid.NearestBin breaks a tie between two bins to the even one. | review r115 |
| R115-3 | P3 | 2 | suspected | MedianFilter moves O(w) elements per insert and remove (13 s at n = 400000, w = 200000). | review r115 |
| R115-4 | P3 | 2 | suspected | A Hampel window whose median is infinite gives a NaN MAD, so a finite sample there is never flagged. | review r115 |
| R115-5 | P3 | 2 | suspected | Welch and Multitaper scale with ScaleB after the sum, which may round subnormal results twice. | review r115 |
| R115-6 | P3 | 2 | suspected | An IReadOnlyList input that changes between reads gives inconsistent results. | review r115 |
| R115-7 | P3 | 2 | suspected | SavitzkyGolay still sums per row for an edge window holding non-finite values. | review r115 |
| R137-1 | P3 | 2 | suspected | SavitzkyGolay's fit still costs O(w·m²) near order w − 2 (seconds at w = 2001, documented; a stable O(w·m) basis would need more than Arnoldi with full reorthogonalisation); Multitaper's quiet bins below about 1e-308 of the peak are lost (inherent at that range?). Written but not run in round 137: PhaseUnwrap against NumPy's algorithm, Quantizer against an exact BigInteger code, MedianFilter and Hampel with infinities and signed zeros, GCC-PHAT against a DFT, CycleMeasurements on sines (analytic review only). | review r137 |
| R138-1 | P3 | 8 | suspected | ZipUtils on netstandard2.0 now reads the reparse tag (as DirectoryUtils does) so a cloud-file placeholder or deduplicated file is not taken for a link; untested, since a readable non-link reparse point needs its filter driver. FileRetention.TimestampedName passes a prefix holding "../" through unchecked (not counted as a bug). | review r138 |
| R139-1 | P3 | 16 | suspected | Unprobed WPF suspects from round 139: ValidationRuleSet.DependsOn re-validates only direct dependents (not transitive); MinLength/MaxLength count UTF-16 units, not text elements; PlotPresenter.ResetView is not clamped to MinZoom; ThemeManager reads a REG_QWORD AppsUseLightTheme as Light; NumericInputBehavior refuses a multi-line paste ("5\n999") rather than taking its first line. | review r139 |
| R116-1 | P3 | 12 | suspected | ConsoleTable pads ANSI-styled cells by their escape characters, misaligning columns. | review r116 |
| R116-2 | P3 | 12 | suspected | ConsoleSpinner may still count some astral wide ranges (beyond those fixed in round 116) as one column. | review r116 |
| R116-3 | P3 | 12 | suspected | AnsiStyle.Wrap ignores partial resets (22, 39, 49) inside the wrapped text. | review r116 |
| R116-4 | P3 | 12 | suspected | ProgressBar's fixed 1e-9 completion tolerance is not scaled to the total. | review r116 |
| R116-5 | P3 | 12 | suspected | Benchmark allocation counts are coarse on .NET Framework. | review r116 |
| R116-6 | P3 | 12 | suspected | StructuredTextFormatter is O(message x delimiter): 3.4 s for a 100k message with a 16k delimiter. | review r116 |
| R116-7 | P3 | 12 | suspected | A throwing Logger clock escapes Log. | review r116 |
| R116-8 | P3 | 12 | suspected | Logger.Dispose racing Log can lose an event already accepted. | review r116 |
| R116-9 | P3 | 12 | suspected | FirstChanceMonitor.ThrottleWindow is not validated. | review r116 |
| R116-10 | P3 | 12 | suspected | ConsoleSpinner.Stop(finalLine) throws on a broken writer. | review r116 |
| R117-1 | P3 | 16 | suspected | NumericRangeValidationRule passes "1e400" (overflows to infinity) when a bound is infinite, and the infinity symbol depends on the culture. | review r117 |
| R117-2 | P3 | 16 | suspected | CountToVisibilityConverter gives Collapsed for a non-zero Complex such as (0,5). | review r117 |
| R117-3 | P3 | 16 | suspected | Text committed through an IME may bypass NumericInputBehavior's checks (not probed). | review r117 |
| R117-4 | P3 | 16 | suspected | RecordingDialogService in Strict mode does not record a call that throws for want of a queued answer. | review r117 |
| R117-5 | P3 | 16 | suspected | ThemeManager on an STA thread that never pumps accepts FollowSystem and queues work that never runs. | review r117 |
| R117-6 | P3 | 16 | suspected | FileDropBehavior's drop re-checks every file on disk, skipping the per-drag cache. | review r117 |
| R118-1 | P3 | 3 | suspected | FitWeibull's log-likelihood may cancel on tightly clustered data (not probed against a reference). | review r118 |
| R118-2 | P3 | 3 | suspected | The two-sample Kolmogorov-Smirnov p-value is Stephens' asymptotic form and may be poor at small n. | review r118 |
| R118-3 | P3 | 3 | suspected | MultipleRegression standard errors are NaN at zero residual degrees of freedom (may be by design; undocumented). | review r118 |
| R118-4 | P3 | 3 | suspected | Bgrat's ln R adds the Stirling corrections' difference in order, the pattern fixed in round 118; no wrong value found (tiny b agreed with mpmath to 1e-15). | review r118 |
| R119-1 | P3 | 1 | suspected | PidController: a derivative kick on a setpoint step (derivative on error) can push the tentative output past a limit and freeze integration for that step; undocumented. | review r119 |
| R120-1 | P3 | 17 | suspected | gen-distribution-references.py: the F-series comment says "a few hundred terms", which may not match the loop's bound. | review r120 |
| R120-2 | P3 | 17 | suspected | bump-version.ps1 may accept an impossible date in the current version (no check that the date parses). | review r120 |
| R120-3 | P3 | 17 | suspected | FormState.TryParse may accept text Serialize never writes (looser than its format). | review r120 |
| R120-4 | P3 | 17 | suspected | WaitCursorScope raises its counter before the cursor setter, so a throwing setter may leave the count off by one. | review r120 |
| R120-5 | P3 | 17 | suspected | SelectionToTsv does no formula escaping, unlike ToCsv(escapeFormulas). | review r120 |
| R120-6 | P3 | 17 | suspected | FormState.Apply on an MDI child may place it against the screen rather than the parent's client area. | review r120 |
| R120-7 | P3 | 17 | suspected | SaveCsv's replace-by-move drops hard links and the target's permissions, and a path like NUL fails. | review r120 |
| R120-8 | P3 | 17 | suspected | verify.sh's MathChecks timeout may leave the test process running. | review r120 |
| R120-9 | P3 | 17 | confirmed | The test projects build with 35 warnings (CA1416 platform calls in IO and Docx tests, nullable CS8602/8605/8621/8714), seen once verify.sh's build line showed the count; a full rebuild prints them. | round 120 gates |
| R121-1 | P3 | 5 | suspected | HexBin with a radius far below a pixel returns no cells without saying why. | review r121 |
| R121-2 | P3 | 5 | suspected | An SVG callout label wider than the plot runs past the plot and the canvas. | review r121 |
| R121-3 | P3 | 5 | suspected | A wide SvgChart legend may spill left of the plot area. | review r121 |
| R121-4 | P3 | 5 | suspected | Not probed this pass: TimingDiagram, ScatterMatrix and MultiPanel output, LinePlot, BandChart, ErrorBarChart extremes. | review r121 |
| R122-1 | P3 | 4 | suspected | `AkimaSpline` (and makima) return −∞ inside a segment for knots and values spanning 1e307 to 1e308; a cubic may genuinely overshoot there, not separated. | review r122 |
| R122-2 | P3 | 4 | suspected | `Integration.CumulativeTrapezoid` keeps a plain running sum, so panels past double range may give NaN where `Trapezoid` now does not. Not probed. | review r122 |
| R122-3 | P3 | 4 | suspected | `RoundToMultiple(0.15, 0.1)` gives 0.1 where `RoundToSignificantDigits` rounds 0.15 to 0.2; whether the multiple's decimal reading is owed was not judged. | review r122 |
| R122-4 | P3 | 4 | suspected | `BaseConverter` with surrogate-pair alphabet characters, and `Polynomial` roots returned as −0, were read but not probed. | review r122 |
| R122-5 | P3 | 4 | suspected | Not run on .NET Framework 4.8 by the reviewers: `Primes` MulMod without UInt128; no thread-safety stress. | review r122 |
| R123-1 | P3 | 11 | suspected | `Slug.Slugify` with a negative `maxLength` means no limit, where `Truncate`, `MiddleEllipsis` and `TextAlign` reject a negative length; documented only as "if positive". | review r123 |
| R123-2 | P3 | 11 | suspected | `SnowflakeIdGenerator.NextId` spins for a full second holding its lock when the clock steps back right after a millisecond's 4096 ids, then throws (documented); every other caller blocks meanwhile. | review r123 |
| R123-3 | P3 | 11 | suspected | Not covered: `GlobMatcher` ignoreCase over non-ASCII case classes against an independent reference, id-generator concurrency stress, and the reviewers' probes on .NET Framework 4.8. | review r123 |
| R124-1 | P3 | 13 | suspected | `TcpLineClient` with a strict encoding: a line of invalid bytes is not held to `MaxLineLength` (10 MiB of 0xFF read under a limit of 1000, then DecoderFallbackException at the LF, not InvalidDataException); the skipped rest of an overlong line is unbounded in time. Memory stays bounded. | review r124 |
| R124-2 | P3 | 13 | suspected | `HttpDownload`: no size cap for a chunked body with no Content-Length; a 206 from byte 0 covering only part of the file may retry the same IOException; under Resume the destination is copied to `.part` before the URL is checked. Not probed. | review r124 |
| R124-3 | P3 | 13 | suspected | `KeyDerivation.Pbkdf2` and `PasswordHasher.Verify` leave password bytes and derived keys in memory (not zeroed). | review r124 |
| R124-4 | P3 | 13 | suspected | Not covered: `CronScheduler` concurrency, `HostInfo` and `PortCheck` at run time, Samoa's skipped 2011-12-30 (absent from this Windows zone data), and the reviewers' probes on .NET Framework 4.8. | review r124 |
| R125-1 | P3 | 14 | suspected | `DeepEquals`: a null key in a custom generic read-only dictionary may report unequal; a type implementing `IDictionary<K,V>` twice could throw an unwrapped TargetException. Not reproduced. | review r125 |
| R125-2 | P3 | 14 | suspected | `ConnectedComponents2D`'s int perimeter could overflow on a component past about 8 GB of cells; `ActivatorUtils.Matches` lets null fill ref, out or pointer parameters; a `Dictionary<object,int>` key cannot be reached through "[1]". | review r125 |
| R125-3 | P3 | 14 | suspected | A NaN or negative `FloatTolerance` is silently treated as exact instead of rejected. | review r125 |
| R125-4 | P3 | 14 | suspected | Not covered: the reviewers' probes on .NET Framework 4.8, and `Memoize` and `AttributeCache` under concurrent use. GridPathfinding's near-tie heap was probed: costs a few ulps above Dijkstra's, paths optimal. | review r125 |
| R136-1 | P3 | 14 | suspected | AttributeCache's static cache holds members and attributes strongly, so a collectible AssemblyLoadContext whose types were looked up cannot unload; ActivatorUtils.TryCreate runs a struct's explicit parameterless constructor and lets what it throws escape (as a class constructor's does: design?); TypeUtils' Uri to string uses Uri.ToString, which unescapes and may not round-trip; ConnectedComponents2D.LabelByValue with a non-transitive comparer may miscount a perimeter; Interval takes a null string endpoint as minus infinity; a path key holding ']' and both quotes cannot be spelled. Not probed. | review r136 |
| R126-1 | P3 | 7 | suspected | `DocxWriter`: tables past Word's 63 columns build without a word; the PNG check reads only the header; `ReportFigure` keeps the caller's PNG array uncopied; `HtmlReport.ImageData` does not check the MIME type. | review r126 |
| R126-2 | P3 | 7 | suspected | `PdfWriter`: code lines with emoji wrap early and lines split only by tabs never wrap; extra table cells are dropped silently; the footer may fall off the page at very large font sizes; AtTop has 1 pt of slack. | review r126 |
| R126-3 | P3 | 7 | suspected | `MarkdownReport`: `Heading("Issue #")` loses the '#' (CommonMark reads it as a closing sequence); `CodeBlock("")` writes an empty line in the fence; a paragraph starting "1. x" may not be escaped. | review r126 |
| R126-4 | P3 | 7 | suspected | Not covered: SettingsStore across processes, on long paths and on corrupt files; PDF xref offsets past 10 digits; DOCX and PDF output through external validators; the reviewers' probes on .NET Framework 4.8. | review r126 |
| R127-1 | P3 | 9 | suspected | `Counter.MostCommon` orders tied counts by dictionary order, which the doc does not define; `Aggregation.Pivot` hands its internal list to the aggregator, which could change it. | review r127 |
| R127-2 | P3 | 9 | suspected | Not covered: LruCache, TtlCache and ObjectPool under concurrent stress; Bloom and Count-Min error rates by experiment; DataTableLite sort stability; Trie with odd Unicode; the reviewers' probes on .NET Framework 4.8. | review r127 |
| R128-1 | P2 | 10 | suspected | An inline continuation that calls Dispose from a thread the consumer callback resumed on (a pool thread, not the loop's), or from PeriodicWorker's work, still waits on itself; only the pipeline's own loop thread is recognised. | review r128 |
| R128-2 | P3 | 10 | suspected | ProcessRunner: KillTree reports "killed" (exit -1) for a child that exited between the HasExited check and Kill, or on access denied; an OnOutputLine callback that never returns keeps Run waiting with no timeout. | review r128 |
| R128-3 | P3 | 10 | suspected | WithCancellation on a completed task with a cancelled token returns the result; no Unix exec-bit check in WhichExe; sub-millisecond timeouts on .NET Framework; FirstMatching and FirstSuccessful treat a null result differently; AsyncLock and Bulkhead fast paths do not re-check Dispose. Not covered: ShellOpen runs, ConcurrentPipeline and PeriodicWorker dispose stress, R1 on .NET Framework 4.8. | review r128 |
| R129-1 | P3 | 6 | suspected | Base58 Encode and Decode are quadratic with no length cap (Decode's default limit is int.MaxValue): a very large input runs for hours or out of memory instead of throwing ArgumentException as the other encoders do. Not covered: SvgDocument fuzzing, PngWriter on huge legal images, ImageBuffer drawing at extreme doubles, the reviewers' correctness probes on .NET Framework 4.8. | review r129 |
| R130-1 | P3 | 4 | suspected | Polynomial.Fit's size cap counts design-matrix cells, not bytes, so about 17 GB passes it and runs out of memory before any argument error; Primes.Sieve(2e9) peaks near 3 to 4 GB. Not probed (memory limit). | review r130 |
| R130-3 | P2 | 5, 7, 8, 14 | confirmed | .NET Framework's double.Parse misreads about 1.5% of short decimals at extreme exponents (and "R" leans on it), so code that parses back or round-trips decimals is off by an ulp there on net48 only: AxisTicks' decimal step, CsvDialect and CsvBinder number reads, TypeUtils and ObjectMembers round-trip checks, ReportTemplates number formats. Numerics core was fixed in round 130. | review r130 |
| R131-1 | P3 | 15 | suspected | JobObject.Start with a blank or missing working directory, or a command line over 32767 characters, throws Win32Exception rather than ArgumentException (with R106-7); ShortcutUtils.Read on a directory or a missing file throws exceptions its doc does not name; GetSpace may skip the long-path rewrite for a short relative path whose full path is long (not reproducible: the current directory cannot be that long); ResourceSampler keeps reporting a process after it exits, and its access-denied Win32Exception is undocumented; a SingleInstanceApp Dispose racing delivery might ack a dropped launch. Not covered: ClipboardUtils live paths, ScreenCapture output, SingleInstance races, EventLogWriter writes, PowerScheme, PrivilegeScope changes, RunElevated. | review r131 |
| R132-1 | P3 | 17 | suspected | PlotPictureBox's smooth (bicubic) path may misplace pixel boundaries at deep zoom as the nearest-neighbour path did; MenuShortcut accepts a doubled modifier ("Ctrl+Ctrl+S"); ToCsv accepts NUL as the delimiter; CSV formula escaping ignores full-width "＝" and "＋"; FormStatePersistence.Capture of an Aero-snapped window records the snapped bounds, not the normal ones; EnsureOnScreen leaves a window 520 of its 600 px below the screen (may be policy); SaveCsv over a hidden file drops the Hidden attribute (with R120-7). Not covered: PowerShell 5.1 and non-English runs of the scripts, the samples, the Windows and Wpf package contents. | review r132 |
| R133-1 | P3 | 1 | suspected | Elliptic designs with edges near 1e-4 of the sample rate are accepted though their quantised sections miss the spec (an 11th-order high-pass with rp 0.6746 dB has −0.4746 dB at its pass edge; SciPy's design has −0.674): IsWellConditioned may need a sensitivity test. PidController's derivative on error may kick on a setpoint change at extreme dt (plausible). Not covered: GroupDelay accuracy next to such poles, elliptic orders above 20 with rs near rp. | review r133 |
| R134-1 | P3 | 5 | suspected | TimingDiagram's last time-axis tick label (fixed 16 px right margin) may be clipped when wide; FromSamples throws ArgumentException for a NaN high threshold but ArgumentOutOfRangeException for a NaN low one, and NaN samples silently hold the level; ScatterMatrix's r label rounds an exact .125 half-to-even; an ErrorBarChart marker at the right edge can sit 1 px over the frame; BandChart's interpolation of two values near MaxValue may round to ∞. Known: an SVG callout label wider than the plot, a wide SvgChart legend spilling left. Not covered: Waterfall, Annotations internals, AxisTicks against a randomized reference. | review r134 |
| R135-1 | P3 | 12 | suspected | ConsoleTable counts wide CJK and emoji cells by UTF-16 length (documented) and keeps other C0 controls (BS, NUL) verbatim; Prompt.Choose matches trimmed input against untrimmed option text; a Prompt given a reader but no interactive flag never reads from it (design?); AnsiStyle treats a malformed ESC[38;0m as a selector, not a reset. Still open from earlier: ProgressBar's unscaled 1e-9 tolerance (no visible effect found), Benchmark's 0 ns mean (not reproduced), Logger.Dispose racing Log, StructuredTextFormatter O(message x delimiter), ConsoleSpinner.Stop(finalLine) throwing on a broken writer (Dispose is quiet). Not covered: FileSink rotation and lock failures, SyslogSink over TCP, HealthCheck, MetricsRegistry concurrency, RateLimitedSink. | review r135 |
| R130-2 | P3 | 4 | suspected | RoundToMultiple(0.3, 0.1) probably 0.30000000000000004; EngineeringNotation.Format(-0.0) drops the sign; a UnitConvert doc comment on the wrong member; temperatures below absolute zero accepted; Percentile interpolation maybe an ulp past the upper value; NextGaussian overflows to infinity for a huge standard deviation; NextDouble's exception type for min > max differs from the BCL. Not covered: AkimaSpline and ScatteredInterpolation (not read), Angle, Primes, RandomUtils, DeterministicRandom and Vector2 (read, not probed), thread safety. | review r130 |
