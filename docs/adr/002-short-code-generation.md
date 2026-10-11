# ADR-002: Random Base62 codes, uniqueness enforced by the database

- **Status**: Accepted
- **Date**: 2026-10-10 (feature 001, research R1–R3)
- **Deciders**: Engineer (approved the case-rule trade-off on 2026-10-10)
- **Related**: feature 001 FR-006–FR-008, FR-012, FR-016 · ADR-001

## Context

Codes must be short, not guessable or sequential (no enumeration), unique, and safe under concurrent
creation. Custom aliases are unique ignoring letter case; generated codes are case-sensitive when
followed. The same URL may be shortened many times, each with its own code.

## Decision

1. **7 random Base62 characters** from `RandomNumberGenerator.GetString` (cryptographically secure,
   unbiased; `System.Random` is never used for codes).
2. **The database decides uniqueness**: one unique index on `NormalizedCode` (the lower-cased
   code). Creation inserts directly; a unique-constraint violation (SQLite extended error **2067**)
   means "taken". Generated codes retry with a new code, up to **5 attempts**, then fail with 500;
   a taken custom alias is a **409**. The rejected entity is detached so the retry does not resend it.
3. **Case rules from one index**: lookup is one indexed query on the lower-cased input; a custom
   alias matches in any case, a generated code only in exact case.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Counter + Base62 | Sequential, so every link is enumerable |
| Hash of the URL | Truncated hashes collide, and the same URL must get different codes |
| Check-then-insert | Races under concurrency; two requests both see "free" |
| Case-sensitive index + separate alias index | Alias-vs-generated collisions would need application checks that race |
| SQLite error code 19 for duplicates (first plan) | Code 19 is every constraint (e.g. NOT NULL); 2067 is the unique violation only |

## Consequences

- Correct under concurrency by construction: 10 parallel claims of one alias give exactly one 201
  and nine 409 (test, 8/8 runs).
- Two generated codes differing only in case cannot coexist: the distinct space is about 7.8 × 10¹⁰
  instead of 3.5 × 10¹² — far beyond prototype scale (at 1 M links, about 1 collision in 78 000
  attempts, retried invisibly).
- Feature 002 reuses the same index to reclaim expired aliases safely (conditional delete + insert).