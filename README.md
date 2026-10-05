# ToolBelt 🧰

**A zero-dependency, cross-platform C# "tool belt" — 200+ small, heavily-tested utilities that drop straight into any project.** Collections, numerics, text, graphs, grids, intervals, async, signal processing, statistics, and more — spread across focused divisions, every file independently copyable, every public contract exercised by tests.

A companion to **UtilityBelt** — a generic, project-agnostic collection of C# helper and utility
classes. ToolBelt is a separate playground repo where we build **net-new** reusable utilities under the
same design laws, so they can later be pulled into the UtilityBelt tree with minimal friction.

## Why

A defect in application code affects one application; a defect in a shared utility propagates into every
project that consumes it. ToolBelt treats that blast radius seriously: each utility is small, focused,
predictable, hard to misuse, and covered by behavioral, property-based, and differential tests
(**1,400+** of them). Drop a single `.cs` file into a project, or reference the whole assembly — either
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
| **Control** | Control loops & filters | `PidController` `KalmanFilter1D` |
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
| **Quality** | Statistical process control | `ProcessCapability` `ControlChart` `MeasurementAgreement` |
| **Resilience** | Retry & rate control | `Retry` `CircuitBreaker` `Bulkhead` `TokenBucketRateLimiter` |
| **Runtime** | Process/runtime introspection & scratch memory | `StartupTiming` `AppInfo` `MemoryPressure` `ArrayPoolScope` |
| **Security** | Hashing, HMAC, KDF, secure random, AEAD | `Hashing` `Hmac` `KeyDerivation` `CryptoRandom` `AuthenticatedEncryption` `SecretsFile` |
| **Signal** | Digital signal processing | `Fft` `Window` `Spectrum` `WelchPsd` `Hilbert` `Goertzel` `Convolution` `Resample` `Quantizer` |
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
| Generate a unique id | `Identifiers.Ulid`, `NanoId`, `ShortGuid` |
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
| Cap a child process's memory / CPU | `ToolBelt.Windows.JobObject` |
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
      Base32.cs             RFC 4648 Base32 encode/decode
      Adler32.cs            Adler-32 checksum (batched + streaming)
      Base58.cs             Base58 (Bitcoin alphabet, leading-zero preservation)
      Base64Url.cs          URL-safe base64 (RFC 4648 §5, unpadded)
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
      ConsoleTable.cs       box-drawing text table (alignment, padding)
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
      BitSet.cs             growable bit set (union / intersect / except)
      LruCache.cs           fixed-capacity least-recently-used cache (O(1))
      MultiDictionary.cs    key -> many-values multimap
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
      ConfigLayers.cs       merge sources by priority into a flat key space (with provenance)
      ConfigBinder.cs       bind a flat key space to a typed object (nested/arrays/enums/durations)
      EnvironmentVariables.cs  typed env reads (int/bool/duration/enum/list), malformed = error naming the variable
      SettingsStore.cs      per-user JSON settings: atomic save, corrupt file moved aside, defaults (net8)
    Control/
      PidController.cs      PID with clamping, anti-windup, derivative-on-measurement
      KalmanFilter1D.cs     scalar Kalman filter (predict/update, converging gain)
    Diagnostics/
      Benchmark.cs          micro-benchmark harness (warmup, stats, bytes/op, compare-to-baseline)
      MetricsRegistry.cs    named counters / gauges / timers with percentile snapshots (thread-safe)
      ScopedTimer.cs        time a using-block to a callback / metrics (injectable clock)
      ExceptionUtils.cs     root-cause / flatten / describe / transient-vs-permanent classify
      EnvironmentReport.cs  one-call bug-report snapshot (versions/OS/culture/uptime/env, secret-safe)
      HealthCheck.cs        named probes w/ enforced timeouts, run concurrently -> worst-status report
      FirstChanceMonitor.cs report thrown-then-swallowed exceptions; throttled per signature, recursion-safe
      DiskSpace.cs          free space on a path's volume, writability probe, pre-run fit check + size estimate
      AssertInvariant.cs    conditionally compiled internal-consistency checks (DEBUG / TOOLBELT_INVARIANTS)
    Documents/
      MarkdownReport.cs     fluent GitHub-flavored Markdown (headings/lists/tables/code/quotes)
      HtmlReport.cs         self-contained HTML doc with embedded CSS; escaping; base64 image embed
      PdfWriter.cs          hand-rolled PDF: exact base-14 metrics, wrapping, pagination, tables (header repeats),
                            PNG/RGBA images with alpha, bookmarks from headings, page numbers, code blocks, title
      DocxWriter.cs         Word .docx via OOXML zip: mixed-format runs, real nested bullet/numbered lists, tables,
                            PNG images, page breaks, header + "Page X of Y" footer, title/author
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
      RangeMap.cs           non-overlapping key-range -> value (splits + coalesces; always minimal)
      RangeSet.cs           union / intersect / difference / complement over intervals
    Net/
      CidrRange.cs          CIDR block (v4/v6): parse, Contains, network/broadcast, count, enumerate
      IpUtils.cs            private/loopback detection, IPv4 netmask <-> prefix length
      PortCheck.cs          TCP reachability within a timeout + find a free local port
      TcpLineClient.cs      line-oriented TCP client (connect/read/write timeouts)
      HostInfo.cs           local host/interface info + DNS resolve with a timeout
      HttpDownload.cs       download to file: progress, resume (Range), checksum, transient retry
    Quality/
      ProcessCapability.cs  Cp/Cpk/sigma-level/ppm (explicit overall vs within-subgroup sigma)
      ControlChart.cs       I-MR limits + run rules (beyond-limits / one-side / trend)
      MeasurementAgreement.cs  Bland-Altman bias + limits of agreement between two methods
    Signal/
      Convolution.cs        direct convolution + full cross-correlation
      Fft.cs                radix-2 + Bluestein FFT — O(n log n) for ANY length; forward/inverse
      FrequencyGrid.cs      FFT bin <-> frequency mapping; resolution bandwidth
      Goertzel.cs           single-frequency magnitude/phase (cheaper than a full transform)
      Hampel.cs             sliding-window outlier rejection (median ± k·MAD)
      Hilbert.cs            analytic signal, amplitude envelope, instantaneous phase/frequency
      LevelConversions.cs   dB/amplitude/power conversions, RMS, dBFS
      MedianFilter.cs       sliding-window median (removes spikes, preserves edges)
      PhaseUnwrap.cs        removes 2π jumps from wrapped phase; group delay −dφ/dω
      PulseMeasurements.cs  rise/fall time, pulse width, duty cycle (sub-sample crossings)
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
      DeepEquals.cs         structural graph equality (cycles, float tolerance, collections)
      ExpressionAccessor.cs compiled get/set delegates by member name (cached; boxing-free typed path)
      FlattenObject.cs      object graph -> flat path->value dictionary
      UnflattenObject.cs    flat paths -> nested dictionaries/lists (inverse of Flatten), conflicts named
      ObjectMapper.cs       copy matching properties across types (convert / ignore / custom)
      PropertyDiff.cs       differences between two graphs as (path, old, new)
      PropertyPath.cs       get/set via dotted path with indexers (clear per-segment errors)
      TypeUtils.cs          friendly names / default / unwrap-nullable / numeric / safe convert / scan
    Identifiers/
      NanoId.cs             compact URL-safe random ids (injectable Random, non-crypto)
      ShortGuid.cs          Guid <-> compact 22-char URL-safe string (lossless)
      SnowflakeIdGenerator.cs  64-bit time-ordered ids (injectable clock)
      Ulid.cs               ULID generator (monotonic, Crockford, injectable clock/rng)
    IO/
      AtomicFile.cs         crash-safe write (temp + swap, optional .bak backup)
      ByteSize.cs           human-readable byte sizes (format + parse, binary/decimal)
      CsvBinder.cs          map string rows <-> typed objects by header (converts via TypeUtils)
      CsvLine.cs            RFC 4180 CSV line parse + format
      CsvDialect.cs         delimiter/decimal/header presets (Excel European, tab, pipe) + Detect sniffing
      FileRetention.cs      prune by count/age/total size (+ dry run), numbered rotation, timestamped names
      JsonFile.cs           JSON load/save with shared options and atomic writes + JsonLines (NDJSON) (net8)
      DotEnv.cs             .env key=value parser (comments, quotes, export)
      FixedWidth.cs         fixed column-width record parse + format
      HexDump.cs            classic offset/hex/ascii hex dump
      LineReader.cs         enumerate lines with byte offsets (mixed line endings)
      SafeFileName.cs       sanitize a string into a safe filename
      StreamUtils.cs        copy-with-progress (+ cancellation) / read-exactly
      StreamWrappers.cs     CountingStream (bytes read/written) and TeeStream (duplicate reads or writes)
      TempFile.cs           disposable temp file & directory scopes
      ZipUtils.cs           zip create/extract/list/read; extract guarded against Zip-Slip
      TarUtils.cs           tar/.tar.gz create/extract/list; traversal and link escapes rejected (net8)
      DirectoryUtils.cs     tree copy (overwrite/filter policies), delete w/ read-only + lock retry, never follows links
      ChecksumManifest.cs   SHA-256 tree manifest in sha256sum format: create/save/verify/compare directories
      FileWatcher.cs        debounced, coalescing FileSystemWatcher; wait-until-stable; rescans on overflow/restart/dir moves
    Numerics/
      Angle.cs              degree normalize / shortest-diff / lerp / deg-rad
      Anova.cs              one-way ANOVA (F-test table) + F-distribution tail probability
      BaseConverter.cs      integer <-> radix string (base 2..36 or custom alphabet)
      Bootstrap.cs          resampling percentile confidence interval (any statistic, seeded)
      ChangePoint.cs        CUSUM + binary-segmentation level-shift detection
      ConfidenceInterval.cs  mean CI (Student-t, or large-sample z) / variance & SD (chi-square) / Wilson proportion
      DeterministicRandom.cs  seeded fixed-algorithm PRNG (xoshiro256**, stable across runtimes)
      Differentiation.cs    numerical derivative (uniform/uneven samples, function, Richardson)
      Distributions.cs      normal/log-normal/exponential/uniform/Student-t/chi-square/F PDF/CDF/quantile (<1e-13 rel vs 40-digit refs)
      EmpiricalDistribution.cs  empirical CDF step function (value<->percentile lookups)
      EngineeringNotation.cs  SI-prefix format + parse (yocto..yotta, round-trip)
      ExponentialMovingAverage.cs  EMA smoothing (alpha)
      Histogram.cs          fixed-bin histogram (under/overflow tracking)
      HypothesisTests.cs    t-test / chi-square / KS / Mann-Whitney U / F-test for variances; IsSignificant(alpha)
      StreamingQuantile.cs  one-pass constant-memory quantiles (P² algorithm) + multi-quantile tracker
      PermutationTest.cs    distribution-free two-sample / paired tests, exact when small, any statistic
      MultipleRegression.cs OLS via Householder QR: SEs, t, p, R²/adj, prediction & mean intervals + Theil-Sen robust line
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
      Primes.cs             primality test / next-prime / factorize / sieve
      RandomUtils.cs        shuffle / sample-without-replacement / weighted / gaussian / exponential / Poisson (PTRS) / gamma
      RootFinding.cs        bisection / Brent / Newton / secant (explicit convergence result)
      Rounding.cs           round to multiple / significant digits
      RunningStatistics.cs  Welford online mean/variance/stddev/min/max
      SpecialFunctions.cs   gamma / log-gamma / erf / regularized incomplete gamma & beta
      ToleranceCheck.cs     evaluate a series against limits (violations, worst deviation)
      Trend.cs              Mann-Kendall trend test + Sen's slope (robust)
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
      GlobMatcher.cs        glob matching (*, ?, [a-z], negation)
      HammingDistance.cs    Hamming distance between equal-length strings / bit sequences
      Indent.cs             indent / dedent multi-line text
      JaroWinkler.cs        Jaro & Jaro-Winkler string similarity
      LevenshteinDistance.cs edit distance + normalized similarity (rolling buffer)
      Mask.cs               mask sensitive strings (keep leading/trailing visible)
      MiddleEllipsis.cs     truncate keeping head + tail
      NaturalComparer.cs    natural sort order (img2 < img10)
      QueryString.cs        URL query-string build + parse (percent-encoding)
      NGramSimilarity.cs    Dice & Jaccard similarity over character n-grams
      RomanNumerals.cs      integer <-> Roman numeral (1..3999, validated)
      Slug.cs               URL/filename-safe slugs (diacritic folding)
      Soundex.cs            American Soundex phonetic key
      StringSimilarity.cs   "did you mean" best/top-N match ranking (pluggable scorer)
      TemplateFormatter.cs  {name} placeholder substitution ({{ }} escaping)
      TextDiff.cs           minimal line-based diff (LCS edit script; +/-/space format)
      Truncate.cs           ellipsis truncation (char-exact + word-aware)
      WordWrap.cs           greedy word wrap to a column width (hard-breaks long words)
    Threading/
      AsyncEvent.cs         awaitable multicast event (sequential/parallel, aggregated errors)
      AtomicCounters.cs     lock-free AtomicLong / AtomicDouble (interlocked add/exchange)
      AsyncLock.cs          async mutual-exclusion lock (held across await)
      AsyncLazy.cs          run-once async initialization (result cached)
      AsyncManualResetEvent.cs  awaitable manual-reset event
      AsyncCountdownEvent.cs  awaitable countdown latch
      ConcurrentPipeline.cs  bounded producer-consumer (block / drop-newest / drop-oldest)
      KeyedLock.cs          per-key async locks (lock striping, ref-counted)
      ParallelUtils.cs      bounded-concurrency async fan-out (fail-fast / collect-all)
      PeriodicWorker.cs     run an action on an interval, graceful stop (injectable delay)
      PauseTokenSource.cs   cooperative pause/resume (PauseToken.WaitWhilePausedAsync at safe points)
      TaskRace.cs           FirstSuccessful / FirstMatching / FirstToComplete / Hedged — losers cancelled and awaited
      CancellationTokenExtensions.cs  await a token (WhenCanceled) + CreateLinkedTimeout source
      TaskExtensions.cs     WithTimeout / WithCancellation / FireAndForget / WhenAllOrFirstException
    Time/
      BusinessDays.cs       business-day add/count (weekends + holidays)
      CronSchedule.cs       5-field cron parse + next-occurrence
      CronScheduler.cs      run jobs on cron schedules: no self-overlap, no catch-up bursts, injectable clock
      DateRange.cs          date/time interval (contains / overlaps / intersect)
      Iso8601Duration.cs    ISO 8601 duration parse/format (PT1H30M)
      IsoWeek.cs            ISO 8601 week-of-year and week-year
      HumanDuration.cs      format/parse TimeSpan as "1h 2m 3s" (round-trip inverse)
      RelativeTime.cs       "3 minutes ago" / "in 2 hours" (injectable clock)
      TimeOfDayRange.cs     half-open time-of-day window (overnight wrap, Contains)
      UnixTime.cs           Unix epoch conversions (seconds/millis, DTO/DateTime)
    Visualization/
      ImageBuffer.cs        RGBA raster canvas (get/set/fill/line/rect/blit)
      PngWriter.cs          BCL-only PNG encoder (deflate + CRC-32 + Adler-32)
      PngReader.cs          BCL-only PNG decoder: all colour types, 1-16 bit, all filters, Adam7, tRNS; CRC/Adler verified
      Bmp.cs                BMP read/write (32-bit BGRA V4 or 24-bit; reads top-down, bitfields, 8-bit palette)
      Palettes.cs           Okabe-Ito / Tableau10, WCAG luminance + contrast ratio, contrasting label colour, hex
      SvgDocument.cs        fluent SVG vector builder (shapes/text/path; invariant coords)
      SvgUtils.cs           shared SVG primitives: escaping, numbers/colors, PathBuilder, transforms, text-width estimate
      AxisTicks.cs          nice ticks + labels: linear (1-2-5, count-bounded), log decades, clock-friendly time
      PlotFrame.cs          shared data<->pixel mapping (linear/log axes) for renderers and overlays
      Annotations.cs        data-space ref lines / bands / callouts / arrows -> SVG (labelled) or raster
      SvgChart.cs           labelled SVG XY chart: line/scatter/bar, linear/log/time axes, grid, legend, annotations
      MultiPanel.cs         grid of SvgChart panels w/ common title + shared x/y (exactly aligned); raster tiling
      Waterfall.cs          time x frequency waterfall: SVG (embedded PNG cells, freq/time axes, color scale) or raster
      HexBin.cs             exact hexagonal binning in pixel space + SVG (log colour, count scale, edge cells clipped)
      ScatterMatrix.cs      pairs plot: histograms on the diagonal, group colours + legend, Pearson r per panel
      TimingDiagram.cs      logic-analyser view: digital lanes, labelled buses, unknown hatching, cursors, SI time axis
      Colormap.cs           value->color (Grayscale/Hot/Cool/Viridis + custom stops)
      Colorbar.cs           render a colormap as a gradient legend strip (vertical/horizontal)
      HeatMap.cs            render a 2-D grid via a colormap (cell size, range, NaN color)
      Histogram.cs          bin 1-D data (exposed) and render the counts as bars
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
      Hashing.cs            SHA-256/384/512 (+ legacy MD5) over bytes/string/stream/file; incremental; hex/base64
      SecretsFile.cs        passphrase-encrypted key/value file (PBKDF2 + AES-GCM, tamper-evident) (net8)
      Hmac.cs               HMAC-SHA-256/384/512 with constant-time verify
      ConstantTime.cs       fixed-time equality for secrets (no timing leak)
      CryptoRandom.cs       CSPRNG bytes / URL-safe token / numeric code / password (unbiased)
      KeyDerivation.cs      PBKDF2-HMAC-SHA-256/512 (hand-rolled, identical on both TFMs) + salt
      PasswordHasher.cs     encoded PBKDF2 hash + constant-time verify + rehash-needed check
      AuthenticatedEncryption.cs  AES-GCM AEAD: self-describing nonce‖ct‖tag blob + detached form (net8.0)
    Process/
      ProcessRunner.cs      run a child: deadlock-free capture, timeout + tree-kill, stdin/env/cancel
      WhichExe.cs           resolve an executable on PATH (+ PATHEXT on Windows)
      ShellOpen.cs          open a file/folder/URL with the default handler (per-OS)
    Logging/
      Log.cs                LogLevel, LogEvent, ILogSink (shared contracts)
      Logger.cs             thread-safe fan-out logger (levels, category, injectable clock)
      LogSinks.cs           DelegateSink / TextWriterSink (console) / RollingMemorySink
      FileSink.cs           file sink with size-based rotation + retention
      SyslogSink.cs         RFC 5424 syslog over UDP or TCP (octet counting); failures counted, never thrown
      AsyncLogSink.cs       off-thread bounded-queue sink (built on ConcurrentPipeline)
      LogDecorators.cs      FilterSink / RouterSink / RateLimitedSink (composable sink wrappers)
      LogFormatters.cs      plain / compact / single-line-JSON line formatters
      StructuredTextFormatter.cs  configurable field order / delimiter / timestamp (delimited preset)
      ScopedContext.cs      ambient flow-local scope label folded into each event's category
  ToolBelt.Windows/         Windows platform satellite (net8.0-windows) — Win32 / registry utilities
    PowerStatus.cs          AC line / battery charge / saver mode via GetSystemPowerStatus
    SingleInstance.cs       single-instance gate over a named mutex (abandoned-owner safe)
    SingleInstanceApp.cs    second launch forwards args + cwd to the primary (per-user pipe) + foreground handoff
    DriveAndVolumeInfo.cs   fixed-drive enumeration + free space for any path
    StorageDeviceInfo.cs    bus type (USB/NVMe/SATA/SD…), removable, SSD vs HDD, vendor/serial; mapped drive -> UNC path
    MonitorInfo.cs          attached monitors: bounds / work area / primary (EnumDisplayMonitors)
    RegistryUtils.cs        hive+subkey read/write/delete/enumerate helpers (default view)
    Elevation.cs            elevation / Administrator role / mandatory integrity level
    JobObject.cs            job object grouping child processes (kill-on-close; memory / process-count / CPU-rate caps; accounting)
    WindowUtils.cs          enumerate top-level windows / foreground / find by title
    FileAssociation.cs      per-extension opener / friendly name / command / ProgID
    ShortcutUtils.cs        create & read .lnk shortcuts via COM IShellLink
    ClipboardUtils.cs       clipboard text, Explorer file lists (CF_HDROP) and images (CF_DIB) via raw Win32 on an STA thread
    ScreenCapture.cs        capture screen/region to a raw BGRA buffer via GDI BitBlt (no System.Drawing)
    ResourceSampler.cs      per-process CPU% (core-normalised) + working-set / private memory
    RunElevated.cs          launch/relaunch a process elevated via ShellExecute "runas" (UAC)
    OsVersionInfo.cs        true OS version via RtlGetVersion (Win10/Win11 detection)
    MemoryStatus.cs         system physical / page-file memory + load % (GlobalMemoryStatusEx)
    IdleTime.cs             time since last user input (GetLastInputInfo)
    PrivilegeScope.cs       enable a token privilege (SeBackup, SeShutdown…) for a scope, restore on dispose
    PowerScheme.cs          active/installed power plans, switch, Use() scope that restores the previous plan
    DpiInfo.cs              per-monitor DPI/scale, system DPI, thread DPI-awareness scope (physical-pixel capture)
    EventLogWriter.cs       Windows Event Log via ReportEvent (no package); source registration helpers
  ToolBelt.Wpf/             WPF UI satellite (net8.0-windows) — MVVM building blocks
    ObservableObject.cs     INotifyPropertyChanged base with SetProperty
    RelayCommand.cs         ICommand over delegates (+ generic RelayCommand<T>)
    AsyncRelayCommand.cs    async ICommand (Task execute) with re-entrancy guard
    NotifyTaskCompletion.cs bindable async-task wrapper (status/result/error via INPC)
    ValidationObservableObject.cs  INotifyDataErrorInfo + INotifyPropertyChanged validation base
    ValidationRules.cs      fluent rule sets (Required/Range/Length/Matches/Must/DependsOn) + RuleValidatedObject + XAML rules
    BindingDiagnostics.cs   BindingFailureListener (surface or throw on binding errors) + pass-through DebugConverter
    ValueConverters.cs      common IValueConverters (bool/visibility/null/enum/count)
    MultiValueConverters.cs IMultiValueConverters (boolean AND / OR for MultiBinding)
    BindingProxy.cs         Freezable DataContext bridge for out-of-tree bindings
    Messenger.cs            thread-safe WEAK-reference pub/sub (recipient-keyed, no forgotten-unsubscribe leak)
    DialogService.cs        IDialogService + WPF impl (MessageBox/file/folder) + strict RecordingDialogService fake
    IncrementalCollection.cs  page-at-a-time ObservableCollection (coalesced loads, stale-page-safe reset)
    FileDropBehavior.cs     attached: dropped files -> ICommand, extension filter, correct drag-over effects
    NumericInputBehavior.cs attached: numeric TextBox (min/max/decimals, culture-aware, arrows/wheel step)
    GlobalHotkeys.cs        system-wide RegisterHotKey on a message-only window + HotkeyGesture parse/format
    ThemeManager.cs         light/dark ResourceDictionary swap in place + Windows app-mode probe/follow + palette
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
    MenuBuilder.cs          declarative MenuStrip/ToolStrip builders, ICommand items, Shortcut parse, FindItem(path)
    PlotPictureBox.cs       zoom/pan/fit image view (crisp pixels, flicker-free) + RGBA->Bitmap bridge
