---

description: "Task list for feature 003 — per-client request budgets (rate limiting)"
---

# Tasks: Per-Client Request Budgets (Rate Limiting)

**Input**: Design documents from `specs/003-reliability-abuse-protection/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md);
interpretation and scope in [docs/scenarios/03-ambiguous.md](../../docs/scenarios/03-ambiguous.md) §2

**Tests**: REQUIRED (constitution II). Each task lists acceptance criteria and the tests that prove
them, written in the same task.

**Regression rule**: the merged features 001 + 002 suite is the baseline (**244 unit + 114
integration on `main` at `076e9be`**). Every task keeps the whole suite green; an existing test may
change only where listed under "Changed tests", with the reason (spec SC-006).

**Granularity**: by engineer decision (plan.md, decision 3) tasks are **coarse**: one capability end
to end per task (configuration, wiring, endpoints, tests). Review, approval and sign-offs per task are
unchanged.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US3
- Sub-bullets: **AC** acceptance criteria, **Tests** required tests, **Changed tests**, **Sign-off**
  high-impact change (constitution VIII)

## Definition of Done (every task)

1. `dotnet build -c Release` (warnings as errors), `dotnet format --verify-no-changes`, `dotnet test`
   (**full suite**), `dotnet list package --vulnerable --include-transitive` all pass
2. One commit per task: Conventional Commit + `Implements Txxx from specs/003-reliability-abuse-protection/tasks.md` + `AI-Assisted:` trailer
3. Task ticked here; entry in `docs/ai-usage-log.md`

---

## Phase 1: Setup

- [ ] T001 Record the regression baseline on this branch before any code change (full suite, build warnings) in docs/scenarios/03-ambiguous.md §6
  - **AC**: counts equal `main` after feature 002 (244 unit + 114 integration, 0 warnings)

---

## Phase 2: User Story 1 — Stop one client from mass-creating links (Priority: P1) 🎯 MVP

**Goal**: per-client create budget; excess rejected with 429 + `Retry-After` + ProblemDetails; nothing created.

**Independent Test**: one client sends more creates than the budget → excess 429, link count grows only by the budget, another client still creates.

- [ ] T002 [US1] Rate-limiting foundation and the **create** policy, end to end, in src/UrlShortener.Api/RateLimiting/ (`RateLimitingOptions`, `RateLimitingOptionsValidator`, `RateLimitPolicies`, `RateLimitRejection`), Program.cs (`AddRateLimiter`, `UseRateLimiter`, options bound with `ValidateOnStart`), appsettings.json (defaults), and `[EnableRateLimiting("create")]` on `LinksController.Create` (research R1–R6)
  - **AC**: create = fixed window "10 per 60 s" (configurable), queue 0; partition = connection remote address or "unknown", forwarded headers not read; 429 with `Retry-After` = lease metadata rounded up, at least 1 (if a limiter supplies no metadata: the policy's window, or one second for the token bucket); body ProblemDetails "Too many requests" (`type` RFC 6585 §4, `traceId`); warning log "Rate limit exceeded for policy {Policy} on {Endpoint}" with the route pattern and no address; invalid settings (< 1) stop startup naming the setting; no global limiter
  - **Tests**: test infrastructure in tests/UrlShortener.IntegrationTests/RateLimiting/: a test-only startup filter that sets the client address from header `X-Test-Client-Ip`, and a factory with small budgets. tests/UrlShortener.IntegrationTests/RateLimiting/CreateRateLimitTests.cs: 11 creates from one client → 10 × 201 then 429 with `Retry-After` 1–60 and problem+json; link count grew by exactly 10; invalid creates (400) count; another client still gets 201; after a short window (test config) the client succeeds again; `X-Forwarded-For` from a throttled client does not escape the budget. tests/UrlShortener.IntegrationTests/RateLimiting/RateLimitingOptionsValidatorTests.cs (each setting < 1 rejected with its name; defaults valid; in the integration project because the unit project does not reference the Api project)
  - **Changed tests**: `ApiFactory` sets very large budgets so existing suites are not throttled (support only, no assertion changes)

---

## Phase 3: User Story 2 — Keep links and statistics available under floods (Priority: P1)

**Goal**: separate per-client budgets for redirects (bursts) and reads (details, stats).

**Independent Test**: one client exceeds the redirect budget → 429 and no click recorded; another client still redirected; the first can still create and read.

- [ ] T003 [US2] **redirect** (token bucket: `Redirect:BurstLimit` 60, `Redirect:TokensPerSecond` 2) and **read** (fixed window 60/60 s) policies; `[EnableRateLimiting("redirect")]` on `RedirectController`, `[EnableRateLimiting("read")]` on `LinksController.Get` and `GetStats`
  - **AC**: burst of `BurstLimit` redirects all 302, the next 429; every redirect outcome counts (302, 404, 410); a rejected redirect records no click; budgets independent per group and per client; health never limited
  - **Tests**: tests/UrlShortener.IntegrationTests/RateLimiting/RedirectAndReadRateLimitTests.cs: burst then 429 with `Retry-After`; unknown and expired codes count; no click stored for rejected redirects (FIFO marker click); exhausted create budget does not block redirect/read and vice versa; read budget covers details and stats together; another client unaffected; 200 health requests from one client all 200
  - **Sign-off**: redirect path

---

## Phase 4: User Story 3 — Operators tune budgets and see rejections (Priority: P2)

**Goal**: configuration, observability and the published contract.

**Independent Test**: changed budget enforced; invalid budget refuses startup; rejection logged by policy without an address; contract lists 429.

- [ ] T004 [US3] Operator-facing completion: `[ProducesResponseType<ProblemDetails>(429, "application/problem+json")]` on the four limited actions; OpenAPI drift test switched to specs/003-reliability-abuse-protection/contracts/openapi.yaml v0.3.0
  - **AC**: generated document lists 429 on `POST /api/links`, `GET /api/links/{code}`, `GET /api/links/{code}/stats`; a configured budget value is enforced; startup with an invalid value fails naming the setting; logs of a rejected request contain the policy name and no client address
  - **Tests**: tests/UrlShortener.IntegrationTests/RateLimiting/RateLimitOperationsTests.cs: host with `RateLimiting:Create:PermitLimit=3` enforces 3; host with `RateLimiting:Read:WindowSeconds=0` fails to start with the setting name; captured logs after a 429 contain "policy create" and not the test client address
  - **Changed tests**: OpenApiContractTests expected statuses gain 429 on the three operations (contract change)

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T005 [P] docs/adr/009-rate-limiting.md (algorithms, client identity and forwarded headers, logging risk, IPv6 /64 and NAT limitations, what was left out and why) and requests.http Part 3 (create budget → 429, health exempt)
  - **AC**: every new request executed against the running app with the expected status
- [ ] T006 Validate and close out: run specs/003-reliability-abuse-protection/quickstart.md scenarios 1–9 and the latency check on the running app; engineer verifies in Swagger/Postman; complete docs/scenarios/03-ambiguous.md §3–8 (impact, decomposition, execution, validation vs baseline, risks, next steps); tick tasks; PR description with the redirect-path sign-off
  - **AC**: all scenarios as expected; p95 redirect under 100 ms within budget; real numbers recorded

**Genuine bug rule** (plan §7 of the project plan): a real defect found during this work gets its own task, fixed test-first with the root cause documented. None is to be invented.

---

## Dependencies & Execution Order

- T001 → T002 (foundation) → T003 → T004 → T005 ∥ T006 (T006 last)
- T003 and T004 both edit `LinksController`; keep them sequential

## Implementation Strategy

1. Baseline (T001)
2. MVP: create budget end to end (T002) → testable: 429 on the 11th create
3. Redirect and read budgets (T003, sign-off) → testable: bursts, separate budgets
4. Operations and contract (T004)
5. ADR, requests.http, validation, scenario doc (T005–T006)
