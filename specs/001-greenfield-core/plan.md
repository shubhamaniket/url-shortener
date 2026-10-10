# Implementation Plan: Greenfield Core URL Shortener

**Branch**: `001-greenfield-core` | **Date**: 2026-10-10 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-greenfield-core/spec.md`

## Summary

Deliver the core URL shortener: create a short link (generated 7-char Base62 code or custom
alias), redirect with a 302, count clicks best-effort, show link details, and report health.
Built as a .NET 8 ASP.NET Core Web API with thin attribute-routed controllers over Application
use cases, Domain rules for URL and alias validation, and EF Core + SQLite in Infrastructure.
Uniqueness is enforced by one unique index on a normalized code (research R2/R3); click counting
is an atomic SQL increment behind an `IClickRecorder` seam, failure-isolated from the redirect
(R6) and replaced by a background recorder in feature 002.

## Technical Context

**Language/Version**: C# 12, .NET 8 (LTS), `net8.0`; builds with the .NET 8 or 9 SDK

**Primary Dependencies**: ASP.NET Core 8 (controllers, ProblemDetails, health checks),
EF Core 8 (`Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`),
Swashbuckle.AspNetCore 6.6.2 (already present)

**Storage**: SQLite file database via EF Core migrations (`ShortLinks` table)

**Testing**: xUnit 2.5 + `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory), xUnit `Assert`,
coverlet collector (all already present)

**Target Platform**: Linux container or macOS/Windows via `dotnet run`

**Project Type**: Web service (REST API); React frontend comes in a later feature

**API Contract**: [contracts/openapi.yaml](contracts/openapi.yaml) (OpenAPI 3.0.3, contract-first,
Postman-importable) with readable notes in [contracts/http-api.md](contracts/http-api.md) (R14)

**Performance Goals**: p95 redirect < 100 ms locally (SC-004): one indexed lookup + one atomic update

**Constraints**: Warnings as errors with `latest-recommended` analyzers; Domain has no framework
references; no new dependencies beyond the two EF Core packages; no raw IP or PII stored or logged

**Scale/Scope**: Prototype, single instance; thousands of links; 4 endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | How this plan complies |
|---|---|---|---|
| I | Engineer-led AI | ✅ | Tasks implemented one at a time with review; AI log + commit trailers |
| II | Test-first | ✅ | Every use case and rule has unit tests; every endpoint has integration tests asserting bodies/headers (R12) |
| III | Clean architecture, thin controllers | ✅ | Domain: entity + validation rules (BCL only). Application: use cases + interfaces. Infrastructure: EF Core, generator, recorder, health check. Controllers call `ILinkService` only, never `DbContext` |
| IV | Security by default | ⚠️ Partial | Scheme allowlist, user-info and self-host rejection, length cap, configured base URL, no IP storage, no secrets. **Rate limiting deferred** (see Complexity Tracking) |
| V | Redirect path reliability | ⚠️ Partial | Click failure never fails the redirect (FR-015a, tested). **Click write is still synchronous** (see Complexity Tracking) |
| VI | Simplicity | ✅ | Only EF Core packages added (justified, R9); custom DB health check instead of a package (R10); BCL `RandomNumberGenerator`, `TimeProvider` |
| VII | Observability | ✅ | Structured logging (source-generated `[LoggerMessage]`), DB health check, global ProblemDetails incl. unmatched routes, no stack traces outside Development |
| VIII | Safe change management | ✅ | Schema only via `InitialCreate` migration; migration flagged for sign-off in the PR |
| IX | Quality gates | ⚠️ Partial | Build (warnings as errors), format, unit + integration tests run locally per task and in CI. **CI format/vulnerability/CodeQL steps deferred** (see Complexity Tracking) |

**Gate result**: PASS with three justified, time-boxed deviations.

**Post-design re-check (after Phase 1)**: unchanged. The design added no new dependency, no
`DbContext` in controllers, and no IP or user-agent storage. Data model and contract match the
spec's FRs (traceability in [data-model.md](data-model.md)).

## Project Structure

### Documentation (this feature)

