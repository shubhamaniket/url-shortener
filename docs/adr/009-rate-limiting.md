# ADR-009: Per-client rate limiting with the built-in ASP.NET Core limiter

- **Status**: Accepted
- **Date**: 2026-10-11
- **Deciders**: Engineer (scope decision for the ambiguous requirement; clarifications Q1–Q3);
  implemented in feature 003 T002–T004
- **Related**: [docs/scenarios/03-ambiguous.md](../scenarios/03-ambiguous.md) · spec FR-001–FR-014 ·
  research R1–R8 · constitution Principle IV ("Public endpoints MUST be rate limited")

## Context

The requirement was only "The service should be reliable and protected from abuse." After
interpreting it (scenario doc §2), the engineer scoped the answer to one capability: stop a single
client from degrading the service for others. The service is public, has no accounts or API keys, runs
as a single instance, and since feature 001 has carried a constitution deviation: its public endpoints
were not rate limited.

The three public endpoint groups differ. Creating a link has lasting cost and is the spam vector.
Following a link is the core function and legitimately comes in bursts (a link posted in a busy chat).
Reading details and statistics is cheap but enumerable.

## Decision

1. **Use ASP.NET Core's built-in rate limiter** (`AddRateLimiter`, `UseRateLimiter`), already in the
   shared framework: no package.
2. **Three named policies, attached by attribute** (`[EnableRateLimiting]`), each per client:

   | Policy | Endpoints | Algorithm | Default |
   |---|---|---|---|
   | `create` | `POST /api/links` | Fixed window | 10 per minute |
   | `redirect` | `GET /{code}` | Token bucket, refilled every second | bursts up to 60, 120 per minute |
   | `read` | `GET /api/links/{code}`, `.../stats` | Fixed window | 60 per minute |

   No global limiter, so `/health` and Swagger are never limited.
3. **No queueing.** Excess requests are rejected at once with **429**, `Retry-After` (whole seconds,
   rounded up, at least 1) and a ProblemDetails body, before any controller runs, so they have no
   effect. Every request counts whatever its outcome, because enumeration and spam consist mostly of
   failing requests (Clarification Q1).
4. **Client identity = the connection's own address**, used only as the limiter's in-memory key.
   `X-Forwarded-For` is ignored (Clarification Q2); the address is never stored or logged.
5. **Budgets are configuration** (`RateLimiting` section), validated at startup: a value below 1 stops
   the service with the setting's name.
6. **One warning per rejection**, naming the policy and the route pattern (`api/links`, `{code}`), never
   the client or the requested code.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Third-party package (e.g. AspNetCoreRateLimit) | New dependency duplicating a built-in feature (constitution VI) |
| Custom middleware | Re-implements windows, partitions and retry metadata the framework provides |
| One global limit for all endpoints | One noisy endpoint would throttle the others; would also limit health probes |
| Sliding window for create | Smoother at window edges but harder to explain in a `Retry-After`; unnecessary at 10 per minute |
| Fixed window for redirects | A burst would lock a client out for the rest of the minute; the token bucket refills every second |
| Queue excess requests | Delays legitimate work behind abusive work; the spec requires immediate rejection |
| Trust `X-Forwarded-For` | Any client could claim a new address per request and bypass every limit |
| Count only successful requests | A code-enumerating bot would never be limited (its requests are 404s) |
| Rate limiting at a reverse proxy or CDN | None exists in this deployment; the spec puts the limit in the service |

## Consequences

**Positive**

- One client can no longer flood creates, redirects or reads; other clients are unaffected
  (integration tests with simulated client addresses).
- Constitution Principle IV's deviation, carried since feature 001, is closed.
- Throttled clients get a standard answer (`429`, `Retry-After`, ProblemDetails with `traceId`).
- Operators can tune budgets without code, and a typo cannot silently disable limiting.

**Negative / accepted risks**

- **Clients sharing an address share a budget** (offices, mobile carriers using NAT).
- **IPv6 clients can rotate addresses** within their network prefix; grouping IPv6 by /64 is a
  follow-up.
- **Behind a reverse proxy, everyone shares the proxy's budget** until trusted-proxy handling
  (`ForwardedHeaders` with known proxies) is configured: required before such a deployment.
- **Single instance only**: budgets live in process memory and reset on restart; several instances
  would each allow the full budget unless a shared store (e.g. Redis) is introduced.
- **Log volume under attack**: one warning per rejection. It carries no client data, but a flood
  produces many lines; sampling or aggregation is a follow-up.
- **Test hosts need very large budgets**, because the functional suites send many requests from one
  test client. Dedicated tests use small budgets and short real windows (the .NET 8 limiter has no
  `TimeProvider` hook).
- **Sample requests must pace themselves**: `requests.http` waits a minute between parts.

## Deliberately left out (scenario doc §2.5)

Health liveness/readiness, a destination blocklist, idempotent create, caching, request correlation
IDs, authentication, malware scanning and distributed rate limiting, each with its reason in the
scenario document.

## Verification

`CreateRateLimitTests` (11th create rejected, nothing created, invalid creates count, another client
unaffected, `X-Forwarded-For` does not escape, recovery after `Retry-After`),
`RedirectAndReadRateLimitTests` (burst then 429, 404s count, rejected redirect records no click,
separate budgets, health never limited), `RateLimitOperationsTests` (configured budget, invalid
settings stop startup, log without address), `RateLimitingOptionsValidatorTests`, `OpenApiContractTests`
(429 in the contract); `requests.http` Part 3 against the running app with default budgets.