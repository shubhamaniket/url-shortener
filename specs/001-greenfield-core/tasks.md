---

description: "Task list for feature 001 — greenfield core URL shortener"
---

# Tasks: Greenfield Core URL Shortener

**Input**: Design documents from `specs/001-greenfield-core/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: REQUIRED. Constitution Principle II (test-first, non-negotiable) applies to every task.
Each task lists its acceptance criteria and the tests that prove them; tests are written in the
same task, before or alongside the code, and the task is not done until they pass.

**Organization**: Grouped by user story (spec.md US1–US5) so each story is an independently
testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: User story from spec.md (US1–US5)
- Sub-bullets: **AC** = acceptance criteria, **Tests** = required tests, **Sign-off** = high-impact
  change needing engineer sign-off in the PR (constitution VIII)

## Path Conventions

Clean-architecture projects from plan.md: `src/UrlShortener.{Domain,Application,Infrastructure,Api}/`,
`tests/UrlShortener.{UnitTests,IntegrationTests}/`.

## Definition of Done (every task)

1. `dotnet build -c Release` (warnings as errors), `dotnet format --verify-no-changes`, `dotnet test` all pass, and `dotnet list package --vulnerable --include-transitive` reports no findings
2. One commit per task: Conventional Commit + `Implements Txxx from specs/001-greenfield-core/tasks.md` + `AI-Assisted:` trailer
3. Task ticked here; entry added to `docs/ai-usage-log.md`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dependencies and tooling the rest of the feature needs

- [x] T001 Add `Microsoft.EntityFrameworkCore.Sqlite` 8.0.x to src/UrlShortener.Infrastructure/UrlShortener.Infrastructure.csproj and `Microsoft.EntityFrameworkCore.Design` 8.0.x (`PrivateAssets="all"`) to src/UrlShortener.Api/UrlShortener.Api.csproj; add `*.db`, `*.db-shm`, `*.db-wal` to .gitignore
  - **AC**: solution builds; `dotnet list package --vulnerable --include-transitive` reports no findings; licenses recorded in the commit body (MIT)
  - **Sign-off**: new dependencies (research R9)
- [x] T002 [P] Create local tool manifest .config/dotnet-tools.json pinning `dotnet-ef` 8.0.x
  - **AC**: `dotnet tool restore` then `dotnet ef --version` prints 8.0.x
- [x] T003 [P] Add a project reference to src/UrlShortener.Infrastructure in tests/UrlShortener.UnitTests/UrlShortener.UnitTests.csproj
  - **AC**: unit test project builds and references Domain, Application, Infrastructure. Hand-written fakes go in tests/UrlShortener.UnitTests/Fakes/ and are created by the first task that needs each one (T011: fixed `TimeProvider`, in-memory `IShortLinkRepository` keyed by normalized code, scripted `IShortCodeGenerator`; T013: throwing and counting `IClickRecorder`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entity, abstractions, persistence, and HTTP pipeline that every story uses

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 Create the `ShortLink` entity and `GeneratedCode` constants in src/UrlShortener.Domain/Links/ShortLink.cs and src/UrlShortener.Domain/Links/GeneratedCode.cs
  - **AC**: fields per data-model: `Id` long; `Code` "3–30"; `NormalizedCode` = "`Code` lower-cased (invariant culture)"; `IsCustomAlias` bool; `OriginalUrl` "≤ 2048"; `CreatedAtUtc` UTC; `ClickCount` "Starts at 0". No public setters except via EF; `Matches(string input)` implements the lookup rule: "matches if `IsCustomAlias` is true, or if `Code == input` with ordinal (case-sensitive) comparison"; `GeneratedCode.Alphabet` = Base62, `GeneratedCode.Length` = 7; Domain project still has no package references
  - **Tests**: tests/UrlShortener.UnitTests/Domain/ShortLinkTests.cs — normalization; alias matches `TEAM-OFFSITE` for `team-offsite`; generated `Abc123x` does not match `abc123x`; new link has `ClickCount` 0 and UTC time
- [x] T005 Create Application abstractions and options in src/UrlShortener.Application/: Abstractions/IShortLinkRepository.cs (`FindByNormalizedCodeAsync`, `TryAddAsync` → `bool`), Abstractions/IShortCodeGenerator.cs, Abstractions/IClickRecorder.cs (`RecordClickAsync(long linkId, CancellationToken)`), Options/ShortLinkOptions.cs (`PublicBaseUrl`), Links/LinkDetails.cs, Links/CreateLinkCommand.cs, Links/AliasAlreadyExistsException.cs, Links/CodeGenerationFailedException.cs, DependencyInjection.cs
  - **AC**: `ShortLinkOptions` rejects a missing or non-absolute or non-http(s) `PublicBaseUrl` at startup (`ValidateOnStart`); Application references Domain only
  - **Change during implementation**: `ShortLinkOptions.Validate()` is a plain method (also rejects user info, path, query, fragment) so Application needs no package yet; startup enforcement is wired in T007. `DependencyInjection.cs` moves to T011, where `LinkService` first needs DI and logging (`Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; sign-off: new dependencies)
  - **Tests**: tests/UrlShortener.UnitTests/Application/ShortLinkOptionsTests.cs — valid http/https accepted; empty, relative, `ftp://` rejected
