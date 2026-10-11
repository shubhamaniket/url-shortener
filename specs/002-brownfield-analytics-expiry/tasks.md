---

description: "Task list for feature 002 — link expiry, click analytics, off-path click recording"
---

# Tasks: Link Expiry, Click Analytics, and Off-Path Click Recording

**Input**: Design documents from `specs/002-brownfield-analytics-expiry/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md); verified
impact analysis in [docs/scenarios/02-brownfield.md](../../docs/scenarios/02-brownfield.md) §3

**Tests**: REQUIRED (constitution II). Each task lists acceptance criteria and the tests that prove
them, written in the same task.

**Brownfield rule**: the feature 001 suite is the regression baseline (**131 unit + 64 integration
on `main`**). Every task keeps the whole suite green. A feature 001 test may change **only** where
listed under "Changed tests" in that task, with the reason (research R10, spec SC-005).

**Organization**: Grouped by user story (spec.md US1 expiry, US2 off-path recording, US3 stats).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US3
- Sub-bullets: **AC** acceptance criteria, **Tests** required tests, **Changed tests** feature 001
  tests changed by design, **Sign-off** high-impact change (constitution VIII)

## Definition of Done (every task)

1. `dotnet build -c Release` (warnings as errors), `dotnet format --verify-no-changes`, `dotnet test`
   (**full suite**), `dotnet list package --vulnerable --include-transitive` all pass
2. One commit per task: Conventional Commit + `Implements Txxx from specs/002-brownfield-analytics-expiry/tasks.md` + `AI-Assisted:` trailer
3. Task ticked here; entry in `docs/ai-usage-log.md`

---

## Phase 1: Setup

- [x] T001 Record the regression baseline: run the full suite on this branch before any code change and write the counts into docs/scenarios/02-brownfield.md (new "Regression baseline" note in section 6)
  - **AC**: counts match `main` (131 unit + 64 integration, 0 failures); no code changed

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain additions and the schema every story needs

- [x] T002 Add expiry to the `ShortLink` entity in src/UrlShortener.Domain/Links/ShortLink.cs: nullable `ExpiresAtUtc`, `IsExpiredAt(DateTime nowUtc)`, optional `expiresAtUtc` argument on both factories
  - **AC**: "Null = never expires"; expired when "`ExpiresAtUtc <= now`"; expiry must be UTC and later than `CreatedAtUtc` (`ArgumentException` otherwise); existing factory calls without expiry compile unchanged
  - **Tests**: extend tests/UrlShortener.UnitTests/Domain/ShortLinkTests.cs: no expiry never expires; expired exactly at and after the instant, not before; non-UTC or not-after-creation expiry rejected. Existing `ShortLinkTests` pass unchanged
  - **Change during implementation**: EF Core maps public properties by convention, so the new property broke every query (`no such column: ExpiresAtUtc`) before the migration existed. `ShortLinkConfiguration` ignores `ExpiresAtUtc` until T005 maps it together with the migration, keeping the schema change in one reviewed migration and every commit green
- [x] T003 [P] Add privacy-reducing rules in src/UrlShortener.Domain/Links/ReferrerHost.cs and src/UrlShortener.Domain/Links/UserAgentFamily.cs (research R3, data-model tables)
  - **AC**: `ReferrerHost.From`: absolute http/https → lower-cased host (≤ 253), credentials/path/query dropped; anything else → `null`. `UserAgentFamily.From`: ordered rules "`Bot` → `Edge` → `Firefox` → `Chrome` → `Safari` → `Other`", missing → `Other`
  - **Tests**: tests/UrlShortener.UnitTests/Domain/ReferrerHostTests.cs (every row of the data-model ReferrerHost table) and UserAgentFamilyTests.cs (real UA strings: Chrome, Edge, Firefox, Safari macOS/iOS, Chrome iOS `CriOS`, Googlebot, Slackbot, facebookexternalhit, WhatsApp preview, curl → Other, empty → Other)
- [x] T004 Add the `ClickEvent` entity in src/UrlShortener.Domain/Links/ClickEvent.cs
  - **AC**: fields per data-model ("`ShortLinkId`", "`OccurredAtUtc`" UTC, "`ReferrerHost` ≤ 253 or null", "`UserAgentFamily` ≤ 16"); factory `ClickEvent.Create(linkId, occurredAtUtc, referer, userAgent)` applies T003's rules so raw values never reach the entity
  - **Tests**: tests/UrlShortener.UnitTests/Domain/ClickEventTests.cs: raw referer with path/query/credentials stored as host only; raw UA stored as family only; non-UTC time rejected
- [x] T005 Persistence and migration in src/UrlShortener.Infrastructure/Persistence/: `ClickEventConfiguration.cs`, `ExpiresAtUtc` mapping (UTC converter) in ShortLinkConfiguration.cs, `DbSet<ClickEvent>` in AppDbContext.cs; generate migration `AddExpiryAndClickEvents`
  - **AC**: remove the temporary `Ignore(ExpiresAtUtc)` from T002 and map it with the UTC converter; schema exactly as data-model: `ShortLinks.ExpiresAtUtc` TEXT NULL; `ClickEvents` with FK "ON DELETE CASCADE" and index "`IX_ClickEvents_ShortLinkId_OccurredAtUtc`"; Down drops both; no change to existing columns
  - **Tests**: tests/UrlShortener.IntegrationTests/Persistence/MigrationTests.cs: **seeded v1 database** — migrate to `InitialCreate`, insert a link with `ClickCount` 7, migrate to latest → link intact, `ClickCount` 7, `ExpiresAtUtc` null; deleting a link cascades its click events; Down then Up round-trip succeeds
  - **Sign-off**: DB migration
  - **Change during implementation**: the generated migration trips analyzer CA1861 (constant array for the index columns), which warnings-as-errors turns into a build failure. `.editorconfig` disables only CA1861, only for `Persistence/Migrations/*.cs`, so generated migrations stay exactly as `dotnet ef` wrote them

**Checkpoint**: schema and domain ready; all feature 001 tests still pass unchanged

---

## Phase 3: User Story 1 — Create a link that expires (Priority: P1) 🎯 MVP

**Goal**: optional expiry on create; expired links return 410; expired aliases can be reclaimed.

**Independent Test**: create a link expiring soon → 302; advance time past expiry → 410, no click; claim its alias → 201.

- [x] T006 [P] [US1] Implement expiry input rule in src/UrlShortener.Domain/Links/ExpiryTime.cs, field `expiresAt` (research R1)
  - **AC**: accepts "`yyyy-MM-ddTHH:mm[:ss[.fffffff]](Z|±HH:mm)`" only; converts to UTC; must be strictly after `now`; messages explain "a time zone is required" / "must be in the future"
  - **Tests**: tests/UrlShortener.UnitTests/Domain/ExpiryTimeTests.cs: `+05:30` → correct UTC; `Z` accepted; no zone, `12/10/2026`, `tomorrow`, past, exactly now → rejected with field `expiresAt`
  - **Sign-off**: input validation
- [x] T007 [US1] Application expiry: `CreateLinkCommand.ExpiresAt`, `LinkDetails.ExpiresAtUtc`, expiry validated and stored on create (both paths); `RedirectResolution` (`Found`/`NotFound`/`Expired`) returned by `ResolveForRedirectAsync`, recording no click when expired — in src/UrlShortener.Application/Links/
  - **AC**: expired check uses `TimeProvider`; details ignore expiry (FR-005)
  - **Tests**: tests/UrlShortener.UnitTests/Application/ExpiryTests.cs: create with/without expiry; invalid expiry stores nothing; before expiry → Found + 1 click; at/after → Expired + 0 clicks; details of an expired link returned with `ExpiresAtUtc`
  - **Changed tests**: ResolveLinkTests (9) assert on `RedirectResolution` instead of `string?` (signature change, same behaviour); fakes/command constructors gain the optional expiry argument
- [x] T008 [US1] Api expiry: `expiresAt` on CreateLinkRequest, `expiresAtUtc` on LinkResponse, `RedirectController` maps `Expired` → 410 ProblemDetails "Link expired" with `Cache-Control: no-store`; ApiFactory gains a controllable clock for tests
  - **AC**: matches contracts/openapi.yaml v0.2.0; OpenAPI drift test switched to the feature 002 contract
  - **Tests**: tests/UrlShortener.IntegrationTests/ExpiryTests.cs: create with offset → `expiresAtUtc` UTC with `Z`; 400 `errors.expiresAt` for no zone and past; 302 before expiry; advance clock → 410 problem+json, no-store, stored count unchanged; details of expired link 200 with `expiresAtUtc`; links without expiry unaffected
  - **Changed tests**: LinkDetailsTests "exactly five fields" → six (`expiresAtUtc`); OpenApiContractTests expected `LinkResponse`/`CreateLinkRequest` fields
  - **Sign-off**: redirect path
- [x] T009 [US1] Reclaim expired codes: `IShortLinkRepository.TryReplaceExpiredAsync` (conditional delete + insert in one transaction, research R6) and the alias path in `LinkService` (src/UrlShortener.Infrastructure/Persistence/ShortLinkRepository.cs, src/UrlShortener.Application/Links/LinkService.cs)
  - **AC**: alias taken by an expired link → old link and its events deleted, new link created (201); not expired → 409 unchanged; generated codes never reclaim
  - **Tests**: unit (fake repository): reclaim vs 409 paths, generator collision with expired code retries. Integration tests/UrlShortener.IntegrationTests/ReclaimTests.cs: reclaim in another letter case → new destination redirects, details refer to the new link (stats are checked in T015), old click events gone; ConcurrencyTests: 10 parallel claims of one expired alias → exactly one 201, nine 409, no 500
  - **Sign-off**: data deletion + concurrency

**Checkpoint**: US1 complete; click recording still synchronous (feature 001 behaviour)

---

## Phase 4: User Story 2 — Off-path click recording (Priority: P1)

**Goal**: redirects only enqueue; a background writer stores events and totals together.

**Independent Test**: block or break the writer → redirects still 302 and fast; normal operation → counts catch up within 5 s.

- [x] T010 [P] [US2] Implement `ChannelClickRecorder` in src/UrlShortener.Infrastructure/Links/ChannelClickRecorder.cs as a concrete class with `bool TryRecord(ClickEvent)` (not yet behind `IClickRecorder`; T012 switches the interface, so the existing recorder keeps working until then)
  - **AC**: bounded channel, capacity from options (default 10 000), `DropOldest`, single reader; drop counter readable and reset by the writer; `TryRecord` never blocks or throws
  - **Tests**: tests/UrlShortener.UnitTests/Infrastructure/ChannelClickRecorderTests.cs: enqueued events readable in order; capacity 3 + 5 writes → 3 newest kept, 2 drops counted; concurrent writers lose nothing below capacity
- [x] T011 [US2] Implement `ClickEventWriter : BackgroundService` in src/UrlShortener.Infrastructure/Links/ClickEventWriter.cs (research R5), registered but fed by nothing yet
  - **AC**: batches up to 500; one transaction inserts events and `ClickCount = ClickCount + n` per link; events whose link no longer exists are filtered out first (counted and logged), so a retired link cannot fail a batch; failed batch logged (size + error, no PII) and discarded, writer keeps running; drop count logged; shutdown drains remaining events within 5 s
  - **Tests**: tests/UrlShortener.IntegrationTests/Persistence/ClickEventWriterTests.cs (real SQLite): batch stores events and totals equal; **a batch mixing events of a deleted link with valid events stores the valid ones**; injected failure on one batch → next batch still stored, and captured logs contain no referrer path/query or raw user agent; stop the host with queued events → they are stored
  - **Change during implementation**: (1) `BackgroundService` is not available to the Infrastructure class library (only the Api gets it through the ASP.NET Core shared framework), so the plan's "no new packages" was wrong here; the engineer approved adding `Microsoft.Extensions.Hosting.Abstractions` 8.0.1 (plan.md decision 4). (2) Storage sits behind `IClickEventStore` (scoped `ClickEventStore`) so tests can make it fail, hang, or hold at a gate (research R11). (3) The flush time is `ClickRecording:ShutdownFlushTimeout` (default 5 s) so the hang test runs in 200 ms. (4) The `Eventually` polling helper planned for T012 is added here, where it is first needed
- [x] T012 [US2] Switch over: `LinkService` builds `ClickEvent` from `ClickContext` (raw `Referer`/`User-Agent` from `RedirectController`, never the IP) and calls `TryRecord`; remove `DbClickRecorder` and the old `RecordClickAsync` contract; add the `Eventually` test helper
  - **AC**: `IClickRecorder` becomes `bool TryRecord(ClickEvent)`, implemented by `ChannelClickRecorder` (singleton); `OccurredAtUtc` comes from `TimeProvider` (so tests control "today"); redirect path performs no database write; failure isolation kept; record in docs/scenarios/02-brownfield.md that the Principle V deviation is closed
  - **Tests**: integration: stored `ClickEvents` rows contain host/family only (no path, query, raw UA); redirect still 302 when the writer's data access throws (failure logged)
  - **Changed tests**: RedirectTests `EachRedirectIsCounted`, ConcurrencyTests `ConcurrentRedirectsAreAllCounted`, LinkDetailsTests `ShowsClickCount…` now wait via `Eventually` (≤ 5 s; FR-012); `RedirectStillWorksWhenClickRecordingFails` targets the new recorder; ShortLinkRepositoryTests `ClickRecorderIncrementsStoredCount` removed (covered by T011); ResolveLinkTests/fakes follow `TryRecord`
  - **Sign-off**: redirect path
  - **Change during implementation**: (1) `ResolveForRedirectAsync` takes the `ClickContext`; unit call sites pass `ClickContext.None`. (2) `ResolveLinkTests`: recording no longer awaits or takes a token, so "recorder timeout" became "recorder throwing `OperationCanceledException` still returns the URL", and "cancelled request propagates" now asserts the lookup's cancellation and that nothing is recorded; the in-memory repository honours cancellation and assigns positive ids as the database does (it used id 0, which `ClickEvent.Create` rightly rejects). (3) T008's expiry integration test waits for a marker click before asserting "no click after 410", since a wrongly queued click would otherwise be stored only later. (4) `Eventually` already exists from T011
- [x] T013 [US2] Overload and impaired-analytics tests in tests/UrlShortener.IntegrationTests/AnalyticsResilienceTests.cs
  - **AC / Tests**: writer blocked indefinitely (data access waits) + small capacity → 50 redirects all **complete with 302 while the writer is still blocked** (the non-timing proof that redirects never wait for analytics); secondary check: in-process p95 under a generous 100 ms; after unblocking, the newest events are stored and drops are logged (SC-001, SC-002 in-process evidence; real latency in T018)
  - **Change during implementation**: added a second test for SC-001 (storage failing on every batch → all 50 redirects 302, failures logged, nothing stored). The blocked-writer test prints its measured p95 for the scenario doc (5 runs: 0.35–0.73 ms, max ≤ 2.2 ms, in-process)

**Checkpoint**: US2 complete; Principle V resolved

---

## Phase 5: User Story 3 — Click statistics (Priority: P2)

**Goal**: `GET /api/links/{code}/stats` with total, 30 daily counts, top 5 referrers.

**Independent Test**: redirects with varied referrers → stats match exactly.

- [ ] T014 [US3] Implement `IClickStatsReader` / `ClickStatsReader` and `LinkStatistics` DTO (src/UrlShortener.Application/Abstractions/, src/UrlShortener.Infrastructure/Links/ClickStatsReader.cs), research R7
  - **AC**: total = stored `ClickCount`; exactly 30 UTC days oldest first with zeros; top 5 sources by count desc then source asc; `NULL` → `(direct)`; aggregation in the database (or the documented in-memory fallback, with the reason recorded) using the index
  - **Tests**: tests/UrlShortener.IntegrationTests/Persistence/ClickStatsReaderTests.cs (seeded events, fixed "today"): padding and boundaries (day 29 included, day 30 excluded), 7 sources → top 5 with tie order, `(direct)` ranking, link with no events
- [ ] T015 [US3] `LinkService.GetStatsAsync` (shared `FindAsync`, ignores expiry) and `GET /api/links/{code}/stats` on LinksController with `LinkStatsResponse`
  - **AC**: 200 per contract; 404 ProblemDetails for unknown; expired link → 200; never records a click; `[ProducesResponseType]` 200/404
  - **Tests**: unit GetStatsTests; integration tests/UrlShortener.IntegrationTests/StatsTests.cs: a reclaimed code's stats start from zero; 3 redirects with `Referer: https://news.example/a?u=1` + 2 without → total 5, today 5, 30 entries, `news.example` 3 then `(direct)` 2 (after `Eventually`); alias any case; expired 200; unknown 404; reading stats twice does not change counts
  - **Changed tests**: OpenApiContractTests expected operations gain the stats endpoint, and a new schema check pins the `LinkStatsResponse` fields (no per-visitor data, FR-020)

**Checkpoint**: all user stories complete

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T016 [P] Write docs/adr/005-async-click-recording.md (at-most-once, DropOldest, shutdown flush, orphan events after reclaim, drop logs delayed while the writer is blocked) and docs/adr/008-stored-click-count.md (stored vs derived, Clarification Q1)
- [ ] T017 [P] Extend requests.http with feature 002 scenarios (expiry, 410, expiry validation, reclaim, stats with Referer); update the root quickstart pointers
  - **AC**: every new request executed against the running app with the expected status
- [ ] T018 Run specs/002-brownfield-analytics-expiry/quickstart.md: scenarios 1–10 on a database migrated from feature 001 data, privacy inspection of `ClickEvents`, latency loop (SC-002); engineer verifies Swagger and Postman (v0.2.0 contract) by hand
  - **AC**: all scenarios as expected; real numbers recorded for the scenario doc
- [ ] T019 Complete docs/scenarios/02-brownfield.md sections 2 and 4–8: interpretation (clarifications, incl. the engineer's override on Q2), decomposition, execution, validation (regression baseline vs final counts, changed tests with reasons), before/after behaviour, migration steps and rollback, risks, next steps
- [ ] T021 [P] Show the request examples in Swagger: generate the Api XML documentation file and call `IncludeXmlComments`, so "Try it out" pre-fills `https://example.com/...`, `team-offsite` and a zoned `expiresAt` instead of `string` (found during T008 manual testing: the `<example>` tags written in feature 001 were never wired up, so a tester sent the placeholder `"customAlias": "string"`). Added during implementation; done before T018 so the manual Swagger check covers it
  - **AC**: Swagger UI and `/swagger/v1/swagger.json` show the examples from the doc comments; warnings-as-errors still pass (missing XML comments on public types must not become build errors)
  - **Tests**: OpenApiContractTests: generated `CreateLinkRequest` schema carries the `customAlias` and `expiresAt` examples
- [ ] T020 Close out: tick tasks, PR description per the template with sign-offs (migration T005, redirect T008/T012, validation T006, data deletion T009)

**Genuine bug rule** (plan §7): if a real defect in feature 001 code is found during this work, add a task here and fix it test-first (failing test → fix → passing test) with the root cause documented. None is to be invented.

---

## Dependencies & Execution Order

- **Setup → Foundational**: T001 → T002 → (T003 ∥ T002) → T004 → T005
- **US1**: T006 ∥ (after T005) → T007 → T008 → T009
- **US2**: T010 ∥ T011 after T005 (T011 needs T004/T005) → T012 (needs T007's `RedirectResolution`, T010, T011) → T013
- **US3**: T014 after T012 (events exist) → T015
- **Polish**: T016 ∥ T017 ∥ T021 any time after their inputs; T021 before T018; T018 → T019 → T020

### Parallel Opportunities

- T003 ∥ T002; T006 ∥ T010 (Domain rule vs Infrastructure recorder); T016 ∥ T017

## Implementation Strategy

1. Baseline + foundational (T001–T005), including the migration sign-off
2. US1 expiry (T006–T009) → testable: 410 and reclaim
3. US2 refactor (T010–T013): build recorder and writer first, switch over in one task, so counting never breaks between commits
4. US3 stats (T014–T015) → testable: stats endpoint
5. Polish (T016–T020)

## Notes

- One task = one reviewed commit; never several tasks in one unattended run
- Push and PR descriptions per task-list phase (engineer's working agreement)