tests/
  ToolBelt.Tests/           hand-rolled, zero-dependency console test runner (exit 0 == all green)
    Framework/
      TestRunner.cs         reflection-based discovery ( *Tests classes )
      Check.cs              minimal assertion helpers
    Guards/
      GuardTests.cs, GuardHardeningTests.cs
    Binary/
      Adler32Tests.cs
      Base32Tests.cs
      Base58Tests.cs
      Base64UrlTests.cs
      Base85Tests.cs
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
      KalmanFilter1DTests.cs
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
    Enums/
      EnumExtensionsTests.cs
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
    Signal/
      ConvolutionTests.cs
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
      DistributionReferenceTests.cs  (+ DistributionReferenceData.cs: 229 mpmath 40-digit reference values)
      InferenceAccuracyTests.cs      (worked examples + simulated coverage / false-positive rates)
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
  ToolBelt.Windows.Tests/   Windows-only integration tests for the platform satellite (net8.0-windows)
    PowerStatusTests.cs
    SingleInstanceTests.cs
    EventLogWriterTests.cs  (reads the event back with wevtutil)
    WindowsWaveTests.cs     (real job caps on PowerShell children, clipboard round-trips, pipe handoff; no machine-state changes)
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
  ToolBelt.MathChecks/      differential cross-check satellite: grades the from-scratch numerics
                            (FFT, distributions, special functions, hypothesis tests, linear algebra,
                            fits, root-finding, statistics)
                            against Math.NET Numerics. The ONLY project with an external NuGet
                            dependency, so it is EXCLUDED from ToolBelt.sln — run it on its own.
scripts/
  build.ps1                 build wrapper (disables the persistent build server)
  test.ps1                  build once + launch the test exe directly
  gen-distribution-references.py  regenerates the 40-digit distribution reference table (needs mpmath)
  gen-png-fixtures.py     regenerates the PNG decoder fixtures (needs Pillow, used as a cross-check)
samples/
  ConsumerSmoke/            a fresh EXTERNAL consumer (public API only); doubles as a usage example
                            and a misuse smoke test. Build + run its exe; exit 0 == all good.
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
scripts\test.ps1 -- --filter Guard --verbose
scripts\test.ps1 -- --list
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