- [x] T006 Implement persistence in src/UrlShortener.Infrastructure/Persistence/: AppDbContext.cs, ShortLinkConfiguration.cs, ShortLinkRepository.cs, DependencyInjection.cs (connection string `ConnectionStrings:Default`, default `Data Source=urlshortener.db`), and generate migration `InitialCreate` into Persistence/Migrations/ with `dotnet ef migrations add`
  - **AC**: schema exactly as data-model: `Code` TEXT NOT NULL max 30; `NormalizedCode` TEXT NOT NULL max 30 with "UNIQUE INDEX `IX_ShortLinks_NormalizedCode`"; `IsCustomAlias` NOT NULL; `OriginalUrl` TEXT NOT NULL max 2048; `CreatedAtUtc` NOT NULL; `ClickCount` NOT NULL default 0. No column for IP address, user agent, or any other visitor data (FR-021). `CreatedAtUtc` uses a value converter that marks values read from SQLite as `DateTimeKind.Utc`. `TryAddAsync` returns `false` (no exception) when SQLite reports a unique violation (`SqliteErrorCode == 19`) and rethrows anything else
  - **Changes during implementation**: unique violation detected by the extended code `SqliteExtendedErrorCode == 2067` (`SQLITE_CONSTRAINT_UNIQUE`), not the generic 19, so NOT NULL or other constraint errors are not misreported as duplicates; a rejected entity is detached so the retry loop can reuse the context; design-time `AppDbContextFactory` added so migrations do not depend on the API host (T007); `ClickCount` has no database default (the entity always writes 0)
  - **Tests**: tests/UrlShortener.IntegrationTests/Persistence/ShortLinkRepositoryTests.cs against a temp SQLite file with migrations applied — add then find by normalized code; second add with same normalized code in different case returns `false` and leaves one row; `CreatedAtUtc` read back has `Kind == DateTimeKind.Utc`
  - **Sign-off**: DB migration (review up/down)
