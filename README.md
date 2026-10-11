# URL Shortener

[![CI](https://github.com/shubhamaniket/url-shortener/actions/workflows/ci.yml/badge.svg)](https://github.com/shubhamaniket/url-shortener/actions/workflows/ci.yml)
[![CodeQL](https://github.com/shubhamaniket/url-shortener/actions/workflows/codeql.yml/badge.svg)](https://github.com/shubhamaniket/url-shortener/actions/workflows/codeql.yml)

A URL shortener built with ASP.NET Core 8, EF Core + SQLite and a small React UI, developed
spec-first with an AI coding agent under explicit engineer review. Create short links (random codes
or custom aliases, optional expiry), follow them (302, or 410 once expired), and see per-link
statistics: total clicks, clicks per day for 30 days, and top referrers. Clicks are recorded off the
redirect path, every public endpoint is rate limited per client, and no IP address is ever stored.

**Contents:** [Features](#features) · [Quick start](#quick-start) · [Using the API](#using-the-api) ·
[Configuration](#configuration) · [Running the tests](#running-the-tests) ·
[Why this tech stack](#why-this-tech-stack) · [Project structure](#project-structure) ·
[Known limitations](#known-limitations) · [Documentation map](#documentation-map)

## Features

| Area | What it does |
|---|---|
| Create | `POST /api/links` with a URL (http/https only, validated), optional custom alias (3–30 chars, unique ignoring case) and optional expiry (ISO 8601 with a time zone) |
| Redirect | `GET /{code}` → **302** with `Cache-Control: no-store`; **410 Gone** after expiry; **404** for unknown codes |
| Details & statistics | `GET /api/links/{code}` and `GET /api/links/{code}/stats` (total, 30 UTC days, top 5 referrers incl. `(direct)`) |
| Analytics | Each redirect queues a click (time, referrer **host only**, browser family); a background writer stores them in batches. Redirects never wait for, or fail because of, analytics |
| Expiry | Expired links stop redirecting; an expired custom alias can be reclaimed (old link and history deleted atomically) |
| Abuse protection | Per-client budgets: create 10/min, redirect 120/min (bursts of 60), details/stats 60/min → **429** with `Retry-After` |
| Errors | Every error is RFC 7807 ProblemDetails with a `traceId`; validation errors keyed by field |
| Web UI | One page at `/`: shorten and copy, look up details and statistics, plain-language errors, keyboard and screen-reader friendly |
| Health | `GET /health` (service + database) |

## Quick start

### A. Docker (recommended: one command, nothing else to install)

Requires [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker Engine with Compose v2).

```bash
git clone https://github.com/shubhamaniket/url-shortener.git
cd url-shortener
docker compose up --build
```

| Open | What |
|---|---|
| http://localhost:8080 | Web UI (look up `demo-docs` to see seeded statistics) |
| http://localhost:8080/api/links | API |
| http://localhost:8080/health | Health check |

The first start builds the image (about a minute), applies database migrations and seeds four demo
links with clicks through the public API. Data lives in the `shortener-data` volume.

| To… | Run |
|---|---|
| Stop (data kept) | `docker compose down` |
| Stop and delete all data (reseeds on next start) | `docker compose down -v` |
| Follow the API logs | `docker compose logs -f api` |

The container runs in Production mode, so Swagger is not exposed there; use option B for Swagger.

### B. Without Docker (local development, with Swagger and hot reload)

Requires the **.NET 8 SDK** (8.0.4xx; the 9.0 SDK also works) and **Node.js 22** (20.19+ works).

```bash
git clone https://github.com/shubhamaniket/url-shortener.git
cd url-shortener

# Terminal 1 — API (Development: creates and migrates the SQLite database automatically)
dotnet run --project src/UrlShortener.Api --launch-profile http
#   API      http://localhost:5058
#   Swagger  http://localhost:5058/swagger

# Terminal 2 — web UI with hot reload (proxies API calls to :5058)
cd web
npm ci
npm run dev
#   UI       http://localhost:5173
```

To have the API serve the UI itself instead (one terminal): `cd web && npm ci && npm run build`,
then start the API and open http://localhost:5058/.

The database file is `src/UrlShortener.Api/urlshortener.db`; delete it to start empty.

## Using the API

```bash
# Create (generated code)
curl -s -X POST localhost:5058/api/links -H 'Content-Type: application/json' \
  -d '{"url":"https://example.com/some/long/path"}'

# Create with a custom alias that expires (zone required)
curl -s -X POST localhost:5058/api/links -H 'Content-Type: application/json' \
  -d '{"url":"https://example.com/sale","customAlias":"spring-sale","expiresAt":"2099-12-31T23:59:00+05:30"}'

# Follow (shows the 302 instead of following it)
curl -i localhost:5058/spring-sale

# Details and statistics
curl -s localhost:5058/api/links/spring-sale
curl -s localhost:5058/api/links/spring-sale/stats
```

(With Docker use port **8080**.) More:

- **[`requests.http`](requests.http)**: 34 ready-made requests for VS Code's REST Client, covering every
  feature (validation, conflicts, expiry and 410, reclaim, statistics, rate limits).
- **Swagger UI** at `/swagger` (option B) with example request bodies.
- **OpenAPI contract**: [specs/003-reliability-abuse-protection/contracts/openapi.yaml](specs/003-reliability-abuse-protection/contracts/openapi.yaml)
  (v0.3.0, importable into Postman; an automated test keeps the code and contract in sync).

## Configuration

Settings come from `appsettings.json` and can be overridden with environment variables
(`Section__Key`, e.g. `RateLimiting__Create__PermitLimit=20`).

| Setting | Default | Meaning |
|---|---|---|
| `ShortLinks:PublicBaseUrl` | `http://localhost:8080` (Development: `http://localhost:5058`) | Base for returned short URLs; the request `Host` header is never trusted |
| `ConnectionStrings:Default` | `Data Source=urlshortener.db` | SQLite database file |
| `Database:MigrateOnStartup` | `false` (always on in Development; on in Docker) | Apply EF Core migrations at startup |
| `RateLimiting:Create:PermitLimit` / `WindowSeconds` | 10 / 60 | Creates per client per window |
| `RateLimiting:Redirect:BurstLimit` / `TokensPerSecond` | 60 / 2 | Redirect burst size and refill rate (120/min) |
| `RateLimiting:Read:PermitLimit` / `WindowSeconds` | 60 / 60 | Details + statistics per client per window |
| `ClickRecording:QueueCapacity` | 10000 | Clicks held in memory before the oldest are dropped |
| `ClickRecording:ShutdownFlushTimeout` | `00:00:05` | Time allowed to store queued clicks on shutdown |

Invalid values (e.g. a budget below 1, a non-http base URL) stop the service at startup with a
message naming the setting.

## Running the tests

```bash
# .NET: build with warnings as errors, formatting, 244 unit + 141 integration tests
dotnet build -c Release
dotnet format --verify-no-changes
dotnet test -c Release

# Web UI: lint, 38 tests (incl. an automated accessibility check), production build
cd web && npm ci && npm run lint && npm test && npm run build
```

Details and coverage numbers: [docs/testing.md](docs/testing.md). Integration tests run the real HTTP
pipeline against real SQLite files (one per test host), including
concurrency races, migration on a seeded database, the background click writer, and rate limits.
CI runs all of the above on every pull request, plus a vulnerable-package gate, a Docker image build
and CodeQL (C# and TypeScript).

## Why this tech stack

The guiding principle: pick the simplest tools that fully meet the prototype's needs, keep everything
runnable with one command, and keep a clear path to production.

| Area | Choice | Why | Alternative considered | Trade-off |
|---|---|---|---|---|
| Backend | .NET 8 (LTS), ASP.NET Core | Mature, fast, strongly typed; built-in rate limiting, health checks, ProblemDetails, DI, background services: very few third-party packages | Node.js / Express | More ceremony than lightweight frameworks |
| API style | Attribute-routed controllers | Clear structure, familiar to most .NET teams; attributes for rate-limit policies and response metadata | Minimal APIs | Slightly more boilerplate ([ADR-001](docs/adr/001-architecture.md)) |
| Architecture | Clean architecture (Api / Application / Domain / Infrastructure) | Business rules isolated and unit-testable; brownfield changes stayed local | Single-project app | More projects to navigate for a small service |
| Data access | EF Core 8 with migrations | Versioned, reviewable schema changes (tested on a database created by the previous version) | Dapper | Less control over raw SQL |
| Database | SQLite | Zero setup for reviewers, file-based, full EF Core support | PostgreSQL / SQL Server in Docker | Single writer, single node; migration path in [ADR-004](docs/adr/004-sqlite.md) |
| Click analytics | In-memory bounded channel + background writer (BCL) | Redirect does no database I/O; batches suit SQLite | Message broker | At-most-once, single instance ([ADR-005](docs/adr/005-async-click-recording.md)) |
| Abuse protection | ASP.NET Core rate limiter | Built in, per-client policies by attribute | Third-party package / proxy | In-memory, per instance ([ADR-009](docs/adr/009-rate-limiting.md)) |
| Frontend | React 18 + TypeScript + Vite (one page, no UI framework) | Type-safe calls against the contract, fast build, ~48 KB gzipped | Server-rendered Razor pages | A Node toolchain for non-Docker development |
| Testing | xUnit + WebApplicationFactory; Vitest + Testing Library + axe-core | Real HTTP-level tests against the actual pipeline; component and accessibility tests for the UI | NUnit / Jest | — |
| Packaging | Docker + Compose, one image | One command for everything; the API serves the UI (one origin) | API + nginx containers | Docker needed for the one-command path ([ADR-010](docs/adr/010-container-deployment.md)) |
| CI / quality gates | GitHub Actions, CodeQL, Dependabot | Build, format, tests + coverage, vulnerability gates, image build on every PR | Azure DevOps Pipelines | — |
| Process | Spec-driven development with GitHub Spec Kit | Spec → clarify → plan → tasks → analyze before code, human approval per task | Ad-hoc prompting | Extra upfront time per feature ([ADR-007](docs/adr/007-spec-driven-development.md)) |

**Production evolution (AWS):** the same container on ECS Fargate behind an Application Load Balancer,
PostgreSQL on Amazon RDS, SQS for click analytics, ElastiCache for shared rate-limit budgets and a
redirect cache, with trusted-proxy configuration; see [docs/architecture.md](docs/architecture.md#scaling-path).

## Project structure

```text
src/
  UrlShortener.Domain/          Entities and rules (URL, alias, expiry, referrer host, browser family)
  UrlShortener.Application/     Use cases (LinkService) and interfaces
  UrlShortener.Infrastructure/  EF Core + SQLite, migrations, code generator, click queue + writer, stats queries
  UrlShortener.Api/             Controllers, rate limiting, error handling, health, hosting of the web UI
tests/
  UrlShortener.UnitTests/       Domain and application rules with hand-written fakes
  UrlShortener.IntegrationTests/  Full HTTP pipeline on real SQLite, concurrency, migrations, contract drift
web/                            React + TypeScript UI (built into the API's wwwroot)
specs/                          Spec Kit artifacts per feature (spec, plan, research, contracts, tasks)
docs/                           ADRs, scenario write-ups, AI usage log
docker/, Dockerfile, docker-compose.yml
requests.http                   Sample requests
```

## Known limitations

- **Single instance**: the click queue and rate-limit budgets live in memory; SQLite allows one writer.
- **At-most-once analytics**: clicks queued during overload or lost in a failed batch are dropped
  (and logged), never retried ([ADR-005](docs/adr/005-async-click-recording.md)).
- **Clients are identified by IP address** for rate limiting: users behind one NAT share a budget, and
  behind a reverse proxy trusted-proxy handling must be configured first ([ADR-009](docs/adr/009-rate-limiting.md)).
- **No accounts**: anyone who knows a code can read its statistics (aggregates only, no per-visitor data).
- **No deduplication**: shortening the same URL twice gives two links ([ADR-006](docs/adr/006-idempotency.md)).

## Documentation map

| Document | Contents |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Components, layers, request pipeline, sequence diagrams, data model, AI-assisted workflow, scaling path |
| [docs/testing.md](docs/testing.md) | Test pyramid, real coverage numbers, techniques, what is not tested |
| [docs/adr/](docs/adr/README.md) | 10 architecture decision records |
| [docs/scenarios/01-greenfield.md](docs/scenarios/01-greenfield.md) | Greenfield: building the core service from a requirement |
| [docs/scenarios/02-brownfield.md](docs/scenarios/02-brownfield.md) | Brownfield: impact analysis, expiry, analytics, off-path recording, migration and rollback |
| [docs/scenarios/03-ambiguous.md](docs/scenarios/03-ambiguous.md) | Ambiguous: interpreting "reliable and protected from abuse", rate limiting |
| [docs/ai-usage-log.md](docs/ai-usage-log.md) | Every AI interaction: accepted, edited or rejected, and what the engineer changed |
| [.specify/memory/constitution.md](.specify/memory/constitution.md) | The nine engineering principles every plan is checked against |
| [specs/](specs/) | Spec Kit artifacts for features 001–004 |

### Traceability: requirement → evidence

| Requirement | Evidence in this repository |
|---|---|
| Working prototype, runnable end-to-end | [docker-compose.yml](docker-compose.yml), [Quick start](#quick-start), [requests.http](requests.http) |
| Architecture overview | [docs/architecture.md](docs/architecture.md), [docs/adr/](docs/adr/README.md) |
| Greenfield scenario | [docs/scenarios/01-greenfield.md](docs/scenarios/01-greenfield.md), [specs/001-greenfield-core/](specs/001-greenfield-core/) |
| Brownfield scenario | [docs/scenarios/02-brownfield.md](docs/scenarios/02-brownfield.md), [specs/002-brownfield-analytics-expiry/](specs/002-brownfield-analytics-expiry/), merged PRs #12–#17 |
| Ambiguous scenario | [docs/scenarios/03-ambiguous.md](docs/scenarios/03-ambiguous.md), [specs/003-reliability-abuse-protection/](specs/003-reliability-abuse-protection/), merged PRs #18–#19 |
| Setup instructions | This README ([Quick start](#quick-start), [Configuration](#configuration)) |
| Testing approach | [docs/testing.md](docs/testing.md) (98% line coverage of hand-written code), [CI workflow](.github/workflows/ci.yml) (coverage report uploaded as an artifact) |
| Limitations and trade-offs | [Known limitations](#known-limitations), consequences sections of each [ADR](docs/adr/README.md), risks sections of the scenario docs |
| 1) Requirement understanding | `spec.md` files with their Clarifications sections, e.g. [002](specs/002-brownfield-analytics-expiry/spec.md), [003 interpretation](docs/scenarios/03-ambiguous.md#2-interpretation) |
| 2) Task decomposition | `tasks.md` files with dependencies and order, e.g. [002](specs/002-brownfield-analytics-expiry/tasks.md) |
| 3) Codebase reasoning (brownfield) | [Impact analysis](docs/scenarios/02-brownfield.md#3-impact-analysis), written and verified before any code |
| 4) AI-assisted execution + traceability | [docs/ai-usage-log.md](docs/ai-usage-log.md), `AI-Assisted:` commit trailers |
| 5) Engineering output | [src/](src/), [tests/](tests/), [web/](web/), OpenAPI contract + Swagger, [migrations](src/UrlShortener.Infrastructure/Persistence/Migrations/) |
| 6) Validation and risk control | CI quality gates, mutation checks and validation sections in the scenario docs |
| 7) Controlled oversight | [PR template](.github/pull_request_template.md) with sign-off checklist, PR history, `High-impact:` commit lines |
| 8) Final engineering summary | Engineering summary (Phase 6, in progress) |

## License

[MIT](LICENSE)
