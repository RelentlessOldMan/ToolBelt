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
| R93-3 | P2 | 14 | suspected | GridPathfinding's MinHeap ties keys within 1e-12 relative, which is not a strict ordering; A* on near-tied keys might close a node a few ulps too high. Not reproduced: steps cost 1 or √2, so distinct f-values differ by about 1/(3n) for n-step paths, far wider than the window at any grid that fits in memory. | review r93 |
| R94-1 | P2 | 8 | suspected | `TarUtils.CreateFromDirectory` excludes the archive by its path string, so an archive inside the source reached through a short (8.3) name or a junction is not recognised: the run fails on a sharing violation (or packs an old copy of the archive). Compare by file identity. | review r94 |
| R94-2 | P3 | 8 | suspected | `TarUtils.CreateFromDirectory` leaves out a folder whose only file is the archive being written. | review r94 |
| R94-3 | P3 | 8 | suspected | `AtomicFile` with a backup fails on names of 252 characters or more ("<name>.bak" passes the 255-character limit); the original is kept, but the write is refused. Undocumented. | review r94 |
| R94-4 | P3 | 8 | suspected | A zip made on Unix with a directory entry ending in a backslash is extracted as a file; on Unix, extraction treats a backslash as a separator while creation keeps it as a name character. | review r94 |
