# Research: Per-Client Request Budgets (Rate Limiting)

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Scenario**:
[docs/scenarios/03-ambiguous.md](../../docs/scenarios/03-ambiguous.md) | **Date**: 2026-10-11

Each entry: Decision / Rationale / Alternatives considered. No open `NEEDS CLARIFICATION` items.

## R1. Mechanism: ASP.NET Core's built-in rate limiter

- **Decision**: `AddRateLimiter` / `UseRateLimiter` with three named policies, attached to endpoints
  with `[EnableRateLimiting("create" | "redirect" | "read")]`. No global limiter, so anything without
  the attribute (health, Swagger) is never limited (FR-009). Queue limit 0: excess requests are
  rejected at once, never delayed (FR-005).
- **Rationale**: Built into ASP.NET Core 8 (`Microsoft.AspNetCore.RateLimiting`,
  `System.Threading.RateLimiting` in the shared framework): no new package (constitution VI names
  rate limiting explicitly). Attribute-based policies fit the attribute-routed controllers (ADR-001
  of the plan).
- **Alternatives**: A custom middleware (re-implements windows, metadata and partitioning); a
  third-party package such as AspNetCoreRateLimit (new dependency, overlaps the built-in); limiting
  at a reverse proxy (none exists; the spec puts the limit in the service).

## R2. Algorithms per group (FR-002–FR-004)

- **Decision**:
  - **create**: fixed window, `PermitLimit` 10 per 60 s.
  - **redirect**: token bucket, `TokenLimit` 60 (burst), refilled 2 tokens per second (120 per
    minute sustained), auto-replenished.
  - **read**: fixed window, 60 per 60 s.
- **Rationale**: Fixed windows are simple to reason about and to explain in a `Retry-After`. Redirects
  need bursts ("a link shared in a busy chat") with a steady ceiling, which is exactly a token bucket;
  refilling per second (not per minute) avoids a minute-long lockout after a burst.
- **Alternatives**: Sliding window for create (smoother at window edges but harder to explain;
  10/minute does not need it); concurrency limiter (limits parallelism, not volume).

## R3. Client identity (FR-011, FR-011a, Clarification 2)

- **Decision**: Partition key = `HttpContext.Connection.RemoteIpAddress?.ToString()`, or the constant
  `"unknown"` when absent. Forwarded headers are not read (no `UseForwardedHeaders`). The key exists
  only inside the limiter's in-memory partition; it is never logged or stored.
- **Rationale**: Clarification 2: trusting `X-Forwarded-For` from anyone lets a client fake its
  address and escape its budget. Requests without an address share one budget (spec edge case).
- **Known limitation** (documented, not solved): IPv6 clients can change address within their
  network prefix; grouping IPv6 addresses by /64 would be a follow-up. Clients behind one NAT share a
  budget (spec assumption).
- **Alternatives**: `UseForwardedHeaders` with known proxies (correct behind a proxy; none exists;
  recorded as future work); per-API-key partitions (no keys exist).

## R4. Rejection response and logging (FR-005, FR-012)

- **Decision**: `RejectionStatusCode = 429`; `OnRejected`:
  1. sets `Retry-After` from the lease's `MetadataName.RetryAfter`, rounded **up** to whole seconds,
     minimum 1 (the fixed-window and token-bucket limiters both provide it);
  2. writes a ProblemDetails body through `IProblemDetailsService` (title "Too many requests",
     `type` RFC 6585 §4, `traceId` added by the existing customisation), so 429s look like every
     other error;
  3. logs a warning "Rate limit exceeded for policy {Policy} on {Endpoint}" with the policy name
     from the endpoint's `EnableRateLimitingAttribute` and the endpoint's route pattern; never the
     address.
- **Rationale**: Consistent errors (constitution VII); `Retry-After` lets well-behaved clients back
  off. Route pattern (`/{code}`), not the request path, so no code values flood the logs either.
- **Accepted risk**: one log line per rejection means a flooding client produces many warnings. It is
  bounded by request volume and carries no client data; sampling or aggregation is a follow-up.
- **Alternatives**: Let `UseStatusCodePages` produce the body (no `Retry-After`, generic title);
  log at Debug (rejections would be invisible to operators, against FR-012).

## R5. Configuration and validation (FR-010)

- **Decision**: `RateLimitingOptions` bound from section `RateLimiting`:
  `Create:PermitLimit`/`Create:WindowSeconds`, `Read:PermitLimit`/`Read:WindowSeconds`,
  `Redirect:BurstLimit`/`Redirect:TokensPerSecond`, all positive integers, validated by an
  `IValidateOptions` with `ValidateOnStart`, so an invalid value stops startup with a message naming
  the setting (same pattern as `ShortLinkOptions`). Policies read the validated options when the
  limiter is configured.
- **Rationale**: Configurable without code (FR-010); a typo that silently disables limiting is worse
  than a refusal to start.

## R6. Pipeline placement

- **Decision**: `app.UseRateLimiter()` after the exception handler and status-code pages and before
  the endpoints run. `WebApplication` inserts routing first, so endpoint metadata (the policy
  attributes) is available to the limiter.
- **Rationale**: Rejected requests never reach the controllers, so they have no effect (FR-006): no
  link created, no click queued. Attribute on the controller class covers every redirect outcome,
  including 404 and 410 (FR-008).

## R7. Testing strategy (SC-001–SC-007)

- **Decision**:
  - **Test hosts get very large budgets** through configuration in `ApiFactory`, so the existing
    feature 001/002 suites (which send many requests quickly) are unaffected (SC-006).
  - **Dedicated tests** build a host with small budgets and short windows (1–2 s).
  - **Telling clients apart in tests**: the test server leaves `RemoteIpAddress` empty, so a
    test-only startup filter sets it from a test header (`X-Test-Client-Ip`). The header exists only
    in the test project; production never reads it (FR-011a).
  - The .NET 8 limiter has no `TimeProvider` hook, so "succeeds after Retry-After" is tested with a
    short real window rather than a fake clock.
- **Alternatives**: Disabling rate limiting in tests (would hide misconfiguration on real
  endpoints); a test-only header read by production code (would be a bypass).

## R8. Contract (FR-014)

- **Decision**: Feature 003 owns `contracts/openapi.yaml` v0.3.0: feature 002's contract plus a
  reusable `TooManyRequests` response (ProblemDetails + `Retry-After` header) on `POST /api/links`,
  `GET /api/links/{code}`, `GET /api/links/{code}/stats` and `GET /{code}`. Controllers declare
  `[ProducesResponseType(429)]`; the OpenAPI drift test switches to this contract.
