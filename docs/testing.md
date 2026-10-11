# Testing

What is tested, at which level, how to run it, real coverage numbers, and what is deliberately not
tested. All figures are from runs on 2026-10-11 on the `main` code after feature 004.

## Test pyramid

| Level | Count | Tooling | What it proves |
|---|---|---|---|
| **Unit** (.NET) | **244** | xUnit, hand-written fakes, fixed clock | Domain rules and use cases in isolation: every validation rule, case rules, expiry, privacy reduction, retries, reclaim, failure isolation |
| **Integration** (.NET) | **141** | xUnit + `WebApplicationFactory`, real SQLite file per test host | The real HTTP pipeline end to end: status codes, ProblemDetails, concurrency, migrations, background writer, rate limits, contract drift |
| **Web UI** | **38** | Vitest + Testing Library + jsdom + axe-core | Error mapping, time zones, lookup parsing, both forms, focus management, accessibility |
| **End to end (manual + scripted)** | quickstarts, `requests.http` (34 requests) | curl, REST Client, browser, Docker | The running service and container behave as documented |

### Unit tests (244)

| Area | Tests | Examples |
|---|---|---|
| Domain: URL rules | 32 | Every malicious/malformed example from the data model (`javascript:`, missing scheme with hint, credentials in the URL, the service's own host, length limits) |
| Domain: entity | 29 | Code normalization, case-sensitive vs any-case matching, expiry invariants |
| Domain: expiry input | 28 | `+05:30`/`-08:00`/`Z` accepted and converted; no zone, past, now, impossible dates, `+15:00` rejected |
| Domain: alias rules | 25 | Length, characters, reserved words in any case |
| Domain: privacy reduction | 41 | Referrer → host only (credentials, path, query dropped; IDN to ASCII); real user-agent strings → six families; `ClickEvent` never holds raw values |
| Application | 78 | Create with collisions and retry exhaustion, alias conflicts, reclaim (incl. a lost race), resolve (found/not found/expired, recording failures isolated, cancellation), details, statistics ("today" in UTC even on a UTC+14 server) |
| Infrastructure | 11 | Code generator alphabet/length; click queue order, drop-oldest, never blocking, concurrent writers |

### Integration tests (141)

| Area | Tests | Examples |
|---|---|---|
| Persistence | 27 | Repository against real SQLite; migration on a seeded feature 001 database, cascade delete, down/up round trip; background writer (batches ≤ 500, deleted-link filter, failure recovery without click data in logs, shutdown flush); statistics queries (window boundaries to the tick, tie order, SQL grouping runs in the database) |
| Rate limiting | 24 | 11th create → 429 with `Retry-After` and nothing created; failures count; `X-Forwarded-For` ignored; bursts; separate budgets; health never limited; invalid settings stop startup; logs without client addresses |
| Create, aliases, details, redirects, errors | 46 | Status codes and bodies, forged `Host` header, ProblemDetails without stack traces, 302 + `no-store` |
| Expiry, reclaim, statistics | 17 | 410 after the clock moves, reclaim in another case, stats end to end |
| Contract drift | 11 | The generated OpenAPI document matches `contracts/openapi.yaml` (operations, statuses, schemas, examples) |
| Concurrency, resilience, click recording | 7 | 20 parallel redirects counted exactly; 10 parallel claims of an expired alias → exactly one winner; redirects stay 302 with the click writer blocked or storage failing; stored click rows contain no raw headers |
| Startup, health, hosting | 9 | Migrations on start, invalid base URL fails fast, DB health, web UI served at `/` |

## Coverage

Collected with coverlet (`--collect:"XPlat Code Coverage"`), unit and integration runs merged by file:

| Project | Lines (hand-written code) | Branches (hand-written code) |
|---|---|---|
| Domain | 151/153 = **98.7%** | 106/110 = 96.4% |
| Application | 120/121 = **99.2%** | 45/46 = 97.8% |
| Infrastructure | 201/205 = **98.0%** | 20/22 = 90.9% |
| Api | 209/216 = **96.8%** | 51/66 = 77.3% |
| **Total** | 681/695 = **98.0%** | 222/244 = **91.0%** |

Including generated code (EF Core migrations, regex source generators): 1190/1331 lines = 89.4%,
438/480 branches = 91.2%. CI uploads the Cobertura reports as the `test-results-and-coverage`
artifact on every run.

**The 14 uncovered hand-written lines, and why:**

| Lines | Why not covered |
|---|---|
| `RateLimitRejection` fallback when a limiter supplies no retry time (7) | Both limiters used always supply it; the fallback is defensive |
| `AppDbContextFactory` (3) | Used only by the `dotnet ef` design-time tools |
| `DestinationUrl`: empty host; over-long URL after normalization (2) | .NET's URI parser already rejects http(s) URLs without a host; normalization would have to grow a 2048-character URL. Both are defensive |
| `RedirectResolution` record declaration (1) | Compiler-generated record members never called |
| `ClickEventWriter` drain-loop exit in the stop race (1) | A race too narrow to hit reliably; the behaviour that matters (stop finishes queued work) has a deterministic test (feature 002, T011/T022) |

Coverage is a floor, not the goal: the important checks are **mutation checks** (break the code on
purpose and confirm a test fails). Every task recorded them in the AI usage log; survivors were
reported, not hidden (e.g. an equivalent mutant in the statistics window).

## How to run

```bash
dotnet test -c Release                                                    # all .NET tests
dotnet test -c Release --collect:"XPlat Code Coverage" --results-directory TestResults   # with coverage
dotnet test tests/UrlShortener.IntegrationTests --filter "FullyQualifiedName~RateLimiting"  # one area

cd web
npm test              # all web tests once
npx vitest            # watch mode
```

Manual and scripted end-to-end checks: the quickstarts in each `specs/*/quickstart.md`, and
[`requests.http`](../requests.http) (run top to bottom; it paces itself for the rate limits).

## Testing techniques worth knowing

- **Real database, isolated**: each integration test host gets its own SQLite file, so unique
  indexes, transactions and cascades are real, and tests do not interfere.
- **Controlled time**: a fixed clock (unit) and a forward-only adjustable clock (integration) make
  expiry and "today" deterministic.
- **Eventual consistency without sleeps**: counts written in the background are awaited with a
  5-second polling helper; "nothing was recorded" is proven by waiting for a later marker click, since
  the queue is first in, first out.
- **Simulated clients**: a test-only startup filter sets the client address from a header so rate
  limits can be tested per client; production never reads that header.
- **Contract drift test**: the API's generated OpenAPI document is compared with the reviewed contract.
- **Accessibility**: axe-core runs on the whole web page after a real create → view-details flow;
  colour contrast (which axe cannot check in jsdom) was computed from the palette (all ≥ 4.5:1).

## Not tested, and why

| Not tested | Why / what covers the risk instead |
|---|---|
| Load and soak beyond one machine | Single-instance prototype; latency measured on the running service (p95 0.5–0.9 ms over 200 redirects) |
| Real browsers (cross-browser UI) | jsdom component tests + manual checks in a browser; no browser automation dependency added |
| Multi-instance behaviour | Out of scope by design (ADR-005, ADR-009, ADR-010) |
| Process crash losing queued clicks | Accepted at-most-once behaviour (ADR-005) |
| Docker image in automated tests | CI builds the image on every PR; the container was verified by hand (non-root, persistence, seed) |
