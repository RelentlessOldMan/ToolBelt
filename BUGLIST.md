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
| R84-C1 | P1 | 6 | fixed | `ImageBuffer.DrawLineClipped` compared inexact edge crossings with exact ends: a segment touching the rectangle only at an endpoint on an edge drew nothing, one ending 5e-24 outside drew a pixel. Each end's side is now decided exactly (test in `BinaryImagesReview84Tests`). | review r84 |
| R84-C2 | P3 | 6 | fixed | `SvgUtils.EstimateTextWidth` counted a flag (two regional indicators) as two em (test in `BinaryImagesReview84Tests`). | review r84 |
| R84-R1 | P1 | 6 | fixed (part) | **Severe.** `PngReader.Decode(Stream)` read up to 2 GB before rejecting non-PNG input ("GIF89a" + zeros: 92 s, 4.3 GB allocated). Fixed: the signature, each chunk's type letters and CRC, and IHDR first are checked as they arrive (test in `BinaryImagesReview84Tests`). Still open: one chunk declaring a huge length (a 2 GB tEXt after a valid IHDR) is read whole before its CRC fails; cap the IDAT total by the size IHDR implies and bound ancillary chunks. | review r84 |
| R84-R2 | P2 | 6 | confirmed | Base32, CrockfordBase32, Base85, Base64Url and QuotedPrintable throw OutOfMemoryException (after up to 80 s, some pre-allocating 2 GB) for output past the longest string; check the exact (or worst-case) length as a long first and throw ArgumentException(data), as Hex does. | review r84 |
| R84-R3 | P2 | 6 | confirmed | `PngReader` uses far more memory than its doc says: image + 4.7× file (byte[]) and + 9.5× (Stream), from doubling MemoryStreams and ToArray. Pre-size or inflate in place, or correct the doc. | review r84 |
| R84-R4 | P2 | 6 | confirmed | `PngReader` accepts forbidden chunks: tRNS in colour types 4 and 6, tRNS of the wrong length (7 for RGB, 1 for grey), more tRNS entries than PLTE, a 256-entry PLTE at bit depth 1. Trailing compressed bytes after the zlib checksum are reported as a checksum mismatch. | review r84 |
| R84-R5 | P2 | 6 | confirmed | `Bmp.Decode` turns an out-of-range palette index into black (PngReader throws) and accepts a pixel offset inside the palette. | review r84 |
| R84-R6 | P3 | 6 | confirmed | `SvgUtils.EstimateTextWidth` differs between net48 and net8 for 440 code points (zero-width detection uses the framework's Unicode tables); embed a fixed range table, as IsWide does. | review r84 |
| R84-R7 | P2 | 6 | confirmed | `Base58` has no length limit and is slower than documented (net8: 100 KB encode 83 s, decode 32 s). Divide by a uint 58^5, decode in chunks, document a cap or add a maxLength overload. | review r84 |
| R84-R8 | P3 | 6 | suspected | `PngReader.Load` and `Bmp.Load` would throw IOException (File.ReadAllBytes) for files over 2 GB, where Decode(Stream) throws InvalidDataException. | review r84 |
