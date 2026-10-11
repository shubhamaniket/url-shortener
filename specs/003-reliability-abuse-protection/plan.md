# Implementation Plan: Per-Client Request Budgets (Rate Limiting)

**Branch**: `003-reliability-abuse-protection` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-reliability-abuse-protection/spec.md`; interpretation
and scope in [docs/scenarios/03-ambiguous.md](../../docs/scenarios/03-ambiguous.md) §2.

## Summary

Answer "reliable and protected from abuse" (scoped by the engineer to rate limiting) with ASP.NET
Core's built-in rate limiter: three named per-client policies (create: fixed window 10/min;
redirect: token bucket 120/min with bursts of 60; read: fixed window 60/min) attached by attribute to
the existing controllers, a rejection handler that returns 429 + `Retry-After` + ProblemDetails and
logs without client identity, and validated configuration. No schema change, no new package. This
closes the constitution Principle IV deviation ("Public endpoints MUST be rate limited") carried since
feature 001.

## Technical Context

**Language/Version**: C# 12, .NET 8 (LTS), `net8.0`

**Primary Dependencies**: Unchanged. `Microsoft.AspNetCore.RateLimiting` and
`System.Threading.RateLimiting` are part of the ASP.NET Core 8 shared framework the Api already
uses: **no new packages**

**Storage**: None new (budgets live in memory; no migration)

**Testing**: xUnit + WebApplicationFactory; test hosts get very large budgets; dedicated tests use
small budgets, short real windows (the .NET 8 limiter has no `TimeProvider` hook), and a test-only
startup filter to simulate distinct client addresses

**Target Platform**: Linux container or macOS/Windows via `dotnet run`

**Project Type**: Web service (REST API)

**API Contract**: [contracts/openapi.yaml](contracts/openapi.yaml) v0.3.0 (feature 002's + 429) with
change notes in [contracts/http-api.md](contracts/http-api.md)

**Performance Goals**: redirect p95 < 100 ms within budget (SC-004); limiter check is in-memory

**Constraints**: client address never stored or logged; forwarded headers ignored; health never
limited; feature 001/002 behaviour unchanged within budget (SC-006)

**Scale/Scope**: single instance; 3 policies on 4 operations; 1 options class + validator; 1
rejection handler

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | How this plan complies |
|---|---|---|---|
| I | Engineer-led AI | ✅ | Interpretation and scope decided by the engineer before the spec; 3 clarifications answered; one task at a time |
| II | Test-first | ✅ | Every acceptance scenario has an integration test; full feature 001/002 suite as regression baseline |
| III | Clean architecture, thin controllers | ✅ | Rate limiting is an HTTP concern: Api layer only (options, policies, rejection handler, attributes); Domain/Application untouched |
| IV | Security by default | ✅ **Resolved** | Public endpoints rate limited; IP used in memory only, never stored or logged; spoofable forwarded headers ignored |
| V | Redirect path reliability | ✅ | Limiter is an in-memory check before the controller; no I/O; redirect sign-off required (Principle VIII) |
| VI | Simplicity | ✅ | Built-in rate limiter, no packages, no custom algorithms |
| VII | Observability | ✅ | 429 as ProblemDetails with `traceId`; a warning per rejection naming the policy and route pattern |
| VIII | Safe change management | ✅ | No migration; redirect-path change flagged for sign-off |
| IX | Quality gates | ⚠️ Partial | Local gates per task as before; CI expansion still Phase 5 (unchanged deviation) |

**Gate result**: PASS. Principle IV's deviation is closed by this feature; IX carries over.

**Post-design re-check (after Phase 1)**: unchanged. No new dependencies, no stored client data, the
contract change is additive (a new error status on existing operations).

## Project Structure

### Documentation (this feature)

```text
specs/003-reliability-abuse-protection/
├── spec.md              # incl. 3 clarifications
├── plan.md              # This file
├── research.md          # R1–R8
├── data-model.md        # in-memory budgets, settings, validation
├── quickstart.md        # 9 manual scenarios + latency
├── contracts/
│   ├── http-api.md      # change notes vs feature 002
│   └── openapi.yaml     # full contract v0.3.0
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
docs/adr/009-rate-limiting.md           # algorithms, client identity, what was left out (new)
docs/scenarios/03-ambiguous.md          # interpretation (§2, done) + sections 3–8
```

### Source Code (changes to the existing layout)

```text
src/UrlShortener.Api/
├── RateLimiting/
│   ├── RateLimitingOptions.cs           # section RateLimiting (Create, Redirect, Read)
│   ├── RateLimitingOptionsValidator.cs  # IValidateOptions, ValidateOnStart
│   ├── RateLimitPolicies.cs             # policy names + AddUrlShortenerRateLimiting()
│   └── RateLimitRejection.cs            # OnRejected: 429, Retry-After, ProblemDetails, log
├── Controllers/LinksController.cs       # [EnableRateLimiting] create / read; [ProducesResponseType(429)]
├── Controllers/RedirectController.cs    # [EnableRateLimiting("redirect")]
├── Program.cs                           # options binding, AddRateLimiter, UseRateLimiter
└── appsettings.json                     # RateLimiting defaults
tests/UrlShortener.IntegrationTests/
├── ApiFactory.cs                        # very large budgets for existing tests
├── RateLimiting/ (ClientAddressStartupFilter, RateLimitingTests, …)
└── OpenApiContractTests.cs              # contract v0.3.0: 429 on the four operations
```

**Structure Decision**: Rate limiting is HTTP infrastructure, so it lives entirely in the Api project
(constitution III); Domain, Application and Infrastructure are untouched.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| **IX** — CI lacks format, vulnerability, CodeQL steps | Carried over; full CI in Phase 5 | Unchanged from feature 001's justification |

## Engineer-approved decisions (2026-10-11)

1. **Scope**: rate limiting only; liveness/readiness, blocklist, idempotency, caching and the others
   are documented as left out (scenario doc §2.5).
2. **Clarifications**: every request counts (Q1); forwarded headers ignored (Q2); defaults create
   10/min, redirect 120/min burst 60, read 60/min (Q3).
3. **Process**: coarse tasks (one capability end to end each) to fit the deadline; review, tests and
   sign-offs unchanged.
