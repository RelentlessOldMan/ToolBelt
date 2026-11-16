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
| R91-1 | P1 | 13 | confirmed | `CronScheduler`: after the clock goes back (a correction after running fast), no job runs until real time reaches the old `NextRun` (a year, for a clock a year fast). Severe. | review r91 |
| R91-2 | P1 | 13 | confirmed | `CidrRange.Contains` never matches an IPv4-mapped IPv6 address (`::ffff:a.b.c.d`, how dual-mode sockets report IPv4 peers) against an IPv4 block: IPv4 deny lists are bypassed. Severe. | review r91 |
| R91-3 | P1 | 13 | confirmed | `TcpLineClient`: a valid character split across reads is lost when a bad byte follows later in the same read (the slow pass resets the decoder's carry). Severe. | review r91 |
| R91-4 | P1 | 13 | confirmed | `TcpLineClient`: the skip rule after a bad byte assumes UTF-8; with UTF-16 one lone surrogate misaligns every later line. Severe. | review r91 |
| R91-5 | P2 | 13 | confirmed | `TcpLineClient`: a line with N bad bytes throws N DecoderFallbackExceptions, not one; MaxLineLength doesn't bound them. | review r91 |
| R91-6 | P2 | 13 | confirmed | `CronScheduler`: a clock that throws once during Add after Start, or one reading past the last occurrence, leaves the job's NextRun null for good. | review r91 |
| R91-7 | P2 | 13 | confirmed | `HttpDownload` writes a body longer than its declared total to disk without limit, then retries (and keeps it under Resume). | review r91 |
| R91-8 | P2 | 13 | confirmed | `SecretsFile.FromBytes`: authenticated but malformed contents throw ArgumentOutOfRangeException/FormatException, not CryptographicException; empty and duplicate keys are taken. | review r91 |
| R91-9 | P2 | 13 | confirmed | `CidrRange.Parse(null)` throws FormatException, not ArgumentNullException. | review r91 |
| R91-10 | P2 | 13 | confirmed | `CronSchedule` searches only 8 years: valid `0 0 29 2 */6` (16 years apart) and `*/7` (28) throw "no occurrence". | review r91 |
| R91-11 | P2 | 13 | confirmed | `Iso8601Duration.Parse` rejects the ISO decimal comma (`PT1,5S`). | review r91 |
