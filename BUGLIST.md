# Bug list

A running list of every suspected and confirmed bug that is not yet fixed and released, so none is dropped between
rounds. Every confirmed bug gets fixed. Priority only orders the work: **P1** wrong results, crashes, hangs or security
issues on ordinary inputs; **P2** the same on extreme inputs (overflow, subnormals, huge sizes); **P3** naming, messages,
docs. A bug leaves this list when its fix is released; the round log in REVIEW-PLAN.md keeps the history.

Status: `suspected` (not yet reproduced), `confirmed` (failing test or probe), `fixed` (in the working tree, unreleased).

| ID | Pri | Section | Status | Summary | Found by |
|----|-----|---------|--------|---------|----------|
| B7 | P2 | 3 Statistics | confirmed | `FCdf(0.5, 5, 1e300)` = 0.22350492887670023 against 0.22350492887667729 (1.0e-13 relative; unchanged by round 46): with a small and b huge the prefix a·ln x + b·ln(1 − x) − ln B(a, b) cancels terms of ~1700, losing ~eps·1700. Likely any small a with b ≫ 1 at tiny x. Next: measure the envelope in the differential harness, then use the gamma-limit (BGRAT with roles swapped) where it is accurate. | round 46 |