- [x] T007 Wire the API host in src/UrlShortener.Api/Program.cs, src/UrlShortener.Api/appsettings.json, src/UrlShortener.Api/appsettings.Development.json: register Application + Infrastructure, bind `ShortLinks:PublicBaseUrl` (`http://localhost:8080` in appsettings.json as the Docker/default value, `http://localhost:5058` in appsettings.Development.json), `TimeProvider.System`, apply migrations on startup when Development or `Database:MigrateOnStartup=true`, remove `UseHttpsRedirection()` (R13); create tests/UrlShortener.IntegrationTests/ApiFactory.cs (environment `Testing`, not the factory's default `Development`, so production-like error handling is what gets tested; temp SQLite file per factory, `MigrateOnStartup=true`, base URL `http://localhost:5058`, hook to replace services, file deleted on dispose) and move tests/UrlShortener.IntegrationTests/HealthTests.cs onto it
  - **AC**: `dotnet run` creates and migrates `urlshortener.db`; app fails fast on an invalid `PublicBaseUrl`; existing health test passes on `ApiFactory`
  - **Tests**: tests/UrlShortener.IntegrationTests/StartupTests.cs — migrated `ShortLinks` table exists; invalid base URL makes host startup throw
  - **Changes during implementation**: `AddInfrastructure()` now reads the connection string when the `DbContext` is created instead of at registration — the test host's configuration overrides arrive after `Program` registers services, so every test factory was silently sharing a default `urlshortener.db` (found by a cross-class test failure); `StartupTests` asserts the factory's own database is used. Base URL validated via `IValidateOptions` + `ValidateOnStart` (from T005). Migration helper `MigrateDatabaseAsync` lives in Infrastructure
- [x] T008 Add global error handling in src/UrlShortener.Api/ErrorHandling/GlobalExceptionHandler.cs and src/UrlShortener.Domain/Links/LinkValidationException.cs; register `AddProblemDetails()`, `UseExceptionHandler()`, `UseStatusCodePages()` in src/UrlShortener.Api/Program.cs
  - **AC** (research R7): `LinkValidationException(field, message)` → 400 `ValidationProblemDetails` with `errors[field]`; `AliasAlreadyExistsException` → 409 ProblemDetails "Alias already in use"; any other exception → 500 ProblemDetails with a generic detail and no stack trace outside Development; an unmatched route returns a 404 `application/problem+json` body
  - **Tests**: tests/UrlShortener.IntegrationTests/ErrorHandlingTests.cs — unmatched path `/api/nope` → 404 `application/problem+json`. The 400/409/500 mappings are asserted end-to-end once endpoints exist (T012: 400 and 500, T017: 409), so no test-only endpoint is added to production code
  - **Changes during implementation**: mappings are also proven now, without touching production code, by a throwing controller that lives in the test project and is plugged into the test host with `AddApplicationPart` (400 field errors, 409, generic 500 with no exception type/message/stack trace); every ProblemDetails response carries `traceId` (added via `CustomizeProblemDetails`, since the MVC writer path did not include it); `/health` verified unaffected by `UseStatusCodePages`

**Checkpoint**: Foundation ready — user stories can start

---

## Phase 3: User Story 1 — Shorten a long URL (Priority: P1) 🎯 MVP

**Goal**: `POST /api/links` turns a valid long URL into a generated short code and short URL.

**Independent Test**: POST a valid URL → 201 with 7-char code, `shortUrl` on the configured base URL, `Location` header, `clickCount` 0; each malicious input → 400 and nothing stored.

- [x] T009 [P] [US1] Implement URL validation in src/UrlShortener.Domain/Links/DestinationUrl.cs, applying data-model rules 1–7 in order to the trimmed input, field `url`
  - **AC**: "Required, not whitespace"; "Length ≤ 2048"; "Parses as an absolute URI"; "Scheme is `http` or `https`" (allowlist after parsing); "Has a host"; "No user info"; "Host ≠ configured public base host (case-insensitive, any scheme/port)"; missing-scheme input (no `://`) gets the hint "Did you mean https://…?"
  - **Tests**: tests/UrlShortener.UnitTests/Domain/DestinationUrlTests.cs — `[Theory]` over every rejected example in data-model.md (`javascript:alert(1)`, `data:text/html,…`, `file:///etc/passwd`, `ftp://x`, `www.example.com/page`, `www.example.com:8080/page`, `localhost:8080`, `/some/path`, `http:///path`, `https://google.com@evil.example/login`, `https://user:pass@site.example`, `HTTP://LOCALHOST:5058/x`, empty, whitespace, 2049 chars); accepted: http, https, 2048 chars exactly, surrounding whitespace trimmed
  - **Sign-off**: input validation / security
  - **Changes during implementation**: the stored value is the parsed, normalized URL (`Uri.AbsoluteUri`), not the raw trimmed input, so the redirect target is exactly what was validated (no parser differential with browsers); length is re-checked after normalization. The `https://` hint is given only when the input has no `://` and becomes a valid http(s) URL with the prefix, so `javascript:alert(1)` gets no misleading hint. Backslash inputs are handled per platform by `System.Uri` (rejected or escaped on Unix) and a test asserts a raw backslash is never stored
- [x] T010 [P] [US1] Implement `Base62CodeGenerator` in src/UrlShortener.Infrastructure/Links/Base62CodeGenerator.cs using `RandomNumberGenerator.GetString(GeneratedCode.Alphabet, GeneratedCode.Length)`; register in Infrastructure DI
  - **AC**: no use of `System.Random`
  - **Tests**: tests/UrlShortener.UnitTests/Infrastructure/Base62CodeGeneratorTests.cs — length 7; only Base62 characters; 1,000 generated codes contain no duplicates
- [x] T011 [US1] Implement `LinkService.CreateAsync` (generated-code path) in src/UrlShortener.Application/Links/ILinkService.cs and src/UrlShortener.Application/Links/LinkService.cs, plus src/UrlShortener.Application/DependencyInjection.cs (moved from T005; adds the two Microsoft.Extensions abstractions packages — sign-off: new dependencies)
  - **AC**: validates URL with the configured base host; generates code, `TryAddAsync`; on `false` retries with a new code, max 5 attempts in total, then throws `CodeGenerationFailedException`; `ShortUrl` = `PublicBaseUrl` + `/` + code; `CreatedAtUtc` from `TimeProvider`; same URL twice → two different links; logs "link created" with the code only via `[LoggerMessage]` (no URL, no IP)
  - **Changes during implementation**: packages added to Application: `Microsoft.Extensions.Options` 8.0.2 (brings `DependencyInjection.Abstractions`) and `Microsoft.Extensions.Logging.Abstractions` 8.0.3 (both MIT, already present transitively via EF Core; sign-off). Until T017 a supplied `customAlias` is rejected with a 400 instead of being silently ignored
  - **Tests**: tests/UrlShortener.UnitTests/Application/CreateLinkTests.cs — happy path fields; one collision then success (scripted generator); 5 collisions → exception and generator called exactly 5 times; generated code equal to an existing alias ignoring case is treated as a collision (FR-016a); invalid URL → `LinkValidationException` and repository untouched
- [x] T012 [US1] Add `POST /api/links` in src/UrlShortener.Api/Controllers/LinksController.cs with src/UrlShortener.Api/Contracts/CreateLinkRequest.cs and src/UrlShortener.Api/Contracts/LinkResponse.cs, matching contracts/openapi.yaml
  - **AC**: thin controller (maps request → `CreateLinkCommand` → `ILinkService`, no `DbContext`); 201 with `LinkResponse` and `Location: /api/links/{code}`; `[ProducesResponseType]` for 201, 400, 409, 500; model-binding errors (e.g. missing `url`) use the same camelCase field keys as domain errors (`errors.url`, not `errors.Url`) via `SystemTextJsonValidationMetadataProvider`
  - **Tests**: tests/UrlShortener.IntegrationTests/CreateLinkTests.cs — 201 body fields, 7-char Base62 code, `createdAtUtc` ending in `Z`, and `Location` header; same URL twice → different codes; `[Theory]` of malicious/malformed URLs → 400 with `errors.url` and zero rows stored; `www.example.com/page` → hint text; forged `Host: evil.example` → `shortUrl` still starts with `http://localhost:5058/`; missing body / missing `url` → 400 with `errors.url`; with an `IShortCodeGenerator` that always returns the same taken code swapped in → 500 ProblemDetails with a generic detail and no stack trace (FR-007, R7)

  - **Changes during implementation**: no controller-wide `[Produces("application/json")]` — it forced `application/json` onto `[ApiController]`'s automatic 400s, breaking the single ProblemDetails format (SC-006); the 201 declares its content type instead. An empty request body yields a 400 ProblemDetails with an `errors` entry for the body, not `errors.url`
**Checkpoint**: US1 works on its own (MVP: links can be created)

---

## Phase 4: User Story 2 — Visit a short URL and be redirected (Priority: P1)

**Goal**: `GET /{code}` redirects with 302 and counts the click best-effort.

**Independent Test**: seed a link (via US1 or directly in the database), GET `/{code}` → 302 to the exact original URL with `Cache-Control: no-store`; unknown code → 404 ProblemDetails; a failing click recorder never changes the 302.

- [x] T013 [US2] Implement click recording and resolution: src/UrlShortener.Infrastructure/Links/DbClickRecorder.cs (single atomic `ExecuteUpdateAsync` `ClickCount + 1`) and `LinkService.ResolveForRedirectAsync` in src/UrlShortener.Application/Links/LinkService.cs
  - **AC**: lookup by normalized code + `ShortLink.Matches`; returns original URL or `null`; recorder awaited inside `try/catch` that logs via `[LoggerMessage]` (code only) and swallows every exception except `OperationCanceledException` (FR-015a, research R6)
  - **Changes during implementation**: cancellation propagates only when the *request* token is cancelled (`catch ... when (cancellationToken.IsCancellationRequested)`); an `OperationCanceledException` raised by the recorder itself (e.g. its own timeout) is treated like any other failure and the redirect continues. Added an integration test for the real `DbClickRecorder` (3 clicks → stored count 3). Failure isolation mutation-checked
  - **Tests**: tests/UrlShortener.UnitTests/Application/ResolveLinkTests.cs — found → URL and recorder called once; throwing recorder → URL still returned and failure logged; unknown → `null`, recorder not called; generated code in wrong case → `null`; captured log messages for a recorder failure contain the code but not the original URL (FR-021, constitution IV — no IP is available to the service layer by design)
  - **Sign-off**: redirect path
- [x] T014 [US2] Add src/UrlShortener.Api/Controllers/RedirectController.cs: `[HttpGet("{code:regex(^[[A-Za-z0-9_-]]{{3,30}}$)}")]`, `[ApiExplorerSettings(IgnoreApi = true)]`, 302 via `Redirect(url)` plus `Cache-Control: no-store`, `NotFound()` otherwise
  - **AC**: `/health` and `/api/...` routes are unaffected; no `DbContext` in the controller
  - **Tests**: tests/UrlShortener.IntegrationTests/RedirectTests.cs (client with `AllowAutoRedirect = false`) — 302 with exact `Location` and `Cache-Control: no-store`; unknown code → 404 problem+json; generated code in wrong case → 404; path failing the constraint (e.g. `/a.b`) → 404; after 3 redirects the stored `ClickCount` is 3 (read via a DbContext scope from `ApiFactory`); with a throwing `IClickRecorder` swapped in, redirect is still 302 (SC-003a)
  - **Sign-off**: redirect path
- [x] T015 [US2] Add concurrent click counting test in tests/UrlShortener.IntegrationTests/ConcurrencyTests.cs
  - **AC / Tests**: 20 parallel `GET /{code}` → all 302 and stored `ClickCount` exactly 20 (SC-003, no lost increments)
  - **Verification**: passed 5/5 runs; mutation check with a read-then-write recorder stored 1 instead of 20, so the test does detect lost updates

**Checkpoint**: US1 + US2 = create and follow links

---

## Phase 5: User Story 3 — Choose a custom alias (Priority: P2)

**Goal**: optional `customAlias` on create, unique ignoring case, resolvable in any case.

**Independent Test**: create with `team-offsite` → 201 with that code; `/TEAM-OFFSITE` redirects; `Team-Offsite` again → 409.

- [x] T016 [P] [US3] Implement alias rules in src/UrlShortener.Domain/Links/CustomAlias.cs, field `customAlias`
  - **AC**: "Length 3–30"; "Only `A–Z a–z 0–9 - _`"; "Not reserved (case-insensitive): `api`, `health`, `swagger`, `admin`, `static`, `assets`"
  - **Changes during implementation**: surrounding whitespace trimmed (consistent with `url`); empty/whitespace alias rejected rather than treated as absent; reserved words match only the whole alias (`api-docs` is allowed)
  - **Tests**: tests/UrlShortener.UnitTests/Domain/CustomAliasTests.cs — rejects `ab`, 31 chars, `my alias`, `über`, `a.b`, `a/b`, `API`, `Health`; accepts `abc`, 30 chars, `team-offsite`, `Team_2026`
  - **Sign-off**: input validation
- [x] T017 [US3] Add the alias path to `LinkService.CreateAsync` in src/UrlShortener.Application/Links/LinkService.cs and `customAlias` to src/UrlShortener.Api/Contracts/CreateLinkRequest.cs
  - **AC**: alias validated, stored with original case, `IsCustomAlias = true`; one `TryAddAsync`, `false` → `AliasAlreadyExistsException` (no retry); `null`/omitted alias → generated path unchanged
  - **Tests**: tests/UrlShortener.UnitTests/Application/CreateLinkWithAliasTests.cs — alias used as code with case kept; taken alias → exception; invalid alias → `LinkValidationException`. tests/UrlShortener.IntegrationTests/CustomAliasTests.cs — 201 with alias; `Team-Offsite` after `team-offsite` → 409 ProblemDetails with title and detail; alias equal to an existing generated code ignoring case → 409; reserved/invalid alias → 400 `errors.customAlias`; `GET /TEAM-OFFSITE` → 302
- [x] T018 [US3] Add the alias race test to tests/UrlShortener.IntegrationTests/ConcurrencyTests.cs
  - **AC / Tests**: 10 parallel creates with the same alias → exactly one 201, nine 409, never 500; exactly one row stored (SC-005)
  - **Verification**: requests alternate the alias's letter case to also exercise case-insensitive uniqueness; passed 8/8 runs. No mutation check: simulating the bug would require dropping the unique index via a migration

**Checkpoint**: aliases work alongside generated codes

---

## Phase 6: User Story 4 — View a link's total click count (Priority: P2)

**Goal**: `GET /api/links/{code}` returns details and the click count without counting a click.

**Independent Test**: create, redirect 3 times, GET details → `clickCount` 3; GET details again → still 3.

- [x] T019 [US4] Implement `LinkService.GetDetailsAsync` in src/UrlShortener.Application/Links/LinkService.cs and `GET /api/links/{code}` in src/UrlShortener.Api/Controllers/LinksController.cs
  - **AC**: same matching rule as redirect; 200 `LinkResponse` or 404 ProblemDetails; no credentials; never calls `IClickRecorder`; `[ProducesResponseType]` for 200 and 404
  - **Tests**: tests/UrlShortener.IntegrationTests/LinkDetailsTests.cs — new link → `clickCount` 0 and exactly the five fields `code`, `shortUrl`, `originalUrl`, `createdAtUtc` (ending in `Z`), `clickCount` (FR-018a); after 3 redirects → 3; reading details twice does not change the count; alias found in any case; unknown → 404 problem+json; `Location` from create resolves to this endpoint
  - **Changes during implementation**: lookup + case rules extracted into one private `FindAsync` shared by redirect and details, so the two cannot drift apart

**Checkpoint**: all link features complete

---

## Phase 7: User Story 5 — Check service health (Priority: P3)

**Goal**: `/health` reflects database availability.

**Independent Test**: running service → 200 `Healthy`; unreachable database → 503 `Unhealthy`.

- [ ] T020 [US5] Implement src/UrlShortener.Infrastructure/Health/DatabaseHealthCheck.cs (`Database.CanConnectAsync()`), register on the existing `AddHealthChecks()`
  - **AC**: no new package (research R10)
  - **Tests**: extend tests/UrlShortener.IntegrationTests/HealthTests.cs — healthy → 200 body `Healthy`; connection string pointing at a non-existent directory → 503 body `Unhealthy`

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T021 [P] Add the OpenAPI drift test in tests/UrlShortener.IntegrationTests/OpenApiContractTests.cs (research R14)
  - **AC / Tests**: fetch `/swagger/v1/swagger.json` (this test alone switches `ApiFactory` to the `Development` environment, where Swagger is enabled) and assert `POST /api/links` documents 201, 400, 409, 500 and `GET /api/links/{code}` documents 200, 404, matching contracts/openapi.yaml
- [ ] T022 [P] Create requests.http at the repository root covering quickstart scenarios 1–12, and delete the template src/UrlShortener.Api/UrlShortener.Api.http
  - **AC**: runnable top to bottom with the VS Code REST Client against `http://localhost:5058`
- [ ] T023 Run the quickstart validation (specs/001-greenfield-core/quickstart.md): manual scenarios 1–12, Postman import of contracts/openapi.yaml, SC-004 latency loop
  - **AC**: every scenario gives the expected result; measured p95 recorded (real number) for the scenario doc
- [ ] T024 Write docs/scenarios/01-greenfield.md with sections: 1 requirement as given, 2 interpretation and clarifications (link spec.md Clarifications), 3 impact analysis (n/a for greenfield; state why), 4 task decomposition (link this file), 5 execution (commits, AI involvement, notable decisions), 6 validation (real test counts, quickstart results, measured p95), 7 risks and trade-offs, 8 outcome and next steps
- [ ] T025 Open PR `001-greenfield-core` → `main` using .github/pull_request_template.md; complete sign-off for the migration (T006), redirect path (T013, T014), validation (T009, T016), and dependencies (T001); merge after CI is green

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001–T003)**: none; T002 and T003 parallel after T001
- **Foundational (T004–T008)**: after Setup; T004 → T005 → T006 → T007 → T008 (each builds on the previous layer)
- **User stories**: after Foundational, in priority order
- **Polish (T021–T025)**: after US1–US5; T023 → T024 → T025

