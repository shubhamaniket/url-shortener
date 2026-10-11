# ADR-006: No deduplication or idempotency keys for link creation (for now)

- **Status**: Accepted (revisit when clients that retry exist)
- **Date**: 2026-10-11 (feature 001 assumption; reconsidered and left out in feature 003)
- **Related**: feature 001 FR-008 · feature 003 scenario doc §2.5 · ADR-009

## Context

The same long URL may be shortened more than once, and a client that retries a timed-out create could
produce two links. Two common remedies exist: deduplicate by URL, or accept an `Idempotency-Key`
header and return the first result for repeated keys.

## Decision

**Neither.** Every successful create produces a new link (feature 001 FR-008). Creation is protected
from abuse by the per-client create budget (ADR-009) instead.

## Alternatives considered

| Alternative | Why rejected (now) |
|---|---|
| Deduplicate by URL (same URL → same code) | Different people shortening the same URL would share one link and its statistics, expiry and alias; leaks that someone else shortened it |
| `Idempotency-Key` header | Correct for retries, but needs key storage with expiry, a response cache, conflict rules for a reused key with a different body, and tests; no client is known to retry |
| Unique index on URL | Same problems as deduplication, enforced by the database |

## Consequences

- Simple, predictable behaviour: one request, one link.
- A client retry after a lost response can create a duplicate link: harmless apart from using a code
  (about 7.8 × 10¹⁰ available, ADR-002).
- If retrying clients appear, add `Idempotency-Key` (keys stored for 24 h with the original response)
  as its own feature.