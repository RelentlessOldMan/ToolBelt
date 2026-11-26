# ToolBelt 🧰

**A zero-dependency, cross-platform C# "tool belt" — 200+ small, heavily-tested utilities that drop straight into any project.** Collections, numerics, text, graphs, grids, intervals, async, signal processing, statistics, and more — spread across focused divisions, every file independently copyable, every public contract exercised by tests.

A companion to **UtilityBelt** — a generic, project-agnostic collection of C# helper and utility
classes. ToolBelt is a separate playground repo where we build **net-new** reusable utilities under the
same design laws, so they can later be pulled into the UtilityBelt tree with minimal friction.

## Why

A defect in application code affects one application; a defect in a shared utility propagates into every
project that consumes it. ToolBelt treats that blast radius seriously: each utility is small, focused,
predictable, hard to misuse, and covered by behavioral, property-based, and differential tests
(**3,300+** of them). Drop a single `.cs` file into a project, or reference the whole assembly — either
works, with no dependencies to inherit.

> **Merge note:** namespaces are currently rooted at `ToolBelt.*` with divisions/paths kept identical to
> UtilityBelt. When we merge upstream, a single root find-replace (`ToolBelt` → `UtilityBelt`) flips it.

## Divisions at a glance

Each division is a coherent namespace (`ToolBelt.<Division>`). The full annotated file list is in [Layout](#layout).

| Division | What's in it | A few examples |
|---|---|---|
| **Binary** | Encodings, checksums, bit twiddling | `Base32` `Base58` `Hex` `Crc32` `Fnv1a` `VarInt` `Bits` `Luhn` |
| **Cli** | Console output, input and process plumbing | `ConsoleTable` `ProgressBar` `AnsiStyle` `ConsoleSpinner` `Prompt` `ConsoleApp` |
| **Collections** | Data structures & sequence ops | `LruCache` `TtlCache` `Deque` `BloomFilter` `Trie` `Batch` `Pairwise` `SortedListExtensions` `ReservoirSampler` `Aggregation` |
| **Configuration** | Layered config, typed binding, env vars, user settings | `ConfigLayers` `ConfigBinder` `EnvironmentVariables` `SettingsStore` |
| **Control** | Control loops, tuning & signal conditioning | `PidController` `PidTuning` `StepResponse` `KalmanFilter1D` `SlewRateLimiter` `SchmittTrigger` |
| **Diagnostics** | Measurement, observability & health | `Benchmark` `MetricsRegistry` `ScopedTimer` `HealthCheck` `DiskSpace` `AssertInvariant` `FirstChanceMonitor` |
| **Documents** | Report writers & builder (MD/HTML/PDF/DOCX) | `ReportBuilder` `ReportTemplates` `MarkdownReport` `HtmlReport` `PdfWriter` `DocxWriter` |
| **Enums** | Enum helpers | `EnumExtensions` `EnumFlags` `EnumMap` |
| **Functional** | Result/optional types, memoization | `Result` `Option` `Either` `Memoize` |
| **Graphs** | Graph algorithms | `Graph` `TopologicalSort` `ShortestPath` `MinimumSpanningTree` |
| **Grids** | 2-D grid algorithms | `FloodFill` `ConnectedComponents2D` `GridPathfinding` |
| **Guards** | Argument validation | `Guard` |
| **Identifiers** | Id generation & encoding | `Ulid` `NanoId` `ShortGuid` `SnowflakeIdGenerator` |
| **Intervals** | Interval/range structures | `Interval` `IntervalTree` `RangeMap` `RangeSet` |
| **IO** | Files, directories, streams, tabular text, zip | `AtomicFile` `DirectoryUtils` `FileWatcher` `ChecksumManifest` `CsvBinder` `CsvDialect` `JsonFile` `JsonLines` `FileRetention` `ZipUtils` `TarUtils` |
| **Logging** | Fan-out logging (plain-string events) | `Logger` `TextWriterSink` `FileSink` `SyslogSink` `AsyncLogSink` `RollingMemorySink` `FilterSink` `RouterSink` `RateLimitedSink` `ScopedContext` |
| **Net** | Address math, TCP/DNS, HTTP | `CidrRange` `IpUtils` `PortCheck` `TcpLineClient` `HostInfo` `HttpDownload` |
| **Numerics** | Math, stats, calculus, random | `DeterministicRandom` `Distributions` `Percentile` `Polynomial` `UnitConvert` `Bootstrap` |
| **Objects** | Reflection / object services | `DeepEquals` `PropertyDiff` `PropertyPath` `ObjectMapper` `FlattenObject` `UnflattenObject` `TypeUtils` |
| **Process** | Child processes | `ProcessRunner` `WhichExe` `ShellOpen` |
| **Quality** | Statistical process control & gauge studies | `ProcessCapability` `ControlChart` `GaugeRR` `MeasurementAgreement` |
| **Resilience** | Retry & rate control | `Retry` `CircuitBreaker` `Bulkhead` `TokenBucketRateLimiter` |
| **Runtime** | Process/runtime introspection & scratch memory | `StartupTiming` `AppInfo` `MemoryPressure` `ArrayPoolScope` |
| **Security** | Hashing, HMAC, KDF, secure random, AEAD | `Hashing` `Hmac` `KeyDerivation` `CryptoRandom` `AuthenticatedEncryption` `SecretsFile` |
| **Signal** | Digital signal processing | `Fft` `Window` `Spectrum` `WelchPsd` `IirFilter` `FirFilter` `Hilbert` `Goertzel` `Convolution` `Resample` `Quantizer` |
| **Text** | Strings & matching | `CaseConverter` `Slug` `GlobMatcher` `JaroWinkler` `TemplateFormatter` |
| **Threading** | Async coordination | `AsyncLock` `KeyedLock` `ParallelUtils` `TaskExtensions` `TaskRace` `PauseTokenSource` `AtomicCounters` |
| **Time** | Dates, durations, schedules | `DateRange` `HumanDuration` `CronSchedule` `CronScheduler` `BusinessDays` `UnixTime` |
| **Visualization** | Raster canvas, charts, PNG/BMP & SVG, palettes | `ImageBuffer` `PngWriter` `PngReader` `Bmp` `Palettes` `SvgDocument` `SvgChart` `MultiPanel` `Waterfall` `HexBin` `ScatterMatrix` `TimingDiagram` `Annotations` `AxisTicks` `Colormap` `HeatMap` `LinePlot` `Histogram` `BoxPlot` `ErrorBarChart` `BandChart` `Colorbar` |

> The cross-platform core above is BCL-only. Windows-only Win32/registry helpers live in a **separate**
> `ToolBelt.Windows` assembly (net8.0-windows) — `PowerStatus`, `SingleInstance`, `DriveAndVolumeInfo`,
> `MonitorInfo`, `RegistryUtils`, `Elevation`, `JobObject`, `WindowUtils`, `FileAssociation`,
> `ShortcutUtils`, `ClipboardUtils`, `ScreenCapture`, `ResourceSampler`, `RunElevated`, `OsVersionInfo`,
> `MemoryStatus`, `IdleTime`, `EventLogWriter`, `SingleInstanceApp`, `StorageDeviceInfo`, `DpiInfo`, `PrivilegeScope`,
> `PowerScheme` — so they never burden the portable core. MVVM / forms helpers live in further
> Windows-only assemblies, `ToolBelt.Wpf` (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`,
> `NotifyTaskCompletion`, `ValidationObservableObject`, `ValidationRules`, `BindingDiagnostics`, `ValueConverters`, `MultiValueConverters`,
> `BindingProxy`, `Messenger`, `DialogService`, `IncrementalCollection`, `FileDropBehavior`, `NumericInputBehavior`, `GlobalHotkeys`, `ThemeManager`, `PlotPresenter`) and
> `ToolBelt.WinForms` (`ControlExtensions`, `ControlTreeExtensions`, `DoubleBufferedExtensions`, `DoubleBufferedPanel`,
> `WaitCursorScope`, `ComboBoxEnumExtensions`, `LayoutSuspender`, `FormStatePersistence`, `DataGridViewExtensions`,
> `MenuBuilder`, `ToolStripBuilder`, `PlotPictureBox`). See [Layout](#layout).

## Find what you need

| I want to… | Reach for |
|---|---|
| Hash / checksum bytes | `Binary.Crc32`, `Binary.Fnv1a`, `Binary.Adler32` |
| Encode binary as text | `Binary.Base32` / `Base58` / `Base64Url` / `Hex` |
| Encode/decode MIME quoted-printable | `Binary.QuotedPrintable` |
| Generate a unique id | `Identifiers.UlidGenerator`, `NanoId`, `ShortGuid` |
| Retry / rate-limit a flaky call | `Resilience.Retry`, `CircuitBreaker`, `TokenBucketRateLimiter` |
| Cache with eviction / expiry | `Collections.LruCache`, `TtlCache` |
| Probabilistic set membership (optionally with deletes) | `Collections.BloomFilter`, `CountingBloomFilter` |
| Parse or format a duration | `Time.HumanDuration`, `Iso8601Duration` |
| Compute the next cron occurrence | `Time.CronSchedule` |
| Run jobs on cron schedules | `Time.CronScheduler` |
| Fuzzy-match strings ("did you mean") | `Text.JaroWinkler`, `LevenshteinDistance`, `NGramSimilarity` |
| Rank "did you mean" suggestions from a list | `Text.StringSimilarity.BestMatch` / `TopMatches` |
| Diff two blocks of text (line by line) | `Text.TextDiff` |
| Change naming case / make a slug | `Text.CaseConverter`, `Slug` |
| Match paths with wildcards | `Text.GlobMatcher` |
| Split / build a command line | `Text.CommandLineSplitter`, `CommandLineBuilder` |
| Run a child process (capture / timeout / kill) | `Process.ProcessRunner` (+ `WhichExe`, `ShellOpen`) |
| Log to sinks (console / file / memory) | `Logging.Logger` + `TextWriterSink` / `RollingMemorySink` |
| Collect metrics / time a block | `Diagnostics.MetricsRegistry`, `ScopedTimer` |
| Flatten / classify an exception | `Diagnostics.ExceptionUtils` |
| App version / memory / startup timing | `Runtime.AppInfo`, `MemoryPressure`, `StartupTiming` |
| Capture a bug-report environment snapshot | `Diagnostics.EnvironmentReport` |
| Startup health checks with timeouts | `Diagnostics.HealthCheck` |
| Find exceptions that are thrown and swallowed | `Diagnostics.FirstChanceMonitor` |
| Send logs to a syslog server (UDP/TCP, RFC 5424) | `Logging.SyslogSink` |
| Write to the Windows Event Log | `ToolBelt.Windows.EventLogWriter` |
| Second app launch hands its arguments to the running one | `ToolBelt.Windows.SingleInstanceApp` |
| Cap a child process's memory / CPU; start it inside the job so nothing it starts escapes | `ToolBelt.Windows.JobObject` (`Start`) |
| Is this drive USB / SSD? Where does Z: really point? | `ToolBelt.Windows.StorageDeviceInfo` |
| Physical pixels on a scaled display (DPI) | `ToolBelt.Windows.DpiInfo` |
| Temporarily enable a Windows privilege | `ToolBelt.Windows.PrivilegeScope` |
| Check / switch the power plan | `ToolBelt.Windows.PowerScheme` |
| Copy files or an image to the clipboard | `ToolBelt.Windows.ClipboardUtils` |
| Declarative view-model validation rules (WPF) | `ToolBelt.Wpf.ValidationRuleSet`, `RuleValidatedObject` |
| Find broken WPF bindings (or fail tests on them) | `ToolBelt.Wpf.BindingFailureListener`, `DebugConverter` |
| Flicker-free custom drawing (WinForms) | `ToolBelt.WinForms.DoubleBufferedPanel` |
| Will this run fit on disk / is the folder writable | `Diagnostics.DiskSpace` (`Preflight`, `EstimateBytes`) |
| Debug-only internal consistency checks | `Diagnostics.AssertInvariant` |
| Borrow a scratch buffer safely | `Runtime.ArrayPoolScope` |
| Map CSV rows ↔ typed objects | `IO.CsvLine`, `CsvBinder` |
| Query untyped rows by column (filter/sort/join/group) | `Collections.DataTableLite` |
| Write a file without torn writes | `IO.AtomicFile` |
| Zip / unzip safely (Zip-Slip guarded) | `IO.ZipUtils` |
| Tar / .tar.gz safely (traversal and links guarded) | `IO.TarUtils` |
| Load/save JSON files (atomic, errors name the line) / JSON Lines | `IO.JsonFile`, `JsonLines` |
| Keep only the newest N logs/captures; numbered rotation | `IO.FileRetention` |
| Count bytes through a stream / copy a stream while reading it | `IO.CountingStream`, `TeeStream` |
| Read European `;` CSV with decimal commas / sniff the dialect | `IO.CsvDialect` |
| Copy / delete / size a directory tree safely | `IO.DirectoryUtils` |
| React to file changes without duplicate or half-written events | `IO.FileWatcher` |
| Hash a tree, verify it later, diff two trees | `IO.ChecksumManifest` (sha256sum-compatible) |
| Merge config from many sources | `Configuration.ConfigLayers` + `ConfigBinder` |
| Typed environment variables that fail loudly when malformed | `Configuration.EnvironmentVariables` |
| Per-user settings file that survives corruption | `Configuration.SettingsStore<T>` |
| Bounded-concurrency async fan-out | `Threading.ParallelUtils.ForEachAsync` |
| Lock across `await` / per key | `Threading.AsyncLock`, `KeyedLock` |
| Await a cancellation token / link it with a timeout | `Threading.CancellationTokenExtensions` |
| First good answer from several attempts / hedged request | `Threading.TaskRace` |
| Pause and resume a long-running async job | `Threading.PauseTokenSource` |
| Cancel on Ctrl+C | `Cli.ConsoleApp.CreateInterruptSource` |
| Reproducible random / sampling | `Numerics.DeterministicRandom`, `RandomUtils` |
| Streaming mean / variance / percentiles | `Numerics.RunningStatistics`, `Percentile` |
| Confidence intervals / bootstrap | `Numerics.ConfidenceInterval`, `Bootstrap` |
| Percentiles of a huge stream in O(1) memory | `Numerics.StreamingQuantile` |
| Random sample of a stream of unknown length | `Collections.ReservoirSampler<T>` |
| Weighted sample without replacement / stratified sample | `Collections.SamplingPlans` |
| lower_bound / upper_bound / nearest on a sorted list | `Collections.SortedListExtensions` |
| Consecutive pairs / differences of a sequence | `Collections.PairwiseExtensions.Pairwise` |
| Rebuild a nested object from dotted paths | `Objects.UnflattenObject` |
| Significance test with no distribution assumptions | `Numerics.PermutationTest` |
| Regress on several predictors / robust line fit | `Numerics.MultipleRegression`, `RobustRegression.TheilSen` |
| Which distribution fits this data? | `Numerics.DistributionFit` |
| Report a measurement with its uncertainty | `Numerics.Uncertainty` |
| Smooth interpolation without ringing / scattered 2-D data | `Numerics.AkimaSpline`, `ScatteredInterpolation` |
| t / chi-square / F critical values and p-values | `Numerics.Distributions` (`StudentTQuantile`, `ChiSquareQuantile`, `FQuantile`, ...) |
| Compare two variances / significance decision | `Numerics.HypothesisTests.FTestEqualVariances`, `TestResult.IsSignificant` |
| Run a significance test (t / χ² / KS / Mann-Whitney) | `Numerics.HypothesisTests` |
| Compare several group means (one-way ANOVA / F-test) | `Numerics.Anova` |
| Fit a curve / solve a linear system | `Numerics.Polynomial`, `LinearAlgebra` |
| Low/high/band-pass or notch a signal (Butterworth, Chebyshev I/II, elliptic, Bessel, EQ biquads) | `Signal.IirFilter`, `Biquad` |
| Lowest filter order that meets a pass/stop spec (`buttord`, `ellipord`, …) | `Signal.IirFilter.ButterworthOrder`, `Chebyshev1Order`, `Chebyshev2Order`, `EllipticOrder` |
| Filter without distorting pulse shape (flat group delay) | `Signal.IirFilter.BesselLowPass`, `GroupDelay` |
| Zero-phase filtering of a recorded signal (`filtfilt`) | `Signal.IirFilter.FiltFilt` |
| Linear-phase FIR filter design (windowed sinc, Kaiser sizing) | `Signal.FirFilter` |
| Rise time / overshoot / settling time of a step response | `Control.StepResponse` |
| Tune a PID loop from a step test | `Control.FopdtModel.FitStep` + `PidTuning` |
| Ramp a setpoint, hysteresis switch, deadband, time-constant smoothing | `Control.SlewRateLimiter`, `SchmittTrigger`, `Deadband`, `FirstOrderLag` |
| Gauge R&R / measurement-system analysis | `Quality.GaugeRR` |
| Gamma / erf / incomplete gamma & beta | `Numerics.SpecialFunctions` |
| Convert units | `Numerics.UnitConvert` |
| Shortest path / dependency order | `Graphs.ShortestPath`, `TopologicalSort` |
| Find a path across a 2-D grid (BFS / A* / Dijkstra) | `Grids.GridPathfinding` |
| Flood-fill / find a connected region | `Grids.FloodFill` |
| Label connected components in a grid | `Grids.ConnectedComponents2D` |
| Query overlapping intervals | `Intervals.IntervalTree`, `RangeSet` |
| Diff or deep-compare object graphs | `Objects.PropertyDiff`, `DeepEquals` |
| Fast reflection-free get/set by member name | `Objects.ExpressionAccessor` |
| CIDR / IP address math | `Net.CidrRange`, `IpUtils` |
| Check a port / find a free one | `Net.PortCheck` |
| Line-oriented TCP request/response | `Net.TcpLineClient` |
| Resolve DNS (with timeout) / list interfaces | `Net.HostInfo` |
| Download a file (resume / checksum / retry) | `Net.HttpDownload` |
| Render a console table / bar / sparkline | `Cli.ConsoleTable`, `ProgressBar`, `Sparkline` |
| Colour console output (honours NO_COLOR) | `Cli.AnsiStyle` |
| Show a spinner while working | `Cli.ConsoleSpinner` |
| Ask yes/no, text, number or choice — safely in scripts | `Cli.Prompt` |
| Main wrapper: Ctrl+C token, exit codes, error reporting | `Cli.ConsoleApp`, `ExitCodes` |
| Pad / centre / fit coloured or CJK text in columns | `Text.TextAlign` |
| Split a line with quotes and escapes (shell or CSV rules) | `Text.Tokenizer` |
| Percent-encode / decode (RFC 3986, strict UTF-8) | `Text.PercentEncoding` |
| Validate method arguments | `Guards.Guard` |
| Draw a raster image / write a PNG | `Visualization.ImageBuffer`, `PngWriter` |
| Read a PNG (any colour type/depth, interlaced) / read or write BMP | `Visualization.PngReader`, `Bmp` |
| Colour-blind-safe series colours / readable label colour (WCAG) | `Visualization.Palettes` |
| Render a heat map / line plot | `Visualization.HeatMap`, `LinePlot` (+ `Colormap`) |
| Nice axis ticks (linear / log / time) | `Visualization.AxisTicks` |
| Labelled vector chart for a report | `Visualization.SvgChart` |
| Several plots in one figure (shared axes) | `Visualization.MultiPanel` |
| Spectrogram / waterfall display | `Visualization.Waterfall` |
| Too many points for a scatter plot (density) | `Visualization.HexBin` |
| Pairs plot of several channels (correlations at a glance) | `Visualization.ScatterMatrix` |
| Digital timing / protocol diagram (clock, buses, cursors) | `Visualization.TimingDiagram` |
| One report, many formats (HTML/MD/PDF/Word) | `Documents.ReportBuilder` |
| Write a Word document (lists, images, page numbers) | `Documents.DocxWriter` |
| Write a PDF (tables, images, bookmarks, page numbers) | `Documents.PdfWriter` |
| Run summary / A-vs-B comparison / pass-fail report | `Documents.ReportTemplates` |
| Limit lines / shaded zones / callouts on a plot | `Visualization.Annotations` (SVG or raster via `LinePlot.Frame`) |
| Map data to pixels for a custom renderer | `Visualization.PlotFrame` (+ `SvgUtils` for SVG output) |
| Hash / HMAC / verify a token safely | `Security.Hashing`, `Hmac`, `ConstantTime` |
| Hash a file / hash data arriving in chunks | `Security.Hashing.Sha256File`, `CreateIncremental` |
| Store secrets encrypted under a passphrase | `Security.SecretsFile` |
| Generate a secure token / password | `Security.CryptoRandom` |
| Hash a login password (with upgrade path) | `Security.PasswordHasher`, `KeyDerivation` |
| Encrypt + authenticate a message (AEAD) | `Security.AuthenticatedEncryption` (AES-GCM) |
| Use a list/array as a dictionary key by contents | `Collections.SequenceEqualityComparer`, `MultisetEqualityComparer` |

## Design laws (inherited from UtilityBelt)

1. **Clone-and-own, drop-in files.** Every `.cs` file is independently copyable and self-contained. Its
   first line declares its only in-repo dependency, e.g.
   `// ToolBelt drop-in — fully self-contained (BCL only).` **No leaf file depends on another leaf file.**
2. **BCL-only core, zero external dependencies.** Anything needing a third-party package lives in an
   opt-in satellite project excluded from the solution. Never packaged, never published as NuGet.
3. **Multi-target `netstandard2.0` + `net8.0`.** Modern fast paths sit behind `#if` guards; netstandard2.0
   fallbacks are first-class, not afterthoughts.
4. **Documented duplication over premature sharing.** The only blessed shared primitive is
   [`Guard`](src/ToolBelt/Guards/Guard.cs). Prefer a little copy-paste to a new cross-file helper.
5. **Stable namespaces** so referenced and copied code interoperate.

## Layout

```
ToolBelt.sln
src/
  ToolBelt/                 portable core (netstandard2.0; net8.0), BCL-only
    Guards/
      Guard.cs              the shared argument-validation primitive
    Binary/
      Base32.cs             RFC 4648 Base32 encode/decode (canonical: one accepted spelling per value)
      Adler32.cs            Adler-32 checksum (batched + streaming)
      Base58.cs             Base58 (Bitcoin alphabet, leading-zero preservation; quadratic, for short values; Decode takes a maxLength)
      Base64Url.cs          URL-safe base64 (RFC 4648 §5, unpadded, canonical)
      Base85.cs             Ascii85 encode/decode
      Bits.cs               popcount / leading-trailing-zeros / rotate (32 & 64-bit)
      Crc16.cs              CRC-16/CCITT-FALSE checksum (+ streaming)
      Crc32.cs              CRC-32 (IEEE) checksum, table-based + streaming
      CrockfordBase32.cs    Crockford Base32 (I/L/O normalization, hyphens ignored)
      EndianConverter.cs    read/write 16/32/64-bit ints in explicit byte order
      Fnv1a.cs              FNV-1a 32/64-bit hash (bytes + strings)
      GrayCode.cs           reflected binary (Gray) code conversion
      Hex.cs                hex encode/decode (case option, strict decode)
      Luhn.cs               Luhn mod-10 checksum (validate + check digit)
      QuotedPrintable.cs    MIME quoted-printable encode/decode (RFC 2045, byte-exact round-trip)
      VarInt.cs             LEB128 var-length ints (unsigned + ZigZag signed)
    Cli/
      ConsoleTable.cs       aligned ASCII text table (alignment, padding; colour sequences take no width)
      ProgressBar.cs        render a text progress bar (bar body / bracketed + percent)
      Sparkline.cs          one-line block-character mini chart of a series
      AnsiStyle.cs          16/256/24-bit colour + bold/underline; NO_COLOR / FORCE_COLOR / TERM=dumb aware
      ConsoleSpinner.cs     in-place spinner (stderr), plain lines when not a terminal
      Prompt.cs             confirm / text / integer / choice; defaults or NonInteractiveException when scripted
      ConsoleApp.cs         Main wrapper: Ctrl+C token, ExitCodes (sysexits, 130), ExitException, error lines
    Collections/
      BinaryHeap.cs         binary-heap priority queue (custom comparer)
      Batch.cs              lazy fixed-size batching of any IEnumerable<T>
      Pairwise.cs           lazy single-pass adjacent pairs (+ selector)
      CircularBuffer.cs     fixed-capacity ring buffer (overwrites oldest)
      BiMap.cs              bidirectional one-to-one map
      Aggregation.cs        group-by summaries (count/mean/min/max/stddev) + pivot
      BloomFilter.cs        probabilistic set membership (no false negatives)
      CountingBloomFilter.cs  counting Bloom filter: probabilistic set with delete (saturating counters)
      CartesianProduct.cs   lazy Cartesian product of sequences
      CountMinSketch.cs     approximate stream frequencies (never under-counts)
      SlidingWindow.cs      lazy fixed-size sliding windows (step 1)
      Combinatorics.cs      lazy permutations & combinations
      Counter.cs            multiset / frequency counter (most-common)
      DataTableLite.cs      untyped in-memory table: project/filter/sort/distinct/group/inner-join by column
      Deque.cs              double-ended queue (growable circular array, O(1) ends)
      DisjointSet.cs        union-find (path compression + union by rank)
      FenwickTree.cs        binary indexed tree (prefix/range sums, point update)
      IndexedPriorityQueue.cs  keyed min-priority queue with decrease-key (O(log n))
      BitSet.cs             growable bit set (union / intersect / except); Count is a long
      LruCache.cs           fixed-capacity least-recently-used cache (O(1))
      MultiDictionary.cs    key -> many-values multimap; Total is a long
      ObjectPool.cs         bounded thread-safe pool of reusable objects (rent/return/reset)
      OrderedDictionary.cs  insertion-ordered generic dictionary
      OrderedSet.cs         insertion-ordered set
      Sampling.cs           Fisher-Yates shuffle + weighted pick (injected Random)
      StreamSampling.cs     ReservoirSampler (Algorithm L) + SamplingPlans: weighted w/o replacement (A-ES), stratified
      SortedListExtensions.cs  LowerBound/UpperBound/EqualRange/NearestBy/InsertSorted/RemoveSorted/RangeSorted
      SequenceEqualityComparer.cs  structural sequence equality: ordered element-wise + unordered multiset (dict/set keys)
      TopN.cs               streaming N-largest via a bounded min-heap
      Trie.cs               prefix tree (contains / starts-with / with-prefix)
      TtlCache.cs           time-expiring cache (injectable clock)
    Configuration/
      ConfigLayers.cs       merge sources by priority into a flat key space (with provenance); keys fold case by one
                            fixed rule on every runtime, and a key held only as null is not found
      ConfigBinder.cs       bind a flat key space to a typed object (nested/arrays/enums/durations); public setters only, null = unset
      EnvironmentVariables.cs  typed env reads (int/bool/duration/enum/list), malformed = error naming the variable
      SettingsStore.cs      per-user JSON settings: atomic save, corrupt file moved aside, defaults, Update restores on a throw;
                            an unreadable file is never overwritten except by Reset, a symlink is written through, any
                            get-only collection or private setter reloads, a lone surrogate is refused (net8)
    Control/
      PidController.cs      PID with clamping, anti-windup, derivative-on-measurement
      PidTuning.cs          FOPDT model fit from a step test (two-point) + SIMC / lambda / Ziegler-Nichols / Cohen-Coon rules
      StepResponse.cs       rise time, settling time, overshoot/undershoot, peak (interpolated, stepinfo-style)
      KalmanFilter1D.cs     scalar Kalman filter (predict/update, converging gain)
      SlewRateLimiter.cs    rate-limited follower (separate rise/fall rates) for setpoint ramps
      SchmittTrigger.cs     comparator with hysteresis (no chatter near the threshold)
      Deadband.cs           deadband shaping + report-by-exception DeadbandFilter
      FirstOrderLag.cs      time-constant low-pass, exact for any (irregular) update interval
    Diagnostics/
      Benchmark.cs          micro-benchmark harness (warmup, stats, bytes/op, compare-to-baseline, MaxIterations; .NET Framework counts the domain's bytes)
      MetricsRegistry.cs    named counters / gauges / timers with percentile snapshots (thread-safe, capped samples per timer)
      ScopedTimer.cs        time a using-block to a callback / metrics (injectable clock; a clock stepped back reports zero)
      ExceptionUtils.cs     root-cause / flatten / describe / transient-vs-permanent classify
      EnvironmentReport.cs  one-call bug-report snapshot (versions/OS/culture/uptime/env, secret-safe)
      HealthCheck.cs        named probes w/ enforced timeouts (an abandoned probe's token is cancelled; a stuck one isn't restarted), run concurrently -> worst-status report
      FirstChanceMonitor.cs report thrown-then-swallowed exceptions; throttled per signature (bounded table), recursion-safe
      DiskSpace.cs          free space on a path's volume, writability probe, pre-run fit check + size estimate
      AssertInvariant.cs    conditionally compiled internal-consistency checks (DEBUG / TOOLBELT_INVARIANTS)
    Documents/
      MarkdownReport.cs     fluent GitHub-flavored Markdown (headings/lists/tables/code/quotes)
      HtmlReport.cs         self-contained HTML doc with embedded CSS; escaping; base64 image embed
      PdfWriter.cs          hand-rolled PDF: exact base-14 metrics (font sizes ≤ 10,000 pt), wrapping, pagination, tables (header repeats),
                            PNG/RGBA images with alpha, bookmarks from headings, page numbers, code blocks, title
      DocxWriter.cs         Word .docx via OOXML zip: mixed-format runs, real nested bullet/numbered lists, tables
                            (up to Word's 63 columns, MaxColumns), PNG images, page breaks, header + "Page X of Y"
                            footer, title/author; Save (here and in PdfWriter) replaces the file whole
      ReportBuilder.cs      typed report model (meta, TOC, props, tables, figures, code, callouts) -> HTML/MD/PDF/DOCX
      ReportTemplates.cs    ready-made RunSummary / Comparison (A vs B, deltas, flags) / PassFailMatrix reports
    Enums/
      EnumExtensions.cs     cached values/names, strict name parse, [Description], flag split
      EnumFlags.cs          generic [Flags] add / remove / toggle / has-all/any
      EnumMap.cs            dense enum-keyed map (array-backed, O(1))
    Functional/
      Either.cs             disjoint union of two types (Left/Right, Match / MapLeft / MapRight)
      Memoize.cs            cache a pure function's results (single-threaded / thread-safe / lazy factory)
      Option.cs             optional value (Some/None, Map / Bind / Where / Match)
      Result.cs             success/failure result type (Map / Bind / Match / Try)
    Graphs/
      Graph.cs              adjacency-list graph (directed/undirected, weighted, BFS/DFS)
      TopologicalSort.cs    Kahn's algorithm; returns a valid order or the actual cycle
      StronglyConnectedComponents.cs  Tarjan's SCC (iterative, reverse-topological order)
      ShortestPath.cs       BFS (unweighted) + Dijkstra (non-negative) over the shared binary heap
      MinimumSpanningTree.cs  Kruskal's MST/forest over the shared union-find
    Grids/
      Cell.cs               (row, col) coordinate + Connectivity (Four/Eight) — the division's shared types
      FloodFill.cs          paint-bucket region find / in-place fill (value or predicate)
      ConnectedComponents2D.cs  label blobs (mask or equal-value); area, bbox, centroid, edge perimeter (holes included)
      GridPathfinding.cs    BFS (fewest steps) / A* (uniform, admissible heuristic) / Dijkstra (weighted)
    Intervals/
      Interval.cs           half-open [start, end) over any comparable key
      IntervalTree.cs       augmented index: intervals containing a point / overlapping a range
      RangeMap.cs           non-overlapping key-range -> value (splits + coalesces; always minimal; blocked, near-linear inserts)
      RangeSet.cs           union / intersect / difference / complement over intervals
    Net/
      CidrRange.cs          CIDR block (v4/v6): parse, Contains, network/broadcast, count, enumerate
      IpUtils.cs            private/loopback detection, IPv4 netmask <-> prefix length
      PortCheck.cs          TCP reachability within a timeout + find a free local port
      TcpLineClient.cs      line-oriented TCP client (connect/read/write timeouts, MaxLineLength, partial lines survive a timeout,
                            a strict encoding's bad line throws once and is dropped)
      HostInfo.cs           local host/interface info + DNS resolve with a timeout
      HttpDownload.cs       download to file via .part: progress, resume (Range), checksum, transient retry, ReadTimeout, MaxBytes;
                            a 204 or 205 answer is an error
    Quality/
      ProcessCapability.cs  Cp/Cpk/sigma-level/ppm (explicit overall vs within-subgroup sigma)
      ControlChart.cs       I-MR limits + run rules (beyond-limits / one-side / trend)
      MeasurementAgreement.cs  Bland-Altman bias + limits of agreement between two methods
      GaugeRR.cs            crossed gauge R&R by ANOVA: variance components, %study var/%tolerance, ndc
    Signal/
      Convolution.cs        direct convolution + full cross-correlation
      Fft.cs                radix-2 + Bluestein FFT — O(n log n) for ANY length; forward/inverse
      IirFilter.cs          Biquad (RBJ cookbook EQ shapes) + SOS cascade: Butterworth / Chebyshev I & II / elliptic / Bessel design, order estimation, group delay, streaming, filtfilt; sections ordered and L∞-scaled, edges within rounding of 0 or Nyquist rejected
      FirFilter.cs          windowed-sinc FIR design (firwin-compatible, exactly symmetric taps), Kaiser sizing, streaming + delay-aligned filtering
      FrequencyGrid.cs      FFT bin <-> frequency mapping; resolution bandwidth
      Goertzel.cs           single-frequency magnitude/phase (cheaper than a full transform)
      Hampel.cs             sliding-window outlier rejection (median ± k·MAD)
      Hilbert.cs            analytic signal, amplitude envelope, instantaneous phase/frequency
      LevelConversions.cs   dB/amplitude/power conversions, RMS, dBFS
      MedianFilter.cs       sliding-window median (removes spikes, preserves edges)
      PhaseUnwrap.cs        removes 2π jumps from wrapped phase (NumPy/MATLAB tolerance semantics); group delay −dφ/dω
      PulseMeasurements.cs  rise/fall time, pulse width, duty cycle (histogram state levels, sub-sample crossings)
      Quantizer.cs          uniform ADC model + ideal 6.02N+1.76 dB SNR
      Resample.cs           linear resample / rate conversion / anti-aliased decimation
      SavitzkyGolay.cs      polynomial smoothing (preserves peaks; polynomials pass through)
      Spectrum.cs           one-sided amplitude / power / dB spectra
      TimeDelayEstimate.cs  cross-correlation & GCC-PHAT lag estimation
      WelchPsd.cs           Welch's averaged-periodogram power spectral density
      Multitaper.cs         sine-taper multitaper PSD (low variance from short records; Welch-compatible units)
      PeakInterpolation.cs  sub-bin peak: parabolic / Gaussian (log-parabolic) + spectral-peak frequency readout
      EnvelopeFollower.cs   streaming attack/release peak envelope (exact time constants)
      CycleMeasurements.cs  cycle-by-cycle period/frequency/high time/duty + jitter stats, hysteresis crossings
      Window.cs             Hann/Hamming/Blackman/Blackman-Harris/flat-top + gain factors
      ZeroCrossing.cs       zero-crossing indices with sub-sample linear interpolation
    Objects/
      ActivatorUtils.cs     construct by best-matching constructor; safe assembly scan-and-create
      AttributeCache.cs     cached custom-attribute lookups for types/members (thread-safe)
      DeepEquals.cs         structural graph equality (cycles, float tolerance, collections, sets; any depth)
      ObjectMembers.cs      shared readable-member lookup, key text and struct value equality (used by DeepEquals, PropertyDiff, FlattenObject)
      ExpressionAccessor.cs compiled get/set delegates by member name (cached; boxing-free typed path)
      FlattenObject.cs      object graph -> flat path->value dictionary (any depth)
      UnflattenObject.cs    flat paths -> nested dictionaries/lists (inverse of Flatten), conflicts named
      ObjectMapper.cs       copy matching properties across types (convert / ignore / custom)
      PropertyDiff.cs       differences between two graphs as (path, old, new) (any depth; a shared pair once, at its first path)
      PropertyPath.cs       get/set via dotted path with indexers (clear per-segment errors; writes structs back; invariant keys)
      TypeUtils.cs          friendly names / default / unwrap-nullable / numeric / safe (never lossy) convert / scan
    Identifiers/
      NanoId.cs             compact URL-safe random ids (injectable Random, non-crypto)
      ShortGuid.cs          Guid <-> compact 22-char URL-safe string (lossless)
      SnowflakeIdGenerator.cs  64-bit time-ordered ids (injectable clock; waits for the clock when a millisecond's ids run out)
      Ulid.cs               ULID generator (monotonic, Crockford, injectable clock/rng; throws when a millisecond's randomness runs out)
    IO/
      AtomicFile.cs         crash-safe write (temp + swap, optional .bak backup)
      ByteSize.cs           human-readable byte sizes (format + parse, binary/decimal)
      CsvBinder.cs          map string rows <-> typed objects by header (invariant round-trip: dates, Guid, TimeSpan, enums, nullables, -0; exact case first)
      CsvLine.cs            RFC 4180 CSV line parse + format (an unterminated quote throws)
      CsvDialect.cs         delimiter/decimal/header presets (Excel European, tab, pipe) + Detect sniffing
      FileRetention.cs      prune by count/age/total size (+ dry run), numbered rotation, timestamped names
      JsonFile.cs           JSON load/save with shared options and atomic writes + JsonLines (NDJSON) (net8)
      DotEnv.cs             .env key=value parser (comments, quotes + inline comments, export, BOM)
      FixedWidth.cs         fixed column-width record parse + format
      HexDump.cs            classic offset/hex/ascii hex dump
      LineReader.cs         enumerate lines with byte offsets (mixed line endings; ASCII-compatible encodings, UTF-8 BOM skipped; line-length cap)
      SafeFileName.cs       sanitize a string into a filename safe on every OS (Windows rules everywhere; capped in UTF-8 bytes)
      StreamUtils.cs        copy-with-progress (+ cancellation) / read-exactly (memory grows with the data)
      StreamWrappers.cs     CountingStream (bytes read/written) and TeeStream (duplicate reads or writes); closed after Dispose even when leaving the inner open
      TempFile.cs           disposable temp file & directory scopes (extension checked; iterative delete, any depth)
      ZipUtils.cs           zip create/extract/list/read (entry and total size caps, keeps empty dirs and times; update via a streamed copy that keeps modes and comments); Zip-Slip guarded, links never followed
      TarUtils.cs           tar/.tar.gz create/extract/list (keeps empty dirs, total size cap); traversal rejected, links never created or followed (net8)
      DirectoryUtils.cs     tree copy (overwrite/filter policies), delete w/ read-only + cancellable lock retry, never follows links (re-checked per file); CopyOptions.CancellationToken
      ChecksumManifest.cs   SHA-256 tree manifest in sha256sum format: create/save/verify/compare directories (with a file filter; a name with an unpaired surrogate is refused)
      FileWatcher.cs        debounced, coalescing FileSystemWatcher (batches replay in order); wait-until-stable; rescans on overflow/restart/dir moves
    Numerics/
      Angle.cs              degree normalize / shortest-diff / lerp / deg-rad
      Anova.cs              one-way ANOVA (F-test table) + F-distribution tail probability
      BaseConverter.cs      integer <-> radix string (base 2..36 or custom alphabet)
      Bootstrap.cs          resampling percentile confidence interval (any statistic, seeded)
      ChangePoint.cs        CUSUM + binary-segmentation level-shift detection
      ConfidenceInterval.cs  mean CI (Student-t, or large-sample z) / variance & SD (chi-square) / Wilson proportion
      DeterministicRandom.cs  seeded fixed-algorithm PRNG (xoshiro256**, stable across runtimes)
      Differentiation.cs    numerical derivative (uniform/uneven samples, function, Richardson)
      Distributions.cs      normal/log-normal/exponential/uniform/Student-t/chi-square/F PDF/CDF/quantile + direct upper tails and the chi-square upper-tail quantile `ChiSquareInverseSurvival` (<1e-13 rel vs mpmath refs: 20 digits, computed at 40)
      EmpiricalDistribution.cs  empirical CDF step function (value<->percentile lookups)
      EngineeringNotation.cs  SI-prefix format + parse (yocto..yotta, round-trip)
      ExponentialMovingAverage.cs  EMA smoothing (alpha)
      Histogram.cs          fixed-bin histogram (under/overflow and NaN tracking); throws if the range is too narrow for the bins
      HypothesisTests.cs    t-test / chi-square / KS (one-sample p exact: Marsaglia-Tsang-Wang to n 140, Smirnov tail) / Mann-Whitney U / F-test for variances; IsSignificant(alpha)
      StreamingQuantile.cs  one-pass constant-memory quantiles (P² algorithm) + multi-quantile tracker
      PermutationTest.cs    distribution-free two-sample / paired tests, exact when small, any statistic (permutations capped at the largest array)
      MultipleRegression.cs OLS via Householder QR (columns centred when there is an intercept): SEs, t, p, R²/adj, prediction & mean intervals + Theil-Sen robust line
      DistributionFit.cs    MLE fits (normal/log-normal/exponential/gamma/Weibull), AIC/BIC, KS plausibility, FitAll
      Uncertainty.cs        round value to its uncertainty (PDG rule), "12.346 ± 0.012" / "12.346(12)" formatting
      AkimaSpline.cs        Akima / modified-Akima (makima) C1 spline: no ringing at steps, exact on lines
      ScatteredInterpolation.cs  inverse-distance-weighted 2-D scattered data -> point or grid (nearest-k / radius)
      Correlation.cs        Pearson correlation coefficient
      Fraction.cs           exact rational number (BigInteger-backed, always reduced)
      Integration.cs        trapezoid / Simpson / adaptive Simpson / cumulative
      Interpolation.cs      lerp / inverse-lerp / clamp / remap / smoothstep
      LinearAlgebra.cs      small dense matrices (multiply/solve/determinant/inverse, LU)
      LinearRegression.cs   ordinary least-squares fit (slope / intercept / R²)
      LookupTable.cs        1-D calibration table (interp + inverse, extrapolation policy)
      MathUtil.cs           gcd / lcm / power-of-two / integer clamp
      MinMaxScaler.cs       min-max feature scaling (to unit / to range)
      MonotoneCubic.cs      monotone cubic interpolation (Fritsch-Carlson, no overshoot)
      Means.cs              arithmetic / geometric / harmonic / root-mean-square
      MovingAverage.cs      streaming simple moving average (fixed window)
      Percentile.cs         percentile / median / quartiles (type-7 interpolation)
      Polynomial.cs         eval/derivative/integral, least-squares fit, real roots (deg <= 3)
      Primes.cs             primality test (Miller-Rabin) / next-prime / factorize (Pollard rho) / sieve
      RandomUtils.cs        shuffle / sample-without-replacement / weighted / gaussian / exponential / Poisson (PTRS) / gamma
      RootFinding.cs        bisection / Brent / Newton / secant (explicit convergence result; brackets must be finite)
      Rounding.cs           round to multiple / significant digits
      RunningStatistics.cs  Welford online mean/variance/stddev/min/max
      SpecialFunctions.cs   gamma / log-gamma / erf / erfc / regularized incomplete gamma (P and Q direct) & beta
      ToleranceCheck.cs     evaluate a series against limits (violations, worst deviation)
      Trend.cs              Mann-Kendall trend test (tau-a) + Sen's slope (robust; exact, n² memory, ≤ Trend.MaxPoints)
      UnitConvert.cs        length/mass/time/angle/data + temperature conversions (+ parse)
      Vector2.cs            immutable 2D vector (dot/cross/length/normalize/lerp/angle)
      WeightedAverage.cs    weighted arithmetic mean
      ZScore.cs             standard-score standardization
    Text/
      AnsiText.cs           strip ANSI escape sequences / measure visible width
      CaseConverter.cs      convert between camel/Pascal/snake/kebab/CONSTANT/Title
      CommandLineBuilder.cs  quote + join args (inverse of the splitter; round-trips)
      CommandLineSplitter.cs  split a command line into args (quote-aware)
      Tokenizer.cs          configurable quote/escape-aware splitter with spans (Shell / Csv presets)
      PercentEncoding.cs    RFC 3986 percent-encode/decode, strict UTF-8, form (+) variant
      TextAlign.cs          display-width pad/centre/fit: ANSI-, combining- and wide-char-aware
      GlobMatcher.cs        glob matching (*, ?, [a-z], negation; linear-time, reversed ranges empty)
      HammingDistance.cs    Hamming distance between equal-length strings / bit sequences (BitDistance is long)
      Indent.cs             indent / dedent multi-line text
      JaroWinkler.cs        Jaro & Jaro-Winkler string similarity
      LevenshteinDistance.cs edit distance + normalized similarity (rolling buffer)
      Mask.cs               mask sensitive strings (keep leading/trailing visible)
      MiddleEllipsis.cs     truncate keeping head + tail
      NaturalComparer.cs    natural sort order (img2 < img10)
      QueryString.cs        URL query-string build + parse (percent-encoding)
      NGramSimilarity.cs    Dice & Jaccard similarity over character n-grams (code points)
      RomanNumerals.cs      integer <-> Roman numeral (1..3999, validated)
      Slug.cs               URL/filename-safe slugs (diacritic folding; separator '-', '_', '.' or '~')
      Soundex.cs            American Soundex phonetic key
      StringSimilarity.cs   "did you mean" best/top-N match ranking (pluggable scorer)
      TemplateFormatter.cs  {name} placeholder substitution ({{ }} escaping)
      TextDiff.cs           minimal line-based diff (LCS edit script; +/-/space format; one bit per table cell, up to 2^33)
      Truncate.cs           ellipsis truncation (char-exact + word-aware)
      WordWrap.cs           greedy word wrap to a column width (hard-breaks long words)
    Threading/
      AsyncEvent.cs         awaitable multicast event (sequential/parallel, aggregated errors)
      AtomicCounters.cs     lock-free AtomicLong / AtomicDouble (interlocked add/exchange); invariant ToString
      AsyncLock.cs          async mutual-exclusion lock (held across await)
      AsyncLazy.cs          run-once async initialization (result cached)
      AsyncManualResetEvent.cs  awaitable manual-reset event
      AsyncCountdownEvent.cs  awaitable countdown latch
      ConcurrentPipeline.cs  bounded producer-consumer (block / drop-newest / drop-oldest); CompleteAsync throws if a Dispose cut the drain short
      KeyedLock.cs          per-key async locks (lock striping, ref-counted)
      ParallelUtils.cs      bounded-concurrency async fan-out (fail-fast / collect-all)
      PeriodicWorker.cs     run an action on an interval, graceful stop (injectable delay)
      PauseTokenSource.cs   cooperative pause/resume (PauseToken.WaitWhilePausedAsync at safe points)
      TaskRace.cs           FirstSuccessful / FirstMatching / FirstToComplete / Hedged — losers cancelled and awaited
      CancellationTokenExtensions.cs  await a token (WhenCanceled) + CreateLinkedTimeout source
      TaskExtensions.cs     WithTimeout / WithCancellation / FireAndForget (a throwing onError is dropped) / WhenAllOrFirstException
    Time/
      BusinessDays.cs       business-day add/count (weekends + holidays)
      CronSchedule.cs       5-field cron parse + next-occurrence (fixed offset, or a time zone with Vixie DST rules)
      CronScheduler.cs      run jobs on cron schedules: no self-overlap, no catch-up bursts, injectable clock and time zone; onError(null, ex) for scheduler failures
      DateRange.cs          date/time interval (contains / overlaps / intersect)
      Iso8601Duration.cs    ISO 8601 duration parse/format (PT1H30M)
      IsoWeek.cs            ISO 8601 week-of-year and week-year
      HumanDuration.cs      format/parse TimeSpan as "1h 2m 3s" (round-trip inverse)
      RelativeTime.cs       "3 minutes ago" / "in 2 hours" (injectable clock)
      TimeOfDayRange.cs     half-open time-of-day window (overnight wrap, Contains)
      UnixTime.cs           Unix epoch conversions (seconds/millis, DTO/DateTime)
    Visualization/
      ImageBuffer.cs        RGBA raster canvas (get/set/fill/line/rect/blit; DrawLineClipped cuts a double-precision line to a rect)
      PngWriter.cs          BCL-only PNG encoder (deflate + CRC-32 + Adler-32), streamed in 64 KiB IDAT chunks
      PngReader.cs          BCL-only PNG decoder: all colour types, 1-16 bit, all filters, Adam7, tRNS; CRC/Adler and chunk order verified, inflated a row at a time; streams checked chunk by chunk, ancillary chunks skipped (MaxAncillaryBytes)
      Bmp.cs                BMP read/write (32-bit BGRA V4 or 24-bit; reads top-down, bitfields, 8-bit palette)
      Palettes.cs           Okabe-Ito / Tableau10, WCAG luminance + contrast ratio, contrasting label colour, hex
      SvgDocument.cs        fluent SVG vector builder (shapes/text/path; invariant coords)
      SvgUtils.cs           shared SVG primitives: escaping, numbers/colors, PathBuilder, transforms, text-width estimate and fit
      AxisTicks.cs          nice ticks + labels: linear (1-2-5, count-bounded, maxTicks ≤ 10,000), log decades, clock-friendly time; tight mode keeps the given bounds
      PlotFrame.cs          shared data<->pixel mapping (linear/log axes) for renderers and overlays
      Annotations.cs        data-space ref lines / bands / callouts / arrows -> SVG (labelled) or raster
      SvgChart.cs           labelled SVG XY chart: line/scatter/bar, linear/log/time axes, grid, legend, annotations
      MultiPanel.cs         grid of SvgChart panels w/ common title + shared x/y (exactly aligned); raster tiling
      Waterfall.cs          time x frequency waterfall: SVG (embedded PNG cells, freq/time axes, color scale) or raster; ValueMin ≤ ValueMax
      HexBin.cs             exact hexagonal binning in pixel space + SVG (log colour, count scale, edge cells clipped)
      ScatterMatrix.cs      pairs plot: histograms on the diagonal, group colours + legend, Pearson r per panel (equal lengths)
      TimingDiagram.cs      logic-analyser view: digital lanes, labelled buses, unknown hatching, cursors, SI time axis (an explicit window needs Start < End; a period or sample interval must be resolvable at its start time)
      Colormap.cs           value->color (Grayscale/Hot/Cool/Viridis + custom stops; MapFraction spans any stop range)
      Colorbar.cs           render a colormap as a gradient legend strip (vertical/horizontal)
      HeatMap.cs            render a 2-D grid via a colormap (cell size, range, NaN color)
      Histogram.cs          bin 1-D data (exposed; decimal edges exact, rounded once) and render the counts as bars (at most one bin per pixel column; explicit min = max counts only that value)
      BoxPlot.cs            five-number summary + Tukey whiskers/outliers; side-by-side boxes
      ErrorBarChart.cs      points with symmetric vertical error bars (auto-scales to caps)
      BandChart.cs          shaded band between lower/upper curves + optional center line
      LinePlot.cs           line/scatter/bar series to an image (auto-scale, frame; no text)
    Resilience/
      Retry.cs              retry with constant/linear/exponential backoff + injectable delay
      CircuitBreaker.cs     Closed/Open/HalfOpen breaker (injectable clock)
      TokenBucketRateLimiter.cs  token-bucket rate limiter (injectable clock)
      Throttle.cs           leading-edge throttle gate (injectable clock)
      Jitter.cs             randomized backoff jitter (full/equal)
      Bulkhead.cs           concurrency limiter (max in-flight)
    Runtime/
      StartupTiming.cs      record named milestones from a start instant (injectable clock)
      AppInfo.cs            assembly version / config / build time / location / runtime description
      MemoryPressure.cs     working set / total allocated / GC counts + delta-since-snapshot
      ArrayPoolScope.cs     self-returning rented array (ArrayPool on net8, allocation on ns2.0; double-return safe)
    Security/
      Hashing.cs            SHA-256/384/512 (+ legacy MD5) over bytes/string (strict UTF-8)/stream/file; incremental; hex/base64
      SecretsFile.cs        passphrase-encrypted key/value file (PBKDF2 + AES-GCM, tamper-evident; a save keeps the file's ACL) (net8)
      Hmac.cs               HMAC-SHA-256/384/512 with constant-time verify
      ConstantTime.cs       fixed-time equality for secrets (no timing leak)
      CryptoRandom.cs       CSPRNG bytes / URL-safe token / numeric code / password (unbiased; lengths capped)
      KeyDerivation.cs      PBKDF2-HMAC-SHA-256/512 (hand-rolled, identical on both TFMs) + salt
      PasswordHasher.cs     encoded PBKDF2 hash + constant-time verify + rehash-needed check (MaxIterations cap)
      AuthenticatedEncryption.cs  AES-GCM AEAD: self-describing nonce‖ct‖tag blob + detached form (net8.0)
    Process/
      ProcessRunner.cs      run a child: deadlock-free capture, timeout + tree-kill, stdin/env/cancel; with no timeout, stops reading when output goes quiet after exit
      WhichExe.cs           resolve an executable on PATH (+ PATHEXT on Windows)
      ShellOpen.cs          open a file/folder/URL with the default handler (per-OS)
    Logging/
      Log.cs                LogLevel, LogEvent, ILogSink (shared contracts)
      Logger.cs             thread-safe fan-out logger (levels, category, injectable clock)
      LogSinks.cs           DelegateSink / TextWriterSink (console) / RollingMemorySink
      FileSink.cs           file sink with size-based rotation + retention
      SyslogSink.cs         RFC 5424 syslog over UDP or TCP (octet counting, connect timeout + back-off); failures counted, never thrown (Emit after Dispose throws ObjectDisposedException)
      AsyncLogSink.cs       off-thread bounded-queue sink (built on ConcurrentPipeline; optional Dispose drain timeout)
      LogDecorators.cs      FilterSink / RouterSink / RateLimitedSink (composable sink wrappers; Emit after Dispose throws ObjectDisposedException)
      LogFormatters.cs      plain / compact / single-line-JSON line formatters
      StructuredTextFormatter.cs  configurable field order / delimiter / timestamp (delimited preset)
      ScopedContext.cs      ambient flow-local scope label folded into each event's category (out-of-order dispose safe; any depth)
  ToolBelt.Windows/         Windows platform satellite (net8.0-windows) — Win32 / registry utilities
    PowerStatus.cs          AC line / battery charge / saver mode via GetSystemPowerStatus (an unknown battery state is BatteryStateKnown = false)
    SingleInstance.cs       single-instance gate over a named mutex (abandoned-owner safe, not tied to the calling thread)
    SingleInstanceApp.cs    second launch forwards args + cwd to the primary (per-user pipe) + foreground handoff; app ids ignore case; at most 100 launches wait
    DriveAndVolumeInfo.cs   fixed-drive enumeration + free space for any path
    StorageDeviceInfo.cs    bus type (USB/NVMe/SATA/SD…), removable, SSD vs HDD, vendor/serial; mapped drive -> UNC path
    MonitorInfo.cs          attached monitors: bounds / work area / primary in physical pixels (EnumDisplayMonitors)
    RegistryUtils.cs        hive+subkey read/write/delete/enumerate helpers (default view)
    Elevation.cs            elevation / Administrator role / mandatory integrity level
    JobObject.cs            job object grouping child processes (kill-on-close; memory / process-count / CPU-rate caps; accounting; start suspended inside the job)
    WindowUtils.cs          enumerate top-level windows / foreground / find by title
    FileAssociation.cs      per-extension opener / friendly name / command / ProgID
    ShortcutUtils.cs        create & read .lnk shortcuts via COM IShellLink (fields up to the shell's 260-char limit; no NULs; a corrupt .lnk is InvalidDataException; a URL or shell target reads back as its parsing name)
    ClipboardUtils.cs       clipboard text, Explorer file lists (CF_HDROP) and images (CF_DIB) via raw Win32 on an STA thread; DecodeDropFiles/DecodeDib parse untrusted blocks within bounds
    ScreenCapture.cs        capture screen/region to a raw BGRA buffer via GDI BitBlt, physical pixels (no System.Drawing)
    ResourceSampler.cs      per-process CPU% (core-normalised, at most 100) + working-set / private memory; IDisposable; Measure takes a CancellationToken
    RunElevated.cs          launch/relaunch a process elevated via ShellExecute "runas" (UAC); JoinArguments quotes args
    OsVersionInfo.cs        true OS version via RtlGetVersion (Win10/Win11 detection)
    MemoryStatus.cs         system physical / page-file memory + load % (GlobalMemoryStatusEx)
    IdleTime.cs             time since last user input (GetLastInputInfo)
    PrivilegeScope.cs       enable a token privilege (SeBackup, SeShutdown…) for a scope, restore on dispose
    PowerScheme.cs          active/installed power plans, switch, Use() scope that restores the previous plan
    DpiInfo.cs              per-monitor DPI/scale, system DPI, thread DPI-awareness scope (physical-pixel capture); Scale throws for dpi < 1
    EventLogWriter.cs       Windows Event Log via ReportEvent (no package); source registration helpers; a NUL in a message is written as U+2400
  ToolBelt.Wpf/             WPF UI satellite (net8.0-windows) — MVVM building blocks
    ObservableObject.cs     INotifyPropertyChanged base with SetProperty
    RelayCommand.cs         ICommand over delegates (+ generic RelayCommand<T>)
    AsyncRelayCommand.cs    async ICommand (Task execute) with re-entrancy guard
    NotifyTaskCompletion.cs bindable async-task wrapper (status/result/error via INPC)
    ValidationObservableObject.cs  INotifyDataErrorInfo + INotifyPropertyChanged validation base (GetErrors(null) = object-level; GetAllErrors)
    ValidationRules.cs      fluent rule sets (Required/Range/Length/Matches/Must/DependsOn) + RuleValidatedObject + XAML rules
    BindingDiagnostics.cs   BindingFailureListener (surface or throw on binding errors; bounded by MaxFailures) + pass-through DebugConverter
    ValueConverters.cs      common IValueConverters (bool/visibility/null/enum by value or name/count)
    MultiValueConverters.cs IMultiValueConverters (boolean AND / OR for MultiBinding)
    BindingProxy.cs         Freezable DataContext bridge for out-of-tree bindings
    Messenger.cs            thread-safe WEAK-reference pub/sub (recipient-keyed, no forgotten-unsubscribe leak)
    DialogService.cs        IDialogService + WPF impl (MessageBox/file/folder) + strict RecordingDialogService fake
    IncrementalCollection.cs  page-at-a-time ObservableCollection (coalesced loads, stale-page-safe reset)
    FileDropBehavior.cs     attached: dropped files -> ICommand, extension filter, correct drag-over effects; innermost target decides
    NumericInputBehavior.cs attached: numeric TextBox (min/max/decimals, culture-aware, arrows/wheel step, checked typing/paste/delete)
    GlobalHotkeys.cs        system-wide RegisterHotKey on a message-only window + HotkeyGesture parse/format
    ThemeManager.cs         light/dark ResourceDictionary swap in place + Windows app-mode probe/follow (SystemThemeApplyFailed) + palette
    PlotPresenter.cs        zoom/pan/fit ImageSource viewer (crisp pixels) + RGBA->BitmapSource bridge
  ToolBelt.WinForms/        WinForms UI satellite (net8.0-windows) — forms helpers
    ControlExtensions.cs    InvokeIfRequired / BeginInvokeIfRequired UI-thread marshalling
    ControlTreeExtensions.cs  recursive descendant enumeration / typed filter / find-by-name
    DoubleBufferedExtensions.cs  toggle a control's (protected) double buffering
    DoubleBufferedPanel.cs  flicker-free custom-drawing panel (Render callback, anti-aliasing)
    WaitCursorScope.cs      using-scope hourglass cursor (restores previous state)
    ComboBoxEnumExtensions.cs  bind enum values to a ComboBox + get/set selection as the enum
    LayoutSuspender.cs      using-scope SuspendLayout/ResumeLayout batch
    FormStatePersistence.cs capture/apply/serialize window placement; Apply validates vs current monitors (never off-screen)
    DataGridViewExtensions.cs  BindList<T> (DisplayName headers), AutoSizeColumns cap, selection->TSV (Excel), CSV export
                            (atomic SaveCsv; opt-in escapeFormulas against spreadsheet formula injection)
    MenuBuilder.cs          declarative MenuStrip/ToolStrip builders, ICommand items, MenuShortcut parse/format, FindItem(path)
    PlotPictureBox.cs       zoom/pan/fit image view (crisp pixels, flicker-free) + RGBA->Bitmap bridge
tests/
  ToolBelt.Tests/           hand-rolled, zero-dependency console test runner (exit 0 == all green)
    Framework/
      TestRunner.cs         reflection-based discovery ( *Tests classes )
      TestPlatform.cs       platform checks that compile on .NET Framework too
      Check.cs              minimal assertion helpers (Close fails when only one side is NaN)
    Guards/
      GuardTests.cs, GuardHardeningTests.cs
    Binary/
      Adler32Tests.cs
      Base32Tests.cs
      Base58Tests.cs
      Base64UrlTests.cs
      Base85Tests.cs
      BinaryImagesReviewTests.cs  (review regressions: VarInt overflow, strict Base32/Base64url/QP decoding, overflow-safe range checks, PNG/BMP sizes, SVG escaping)
      BinaryImagesReview2Tests.cs (second-pass regressions: exact line clipping, XML whitespace escaping, QP soft breaks and blank runs, image size limits, negative SVG lengths)
      BinaryImagesReview3Tests.cs (third-pass regressions: atomic SVG elements, exact far-line clipping, strict PNG chunk layout, streamed PNG encode/decode, canonical Base32/Base64url)
      BinaryImagesReview4Tests.cs (fourth-pass regressions: tall-narrow PNG speed, bounded drain and stream reads, exact SVG number rounding, zero-width text, canonical Crockford/Base85, Base58 encode speed)
      BinaryImagesReview84Tests.cs (round 84: exact line clipping, flag widths, fixed Unicode tables, PNG stream checks and memory, PNG/BMP palette rules, encoder length limits, Base58 speed)
      BinaryImagesReview99Tests.cs (round 99: streamed PNG transparency, oversized PNG chunks refused at the header, SVG size message)
      BinaryImagesReview113Tests.cs (round 113: empty PNG chunks bounded, byte-at-a-time PNG streams, BMP sides over 32768, BITFIELDS masks over the pixels, Polyline null stroke)
      BinaryImagesReview129Tests.cs (round 129: image size checks that overflowed long in ImageBuffer, Bmp and PngReader)
      BinaryImagesReview159Tests.cs (round 159: clipped lines matching the whole line inside the rectangle and empty rectangles; small IDATs through the PNG stream; SVG widths of what is written and tiny sizes; Luhn, Base58 and Base32 length limits; BMP masks after short headers)
      BinaryImagesReview169Tests.cs (round 169: PNG deflate streams with no final block or with data before the Adler-32, against random images at every compression level; huge IHDR widths reported unsigned)
      BinaryImagesReview191Tests.cs (round 191: astral letters and line separators in the SVG width estimate, a tiny positive stroke width)
      BinaryImagesReview205Tests.cs (round 205: wide BMP emoji and emoji presentation in EstimateTextWidth)
      BitsTests.cs
      Crc16Tests.cs
      Crc32Tests.cs
      CrockfordBase32Tests.cs
      EndianConverterTests.cs
      Fnv1aTests.cs
      GrayCodeTests.cs
      HexTests.cs
      LuhnTests.cs
      QuotedPrintableTests.cs
      VarIntTests.cs
    Cli/
      ConsoleTableTests.cs
      ConsoleHelpersTests.cs
      ProgressBarTests.cs
      SparklineTests.cs
    Collections/
      AggregationTests.cs
      BatchTests.cs, BatchHardeningTests.cs
      BiMapTests.cs
      BinaryHeapTests.cs
      BitSetTests.cs
      BloomFilterTests.cs
      CountingBloomFilterTests.cs
      CircularBufferTests.cs
      CartesianProductTests.cs
      CombinatoricsTests.cs
      CollectionsReviewTests.cs (review round 9 regressions: deep tries/permutations, sketch hashing, enumerator versions, huge capacities)
      CollectionsReview2Tests.cs (second-pass regressions: pool return race, throwing comparers, overflow-safe sums and counts)
      CollectionsReview3Tests.cs (third-pass regressions: Bloom hash mixing, sums at double.MaxValue, throwing hashes, enumerator checks, argument names)
      CollectionsReview4Tests.cs (fourth-pass regressions: Bloom probe mixing and lone surrogates, tiny-value spread, long counts, size caps, ordinal table sorts)
      CollectionsReview90Tests.cs (round 90 review regressions: mixed sequence/multiset hashes, value-hashed keys, TTL expiry, trie memory, streaming Summarize)
      CollectionsReview96Tests.cs (round 96: Summarize means within [min, max], zero-only Randoms, multiset counts past 2^32, null sketch items, cheap Clear, default rows)
      CollectionsReview111Tests.cs (round 111: 64-bit sketch keys, precise deviations, throwing hashes in LRU, heap, BiMap and MultiDictionary, endless TTLs, copied product lists, Bloom sizes for a few items, .NET Framework array limits)
      CollectionsReview127Tests.cs (round 127: exact NearestBy distances, changed product inputs, ring slots near the largest array, BitSet growth cap)
      CollectionsReview157Tests.cs (round 157: Bloom sizing to the true false-positive rate, uniform 64-bit probes, a TtlCache expiry at the end of the calendar, signed zeros in Summarize)
      CollectionsReview173Tests.cs (round 173: NaN inside tuple and object keys on .NET Framework, signed-zero sums in Summarize, MostCommon ties by first added, CountMinSketch size product)
      CollectionsReview189Tests.cs (round 189: comparers that change the heap, TopN or queue they order, DisjointSet and FenwickTree ParamNames, Pivot's read-only values, Summarize's mean after cancellation, DataTableLite float and -0 text on every runtime)
      CollectionsReview199Tests.cs (round 199: DataTableLite's shortest text worked out exactly on every runtime, mixed numbers in OrderBy, ObjectPool's last slot, BitSet no-op changes and enumerators)
      CounterTests.cs
      CountMinSketchTests.cs
      DataTableLiteTests.cs
      SlidingWindowTests.cs
      DequeTests.cs
      DisjointSetTests.cs
      FenwickTreeTests.cs
      IndexedPriorityQueueTests.cs
      LruCacheTests.cs
      MultiDictionaryTests.cs
      ObjectPoolTests.cs
      OrderedDictionaryTests.cs
      OrderedSetTests.cs
      SamplingTests.cs
      SamplingSortedPairwiseTests.cs (uniformity/conditional-probability checks, scan-vs-bound differential)
      SequenceEqualityComparerTests.cs
      TopNTests.cs
      TrieTests.cs
      TtlCacheTests.cs
    Configuration/
      ConfigLayersTests.cs
      ConfigBinderTests.cs
    Control/
      PidControllerTests.cs
      PidTuningTests.cs     (FOPDT fit + tuning rules, closed-loop on a simulated plant)
      StepResponseTests.cs  (vs analytic second-order / first-order formulas)
      SignalConditioningTests.cs  (slew limiter, Schmitt trigger, deadband, first-order lag)
      KalmanFilter1DTests.cs
      ControlReviewTests.cs (review regressions: reverse-acting anti-windup, non-finite inputs)
      FiltersControlReview2Tests.cs (second-pass regressions: IIR steady state at tiny cutoffs, equal Schmitt thresholds, overflow in PID / lag / Kalman / step analysis, parameter names)
      FiltersControlReview3Tests.cs (third-pass regressions: IIR cascade accuracy against double-double, poles within rounding of the unit circle, tiny-ripple orders, symmetric FIR taps, PID and tuning overflow)
      FiltersControlReview4Tests.cs (fourth-pass regressions: PID windup stall, elliptic passband spec at high order, deep-stop-band MagnitudeDb, subnormal state flush, huge and subnormal sample rates, FOPDT and tuning-rule overflow)
      FiltersControlReview83Tests.cs (round 83: section scaling, rescaling near double.MaxValue, group delay at unit-circle zeros, causal flush, PID NaN windup, deadband ties, huge lag cutoffs)
      FiltersControlReview107Tests.cs (round 107: rescaling only what overflowed, FiltFilt split by size, the streaming shift stepping down, aliased responses, extreme Q named, ulp-close Butterworth bands, subnormal Kaiser rates, Simc and Kalman Reset)
      FiltersControlReview119Tests.cs (round 119: PID integral and slew limiter past overflow, IIR steady state and response as mantissa and exponent, argument blame)
      FiltersControlReview133Tests.cs (round 133: Kaiser at 8 dB, responses next to a pole from exact roots, subnormal Nyquist checks, overflow rescue ahead of a NaN, Kalman and PID at tiny scales, Cohen-Coon at huge tau, StepResponse arguments, the slew limiter's halved path)
      FiltersControlReview150Tests.cs (round 150: PID terms overflowing with opposite signs, an integral past double.MaxValue, Td underflow in the open-loop rules, band edges that cancelled, edges at Nyquist or 0, the subnormal flush after a large sample)
      FiltersControlReview190Tests.cs (round 190: the lag and Kalman blends over long steps, +0 gains, a PID integral past 4e488, FIR edges that underflow at the sample rate, Biquad text on every runtime, FitStep and Analyze ParamNames)
      FiltersControlReview200Tests.cs (round 200: PID derivative overflow and cancellation, the output held at the windup limit, Kalman weights and the lag decay that underflow)
      FiltersControlReview215Tests.cs (round 215: a slew gap that rounds to the step, narrow FIR band-pass taps and the band centre by Nyquist)
    Diagnostics/
      BenchmarkTests.cs
      MetricsRegistryTests.cs
      ScopedTimerTests.cs
      ExceptionUtilsTests.cs
      EnvironmentReportTests.cs
      HealthCheckTests.cs
      DiskSpaceTests.cs
      AssertInvariantTests.cs
    Documents/
      MarkdownReportTests.cs
      HtmlReportTests.cs
      PdfWriterTests.cs     (independent xref-table + structure parser)
      PdfFeaturesTests.cs   (object-level reader: outline tree links, image/SMask streams, WinAnsi bytes, line widths)
      DocxWriterTests.cs    (unzips parts + parses OOXML)
      DocxFeaturesTests.cs  (+ opens the file in real Word via COM when installed: pages, list numbers, footer fields)
      ReportBuilderTests.cs
      ReportTemplatesTests.cs
      DocumentsConfigReviewTests.cs  (review round 7 regressions: documents + configuration)
      DocumentsConfigReview2Tests.cs (second-pass regressions: binder errors and decimal commas, rows split across PDF pages, AFM widths, GitHub slugs, Markdown escaping)
      DocumentsConfigReview3Tests.cs (third-pass regressions: get-only settings, silent binder drops, layer lookups, PDF indents, Markdown TOC and lists)
      DocumentsConfigReview4Tests.cs (fourth-pass regressions: settings saves refused after an unreadable load, symlinks, deep binder nesting, adjacent Markdown lists, PDF wrap cost, half-written tables)
      DocumentsConfigReview5Tests.cs (fifth-pass regressions: every get-only collection and private setter reloads, exact Update rollback, binder drops, PDF indents, GitHub anchors)
      DocumentsConfigReview100Tests.cs (round 100: read-only collections and unloadable values in settings, settings ACLs, lone-surrogate chars, PDF column ulp, binder null items, empty indexes, casing clashes and tree cost, env names with NUL)
      DocumentsConfigReview109Tests.cs (round 109: settings that would not load back, nested updates, linked and non-UTF-8 files, non-flags enum lists, null nested values, one duration syntax, Markdown rules and raw lists, empty figures, tiny PDF sizes, DOCX CR and footer)
      DocumentsConfigReview126Tests.cs (round 126: hidden config properties, exact durations up to TimeSpan.MaxValue, RequireAll names, undefined enums, fixed DOCX entry times, very wide DOCX tables, column-less HTML tables, nested Raw list items)
      DocumentsConfigReview151Tests.cs (round 151: settings loads and saves during another instance's replace, stale and ~RF temps, atomic DOCX and PDF saves, truncated PNGs, 63-column tables, PDF footer and tabs, copied figures, infinite and tiny thresholds, Markdown trailing '#', lists and empty code, HTML controls, the U+0130 slug)
      DocumentsConfigReview160Tests.cs (round 160: long names and held files in PDF and DOCX saves, settings paths ending in a separator, loads during a save or of a file that vanishes mid-read and beside other stores' temps, report escaping and slugs on every runtime, infinity, NaN and -0 in the binder and environment, derived nested sections)
      DocumentsConfigReview181Tests.cs (round 181: computed section getters, property names holding the separator, huge values in errors, TimeSpan.MinValue in seconds, the nearest float on every runtime, nulls in config layers, a settings file holding null, line separators and newer Unicode letters in slugs, Raw's first-line indentation, changes from 0 and -0 in Comparison)
      DocumentsConfigReview194Tests.cs (round 194: Word tables over 63 columns, signless zeros and short huge percentages in Comparison, DateTime offsets as UTC, properties differing only in case, list items that are only a marker)
      DocumentsConfigReview217Tests.cs (round 217: net48 GetDouble rounding, long duration fractions, PDF break spaces, nested bullets and emoji code lines, root constructor exceptions, CodeBlock and ImageData checks, ConfigLayers key casing and null-only keys)
      DocumentsConfigReview155Tests.cs (round 155: a load during a slow save waits two seconds, then warns)
    Enums/
      EnumExtensionsTests.cs
      EnumReview89Tests.cs (round 89 review regressions: alias descriptions, exact-case parse, ulong flags)
      EnumFlagsTests.cs
      EnumMapTests.cs
    Functional/
      EitherTests.cs
      MemoizeTests.cs
      OptionTests.cs
      ResultTests.cs
    Graphs/
      GraphTests.cs
      TopologicalSortTests.cs
      StronglyConnectedComponentsTests.cs
      ShortestPathTests.cs
      MinimumSpanningTreeTests.cs
    Grids/
      FloodFillTests.cs
      ConnectedComponents2DTests.cs
      GridPathfindingTests.cs
    Intervals/
      IntervalTreeTests.cs
      RangeMapTests.cs
      RangeSetTests.cs
    Quality/
      ProcessCapabilityTests.cs
      ControlChartTests.cs
      MeasurementAgreementTests.cs
      GaugeRRTests.cs       (GaugeRRReferenceData.cs from scripts/gen-gauge-references.py)
    Signal/
      ConvolutionTests.cs
      FilterDesignTests.cs  (FilterReferenceData.cs from scripts/gen-filter-references.py, SciPy)
      FilterReviewTests.cs  (review regressions: extreme-rate / high-order designs, tiny ripple)
      FftTests.cs
      FrequencyGridTests.cs
      GoertzelTests.cs
      HampelTests.cs
      HilbertTests.cs
      LevelConversionsTests.cs
      MedianFilterTests.cs
      PhaseUnwrapTests.cs
      PulseMeasurementsTests.cs
      QuantizerTests.cs
      ResampleTests.cs
      SavitzkyGolayTests.cs
      SpectrumTests.cs
      TimeDelayEstimateTests.cs
      WelchPsdTests.cs
      SignalExtrasTests.cs  (multitaper, peak interpolation, envelope, group delay, cycles)
      SignalReviewTests.cs  (review regressions: high-order Savitzky-Golay, overflow clamps, NaN handling, GCC-PHAT scale)
      SignalReview2Tests.cs (second-pass regressions: pulse state levels and edges, zero runs, NaN medians, overflow and scale)
      SignalReview3Tests.cs (third-pass regressions: high-order Savitzky-Golay, Bluestein and Hilbert overflow, huge-rate scaling, band edges)
      SignalReview4Tests.cs (fourth-pass regressions: spectral overflow, subnormal FFT/cycle/pulse, NumPy unwrap tolerance, sliding median/Hampel, length checks)
      SignalReview86Tests.cs (round 86: huge power-of-two FFTs, linear rise time, exact quantizer codes, unwrap at ±π, zero signs in medians, file headers)
      SignalReview104Tests.cs (round 104: subnormal convolution, smoothing, vertices, inverse FFT and interpolation, rounded crossing hand-off, Hampel overflow, correlation ties, envelope coefficient, duty cycle and group delay at extremes, Goertzel aliasing)
      SignalReview115Tests.cs (seventh pass: many tied correlation lags, Goertzel aliases, cycle frequencies at extreme rates, Bluestein inverse subnormals, linear-time Hampel and Savitzky-Golay edges)
      SignalReview137Tests.cs (eighth pass: Savitzky-Golay order w - 1 as the identity, over-long lists as argument errors, finite group-delay input, finite Hampel thresholds, finite window measures, quiet bins of tiny spectra, tiny convolution overlaps beside a normal peak)
      SignalReview153Tests.cs (ninth pass: infinite samples and a largest-double bin in Bluestein FFTs, group delay on uneven grids, Welch overlap rounding, correlation ties at extreme scales, a constant largest double in Savitzky-Golay, Hampel NaN counts)
      SignalReview168Tests.cs (round 168: infinite samples in power-of-two FFTs against the definition, magnitudes with an infinite part on both frameworks, the phase range of a negative constant, Hampel on subnormal data)
      SignalReview187Tests.cs (round 187: near-flat parabolic peaks against the exact vertex, Gaussian offsets at any scale and exact flat heights, bit-symmetric windows, dBFS of a subnormal ratio, exact cross-correlation ties, half lengths and half bins rounded up)
      SignalReview198Tests.cs (round 198: ScaleB rounding once, exact convolution rescue, exact cycle periods, a level near an extreme, coincident Resample points, ToRate's length, Decimate near overflow, -0 in SavitzkyGolay, large dB values, Hampel's infinite median, CrossCorrelationPeak's value, NearestBin's midpoints, Parabolic's denominator, a changing MedianFilter input)
      SignalReview208Tests.cs (round 208: spectrum amplitudes divided once, Welch segments scaled by their own peaks, midpoints of a subnormal rounded once, the exact tie-break's budget)
      FiltersReview170Tests.cs (round 170: group delay at and near a notch's zeros against the poles' delay, NaN exactly on a zero for every order, elliptic and Chebyshev I designs holding −rp at the edge or refused)
      WindowTests.cs
      ZeroCrossingTests.cs
    Objects/
      ActivatorUtilsTests.cs
      AttributeCacheTests.cs
      DeepEqualsTests.cs
      ExpressionAccessorTests.cs
      FlattenObjectTests.cs
      ObjectMapperTests.cs
      PropertyDiffTests.cs
      PropertyPathTests.cs
      TypeUtilsTests.cs
      ObjectsGraphsReviewTests.cs (round 14 review regressions: deep graphs, sets/shapes/value tuples, paths, accessors, mapper, cycles, range maps)
      ObjectsGraphsReview2Tests.cs (second-pass regressions: shared subgraphs, FileSystemInfo leaves, struct fields, enum conversion, huge grids)
      ObjectsGraphsReview3Tests.cs (third-pass regressions: computed self-typed properties, overridden accessors, enum ranges, overflowing paths, argument validation)
      ObjectsGraphsReview59Tests.cs (fourth pass: chains of made objects, boxed cycles, member order, flatten collisions, lossy conversions, by-ref-like members)
      ObjectsReview60Tests.cs (round 59 follow-ups: deep generic names, lazy sequences of new objects, leaf-root unflatten)
      ObjectsGraphsReview77Tests.cs (fifth pass: opaque leaves, walk budgets, generic dictionaries, round-trip keys, exact conversions, A* ties, RangeMap blocks)
      ObjectsGraphsReview93Tests.cs (round 93 review regressions: the flatten key budget, read-only graph views)
      ObjectsGraphsReview125Tests.cs (round 125 review regressions: private state and Equals overrides, StringBuilder and Regex leaves, self-holding Memory, exact-case mapping, path nulls and syntax, decimal to floating, checked typed accessors, ordinal string intervals)
      ObjectsReview136Tests.cs (eighth pass: struct defaults without their constructor, DateTime text round trips, exact-case enum names in conversions and path keys, quoted path keys in Flatten and PropertyDiff, invariant Cell text, plain attribute arrays, wrong-type accessor arguments)
      ObjectsReview145Tests.cs (ninth pass: bounded fan-out of made objects, pairs and objects revisited by a shorter path, enums to floating values and ulong enums, exact names in ObjectMapper, null instances in ExpressionAccessor, Result.Try with a throwing Message)
      ObjectsReview163Tests.cs (tenth pass: path keys of every key type read back (BigInteger, DateOnly, TimeOnly, Half, Int128, nint, tuples), keys holding both quotes, invariant tuple keys, double to float only when the float names the value, structs without Equals and signed NaN alike on .NET Framework)
      ObjectsGraphsReview179Tests.cs (round 179: infinite edges in breadth-first search against Dijkstra on unit weights, void and other instance-less types in ExpressionAccessor)
    Identifiers/
      NanoIdTests.cs
      ShortGuidTests.cs
      SnowflakeIdGeneratorTests.cs
      UlidTests.cs
    IO/
      AtomicFileTests.cs
      ByteSizeTests.cs
      CsvBinderTests.cs
      CsvLineTests.cs
      DotEnvTests.cs
      FixedWidthTests.cs
      HexDumpTests.cs
      LineReaderTests.cs
      SafeFileNameTests.cs
      StreamUtilsTests.cs
      TempFileTests.cs
      ZipUtilsTests.cs
      DirectoryUtilsTests.cs  (real junction, ACL-denied subtree, locked file)
      ChecksumManifestTests.cs
      FilesWaveTests.cs     (retention, stream wrappers, CSV dialects, env vars, file hashing)
      JsonTarSecretsTests.cs (JSON/JSONL, settings recovery, hostile tars, secrets tamper sweep)
      FileWatcherTests.cs     (deterministic coalescing rules + real file-system scenarios)
      IOReviewTests.cs      (review round 8 regressions: links in zip/tar, encodings, CSV/.env parsing, overflow-safe sizes)
      IOReview2Tests.cs     (second-pass regressions: copy through junctions, hard links, truncated archives, watcher filters)
      IOReview3Tests.cs     (third-pass regressions: safe replace on copy and create, tar metadata bombs, device names, archive times, watcher dispose)
      IONetFrameworkReview57Tests.cs (round 57: zip names .NET Framework cannot read, copies into the source through a junction)
      IOReview58Tests.cs    (fourth pass: junction swaps mid-operation, tar guard parsing, names ending in a dot, archive names, -0, surrogates)
      IOReview58ApiTests.cs (fourth pass: cancellable delete, line-length and archive total caps)
      IOReview75Tests.cs     (fifth pass: deep-tree delete and copy, streaming zip replace, invariant CSV floats, Win32 wildcards, reparse tags)
      IOReview94Tests.cs     (round 94 review regressions: mixed tar metadata headers, temp-folder cleanup, files over folders)
      IOReview112Tests.cs    (round 112 review regressions: CSV decimal separators, undefined overwrite policies, empty zip names, blank nullable fields, V7 tar directories, Windows tar modes)
      IOReview138Tests.cs    (eighth pass: trailing-dot rescans, '*' in watched names, linear retention matching, code-page and mixed zip names, zip comments and Unix modes, Unix-made zip folders, folders holding only the archive)
      IOReview143Tests.cs    (ninth pass: failed replaces keep read-only, overwrites keep Hidden and replace shared-delete targets, no folders for unreadable archives, whitespace chars and relative Uris in CsvBinder)
      IOReview172Tests.cs    (tenth pass: ByteSize parsing and formatting rounded once from the exact value, CsvDialect numbers past double's range on .NET Framework, a PAX global header before a GNU long name)
      IOReview188Tests.cs    (eleventh pass: roots ending in a dot, copies through a short name, archives reached through a junction, tar replace keeping Hidden, every tar entry listed after small files, 8.3-alias entries refused, zip host kept by AddOrReplaceEntry, lone-surrogate names refused, too-long backup names, TempFile extensions, non-recursive snapshots ignoring short names)
      IOReview195Tests.cs    (twelfth pass: ByteSize with non-ASCII digits, NaN and Infinity words on every runtime, correctly rounded CSV and config numbers on .NET Framework, FileChangeCoalescer against a model file system)
      IOReview212Tests.cs    (thirteenth pass: PAX sparse tar entries, repeated directory entries in tar and zip, duplicate zip names, repeated ./ and unpaired surrogates in manifests, ShiftNumbered on a trimmed name and a read-only copy, coalescer batches against a replaying model)
      CoverageGapTests.cs   (behaviour behind coverage holes found by a coverlet run)
    Numerics/
      AngleTests.cs
      AnovaTests.cs
      BaseConverterTests.cs
      BootstrapTests.cs
      ChangePointTests.cs
      ConfidenceIntervalTests.cs
      CorrelationTests.cs
      DeterministicRandomTests.cs
      DifferentiationTests.cs
      DistributionsTests.cs
      DistributionsTailTests.cs      (closed forms, identities, deep-tail round trips)
      DistributionReferenceTests.cs  (+ DistributionReferenceData.cs: 337 mpmath reference values, 20 digits computed at 40-digit precision)
      DistributionReferenceReview185Tests.cs  (round 185: every reference probability in [0, 1], no cancellation noise)
      InferenceAccuracyTests.cs      (worked examples + simulated coverage / false-positive rates)
      StatisticsReviewTests.cs       (review regressions: extreme tails, huge df, round-off variance, non-finite inputs, SPC)
      StatisticsReview2Tests.cs      (second-pass regressions: scale invariance at 1e±170, far-tail quantiles, lnΓ near 1 and 2)
      StatisticsReview3Tests.cs      (third-pass regressions: sums near MaxValue, regression scaling, F density at large df, Γ accuracy)
      StatisticsReview4Tests.cs      (fourth-pass regressions: data on an offset, tight gamma/log-normal fits, exact KS p, far tails, huge counts)
      StatisticsReview87Tests.cs     (round 87: far lower-tail quantiles, confidences next to 1, subnormal chi-square/F, digamma series, offset CUSUM)
      StatisticsReview108Tests.cs    (round 108: small-x chi-square pdf, F tails under a huge d2, corrected two-pass sds, per-group ANOVA and gauge floors, permutation ties, undefined enums, overflowing Theil-Sen)
      NumericsReview118Tests.cs      (round 118: F tails when d1·x overflows, tiny-shape tails, quantiles past MaxValue, subnormal t-tests and intervals)
      StatisticsReview140Tests.cs    (round 140: regressions across 1e+-300 scales, Theil-Sen intercept overflow, too-small normal spread, permutation ties on offset data)
      StatisticsReview146Tests.cs    (round 146: F-test far tails, spreads past MaxValue in the t and F tests, small means beside huge values, counts past int and Array.MaxLength, the exact KS tail's digits, the Wilson lower bound, regression Predict near MaxValue)
      StatisticsReview171Tests.cs    (round 171: chi-square quantiles below the smallest double, all-tied Mann-Whitney, KS p against SciPy, exact paired permutation p, Pearson on offset data, large-shape beta and F against 40 digits)
      StatisticsReview186Tests.cs    (round 186: two-sample permutation ties on exact sums, Bland-Altman and Individuals limits near MaxValue, Weibull shape and log-likelihood on tight data, Weibull on values a few ulps apart)
      StatisticsReview201Tests.cs    (round 201: lognormal, gamma and Weibull shapes scale-free at 2^+-1000, a subnormal gamma scale, the beta fraction at a second shape near MaxValue in all four copies, a regression slope that overflowed where it is exactly 0)
      StatisticsReview216Tests.cs    (round 216: confidence intervals for a spread past double.MaxValue, the exact KS tail past 100,000 points)
      HugeShapeTests.cs              (round 46: incomplete beta at shapes near MaxValue and subnormal; drop-in copies agree)
      DifferentialHarnessTests.cs    (round 47: tiny-shape tails and prefixes, huge-other-shape prefix, from the mpmath harness)
      NumericsCoreReviewTests.cs     (review regressions: QR fit, cancellation-free roots, extreme magnitudes, NaN rejection, fast primes)
      NumericsCoreReview2Tests.cs    (second-pass regressions: finite overflow at ±1e308, checked arithmetic, decimal-exact rounding)
      NumericsCoreReview3Tests.cs    (third-pass regressions: LU and cubic overflow, false root convergence, tiny-scale splines and stats)
      NumericsCoreReview4Tests.cs    (fourth-pass regressions: overflowing Simpson, LU growth, angles, splines, means, IDW, cubic double roots)
      NumericsCoreReview85Tests.cs   (round 85: double roots of exact cubics, aliased Simpson samples, tiny knots beside huge ones, exact angle units, uncertainty digits, bounds of means and scaling)
      NumericsCoreReview95Tests.cs   (round 95: subnormal fractions, roots past the double range, tiny weights, data a few ulps apart, uniform NextInt64, Poisson zero draws)
      NumericsCoreReview122Tests.cs  (round 122: exact Lerp ends, quantile ranks, infinite angles, an exact moving sum, harmonic means in range, undefined enums, oversized products, overflowing trapezoid panels and derivatives)
      NumericsCoreReview130Tests.cs  (round 130: Polynomial.Fit's exact distinct-x count, Uncertainty rounding the shortest decimal at every place, Simpson's running sum past double range, Lerp between equal infinite ends, exact decimal reads in Rounding and EngineeringNotation on .NET Framework)
      NumericsCoreReview156Tests.cs  (round 156: Akima slope terms past the double range, Quantile and Percentile ranking decimals alike, far samples and tiny weight ratios in IDW, exact LerpDegrees ends, single-rounded tiny variances, NextExponential's −0, infinite tolerance limits)
      NumericsCoreReview162Tests.cs (round 162: matrices spanning the double range, by row and column matching scaling, and exactly singular ones, against exact determinants; unit conversions rounded once; engineering notation past yotta; +0 roots; cumulative trapezoid and adaptive Simpson past double range; secant guesses far apart; infinite lerp steps; surrogate digits)
      NumericsCoreReview177Tests.cs (round 177: tiny samples in moving averages and running sums, monotone cubics ulp by ulp, streaming quantiles past five values, angle differences rounded once, gamma draws with a huge scale, infinite lerp ends, non-finite x in derivatives, shortest round-trip ToString on .NET Framework; against exact BigInteger references)
      NumericsCoreReview197Tests.cs (round 197: Lerp and Remap past overflow, an infinite radius, RoundToMultiple on the decimals, degenerate random sources, NextGaussian's overflow, NextDouble's range error, the subnormal standard deviation, poles in Bisection and Brent, -0 and huge exponents in EngineeringNotation, FromBase's overflow, temperatures below absolute zero)
      NumericsCoreReview209Tests.cs (round 209: Polynomial.Evaluate at infinity, close quadratic roots, WeightedAverage's small means, Format just below a power of ten, AdaptiveSimpson's overflow retry)
      NumericsExtremeReview56Tests.cs (round 56, cross-cutting: values near MaxValue, subnormals, infinities, sizes past Array.MaxLength)
      NumericsReviewRegressionTests.cs  (2026-10-05 review regressions: Numerics and Signal)
      StreamingQuantileTests.cs
      SamplingAndPermutationTests.cs (Poisson/gamma sampling vs pmf/CDF; exact permutation p-values)
      RegressionAndFitTests.cs       (textbook SEs, interval coverage, MLE optimality, family recovery)
      InterpolationAndUncertaintyTests.cs
      EmpiricalDistributionTests.cs
      EngineeringNotationTests.cs
      ExponentialMovingAverageTests.cs
      FractionTests.cs
      HistogramTests.cs
      HypothesisTestsTests.cs
      IntegrationTests.cs
      InterpolationTests.cs
      LinearAlgebraTests.cs
      LinearRegressionTests.cs
      LookupTableTests.cs
      MathUtilTests.cs
      MeansTests.cs
      MinMaxScalerTests.cs
      MonotoneCubicTests.cs
      MovingAverageTests.cs
      PercentileTests.cs
      PolynomialTests.cs
      PrimesTests.cs
      RandomUtilsTests.cs
      RootFindingTests.cs
      RoundingTests.cs
      RunningStatisticsTests.cs
      SpecialFunctionsTests.cs
      ToleranceCheckTests.cs
      TrendTests.cs
      UnitConvertTests.cs
      Vector2Tests.cs
      WeightedAverageTests.cs
      ZScoreTests.cs
    Text/
      AnsiTextTests.cs
      CaseConverterTests.cs, CaseConverterHardeningTests.cs
      CommandLineBuilderTests.cs
      CommandLineSplitterTests.cs
      GlobMatcherTests.cs
      HammingDistanceTests.cs
      IndentTests.cs
      JaroWinklerTests.cs
      LevenshteinDistanceTests.cs
      MaskTests.cs
      MiddleEllipsisTests.cs
      NaturalComparerTests.cs
      NGramSimilarityTests.cs
      QueryStringTests.cs
      TokenizerAndEncodingTests.cs (CSV agreement with CsvLine; encoding agreement with Uri.EscapeDataString)
      RomanNumeralsTests.cs
      SlugTests.cs
      SoundexTests.cs
      StringSimilarityTests.cs
      TemplateFormatterTests.cs
      TextDiffTests.cs
      TextReviewTests.cs (round 11 review regressions: Text, Identifiers, Enums, Guards)
      TextReview2Tests.cs (round 32 review regressions: escapes, slug folding, case folding, validation)
      TextReview3Tests.cs (round 50 review regressions: id generator edges, DCS/APC strings, case-less letters, net48 limits)
      TextReview4Tests.cs (round 74 review regressions: snowflake lead, linear glob/Jaro/n-grams, diff memory, VT escapes, wide scripts, slug folds)
      TextReview89Tests.cs (round 89 review regressions: emoji-plane and mark widths, Soundex and slug folds, no-break spaces)
      TextRobustnessReview89Tests.cs (round 89 review regressions: exact n-gram classes, diff line ids, glob masks, top-k heap, query surrogates)
      TextReview98Tests.cs (round 98 review regressions: escapes cut short, emoji selector past escapes and on supplementary symbols, glob masks by token group)
      TextReview123Tests.cs  (round 123: letters with no one-letter capital in every case style, NanoId's default size, emoji selector past a control inside an escape)
      TextReview141Tests.cs  (round 141: .NET Framework casing gaps, emoji selector beside pads, ellipses and cut escapes, null newline, Ulid draw order, overlapping tokenizer roles)
      TextReview154Tests.cs  (round 154: n-grams over code points, Dedent keeping every CR, ellipses Fit refuses)
      TextReview174Tests.cs (round 174: runs of leading variation selectors in TextAlign, U+0345 in CaseConverter on .NET Framework)
      MiscReviewRegressionTests.cs  (2026-10-05 review regressions: Text, Cli, Threading, Time, Diagnostics, Collections)
      TruncateTests.cs
      WordWrapTests.cs
    Net/
      CidrRangeTests.cs
      IpUtilsTests.cs
      PortCheckTests.cs
      TcpLineClientTests.cs
      HostInfoTests.cs
      HttpDownloadTests.cs
    Threading/
      AsyncEventTests.cs
      AtomicCountersTests.cs
      AsyncLockTests.cs
      AsyncLazyTests.cs
      AsyncManualResetEventTests.cs
      AsyncCountdownEventTests.cs
      ConcurrentPipelineTests.cs
      ConcurrencyReviewTests.cs (review round 10 regressions: process timeouts and callbacks, pipeline completion, worker loops, dispose races)
      ConcurrencyReview2Tests.cs (second-pass regressions: backoff, breaker generations, clock steps, hedging, dispose from onError)
      ConcurrencyReview3Tests.cs (third-pass regressions: child stdin, token-bucket rounding, worker delay cancellation, retry policy nulls)
      ConcurrencyReview4Tests.cs (fourth-pass regressions: draining after decode errors, grandchild-held pipes, cancelled races and events, observed faults, exact retry ticks)
      ConcurrencyReview81Tests.cs (fifth pass: stdin encoder failures, unterminated output, parallel event cancels, observed faults, hedge timers, pool clearing)
      ConcurrencyReview97Tests.cs (sixth pass: completing a pipeline from its consumer, drop policies after Dispose, one fault observer per task)
      ConcurrencyReview114Tests.cs (seventh pass: consumer re-queuing into a full pipeline, completing and disposing from consumer-started work, calls after Dispose, producers blocked during Dispose on .NET Framework, caller cancels in Retry, linear races and hedges)
      ConcurrencyReview128Tests.cs (round 128: token-bucket refill under polling, WhichExe trailing dots, the exact retry cap, the pipeline consumer seen from inline continuations, concurrent parallel handlers and race attempts)
      ConcurrencyReview147Tests.cs (round 147: token-bucket capacity below one, StartupTiming's negative step, the pipeline's SuppressFlow way out for work the callback starts)
      ConcurrencyReview165Tests.cs (round 165: AtomicDouble text that reads back on .NET Framework, StartupTiming's exact report, a cancelled token wins in WithCancellation and PauseToken, one pending pipeline wake-up, null tasks reported as InvalidOperationException, a stuck output callback, a late cancel after every item finished)
      ConcurrencyReview183Tests.cs (round 183: a failed last item disposed from onError, an empty pipeline disposed then completed, a count within the grant tolerance of capacity, the synchronous Run with the pool busy)
      ConcurrencyReview206Tests.cs (round 206: quoted and space-padded PATH entries in WhichExe, PeriodicWorker's concurrent and inline Dispose, a token bucket above 2^53)
      FlakeRootCauseTests.cs  (round 46: pipeline, async log sink and process readers run off the thread pool)
      CallbackReview54Tests.cs (round 54, cross-cutting: throwing comparers, hashes, clocks, handlers, error sinks and token callbacks)
      CancelDisposeReview55Tests.cs (round 55, cross-cutting: the caller's token, cancelled tokens honoured, Dispose racing the work)
      KeyedLockTests.cs
      ParallelUtilsTests.cs
      PeriodicWorkerTests.cs
      PauseAndRaceTests.cs
      CancellationTokenExtensionsTests.cs
      TaskExtensionsTests.cs
    Time/
      BusinessDaysTests.cs
      CronScheduleTests.cs
      CronSchedulerTests.cs
      DateRangeTests.cs
      Iso8601DurationTests.cs
      IsoWeekTests.cs
      HumanDurationTests.cs
      RelativeTimeTests.cs
      SecurityNetTimeReviewTests.cs (round 13 review regressions: cron DST and bounds, durations, CIDR, secrets, TCP lines, downloads)
      SecurityNetTimeReview2Tests.cs (second-pass regressions: cron make-up at the gap edge, octal CIDR, TCP BOM and overlong lines, forged digests, scheduler errors)
      SecurityNetTimeReview3Tests.cs (third-pass regressions: lone-surrogate passwords, checked-build casts, IPv6 connects, cron at MinValue, scheduler dispose)
      SecurityNetTimeReview4Tests.cs (fourth-pass regressions: TCP decode errors, ACLs kept on save, torn scheduler reads, Unix time at the calendar ends)
      SecurityNetTimeReview5Tests.cs (round 91 review regressions: scheduler clock corrections, IPv4-mapped CIDR matches, strict TCP decoding, download overruns, malformed secrets, rare cron schedules)
      SecurityNetTimeReview6Tests.cs (round 92 review regressions: bad bytes between CR and LF, calendar years in relative times)
      SecurityNetTimeReview7Tests.cs (round 124 review regressions: stale destinations under Resume, wildcard cron jobs after a step back, truncated AEAD blobs, line breaks in secrets keys)
      SecurityNetTimeReview8Tests.cs (round 142: misplaced resume ranges, argument checks before download, cron 7/N, scheduler wake on Add, lone surrogates on send, empty hosts)
      SecurityNetTimeReview9Tests.cs (round 149: TcpLineClient's limits under a strict encoding and while skipping, lossy sends, the host's ParamName, HttpDownload's 206 totals and short ranges, its own .part, nothing local before the answer, folder destinations, sub-ms timeouts, MaxBytes)
      SecurityNetTimeReview10Tests.cs (round 166: cron across a fall-back that crosses midnight, CreateIncremental's bad names, TcpLineClient's blank host)
      SecurityNetTimeReview192Tests.cs (round 192: an HttpDownload resume that looped forever, cron at the end of the calendar, mapped CIDR blocks and plain prefixes, duration grammars, canonical password hashes, port 0)
      SecurityNetTimeReview193Tests.cs (round 193: a 416 resume held to MaxBytes and reporting progress, no space before a CIDR slash, bad-byte counting, cron's impossible dates in a zone, no rerun after a clock step back)
      SecurityNetTimeReview218Tests.cs (round 218: HttpDownload resumes without a total, short ranges from byte 0, 204 and 205 answers)
      TimeOfDayRangeTests.cs
      UnixTimeTests.cs
    Visualization/
      ImageBufferTests.cs
      PngWriterTests.cs
      HexScatterTimingTests.cs (brute-force nearest-hex check, segment logic, well-formed SVG)
      RasterFormatsTests.cs (PNG fixtures from scripts/gen-png-fixtures.py, cross-checked vs Pillow)
      PngFixtureData.cs     (generated)
      SvgDocumentTests.cs
      SvgUtilsTests.cs
      AxisTicksTests.cs
      PlotFrameTests.cs
      AnnotationsTests.cs
      SvgChartTests.cs
      MultiPanelTests.cs
      WaterfallTests.cs
      ColormapTests.cs
      ColorbarTests.cs
      HeatMapTests.cs
      HistogramTests.cs
      BoxPlotTests.cs
      ErrorBarChartTests.cs
      BandChartTests.cs
      LinePlotTests.cs
      ChartsReviewTests.cs  (review regressions: far out-of-range data clipped, non-finite values, overflow-safe ranges and ticks, option validation)
      ChartsReview2Tests.cs (second-pass regressions: subnormal and ±1e308 ranges, exact decimal ticks, one explicit limit past the data, bounded work)
      ChartsReview3Tests.cs (third-pass regressions: sub-millisecond time ticks, one bound past the data, overflowing spans, option validation)
      ChartsReview4Tests.cs (fourth-pass regressions: two ticks over ±MaxValue, bounded hatch and binning, limits at MaxValue, subnormal Pearson)
      ChartsReview80Tests.cs (fifth pass: tight tick search and bounds, exact exponent labels, option validation, unresolvable steps, ordered quartiles, callout sides)
      ChartsReview148Tests.cs (round 148: wide labels, legends and titles kept on the chart, the last timing tick, BandChart's centre in the range, a constant column's r, offset and threshold validation)
      ChartsReview101Tests.cs (round 101: histogram edges over huge ranges, far values mapped without overflow, finite bar padding, margin and image-size validation, an End before every change, FormatSeconds unit boundary)
      ChartsReview121Tests.cs (round 121: lines to far points, band alpha compositing, huge callout offsets, drawn-bar widths, default frames, one-pixel colorbars, tick rounding past 2^52, subnormal waterfall ranges, undefined enums)
      ChartsReview134Tests.cs (round 134: equal explicit limits, the first x label's margin, box fences past MaxValue, band envelopes and far centre lines, far whiskers, clip underflow, whole colormap stop ranges, scatter-matrix legends and subnormal bins, timing clocks, markers and hatching, hex-bin index limits, shared ranges beside empty panels)
      ChartsReview167Tests.cs (round 167: AxisTicks against a brute-force step search, one-ulp spans, tight tick bounds and labels, Time near MaxValue and past 2^53 s, subnormal decades, NaN samples, drawn-bar padding, edge markers, half-away r labels)
      ChartsReview178Tests.cs (round 178: scatter points and reference lines on the same column, markers, callouts and arrowheads kept out of the margin, short arrows, SVG arrows on huge frames, labels held inside the plot, huge and infinite box-plot values, decimal histogram edges rounded once, colormap halves)
      ChartsReview196Tests.cs (round 196: axis ticks settled on their exact decimals, no extra loose step, the smallest tight step, tight ticks inside the range, flat and subnormal ranges at two ticks, distinct sub-minute time labels, bars past the range clipped, legends on narrow charts, segments missing the range, distinct timing offsets, ScatterMatrix title fitted)
      ChartsReview210Tests.cs (round 210: box-plot outliers against exact fences, an all-infinite box plot drawn, equal multi-panel plot areas, thick dotted lines stamped once, tiled bands without a seam, a histogram from double.MaxValue, Waterfall's flat label read back on .NET Framework, in-frame lines as one subpath)
      DocsVizReviewRegressionTests.cs  (2026-10-05 review regressions: Documents and Visualization)
    Resilience/
      RetryTests.cs
      CircuitBreakerTests.cs
      TokenBucketRateLimiterTests.cs
      ThrottleTests.cs
      JitterTests.cs
      BulkheadTests.cs
    Runtime/
      StartupTimingTests.cs
      AppInfoTests.cs
      MemoryPressureTests.cs
      ArrayPoolScopeTests.cs
    Security/
      HashingTests.cs
      HmacTests.cs
      ConstantTimeTests.cs
      CryptoRandomTests.cs
      KeyDerivationTests.cs
      PasswordHasherTests.cs
      AuthenticatedEncryptionTests.cs
    Process/
      ProcessRunnerTests.cs   (Windows-only; drives cmd.exe)
      WhichExeTests.cs
      ShellOpenTests.cs
    Logging/
      LoggerTests.cs
      LogSinksTests.cs
      FileSinkTests.cs
      AsyncLogSinkTests.cs
      LogDecoratorsTests.cs
      LogFormattersTests.cs
      StructuredTextFormatterTests.cs
      ScopedContextTests.cs
      SyslogAndFirstChanceTests.cs (real UDP/TCP loopback collectors, reconnect)
      LoggingReviewTests.cs (round 12 review regressions: Logging, Diagnostics, Cli)
      LoggingReview2Tests.cs (second-pass regressions: async sink cross-deadlock, syslog BOM and facility range, stack trace layout, metrics rounding, rotation with huge maxFiles, decorator failures)
      LoggingReview3Tests.cs (third-pass regressions: async sink self-dispose, clock steps, wrapped exit codes, counter saturation, delimiter overlap)
      LoggingReview4Tests.cs (fourth-pass regressions: rate-limiter deadlock, syslog IPv6 and stalled collectors, null events, metrics sample cap, health status, ANSI resets, column-aware spinner, progress floor)
      LoggingDiagnosticsCliReview78Tests.cs (fifth pass: out-of-order scopes, linear scope and delimiter costs, throwing Messages, disposed decorators, bounded signatures, emoji widths)
      LoggingReview103Tests.cs (sixth pass: cross-consumer re-entry, concurrent health checks, summary order, later SGR resets, scopes disposed elsewhere, disposed sinks, unencodable lines, one-line spinner, undefined enums)
      LoggingReview116Tests.cs (seventh pass: wide spinner characters, kept colour sequences, a guarded Tick, undefined table alignment, FTP replies and programmer errors in IsTransient, sinks after Dispose, broken owned writers, failed reopens after rotation)
      LoggingReview135Tests.cs (eighth pass: spinner widths of flags, ZWJ sequences and narrow symbols, any writer failure kept quiet, styled table cells, partial ANSI resets, a throwing logger clock, undefined minimum levels, negative throttle windows, benchmark iteration limits, racing ScopedTimer disposal)
      LoggingReview152Tests.cs (ninth pass: sub-millisecond spinner intervals, Stop from inside a draw, colon colour codes, FileSink on the null device, undefined levels in filters, routes and Log, trailing spaces and multi-line messages in reports, AsyncLogSink disposed from its inner sink)
      LoggingReview161Tests.cs (round 161: very deep and cyclic inner-exception chains in every formatter and the verbose console report, colon-form underline resets, environment names holding a NUL)
      LoggingReview180Tests.cs (round 180: the metrics mean after an overflowing sum and of signed zeros, sub-millisecond timeouts in HealthCheck, SyslogSink and AsyncLogSink, a writer stopping the spinner during a Message draw, probes off the thread pool, trimmed Choose options)
      LoggingReview202Tests.cs (round 202: bold and dim kept apart under a 22 reset, multi-line environment values and Data keys indented, one-line ConsoleApp failures, a ProgressBar tolerance that scales with the width, signed zeros in the metrics in any order, linear formatter escaping, a throwing FirstChanceMonitor clock)
      LoggingReview213Tests.cs (round 213: an underflowing size estimate, a clock stepped back in ScopedTimer, multi-line probe messages in HealthReport, astral characters in syslog header tokens, pool work from AsyncLogSink's inner sink on a full queue)
  ToolBelt.Windows.Tests/   Windows-only integration tests for the platform satellite (net8.0-windows)
    PowerStatusTests.cs
    SingleInstanceTests.cs
    EventLogWriterTests.cs  (reads the event back with wevtutil)
    WindowsWaveTests.cs     (real job caps on PowerShell children, clipboard round-trips, pipe handoff; no machine-state changes)
    WindowsReviewTests.cs   (round 15 review regressions: DPI, ProgID, registry, job lifetime, single instance, shortcuts, event log)
    WindowsReview2Tests.cs  (second-pass regressions: gate thread affinity, forward timeouts and takeover, registry conversion, impersonation)
    WindowsReview3Tests.cs  (third-pass regressions: hive-root delete guard, stalled-primary forward, real DPI for unaware callers, unsigned registry writes)
    WindowsReview4Tests.cs  (fourth-pass regressions: bounded CF_HDROP parsing, lone surrogates forwarded, namespace prefixes, long and file paths for GetSpace, registry flags and leading backslashes, cancellable Measure)
    WindowsReview79Tests.cs (fifth pass: case-insensitive app ids, bounded launch queue, synchronous forward, NUL and empty names rejected, corrupt shortcuts, clipboard text bound)
    WindowsReview106Tests.cs (sixth pass: NULs in registry paths and value names, invariant text values, one folded app id, one forward timeout, long gate names, empty shortcuts, null ParamNames)
    WindowsReview110Tests.cs (seventh pass: lone surrogates on the clipboard, registry values that read back, Security-log sources, many forwarded arguments, device and UNC path checks, zero job handles)
    WindowsReview131Tests.cs (eighth pass: long and blank shortcut paths, an icon index alone, device-spelled UNC and volume paths, registry integers written back by their bits, collections, Version, IPAddress and Uri, capture edges past int range)
    WindowsReview144Tests.cs (ninth pass: negative registry numbers read back, integer text as words, refused writes create no keys, no resend after a lost answer)
    WindowsReview164Tests.cs (tenth pass: invariant info and rectangle text, opaque captures, regions wider than a GDI bitmap, refused deep registry paths leave nothing, relative shortcut targets)
    WindowsReview184Tests.cs (eleventh pass: NULs in JoinArguments and RunElevated.Start, zero-timeout launches to a running primary)
    WindowsReview207Tests.cs (twelfth pass: one forward deadline, registry keys over 32 levels deep, privilege scopes closed out of order, a process that has exited, JobObject.Start's arguments, a DIB header too short for its masks)
    WindowsReview211Tests.cs (thirteenth pass: free space for a short relative path under a deep directory, damaged and URL shortcuts, \\?\ shortcut targets, an unknown battery state, a path as a file extension, an unpadded last DIB row, NVMe over Fabrics, the dotnet host's options and directory on restart, the event-log truncation length)
    FlakeRootCauseTests.cs  (round 46: JobObject.Start keeps grandchildren in the job, refuses over the limit before running)
    DriveAndVolumeInfoTests.cs
    MonitorInfoTests.cs
    RegistryUtilsTests.cs
    ElevationTests.cs
    JobObjectTests.cs
    WindowUtilsTests.cs
    FileAssociationTests.cs
    ShortcutUtilsTests.cs
    ClipboardUtilsTests.cs
    ScreenCaptureTests.cs
    ResourceSamplerTests.cs
    RunElevatedTests.cs
    OsVersionInfoTests.cs
    MemoryStatusTests.cs
    IdleTimeTests.cs
  ToolBelt.Wpf.Tests/       WPF UI satellite tests (net8.0-windows)
    ObservableObjectTests.cs
    RelayCommandTests.cs
    AsyncRelayCommandTests.cs
    NotifyTaskCompletionTests.cs
    ValidationObservableObjectTests.cs
    ValidationAndBindingDiagnosticsTests.cs
    ValueConvertersTests.cs
    MultiValueConvertersTests.cs
    BindingProxyTests.cs
    MessengerTests.cs
    DialogServiceTests.cs
    IncrementalCollectionTests.cs
    FileDropBehaviorTests.cs
    NumericInputBehaviorTests.cs
    GlobalHotkeysTests.cs
    ThemeManagerTests.cs
    PlotPresenterTests.cs
    WpfReviewTests.cs       (round 16 review regressions: command threading, numeric input, paging reset, regex timeouts, converters)
    WpfReview2Tests.cs      (round 33 review regressions: numeric clamping, hotkey threading, theme adoption, drag filtering, validation)
    WpfReview3Tests.cs      (round 49 review regressions: numeric deletions, shared-load cancellation, theme follow failures, hotkey gestures)
    WpfReview4Tests.cs      (round 73 review regressions: bound-box culture, overtype and text drops, nested drop targets, object-level errors, anchored patterns, hotkey ids)
    WpfReview82Tests.cs     (round 82: kept trace levels, throwing rules, dispatcher shutdown, Messenger index, drop effects and inherited AllowDrop, MaxLength pastes)
    WpfReview102Tests.cs    (round 102: theme self-merge, bad trace switches and formats, null names and NaN bounds, dialog filters, FitMatrix sizes, bound and DynamicResource AllowDrop, Minimum rounded, Enter and Tab, Messenger re-registration order)
    WpfReview117Tests.cs    (round 117: loading more from a finished load's notifications, linear Messenger unregister, NaN numeric bounds, look-alike hotkey letters, "*." drop filter)
    WpfReview139Tests.cs    (round 139: exponent text in numeric boxes, editing down refused text, finite clamped fits, Matches' ParamName)
    WpfReview158Tests.cs    (round 158: numeric boxes rounding the decimal they read as, in range and idempotent; Completion with a carried context whose dispatcher shut down; repeated hotkey modifiers; a null callback for a held hotkey)
    WpfReview175Tests.cs    (round 175: a reset from a reset handler, a throwing PropertyChanged handler in SetAndValidate, Complex and null enumerators in CountToVisibility, QWORD theme values, RecordingDialogService records and copies, overflowing literals in NumericRangeValidationRule, ResetView inside the zoom limits)
    WpfReview182Tests.cs    (round 182: zooms an ulp outside the bounds, the live Calls list, culture order for strings as object, text past the separator that cannot reach the range, coerced text the filter refused, lone signs left on lost focus)
    WpfReview203Tests.cs    (round 203: a step past MaxValue at decimal places, a one-unit step that stuck, the keystroke filter at huge place counts, critical and warning trace lines, a ';' in an indexer path, ResetView at a singular zoom, NFloat NaN bounds, a throwing CanExecuteChanged handler)
    WpfReview214Tests.cs    (round 214: both themes merged by the app, FollowSystem after its thread ended, ZoomAbout on y-up and non-uniform views, a one-shot sequence and char / DBNull in CountToVisibilityConverter, a loader's null Task)
    CallbackReview54Tests.cs  (round 54: throwing handlers in validation, commands and incremental loading)
    CancelDisposeReview55Tests.cs (round 55: incremental loading's cancelled tokens)
    UiReviewRegressionTests.cs  (2026-10-05 review regressions: WPF satellite)
  ToolBelt.WinForms.Tests/  WinForms UI satellite tests (net8.0-windows)
    ControlExtensionsTests.cs
    ControlTreeExtensionsTests.cs
    DoubleBufferedExtensionsTests.cs
    DoubleBufferedPanelTests.cs
    WaitCursorScopeTests.cs
    ComboBoxEnumExtensionsTests.cs
    LayoutSuspenderTests.cs
    FormStatePersistenceTests.cs
    DataGridViewExtensionsTests.cs
    MenuBuilderTests.cs
    PlotPictureBoxTests.cs
    WinFormsReviewTests.cs  (round 17 review regressions: off-screen placement, disposed controls, view limits, cursor scopes, command leaks, shortcuts)
    WinFormsReview2Tests.cs (round 34 review regressions: CSV truncation, deep-zoom painting, handle-less marshalling, shortcuts, empty filters)
    WinFormsReview3Tests.cs (round 51 review regressions: zoom overflow, worker-raised CanExecuteChanged, atomic SaveCsv, formula escaping)
    WinFormsReview4Tests.cs (round 60 review regressions: shortcut keys, reusable ToolStripBuilder, command leaks, image cells, deep-zoom pans, disposed posts)
    WinFormsReview88Tests.cs (round 88: sided modifier keys, formatting no key, header sizing with hidden headers, empty working areas, bound combos, lost capture, all-or-nothing builders)
    WinFormsReview105Tests.cs (round 105: wheel marked handled, re-fit inside ViewChanged, capture released by a handler, builders disposing a failed item, side-effect-free FindItem, far-apart working areas, README discovery names)
    WinFormsReview120Tests.cs (round 120: one-column and formula-escaped CSV, unformattable cells and non-string headers, long SaveCsv names, a dead UI thread, disposed plot images, zoom limits, script summary and locks)
    WinFormsReview132Tests.cs (round 132: deep-zoom pixel placement, an overflowing wheel power, FindItem skipping hosted controls and blank paths, strict FormState.TryParse, the runner's --list exit code)
    WinFormsReview155Tests.cs (round 155: hidden rows in a selection, SaveCsv to NUL, streams and links, surrogate delimiters, Apply of an empty state)
    WinFormsReview176Tests.cs (round 176: other buttons during a pan, a zoom by 1, typed text in SetSelectedEnum, AutoSizeColumns on a Fill grid)
    WinFormsReview204Tests.cs (round 204: smooth boundaries at deep zoom, a repeated shortcut modifier)
  ToolBelt.MathChecks/      differential cross-check satellite: grades the from-scratch numerics
                            (FFT, distributions, special functions, hypothesis tests, linear algebra,
                            fits, root-finding, statistics)
                            against Math.NET Numerics. The ONLY project with an external NuGet
                            dependency, so it is EXCLUDED from ToolBelt.sln — run it on its own.
    *CrossCheckTests.cs     one file per area (Anova, Distributions, DistributionsTail, Fft, Fit, HypothesisTests,
                            LinearAlgebra, Regression, RootFinding, SpecialFunctions, Statistics) vs Math.NET
    IncompleteFunctionsGradedTests.cs  incomplete beta/gamma graded against mpmath in condition-number units
    Data/incomplete-*.tsv   50-digit mpmath references with condition numbers (from scripts/gen-incomplete-references.py)
scripts/
  build.ps1                 build wrapper (disables the persistent build server)
  test.ps1                  build once + launch the test exe directly (-Configuration X; other args go to the runner; 20 min cap per suite)
  bump-version.ps1          works out the next YYYY.M.D.N release version, writes it to Directory.Build.props, prints it
  bump-version-tests.ps1    grades bump-version.ps1 on a scratch copy against a model (600 random versions; verify.sh runs it)
  tooling-tests.sh          checks checked-build.sh's path handling, test.ps1's -Configuration and gen-incomplete-references.py's failed rows (verify.sh runs it)
  verify.sh                 full verify (Git Bash): sln build, the four suites, MathChecks, the bump-version and tooling self-tests, ConsumerSmoke; one line per suite
  checked-build.sh          core suite against an overflow-checked library build, in a scratch worktree
  net48.sh                  chosen core test folders (at least one) on .NET Framework 4.8 against the netstandard2.0 build
  coverage.ps1              line/branch coverage of the core via the coverlet.console global tool (+ HTML report)
  gen-distribution-references.py  regenerates the distribution reference table (20 digits, computed at 40) (needs mpmath + scipy)
  gen-png-fixtures.py     regenerates the PNG decoder fixtures (needs Pillow, used as a cross-check)
  gen-filter-references.py  regenerates the IIR/FIR filter references from SciPy (butter, cheby1/2, ellip, bessel, group delay, sosfiltfilt, firwin)
  gen-gauge-references.py   regenerates the gauge R&R references (least-squares ANOVA + scipy F tails)
  gen-incomplete-references.py  regenerates the incomplete beta/gamma references for MathChecks (mpmath; hours)
samples/
  ConsumerSmoke/            a fresh EXTERNAL consumer (public API only); doubles as a usage example
                            and a misuse smoke test. Built for net8.0, net48 and net8.0-windows (Satellites.cs:
                            the three satellites); verify.sh runs all three; exit 0 == all good.
  Benchmarks/               runs the Benchmark harness against hot-path utilities (run -c Release)
```

## Testing

Hand-rolled, zero-dependency runner. No xUnit/NUnit/MSTest. A test is any **public, parameterless,
`void`- or `Task`-returning** instance method on a public non-abstract class whose name ends in `Tests`;
a fresh instance is constructed per method. `[Skip("reason")]` parks a test or class.

**Differential testing is the house style:** grade a new algorithm against a naive reference over
hundreds of randomized trials rather than hand-computed literals (those have been wrong repeatedly).
Edge cases live in a `*HardeningTests` file next to the type.

```powershell
# Build everything
scripts\build.ps1

# Run all tests
scripts\test.ps1

# Filter / verbose / list
scripts\test.ps1 --filter Guard --verbose   # fails if no suite has a matching test
scripts\test.ps1 --list
scripts\test.ps1 -Configuration Release
```

**Cross-check satellite.** The trickier numerics (FFT, distributions, linear algebra, least-squares
fits, root-finding, percentiles) get a *second, independent opinion*: `tests/ToolBelt.MathChecks`
grades them against [Math.NET Numerics](https://numerics.mathdotnet.com/). It is the only project with
an external NuGet dependency, so it is deliberately excluded from `ToolBelt.sln` — the shipping library
and its main suite stay BCL-only. Run it on its own (needs network access on first restore):

```powershell
dotnet run --project tests\ToolBelt.MathChecks -c Release
```

The main suite's closed-form/differential tests remain authoritative; this is defence in depth.

It also holds the **incomplete beta/gamma harness**: 6880 values of I_x(a, b) and 848 of P(a, x)/Q(a, x), shapes from
1e-300 to 1e20, against 50-digit mpmath references computed without cancellation. Each error is graded in units of
eps·(1 + κ), κ being the function's condition number (input rounding alone moves the value by ~eps·κ); over 16 fails.
`python scripts/gen-incomplete-references.py grid` rebuilds the references (hours); a failing row over 16 eps that has
no κ yet is listed in `Data/incomplete-*.need.tsv`, and `... kappa` fills those in.

## Adding a type

Beyond the code itself, keep the docs in sync so people can still find things:

1. Add the file to the **[Layout](#layout)** block (the exhaustive index).
2. Add a **test file** (`<Type>Tests.cs`, plus `<Type>HardeningTests.cs` for edge cases).
3. If it does something a consumer would search for, add a row to **[Find what you need](#find-what-you-need)**.
4. If it starts a **new division**, add a row to **[Divisions at a glance](#divisions-at-a-glance)** (and list a couple of its types in the examples column of existing divisions when you add notable ones).

The discovery tables (Divisions at a glance, Find what you need) are hand-maintained and describe *capabilities*,
so they only change when a genuinely new capability or division lands — not on every file.

## Contributing

This is a personal tool, published as-is — **issues and pull requests aren't accepted** (PRs auto-close). Fork it and make it your own. 🧰

## License

MIT — see [LICENSE](LICENSE). © 2026 RelentlessOldMan.
