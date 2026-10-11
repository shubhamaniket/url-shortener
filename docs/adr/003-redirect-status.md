# ADR-003: 302 redirects with `Cache-Control: no-store`

- **Status**: Accepted
- **Date**: 2026-10-10 (feature 001, research R8)
- **Related**: feature 001 FR-013 · feature 002 (410 Gone, reclaimable aliases) · ADR-005

## Context

Following a short link returns an HTTP redirect. A permanent redirect (301/308) is cached by browsers
and proxies indefinitely; a temporary one (302/307) is not, by default. The service counts clicks,
lets links expire (feature 002), and lets an expired alias be reclaimed for a new destination.

## Decision

`GET /{code}` answers **302 Found** with `Location` and **`Cache-Control: no-store`**. Expired links
answer **410 Gone** (ProblemDetails, also `no-store`); unknown codes 404.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| 301 Moved Permanently | Browsers cache it: repeat visits never reach the service (clicks lost), and an expired or reclaimed link would keep sending users to the old destination |
| 308 Permanent Redirect | Same caching problem as 301 |
| 307 Temporary Redirect | Equivalent for GET; 302 is the conventional, universally understood choice for shorteners |

## Consequences

- Every visit reaches the service, so counting, expiry and reclaiming work as specified.
- One extra round trip per repeat visit compared with a cached 301; redirects take 0.5–0.9 ms p95
  server-side, so the cost is network latency only.
- Search engines treat 302 targets as temporary; acceptable for a link shortener.