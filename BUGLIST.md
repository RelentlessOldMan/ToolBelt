# Bug list

A running list of every suspected and confirmed bug that is not yet fixed and released, so none is dropped between
rounds. Every confirmed bug gets fixed. Priority only orders the work: **P1** wrong results, crashes, hangs or security
issues on ordinary inputs; **P2** the same on extreme inputs (overflow, subnormals, huge sizes); **P3** naming, messages,
docs. A bug leaves this list when its fix is released; the round log in REVIEW-PLAN.md keeps the history.

Status: `suspected` (not yet reproduced), `confirmed` (failing test or probe), `fixed` (in the working tree, unreleased).

| ID | Pri | Section | Status | Summary | Found by |
|----|-----|---------|--------|---------|----------|
| 52-1 | P3 | 17 / net48 round | confirmed | The Visualization test folder doesn't compile on .NET Framework 4.8 (span `AsSpan`/`SequenceEqual`, `ZipArchive` reference, `MemoryStream.Write(byte[])`, two-argument `FirstOrDefault`), so `net48.sh Visualization` can't run. Round 54 found the same in Threading, Collections, Logging and Time (`DateTimeOffset.UnixEpoch`, `System.Index`, `TcpListener` in `using`, `SecretsFile`/`AuthenticatedEncryption`, `HttpClient` reference) and IO (`System.Formats.Tar`, `System.Text.Json`). Fix in the whole-suite net48 round. | round 52, 54 net48 runs |
| 54-1 | P2 | 15 / net48 round | confirmed | On .NET Framework (the netstandard2.0 build), `Benchmark.AllocatedBytesPerOperation` measures with `GC.GetTotalMemory(false)`, which drops at every collection: `BenchmarkTests.MeasuresAllocation` gets 987 B/op for a 4096-byte allocation. | round 54 net48 run |
| 52-2 | P3 | 13 | suspected | `SecurityNetTimeReviewTests.Http_CallerCancellation_IsNotRetried` failed once in a reviewer's one-core checked-build run; it passed in the round 52 checked build. Probably timing; look at it in the cancellation round. | round 52 reviewer |
| 53-1 | P2 | 1 | confirmed | `PidController` in derivative-on-error mode: after an update whose error overflowed (stored `_lastError` = ±∞), the next output's scaled retry is still ±∞ or NaN instead of the true finite value. | round 53 fix work |
| 53-2 | P3 | 1 | confirmed | `IirFilter` root pairing throws "The design is numerically degenerate (a root is not finite)" with no parameter name. | round 53 fix work |