```text
specs/001-greenfield-core/
├── spec.md
├── plan.md              # This file
├── research.md          # Phase 0: decisions R1–R13
├── data-model.md        # Phase 1: entity, validation rules, schema
├── quickstart.md        # Phase 1: run + manual validation scenarios
├── contracts/
│   ├── http-api.md      # Phase 1: endpoint contract (readable)
│   └── openapi.yaml     # Phase 1: same contract, OpenAPI 3.0.3 (Postman import)
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── UrlShortener.Domain/                 # no package references
│   ├── Links/ShortLink.cs               # entity, invariants, NormalizedCode, lookup match rule
│   ├── Links/DestinationUrl.cs          # URL rules 1–7
│   ├── Links/CustomAlias.cs             # alias rules + reserved words
│   ├── Links/GeneratedCode.cs           # alphabet + length constants
│   └── Links/LinkValidationException.cs
├── UrlShortener.Application/            # references Domain only
│   ├── Links/ILinkService.cs + LinkService.cs     # Create, Resolve (redirect), GetDetails
│   ├── Links/CreateLinkCommand.cs, LinkDetails.cs
│   ├── Links/AliasAlreadyExistsException.cs, CodeGenerationFailedException.cs
│   ├── Abstractions/IShortLinkRepository.cs       # FindByNormalizedCodeAsync, TryAddAsync
│   ├── Abstractions/IShortCodeGenerator.cs
│   ├── Abstractions/IClickRecorder.cs
│   ├── Options/ShortLinkOptions.cs                # PublicBaseUrl
│   └── DependencyInjection.cs
├── UrlShortener.Infrastructure/         # references Application; EF Core Sqlite
│   ├── Persistence/AppDbContext.cs + ShortLinkConfiguration.cs
│   ├── Persistence/Migrations/                    # InitialCreate
│   ├── Persistence/ShortLinkRepository.cs         # unique violation → TryAdd false
│   ├── Links/Base62CodeGenerator.cs
│   ├── Links/DbClickRecorder.cs                   # atomic ExecuteUpdate (temporary, see R6)
│   ├── Health/DatabaseHealthCheck.cs
│   └── DependencyInjection.cs
└── UrlShortener.Api/
    ├── Controllers/LinksController.cs             # POST /api/links, GET /api/links/{code}
    ├── Controllers/RedirectController.cs          # GET /{code}
    ├── Contracts/CreateLinkRequest.cs, LinkResponse.cs
    ├── ErrorHandling/GlobalExceptionHandler.cs
    └── Program.cs

tests/
├── UrlShortener.UnitTests/              # + reference to Infrastructure (generator tests)
│   ├── Domain/  Application/  Infrastructure/
│   └── Fakes/   (scripted generator, in-memory repository, throwing recorder, fixed clock)
└── UrlShortener.IntegrationTests/
    ├── ApiFactory.cs                    # temp SQLite file, migrations, base URL override
    ├── CreateLinkTests.cs  RedirectTests.cs  LinkDetailsTests.cs
    ├── ConcurrencyTests.cs              # alias race, concurrent click counting
    └── HealthTests.cs                   # moved to ApiFactory

.config/dotnet-tools.json                # dotnet-ef pinned
requests.http                            # sample calls (replaces the template UrlShortener.Api.http)
```

**Structure Decision**: Keep the four-project clean-architecture layout created in Phase 0 and add
feature folders (`Links/`) inside each layer. Request/response DTOs live in the Api project so the
HTTP contract can change without touching use cases.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| **V** — click count written synchronously on the redirect path | Feature 001 needs a working count; the move off the hot path is the planned brownfield change in feature 002. Failure isolation (FR-015a) is already in place and tested, and the write is a single atomic `UPDATE` behind `IClickRecorder` | Building the `Channel<T>` background recorder now would pull feature 002 forward and remove the brownfield scenario's main refactor. Fire-and-forget `Task.Run` is unsafe (disposed `DbContext`, unobserved exceptions) |
| **IV** — public endpoints not rate limited | Rate limiting is the core of feature 003 (reliability and abuse protection), where policies are designed and tested together with 429/`Retry-After` | Adding a placeholder limiter now would be untested policy guesswork and get redesigned in 003 |
| **IX** — CI does not yet run format, vulnerability check, or CodeQL | Full CI is scheduled for Phase 5. Until then each task runs `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`, and `dotnet list package --vulnerable` locally before commit | Expanding CI now competes with feature work on a tight deadline; the gates still run locally |

## Engineer-approved decisions (2026-10-10)

1. **R3 — one unique index on the normalized code.** Generated codes that differ only in case
   cannot coexist (treated as a collision). Chosen over a partial alias-only index, which would
   leave alias-vs-generated collisions to a racy application check. Spec "Letter case"
   assumption reworded.
2. **R6 / Complexity Tracking V** — the synchronous, failure-isolated click write is accepted
   until feature 002 replaces it with a background recorder.
3. **R13 — remove `UseHttpsRedirection()`** from `Program.cs`; HTTPS enforcement (proxy redirect,
   HSTS) is a deployment concern documented in `docs/deployment.md`.
