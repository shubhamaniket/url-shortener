# Risks, Trade-offs and Limitations

What can go wrong, what was done about it, how that was validated, and what was knowingly accepted.
Figures come from the scenario documents and test runs on 2026-10-11.

## Risk register

| Risk / failure scenario | Impact | Mitigation (built) | Validation |
|---|---|---|---|
| Malicious destination (`javascript:`, `data:`, credentials disguising the host, e.g. `https://google.com@evil.example`) | Users harmed through our domain | http/https allowlist after parsing; URLs with user info rejected; the normalized URL is what gets stored and redirected to | 32 unit tests from the data model's malicious examples |
| Redirect loop (short URL pointing at the service) | Infinite redirects | Destinations on the service's own host rejected (any case or port) | Unit + integration tests |
| Host-header injection into returned short URLs | Phishing links on someone else's domain | Short URLs built only from the configured base URL; the request `Host` is never used | Integration test with a forged `Host` header |
| Guessable or enumerable codes | Private links discovered | 7 cryptographically random Base62 characters; route constraint; redirect budget per client | Generator tests; rate-limit tests (404s count) |
| Code collision | Wrong redirect or failed create | Unique index decides; generated codes retry up to 5 times ([ADR-002](adr/002-short-code-generation.md)) | Unit tests with forced collisions and retry exhaustion |
| Alias race (two concurrent creates or reclaims) | Duplicate or 500 | Database unique constraint → 409; reclaim is a conditional delete + insert in one immediate transaction | 10-way race tests: exactly one winner, no 500 (8/8 runs) |
| Analytics failure or slowness breaks the redirect | Outage of the core feature | Redirect only enqueues in memory; storage in a background writer; failures caught and logged ([ADR-005](adr/005-async-click-recording.md)) | Redirects still 302 with storage failing on every batch (50/50) and with the writer blocked (p95 0.35–0.73 ms in-process) |
| Analytics queue overload | Memory growth | Bounded queue (10 000), oldest dropped and counted, drops logged | Unit tests; blocked-writer integration test ("39 queued clicks were dropped") |
| Abuse: mass creation, floods, scraping | Resource exhaustion, degraded service for others | Per-client budgets (create 10/min, redirect 120/min with bursts of 60, read 60/min), 429 + `Retry-After` ([ADR-009](adr/009-rate-limiting.md)) | 24 integration tests; quickstart on the running app |
| Rate-limit bypass by spoofed `X-Forwarded-For` | Limits useless | Forwarded headers ignored; client = connection address | Integration test |
| PII exposure | Privacy / compliance | No IP stored; referrer reduced to host; user agent reduced to one of six families; logs carry codes and counts only | Unit tests on the reductions; stored-row test (no fragment of raw headers); log tests (no addresses, no click data) |
| Expired link still reachable through caches | Users sent to retired destinations | 302 and 410 both `Cache-Control: no-store` ([ADR-003](adr/003-redirect-status.md)) | Integration tests |
| Reclaimed alias attributed old history | Misleading statistics | Reclaim deletes the old link and its events in the same transaction; queued clicks for the deleted link are skipped by the writer | Reclaim and writer tests |
| Schema change breaks existing data | Data loss | EF migrations only, additive change, sign-off | Migration test on a seeded database; feature 001 database migrated by hand (T018); both rollback paths tested |
| Unhandled errors leak internals | Information disclosure | Global handler → ProblemDetails with `traceId`, no stack traces outside Development | Integration tests |
| SQLite write contention | Slow writes at scale | Batched click writes; documented migration path ([ADR-004](adr/004-sqlite.md)) | Documented limitation |
| AI-generated insecure or incorrect code | Defects | Spec-first process, one task at a time with engineer review and sign-offs, tests + mutation checks, analyzers as errors, CodeQL, AI usage log ([ADR-007](adr/007-spec-driven-development.md)) | CI gates; correction tables in the scenario docs |
| Vulnerable dependencies | Supply-chain risk | Minimal dependencies, each justified; NuGet and npm vulnerability gates in CI; Dependabot (NuGet, npm, Actions, Docker) | CI steps (0 findings) |
| Destination blocklist | Known-bad domains shortened | **Not built** (feature 003 scope decision): a static list gives false assurance; a maintained reputation feed is a future dependency | Documented in [scenario 03](scenarios/03-ambiguous.md#25-scope-chosen-and-what-was-deliberately-left-out) |

## Failure scenarios

| Scenario | What happens | Recovery |
|---|---|---|
| Database unavailable | Creates, lookups and redirects fail with 500 ProblemDetails; `/health` returns 503; queued clicks fail to store and are logged and dropped | Restore the database; the service needs no restart |
| Click storage failing or slow | Redirects unaffected; failed batches logged and discarded; queue fills, then drops oldest (logged) | Automatic once storage recovers |
| Process stops gracefully | The writer keeps storing queued clicks for up to 5 s, then logs how many were not stored | — |
| Process crashes | Clicks still in memory are lost (not logged) | Accepted (at-most-once) |
| One client floods the service | That client gets 429 with `Retry-After`; others unaffected; one warning per rejection in the logs | Automatic per window |
| Invalid configuration | Service refuses to start with a message naming the setting | Fix the setting |
| CI flake | A real defect hid behind one: the shutdown-loss log was split and double-counted (feature 002, T022) | Deterministic test first, then the fix |

## Trade-offs

| Chose | Over | Because | Cost |
|---|---|---|---|
| Fast, failure-proof redirects | Exact, durable analytics | The redirect is the product; analytics is secondary | At-most-once clicks; counts eventually consistent (≤ 5 s) |
| Stored click total + events | Deriving the total from events | Preserves pre-analytics totals | Breakdowns of old links can sum to less than the total ([ADR-008](adr/008-stored-click-count.md)) |
| One unique index for all case rules | Full case-sensitive code space | Atomic correctness under concurrency | ~7.8 × 10¹⁰ instead of 3.5 × 10¹² distinct generated codes |
| Reclaimable expired aliases (engineer's override) | Permanently reserved aliases | An alias that never frees up defeats expiry | A reclaimed alias can send old traffic to a new owner |
| SQLite, one container | A database server and orchestration | Zero setup for reviewers | Single instance, single writer |
| Built-in features (rate limiter, background service, channels) | Third-party packages | Constitution VI: fewer dependencies | Less out-of-the-box tuning |
| Rate limiting only, for the ambiguous scenario | Health split, blocklist, idempotency, caching | Engineer's scope decision under time pressure; each left-out item documented | Those protections are not in place |
| Coarse tasks in later phases | Fine-grained tasks | Fit the deadline without dropping review or tests | Fewer, larger reviews |

## Limitations

- **Runs locally, not deployed.** The production design (AWS) is in [deployment.md](deployment.md).
- **Single instance**: in-memory click queue and rate-limit budgets; SQLite single writer.
- **At-most-once analytics**; statistics are UTC days only, kept indefinitely (no retention policy).
- **Client identity is the IP address**: users behind one NAT share a budget; behind a reverse proxy,
  trusted-proxy configuration is required first.
- **No accounts**: anyone with a code sees its statistics (aggregates only).
- **No deduplication or idempotency keys** ([ADR-006](adr/006-idempotency.md)).
- **No destination blocklist or malware scanning.**
- **Health check is a single endpoint** (no liveness/readiness split).
- **Static UI files are served uncompressed** by the API (about 151 KB; 48 KB if compressed).
- **Redirect rejection logs show the raw route template** instead of `/{code}` (cosmetic).

## Assumptions

- Prototype scale on one machine; legitimate users create a few links a minute.
- Losing a few clicks under failure is acceptable; failing a redirect is not (feature 001 Q1).
- Statistics may be public to anyone who knows the code (feature 001 Q2).
- Expiry times always carry a time zone; days in statistics are UTC calendar days.
- Clients connect directly or through a proxy that will be configured explicitly.
- Modern evergreen browsers for the web UI.
