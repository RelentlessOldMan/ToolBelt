# Bug list

A running list of every suspected and confirmed bug that is not yet fixed and released, so none is dropped between
rounds. Every confirmed bug gets fixed. Priority only orders the work: **P1** wrong results, crashes, hangs or security
issues on ordinary inputs; **P2** the same on extreme inputs (overflow, subnormals, huge sizes); **P3** naming, messages,
docs. A bug leaves this list when its fix is released; the round log in REVIEW-PLAN.md keeps the history.

Status: `suspected` (not yet reproduced), `confirmed` (failing test or probe), `fixed` (in the working tree, unreleased).

| ID | Pri | Section | Status | Summary | Found by |
|----|-----|---------|--------|---------|----------|
| 56-1 | P3 | 15 | confirmed | `Thread.Sleep` in library code, against the house rules: Windows `ResourceSampler.Measure`, `ClipboardUtils`' open retry and `SingleInstanceApp`'s wait for a starting primary (the last two found in round 57). DirectoryUtils' part was fixed in round 58 (a cancellable wait). All are synchronous APIs; decide between an injectable delay and documenting the exception. | round 56 review |
