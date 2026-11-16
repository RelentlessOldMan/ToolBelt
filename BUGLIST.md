# Bug list

A running list of every suspected and confirmed bug that is not yet fixed and released, so none is dropped between
rounds. Every confirmed bug gets fixed. Priority only orders the work: **P1** wrong results, crashes, hangs or security
issues on ordinary inputs; **P2** the same on extreme inputs (overflow, subnormals, huge sizes); **P3** naming, messages,
docs. A bug leaves this list when its fix is released; the round log in REVIEW-PLAN.md keeps the history.

Status: `suspected` (not yet reproduced), `confirmed` (failing test or probe), `fixed` (in the working tree, unreleased).

| ID | Pri | Section | Status | Summary | Found by |
|----|-----|---------|--------|---------|----------|
| R69-1 | P2 | 12 | confirmed | `AsyncLogSink.Dispose` has no drain timeout: an inner sink that hangs hangs Dispose (open since round 29). | review r29, r69 |
| R69-2 | P2 | 12 | suspected | `DiskSpace` reads `IsReady` on every drive before matching the path, so an offline drive could stall the call. | review r69 |
| R69-3 | P2 | 12 | suspected | After Ctrl+C, `Prompt` silently takes the default (`ReadLine()` returns null on Windows). | review r69 |
| R69-4 | P2 | 12 | suspected | A `Logger` clock that throws escapes `Log`. | review r69 |
| R69-5 | P2 | 12 | suspected | `Benchmark` with `int.MaxValue` iterations throws OutOfMemoryException, not an argument error. | review r69 |
| R81-1 | P2 | 10 | suspected | `ProcessRunner.KillTree` reports the kill as done when `Kill` threw (access denied, say) with the child still running. | review r81 |
| R81-2 | P2 | 10 | suspected | After a timeout, a grandchild holding the inherited pipes keeps both reader threads blocked until it exits (a thread pair per run, worst on netstandard2.0 with no tree kill). | review r81 |
| R90-1 | P1 | 9 | confirmed | `MultisetEqualityComparer.GetHashCode` sums raw element hashes: int multisets with one sum collide (280,840 3-subsets gave 352 hashes, a HashSet took 29 s). Severe. | review r90 |
| R90-2 | P1 | 9 | confirmed | `SequenceEqualityComparer.GetHashCode` is `hash*31 + h`: `[i, -31i]` all collide (40,000 keys, 30 s). | review r90 |
| R90-3 | P1 | 9 | confirmed | `DataTableLite.Distinct` uses the same row hash (40,000 rows of `(i, -31i)`, 8 s). | review r90 |
| R90-4 | P2 | 9 | confirmed | .NET Framework: equal decimals (1.0m, 1.00m) and NaNs with other payloads hash differently, so DataTableLite GroupBy/Distinct/InnerJoin, Summarize, Pivot, Counter, OrderedSet, OrderedDictionary, BiMap and the other hash-keyed types split one key in two. | review r90 |
| R90-5 | P2 | 9 | confirmed | `DataTableLite.Columns` returns the internal array; a cast-and-write desyncs the name index. | review r90 |
| R90-6 | P2 | 9 | confirmed | `CountMinSketch` depth past Array.MaxLength throws OutOfMemoryException, not ArgumentOutOfRangeException. | review r90 |
| R90-7 | P2 | 9 | confirmed | `BitSet.EnumerateSetBits` takes its version at the first MoveNext, so a change after GetEnumerator isn't caught. | review r90 |
| R90-8 | P2 | 9 | confirmed | `TtlCache.Remove` returns true for an expired, unpruned entry. | review r90 |
| R90-9 | P2 | 9 | confirmed | `TtlCache` saturates expiry to "never" when the local (offset) time is near year 9999 though the UTC expiry fits. | review r90 |
| R90-10 | P2 | 9 | confirmed | Summarize, Pivot and Stratified throw ArgumentNullException("key") when a selector returns null. | review r90 |
| R90-11 | P2 | 9 | confirmed | `OrderedDictionary` indexer set and `LruCache.Set` replace the stored key instance (Dictionary keeps the first). | review r90 |
| R90-12 | P2 | 9 | confirmed | .NET Framework: `DataTableLite.ToString` prints doubles with 15 digits, so 0.30000000000000004 shows as 0.3. | review r90 |
| R90-13 | P2 | 9 | confirmed | `TtlCache` never lowers its prune threshold after Clear, Remove or Prune, so write-once keys pile up to the old peak. | review r90 |
| R90-14 | P2 | 9 | confirmed | `Trie` keeps a dictionary per node (about 250 bytes per character); an Add that runs out of memory leaves a partial branch that StartsWith reports. | review r90 |
| R90-15 | P2 | 9 | confirmed | `Aggregation.Summarize` keeps every value of every group (20M items, 384 MB; a group can't pass 2^31 values). | review r90 |
