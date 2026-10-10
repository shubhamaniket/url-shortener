# Implementation Plan: Link Expiry, Click Analytics, and Off-Path Click Recording

**Branch**: `002-brownfield-analytics-expiry` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-brownfield-analytics-expiry/spec.md`; verified impact
analysis in [docs/scenarios/02-brownfield.md](../../docs/scenarios/02-brownfield.md) §3.

## Summary

Change the merged feature 001 service safely to add optional link expiry (410 Gone, reclaimable
expired aliases), per-click analytics with a statistics endpoint, and move click recording off the
redirect path. Redirects only enqueue a privacy-reduced click event into a bounded in-memory
channel; a background writer stores batches of events and increases the stored total in one
transaction. All schema changes are additive in one EF migration (sign-off). This resolves the
constitution Principle V deviation recorded in feature 001's plan.

## Technical Context

**Language/Version**: C# 12, .NET 8 (LTS), `net8.0`; builds with the .NET 8 or 9 SDK

**Primary Dependencies**: Unchanged from feature 001 (ASP.NET Core 8, EF Core 8 SQLite,
Microsoft.Extensions.Options/Logging.Abstractions, Swashbuckle). `System.Threading.Channels` and
`BackgroundService` are part of .NET: **no new packages**

**Storage**: SQLite via EF Core; new migration `AddExpiryAndClickEvents` (additive)

**Testing**: xUnit + WebApplicationFactory (unchanged); integration tests exercise the real hosted
writer; eventual-consistency assertions poll with a 5-second timeout

**Target Platform**: Linux container or macOS/Windows via `dotnet run`

**Project Type**: Web service (REST API)

**API Contract**: [contracts/openapi.yaml](contracts/openapi.yaml) v0.2.0 (extends feature 001's)
with change notes in [contracts/http-api.md](contracts/http-api.md)

**Performance Goals**: p95 redirect < 100 ms even with analytics storage slow or failing (SC-002);
clicks visible within 5 s (FR-012)

**Constraints**: Redirect path performs no analytics I/O; bounded memory for queued clicks
(10 000 events); no IP, full referrer, or raw user agent stored or logged; feature 001 tests pass
unchanged except the intentional changes listed in research R10

**Scale/Scope**: Prototype, single instance; 1 new table, 1 new column, 1 new endpoint, 2 changed
endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | How this plan complies |
|---|---|---|---|
| I | Engineer-led AI | ✅ | Impact analysis verified by the engineer before any code; one task at a time; AI log |
| II | Test-first | ✅ | Every rule and flow has tests; regression baseline is the full feature 001 suite; seeded-database migration test |
| III | Clean architecture, thin controllers | ✅ | Expiry, referrer and UA rules in Domain; resolution/stats in Application; channel, writer, stats query in Infrastructure; controllers map HTTP only |
| IV | Security by default | ⚠️ Partial | Privacy reduction at the Domain edge (host only, UA family, no IP); expiry input validated with a mandatory zone. **Rate limiting still deferred to feature 003** (unchanged deviation) |
| V | Redirect path reliability | ✅ **Resolved** | Redirect only enqueues (no I/O); analytics failures and overload cannot fail or slow it; tested with a failing writer |
| VI | Simplicity | ✅ | No new packages; built-in `Channel<T>` and `BackgroundService`; no UA-parsing library |
| VII | Observability | ✅ | Structured logs for dropped events and failed batches (counts only, no PII); 410 as ProblemDetails |
| VIII | Safe change management | ✅ | One additive migration with up/down, tested against a seeded v1 database; **sign-off required** |
| IX | Quality gates | ⚠️ Partial | Local gates per task as in feature 001; CI expansion still planned for Phase 5 (unchanged deviation) |

**Gate result**: PASS. Principle V's deviation from feature 001 is closed by this feature; IV and
IX deviations carry over unchanged (feature 001 plan, Complexity Tracking).

**Post-design re-check (after Phase 1)**: unchanged. No new dependencies; no raw personal data in
the data model or logs; contract changes are additive.

## Project Structure

### Documentation (this feature)

```text
specs/002-brownfield-analytics-expiry/
├── spec.md              # incl. 5 clarifications
├── plan.md              # This file
├── research.md          # R1–R11
├── data-model.md        # ShortLink.ExpiresAtUtc, ClickEvent, value rules, migration
├── quickstart.md        # 10 manual scenarios + latency check
├── contracts/
│   ├── http-api.md      # change notes vs feature 001
│   └── openapi.yaml     # full contract v0.2.0
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
docs/adr/005-async-click-recording.md   # at-most-once delivery (new)
docs/adr/008-stored-click-count.md      # stored vs derived total (new)
```

### Source Code (changes to the existing layout)

```text
src/UrlShortener.Domain/Links/
├── ShortLink.cs                 # + ExpiresAtUtc, IsExpiredAt
├── ExpiryTime.cs                # new: zone-required ISO 8601, future-only
├── ClickEvent.cs                # new entity
├── ReferrerHost.cs              # new: referer → host or null
└── UserAgentFamily.cs           # new: UA → fixed family
src/UrlShortener.Application/
├── Abstractions/IClickRecorder.cs        # changed: bool TryRecord(ClickEvent)
├── Abstractions/IClickStatsReader.cs     # new
├── Abstractions/IShortLinkRepository.cs  # + TryReplaceExpiredAsync
└── Links/ (LinkService, ILinkService, CreateLinkCommand, LinkDetails, ClickContext,
            RedirectResolution, LinkStatistics)
src/UrlShortener.Infrastructure/
├── Links/ChannelClickRecorder.cs   # new (replaces DbClickRecorder)
├── Links/ClickEventWriter.cs       # new BackgroundService
├── Links/ClickStatsReader.cs       # new
├── Persistence/ClickEventConfiguration.cs, ShortLinkConfiguration.cs, ShortLinkRepository.cs
└── Persistence/Migrations/<ts>_AddExpiryAndClickEvents.cs
src/UrlShortener.Api/
├── Controllers/RedirectController.cs   # ClickContext from headers; 410
├── Controllers/LinksController.cs      # expiresAt; GET {code}/stats
└── Contracts/ (CreateLinkRequest, LinkResponse, LinkStatsResponse)
tests/ … (see research R10 for changed tests; new tests per task)
```

**Structure Decision**: Keep feature 001's four-project layout and `Links/` feature folders;
changes follow the impact analysis file list (§3.2).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| **IV** — public endpoints still not rate limited | Carried over unchanged; designed in feature 003 | Unchanged from feature 001's justification |
| **IX** — CI lacks format, vulnerability, CodeQL steps | Carried over unchanged; full CI in Phase 5 | Unchanged from feature 001's justification |

## Engineer-approved decisions (2026-10-11)

1. **R4 — queue full policy `DropOldest`, capacity 10 000.** Under overload the oldest queued
   clicks are lost (and logged); redirects never wait.
2. **R6 — reclaiming deletes the expired link and its click events** (data minimisation) rather
   than keeping them unreachable under a tombstone code.
3. **R1 — field naming**: request `expiresAt` (any zone), response `expiresAtUtc` (always UTC).
