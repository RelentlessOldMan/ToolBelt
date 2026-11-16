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
| R93-1 | P2 | 14 | confirmed | `FlattenObject.Flatten` checks its 2^29-character key cap only when an item is popped, after `Walk` has built every child's full path: one 4,000-character key above a 1M-item array allocated 7.8 GB before the documented InvalidOperationException (more items or a longer key gives OutOfMemoryException). Count each child's key length as it is made, or join parent path and token on pop. | review r93 |
| R93-2 | P3 | 14 | confirmed | `Graph.Edges` and `Neighbors()` return the internal `List<>`s; a caller that casts and changes one puts `Edges`/`EdgeCount` out of step with the adjacency lists (the class round 90 fixed in DataTableLite.Columns). Return read-only wrappers. | review r93 |
| R93-3 | P2 | 14 | suspected | GridPathfinding's MinHeap ties keys within 1e-12 relative, which is not a strict ordering; A* on near-tied keys might close a node a few ulps too high. | review r93 |
| R93-4 | P3 | 14 | suspected | `DeepEquals` `FloatTolerance` applies only when both values are double or both float; double vs float and decimal fall back to exact `Equals` (undocumented). | review r93 |
| R93-5 | P3 | 14 | suspected | `UnflattenObject` rebuilds a root list flattened to `[0]`, `[1]` as a dictionary `{"0":..,"1":..}`, not a list. | review r93 |
