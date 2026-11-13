# Bug list

A running list of every suspected and confirmed bug that is not yet fixed and released, so none is dropped between
rounds. Every confirmed bug gets fixed. Priority only orders the work: **P1** wrong results, crashes, hangs or security
issues on ordinary inputs; **P2** the same on extreme inputs (overflow, subnormals, huge sizes); **P3** naming, messages,
docs. A bug leaves this list when its fix is released; the round log in REVIEW-PLAN.md keeps the history.

Status: `suspected` (not yet reproduced), `confirmed` (failing test or probe), `fixed` (in the working tree, unreleased).

| ID | Pri | Section | Status | Summary | Found by |
|----|-----|---------|--------|---------|----------|
| 59-1 | P2 | 14 | suspected | `TypeUtils.FriendlyName` recurses per generic argument; a very deeply nested generic type might overflow the stack. Probe whether the runtime can even build one deep enough. | round 59 review |
| 59-2 | P3 | 14 | suspected | `FlattenObject.Flatten(5)` gives `{"": 5}`, which `UnflattenObject` refuses as an empty path; document the round-trip limit or accept it. | round 59 review |
| 59-3 | P2 | 14 | suspected | A stable `IEnumerable` property that yields new objects on each enumeration is classed Stable; a chain through it could still run to the depth caps. | round 59 testing |
| 56-1 | P3 | 15 | confirmed | `Thread.Sleep` in library code, against the house rules: Windows `ResourceSampler.Measure`, `ClipboardUtils`' open retry and `SingleInstanceApp`'s wait for a starting primary (the last two found in round 57). DirectoryUtils' part was fixed in round 58 (a cancellable wait). All are synchronous APIs; decide between an injectable delay and documenting the exception. | round 56 review |
