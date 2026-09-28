# ToolBelt 🧰

**A zero-dependency, cross-platform C# "tool belt" — 180+ small, heavily-tested utilities that drop straight into any project.** Collections, numerics, text, graphs, intervals, async, signal processing, statistics, and more — spread across focused divisions, every file independently copyable, every public contract exercised by tests.

A companion to **UtilityBelt** — a generic, project-agnostic collection of C# helper and utility
classes. ToolBelt is a separate playground repo where we build **net-new** reusable utilities under the
same design laws, so they can later be pulled into the UtilityBelt tree with minimal friction.

## Why

A defect in application code affects one application; a defect in a shared utility propagates into every
project that consumes it. ToolBelt treats that blast radius seriously: each utility is small, focused,
predictable, hard to misuse, and covered by behavioral, property-based, and differential tests
(**1,100+** of them). Drop a single `.cs` file into a project, or reference the whole assembly — either
works, with no dependencies to inherit.

> **Merge note:** namespaces are currently rooted at `ToolBelt.*` with divisions/paths kept identical to
> UtilityBelt. When we merge upstream, a single root find-replace (`ToolBelt` → `UtilityBelt`) flips it.

## Divisions at a glance

Each division is a coherent namespace (`ToolBelt.<Division>`). The full annotated file list is in [Layout](#layout).

| Division | What's in it | A few examples |
|---|---|---|
| **Binary** | Encodings, checksums, bit twiddling | `Base32` `Base58` `Hex` `Crc32` `Fnv1a` `VarInt` `Bits` `Luhn` |
| **Cli** | Console output rendering | `ConsoleTable` `ProgressBar` `Sparkline` |
| **Collections** | Data structures & sequence ops | `LruCache` `TtlCache` `Deque` `BloomFilter` `Trie` `Batch` `Aggregation` |
| **Configuration** | Layered config & typed binding | `ConfigLayers` `ConfigBinder` |
| **Control** | Control loops & filters | `PidController` `KalmanFilter1D` |
| **Diagnostics** | Measurement & observability | `Benchmark` `MetricsRegistry` `ScopedTimer` `ExceptionUtils` `EnvironmentReport` |
| **Enums** | Enum helpers | `EnumExtensions` `EnumFlags` `EnumMap` |
| **Functional** | Result/optional types, memoization | `Result` `Option` `Either` `Memoize` |
| **Graphs** | Graph algorithms | `Graph` `TopologicalSort` `ShortestPath` `MinimumSpanningTree` |
| **Guards** | Argument validation | `Guard` |
| **Identifiers** | Id generation & encoding | `Ulid` `NanoId` `ShortGuid` `SnowflakeIdGenerator` |
| **Intervals** | Interval/range structures | `Interval` `IntervalTree` `RangeMap` `RangeSet` |
| **IO** | Files, streams, tabular text, zip | `AtomicFile` `CsvLine` `CsvBinder` `FixedWidth` `LineReader` `ByteSize` `ZipUtils` |
| **Logging** | Fan-out logging (plain-string events) | `Logger` `TextWriterSink` `FileSink` `AsyncLogSink` `RollingMemorySink` `FilterSink` `RouterSink` `RateLimitedSink` `ScopedContext` |
| **Net** | Address math, TCP/DNS, HTTP | `CidrRange` `IpUtils` `PortCheck` `TcpLineClient` `HostInfo` `HttpDownload` |
| **Numerics** | Math, stats, calculus, random | `DeterministicRandom` `Distributions` `Percentile` `Polynomial` `UnitConvert` `Bootstrap` |
| **Objects** | Reflection / object services | `DeepEquals` `PropertyDiff` `PropertyPath` `ObjectMapper` `TypeUtils` |
| **Process** | Child processes | `ProcessRunner` `WhichExe` `ShellOpen` |
| **Quality** | Statistical process control | `ProcessCapability` `ControlChart` `MeasurementAgreement` |
| **Resilience** | Retry & rate control | `Retry` `CircuitBreaker` `Bulkhead` `TokenBucketRateLimiter` |
| **Runtime** | Process/runtime introspection | `StartupTiming` `AppInfo` `MemoryPressure` |
| **Security** | Hashing, HMAC, secure random | `Hashing` `Hmac` `ConstantTime` `CryptoRandom` |
| **Signal** | Digital signal processing | `Convolution` `Goertzel` `MedianFilter` `SavitzkyGolay` |
| **Text** | Strings & matching | `CaseConverter` `Slug` `GlobMatcher` `JaroWinkler` `TemplateFormatter` |
| **Threading** | Async coordination | `AsyncLock` `KeyedLock` `ParallelUtils` `TaskExtensions` `AtomicCounters` |
| **Time** | Dates, durations, schedules | `DateRange` `HumanDuration` `CronSchedule` `BusinessDays` `UnixTime` |
| **Visualization** | Raster canvas, charts & PNG | `ImageBuffer` `PngWriter` `Colormap` `HeatMap` `LinePlot` |

## Find what you need

| I want to… | Reach for |
|---|---|
| Hash / checksum bytes | `Binary.Crc32`, `Binary.Fnv1a`, `Binary.Adler32` |
| Encode binary as text | `Binary.Base32` / `Base58` / `Base64Url` / `Hex` |
| Generate a unique id | `Identifiers.Ulid`, `NanoId`, `ShortGuid` |
| Retry / rate-limit a flaky call | `Resilience.Retry`, `CircuitBreaker`, `TokenBucketRateLimiter` |
| Cache with eviction / expiry | `Collections.LruCache`, `TtlCache` |
| Parse or format a duration | `Time.HumanDuration`, `Iso8601Duration` |
| Compute the next cron occurrence | `Time.CronSchedule` |
| Fuzzy-match strings ("did you mean") | `Text.JaroWinkler`, `LevenshteinDistance`, `NGramSimilarity` |
| Change naming case / make a slug | `Text.CaseConverter`, `Slug` |
| Match paths with wildcards | `Text.GlobMatcher` |
| Split / build a command line | `Text.CommandLineSplitter`, `CommandLineBuilder` |
| Run a child process (capture / timeout / kill) | `Process.ProcessRunner` (+ `WhichExe`, `ShellOpen`) |
| Log to sinks (console / file / memory) | `Logging.Logger` + `TextWriterSink` / `RollingMemorySink` |
| Collect metrics / time a block | `Diagnostics.MetricsRegistry`, `ScopedTimer` |
| Flatten / classify an exception | `Diagnostics.ExceptionUtils` |
| App version / memory / startup timing | `Runtime.AppInfo`, `MemoryPressure`, `StartupTiming` |
| Capture a bug-report environment snapshot | `Diagnostics.EnvironmentReport` |
| Map CSV rows ↔ typed objects | `IO.CsvLine`, `CsvBinder` |
| Write a file without torn writes | `IO.AtomicFile` |
| Zip / unzip safely (Zip-Slip guarded) | `IO.ZipUtils` |
| Merge config from many sources | `Configuration.ConfigLayers` + `ConfigBinder` |
| Bounded-concurrency async fan-out | `Threading.ParallelUtils.ForEachAsync` |
| Lock across `await` / per key | `Threading.AsyncLock`, `KeyedLock` |
| Reproducible random / sampling | `Numerics.DeterministicRandom`, `RandomUtils` |
| Streaming mean / variance / percentiles | `Numerics.RunningStatistics`, `Percentile` |
| Confidence intervals / bootstrap | `Numerics.ConfidenceInterval`, `Bootstrap` |
| Fit a curve / solve a linear system | `Numerics.Polynomial`, `LinearAlgebra` |
| Convert units | `Numerics.UnitConvert` |
| Shortest path / dependency order | `Graphs.ShortestPath`, `TopologicalSort` |
| Query overlapping intervals | `Intervals.IntervalTree`, `RangeSet` |
| Diff or deep-compare object graphs | `Objects.PropertyDiff`, `DeepEquals` |
| CIDR / IP address math | `Net.CidrRange`, `IpUtils` |
| Check a port / find a free one | `Net.PortCheck` |
| Line-oriented TCP request/response | `Net.TcpLineClient` |
| Resolve DNS (with timeout) / list interfaces | `Net.HostInfo` |
| Download a file (resume / checksum / retry) | `Net.HttpDownload` |
| Render a console table / bar / sparkline | `Cli.ConsoleTable`, `ProgressBar`, `Sparkline` |
| Validate method arguments | `Guards.Guard` |
| Draw a raster image / write a PNG | `Visualization.ImageBuffer`, `PngWriter` |
| Render a heat map / line plot | `Visualization.HeatMap`, `LinePlot` (+ `Colormap`) |
| Hash / HMAC / verify a token safely | `Security.Hashing`, `Hmac`, `ConstantTime` |
| Generate a secure token / password | `Security.CryptoRandom` |
| Hash a login password (with upgrade path) | `Security.PasswordHasher`, `KeyDerivation` |

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
      VarInt.cs             LEB128 var-length ints (unsigned + ZigZag signed)
    Cli/
      ConsoleTable.cs       box-drawing text table (alignment, padding)
      ProgressBar.cs        render a text progress bar (bar body / bracketed + percent)
      Sparkline.cs          one-line block-character mini chart of a series       aligned monospace text tables
    Collections/
      BinaryHeap.cs         binary-heap priority queue (custom comparer)
      Batch.cs              lazy fixed-size batching of any IEnumerable<T>
      CircularBuffer.cs     fixed-capacity ring buffer (overwrites oldest)
      BiMap.cs              bidirectional one-to-one map
      Aggregation.cs        group-by summaries (count/mean/min/max/stddev) + pivot
      BloomFilter.cs        probabilistic set membership (no false negatives)
      CartesianProduct.cs   lazy Cartesian product of sequences
      CountMinSketch.cs     approximate stream frequencies (never under-counts)
      SlidingWindow.cs      lazy fixed-size sliding windows (step 1)
      Combinatorics.cs      lazy permutations & combinations
      Counter.cs            multiset / frequency counter (most-common)
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
      TopN.cs               streaming N-largest via a bounded min-heap
      Trie.cs               prefix tree (contains / starts-with / with-prefix)
      TtlCache.cs           time-expiring cache (injectable clock)
    Configuration/
      ConfigLayers.cs       merge sources by priority into a flat key space (with provenance)
      ConfigBinder.cs       bind a flat key space to a typed object (nested/arrays/enums/durations)
    Control/
      PidController.cs      PID with clamping, anti-windup, derivative-on-measurement
      KalmanFilter1D.cs     scalar Kalman filter (predict/update, converging gain)
    Diagnostics/
      Benchmark.cs          micro-benchmark harness (warmup, stats, bytes/op, compare-to-baseline)
      MetricsRegistry.cs    named counters / gauges / timers with percentile snapshots (thread-safe)
      ScopedTimer.cs        time a using-block to a callback / metrics (injectable clock)
      ExceptionUtils.cs     root-cause / flatten / describe / transient-vs-permanent classify
      EnvironmentReport.cs  one-call bug-report snapshot (versions/OS/culture/uptime/env, secret-safe)
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
      Goertzel.cs           single-frequency magnitude/phase (cheaper than a full transform)
      MedianFilter.cs       sliding-window median (removes spikes, preserves edges)
      SavitzkyGolay.cs      polynomial smoothing (preserves peaks; polynomials pass through)
      ZeroCrossing.cs       zero-crossing indices with sub-sample linear interpolation
    Objects/
      ActivatorUtils.cs     construct by best-matching constructor; safe assembly scan-and-create
      AttributeCache.cs     cached custom-attribute lookups for types/members (thread-safe)
      DeepEquals.cs         structural graph equality (cycles, float tolerance, collections)
      FlattenObject.cs      object graph -> flat path->value dictionary
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
      DotEnv.cs             .env key=value parser (comments, quotes, export)
      FixedWidth.cs         fixed column-width record parse + format
      HexDump.cs            classic offset/hex/ascii hex dump
      LineReader.cs         enumerate lines with byte offsets (mixed line endings)
      SafeFileName.cs       sanitize a string into a safe filename
      StreamUtils.cs        copy-with-progress (+ cancellation) / read-exactly
      TempFile.cs           disposable temp file & directory scopes
      ZipUtils.cs           zip create/extract/list/read; extract guarded against Zip-Slip
    Numerics/
      Angle.cs              degree normalize / shortest-diff / lerp / deg-rad
      BaseConverter.cs      integer <-> radix string (base 2..36 or custom alphabet)
      Bootstrap.cs          resampling percentile confidence interval (any statistic, seeded)
      ChangePoint.cs        CUSUM + binary-segmentation level-shift detection
      ConfidenceInterval.cs  z-based mean CI + Wilson proportion CI
      DeterministicRandom.cs  seeded fixed-algorithm PRNG (xoshiro256**, stable across runtimes)
      Differentiation.cs    numerical derivative (uniform/uneven samples, function, Richardson)
      Distributions.cs      normal/exponential/uniform PDF/CDF/quantile (Acklam inverse-normal)
      EmpiricalDistribution.cs  empirical CDF step function (value<->percentile lookups)
      EngineeringNotation.cs  SI-prefix format + parse (yocto..yotta, round-trip)
      ExponentialMovingAverage.cs  EMA smoothing (alpha)
      Histogram.cs          fixed-bin histogram (under/overflow tracking)
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
      RandomUtils.cs        shuffle / sample-without-replacement / weighted / gaussian / exponential
      RootFinding.cs        bisection / Brent / Newton / secant (explicit convergence result)
      Rounding.cs           round to multiple / significant digits
      RunningStatistics.cs  Welford online mean/variance/stddev/min/max
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
      GlobMatcher.cs        glob matching (*, ?, [a-z], negation)
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
      TemplateFormatter.cs  {name} placeholder substitution ({{ }} escaping)
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
      TaskExtensions.cs     WithTimeout / WithCancellation / FireAndForget / WhenAllOrFirstException
    Time/
      BusinessDays.cs       business-day add/count (weekends + holidays)
      CronSchedule.cs       5-field cron parse + next-occurrence
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
      Colormap.cs           value->color (Grayscale/Hot/Cool/Viridis + custom stops)
      HeatMap.cs            render a 2-D grid via a colormap (cell size, range, NaN color)
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
    Security/
      Hashing.cs            SHA-256/384/512 (+ legacy MD5) over bytes/string/stream; hex/base64
      Hmac.cs               HMAC-SHA-256/384/512 with constant-time verify
      ConstantTime.cs       fixed-time equality for secrets (no timing leak)
      CryptoRandom.cs       CSPRNG bytes / URL-safe token / numeric code / password (unbiased)
      KeyDerivation.cs      PBKDF2-HMAC-SHA-256/512 (hand-rolled, identical on both TFMs) + salt
      PasswordHasher.cs     encoded PBKDF2 hash + constant-time verify + rehash-needed check
    Process/
      ProcessRunner.cs      run a child: deadlock-free capture, timeout + tree-kill, stdin/env/cancel
      WhichExe.cs           resolve an executable on PATH (+ PATHEXT on Windows)
      ShellOpen.cs          open a file/folder/URL with the default handler (per-OS)
    Logging/
      Log.cs                LogLevel, LogEvent, ILogSink (shared contracts)
      Logger.cs             thread-safe fan-out logger (levels, category, injectable clock)
      LogSinks.cs           DelegateSink / TextWriterSink (console) / RollingMemorySink
      FileSink.cs           file sink with size-based rotation + retention
      AsyncLogSink.cs       off-thread bounded-queue sink (built on ConcurrentPipeline)
      LogDecorators.cs      FilterSink / RouterSink / RateLimitedSink (composable sink wrappers)
      LogFormatters.cs      plain / compact / single-line-JSON line formatters
      StructuredTextFormatter.cs  configurable field order / delimiter / timestamp (delimited preset)
      ScopedContext.cs      ambient flow-local scope label folded into each event's category
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
      VarIntTests.cs
    Cli/
      ConsoleTableTests.cs
      ProgressBarTests.cs
      SparklineTests.cs
    Collections/
      AggregationTests.cs
      BatchTests.cs, BatchHardeningTests.cs
      BiMapTests.cs
      BinaryHeapTests.cs
      BitSetTests.cs
      BloomFilterTests.cs
      CircularBufferTests.cs
      CartesianProductTests.cs
      CombinatoricsTests.cs
      CounterTests.cs
      CountMinSketchTests.cs
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
      GoertzelTests.cs
      MedianFilterTests.cs
      SavitzkyGolayTests.cs
      ZeroCrossingTests.cs
    Objects/
      ActivatorUtilsTests.cs
      AttributeCacheTests.cs
      DeepEqualsTests.cs
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
    Numerics/
      AngleTests.cs
      BaseConverterTests.cs
      BootstrapTests.cs
      ChangePointTests.cs
      ConfidenceIntervalTests.cs
      CorrelationTests.cs
      DeterministicRandomTests.cs
      DifferentiationTests.cs
      DistributionsTests.cs
      EmpiricalDistributionTests.cs
      EngineeringNotationTests.cs
      ExponentialMovingAverageTests.cs
      FractionTests.cs
      HistogramTests.cs
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
      RomanNumeralsTests.cs
      SlugTests.cs
      SoundexTests.cs
      TemplateFormatterTests.cs
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
      TaskExtensionsTests.cs
    Time/
      BusinessDaysTests.cs
      CronScheduleTests.cs
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
      ColormapTests.cs
      HeatMapTests.cs
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
    Security/
      HashingTests.cs
      HmacTests.cs
      ConstantTimeTests.cs
      CryptoRandomTests.cs
      KeyDerivationTests.cs
      PasswordHasherTests.cs
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
scripts/
  build.ps1                 build wrapper (disables the persistent build server)
  test.ps1                  build once + launch the test exe directly
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
