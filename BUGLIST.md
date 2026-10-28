# Bug list

A running list of every suspected and confirmed bug that is not yet fixed and released, so none is dropped between
rounds. Every confirmed bug gets fixed. Priority only orders the work: **P1** wrong results, crashes, hangs or security
issues on ordinary inputs; **P2** the same on extreme inputs (overflow, subnormals, huge sizes); **P3** naming, messages,
docs. A bug leaves this list when its fix is released; the round log in REVIEW-PLAN.md keeps the history.

Status: `suspected` (not yet reproduced), `confirmed` (failing test or probe), `fixed` (in the working tree, unreleased).

| ID | Pri | Section | Status | Summary | Found by |
|----|-----|---------|--------|---------|----------|
| B7 | P2 | 3 Statistics | fixed | `FCdf(0.5, 5, 1e300)` = 0.22350492887670023 against 0.22350492887667729 (1.0e-13 relative): with a small and b huge the prefix's p·ln x and p·ln(a + b) were each ~1700 and cancelled. Now merged as p·ln(x·(a + b)) in BetaLnFront. All four copies. | round 46 |
| B8 | P2 | 3 Statistics | fixed | `RegularizedGammaP(1e-300, 1e-10)` = 0.9999999999999791, true 1 − 2.3e-298 (94 eps, condition number ~0): with a < 1 and P > ½, P came from the series, whose prefix carries lnΓ(a)·eps (lnΓ(1e-300) = 690). Now P = 1 − Q, from the accurate small-shape Q. All four copies. | differential harness (round 47) |
| B9 | P2 | 3 Statistics | fixed | Tiny-shape incomplete beta and gamma. (a) With b < 1 above the split (or a < 1 below it) the small tail was 1 − the complement: `I_0.7(1, 1e-20)` = 3.6e-15 against 1.2e-20, `I_0.99(50, 1e-300)` = 2.8e-14 against 5.6e-301. Now summed directly (SmallShapeTail: series, or series + BGRAT). (b) The prefix 1/B(a, b) and 1/Γ(a) carried ln(min shape) inside the exponential (ln 1e-300 = −690.8 → ~250 eps): `I_0.01(1e-300, 50)` = 1 − 2.8e-14, `Q(1e-300, 2)` 2.2e-14 off, `ChiSquarePdf(1, 2e-300)` 2.4e-14 off. Now the factor is applied outside (GammaFront, LnPBeta). All four copies. | differential harness (round 47) |