### User Story Dependencies

- **US1 (P1)**: Foundational only
- **US2 (P1)**: Foundational; integration tests seed links through US1's endpoint, so run after US1 in this single-developer plan
- **US3 (P2)**: extends US1's create path (T017 edits `LinkService.CreateAsync` and the request DTO); T016 can be done any time after Foundational
- **US4 (P2)**: needs US2 for the click-count assertions
- **US5 (P3)**: Foundational only; independent of US1–US4

### Within Each Task

Tests first (or alongside), then implementation, then build/format/test, then commit (see Definition of Done).

### Parallel Opportunities

- T002 ∥ T003
- T009 ∥ T010 (Domain rule vs. Infrastructure generator)
- T016 ∥ any US1/US2 task (pure Domain rule)
- T020 ∥ US3/US4 (separate files)
- T021 ∥ T022

---

## Parallel Example: User Story 1

```bash
# Different projects, no shared files:
Task: "T009 [US1] DestinationUrl rules + DestinationUrlTests"
Task: "T010 [US1] Base62CodeGenerator + Base62CodeGeneratorTests"
# Then sequentially:
Task: "T011 [US1] LinkService.CreateAsync + CreateLinkTests"
Task: "T012 [US1] POST /api/links + integration CreateLinkTests"
```

---

## Implementation Strategy

### MVP First

1. Setup + Foundational (T001–T008)
2. US1 (T009–T012) → **validate**: create links via curl/Postman
3. US2 (T013–T015) → **validate**: create + follow links (the minimum useful shortener)

### Incremental Delivery

4. US3 aliases (T016–T018) → US4 details (T019) → US5 health (T020)
5. Polish (T021–T025): contract drift test, requests.http, quickstart validation, scenario doc, PR

### Time box

Target ≈ 4 hours for T001–T025 (≈ 10 minutes per task). If behind schedule, keep every test
task; T021 (drift test) and T022 (requests.http) are the first to slip into the Phase 3 window.

---

## Notes

- One task = one reviewed commit; never implement several tasks in one unattended run
- [P] = different files, no dependency on an incomplete task
- Sign-off items are collected in the PR checklist (T025)
- Constitution deviations (sync click write, no rate limiting, partial CI) are tracked in plan.md Complexity Tracking, not fixed here
