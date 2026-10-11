# Architecture

How the URL shortener is built, how a request flows through it, and how it was developed with an AI
agent under engineer control. Decisions and their alternatives are in the [ADRs](adr/README.md).

## Components

```mermaid
flowchart LR
  subgraph Client
    B["Browser<br/>(web UI)"]
    C["API client<br/>(curl, Postman)"]
  end

  subgraph Service["ASP.NET Core 8 service (one process, one container)"]
    direction TB
    MW["Pipeline: exception handler → ProblemDetails,<br/>static files (web UI), rate limiter"]
    API["Api<br/>LinksController · RedirectController<br/>health check"]
    APP["Application<br/>LinkService (create, resolve, details, stats)"]
    DOM["Domain<br/>ShortLink · ClickEvent · DestinationUrl ·<br/>CustomAlias · ExpiryTime · ReferrerHost · UserAgentFamily"]
    subgraph INF["Infrastructure"]
      REPO["ShortLinkRepository<br/>ClickStatsReader"]
      GEN["Base62CodeGenerator"]
      Q["ChannelClickRecorder<br/>(bounded queue, 10 000)"]
      W["ClickEventWriter<br/>(background service, batches ≤ 500)"]
    end
  end

  DB[("SQLite<br/>ShortLinks · ClickEvents")]

  B --> MW
  C --> MW
  MW --> API --> APP --> DOM
  APP -. interfaces .-> REPO & GEN & Q
  Q --> W
  REPO --> DB
  W --> DB
```

## Layers and the dependency rule

`Api → Application → Domain`; `Infrastructure` implements interfaces declared in `Application`.
Nothing depends on `Api`, and `Domain` has no package dependencies ([ADR-001](adr/001-architecture.md)).

| Layer | Responsibility | Does not |
|---|---|---|
| **Domain** | Rules as small types: URL validation (http/https only, no credentials, not the service itself), alias rules and reserved words, zone-required expiry, referrer reduced to a host, browser family, entity invariants | Know about HTTP, EF Core or time sources |
| **Application** | Use cases in `LinkService`: create (generated code with retries, or alias with reclaim of expired aliases), resolve for redirect (found / not found / expired, queue a click), details, statistics; interfaces for storage, code generation, click recording, statistics | Touch the database or HTTP directly |
| **Infrastructure** | EF Core + SQLite (repository, migrations, unique-index duplicate detection), cryptographic code generator, in-memory click queue and background writer, statistics queries | Contain business rules |
| **Api** | Thin controllers (HTTP ↔ use cases), ProblemDetails errors, rate-limit policies, options validation, health check, serving the built web UI | Contain business logic or data access |

## Request pipeline

Order in `Program.cs`, and why:

1. **Exception handler + status-code pages** → every error, including routing 404s, is ProblemDetails
   with a `traceId`.
2. **Static files** (`index.html` at `/`, files under `/assets`) → the web UI; no catch-all route,
   which would collide with `/{code}` ([feature 004 plan](../specs/004-web-ui/plan.md), R1).
3. **Rate limiter** → per-client budgets by endpoint attribute; a rejected request never reaches a
   controller, so it has no effect ([ADR-009](adr/009-rate-limiting.md)).
4. **Controllers** → `api/links` (create, details, stats) and `/{code}` (redirect, route-constrained
   to 3–30 safe characters); `/health`.

## Main flows

### Create a link

```mermaid
sequenceDiagram
  autonumber
  participant C as Client
  participant API as LinksController
  participant S as LinkService
  participant D as Domain rules
  participant R as Repository
  participant DB as SQLite

  C->>API: POST /api/links {url, customAlias?, expiresAt?}
  Note over API: rate limiter: create budget
  API->>S: CreateAsync(command)
  S->>D: DestinationUrl.Parse, CustomAlias.Parse, ExpiryTime.Parse
  D-->>S: valid values (or 400 field errors)
  alt generated code
    loop up to 5 attempts
      S->>R: TryAddAsync(new code)
      R->>DB: INSERT (unique index on NormalizedCode)
      DB-->>R: ok, or unique violation (2067)
    end
  else custom alias
    S->>R: TryAddAsync(alias)
    opt taken by an expired link
      S->>R: TryReplaceExpiredAsync (conditional DELETE + INSERT, one transaction)
    end
  end
  S-->>API: LinkDetails
  API-->>C: 201 Created + Location
```

### Follow a link (click recorded off the redirect path)

```mermaid
sequenceDiagram
  autonumber
  participant C as Browser
  participant RC as RedirectController
  participant S as LinkService
  participant DB as SQLite
  participant Q as Click queue
  participant W as ClickEventWriter

  C->>RC: GET /{code}  (Referer, User-Agent)
  Note over RC: rate limiter: redirect budget
  RC->>S: ResolveForRedirectAsync(code, referer, user agent)
  S->>DB: indexed lookup by normalized code
  alt expired
    S-->>RC: Expired
    RC-->>C: 410 Gone (no click)
  else found
    S->>Q: TryRecord(ClickEvent: host + browser family only)
    Note right of Q: in memory, never blocks<br/>when full, the oldest is dropped and counted
    S-->>RC: Found(url)
    RC-->>C: 302 Location, Cache-Control: no-store
  end
  W-->>Q: read as events arrive
  W->>DB: one transaction: INSERT events, ClickCount += n (skip deleted links)
```

The redirect never waits for analytics: p95 0.5–0.9 ms on the running service, and still 302 with the
writer blocked or storage failing ([ADR-005](adr/005-async-click-recording.md)).

### Statistics

`GET /api/links/{code}/stats` resolves the code with the same rules as redirects (expiry ignored),
then reads the stored total and runs two grouped queries in SQLite: clicks per UTC day for the last
30 days (padded to 30 entries) and the all-time top 5 referrer hosts, `(direct)` for none
([ADR-008](adr/008-stored-click-count.md)).

## Data model

```mermaid
erDiagram
  ShortLinks ||--o{ ClickEvents : "has (cascade delete)"
  ShortLinks {
    long Id PK
    string Code "as created"
    string NormalizedCode "lower-cased, UNIQUE"
    bool IsCustomAlias
    string OriginalUrl "≤ 2048"
    datetime CreatedAtUtc
    datetime ExpiresAtUtc "nullable"
    long ClickCount "stored all-time total"
  }
  ClickEvents {
    long Id PK
    long ShortLinkId FK
    datetime OccurredAtUtc "indexed with ShortLinkId"
    string ReferrerHost "host only, nullable"
    string UserAgentFamily "Bot/Edge/Firefox/Chrome/Safari/Other"
  }
```

No IP address, full referrer URL or raw user agent is stored anywhere.

## How it was built: AI-assisted, engineer-led

```mermaid
flowchart LR
  K["Constitution<br/>9 principles"] --> SP["specify"]
  SP --> CL["clarify<br/>(engineer answers)"]
  CL --> PL["plan + research<br/>(decisions, alternatives)"]
  PL --> TK["tasks<br/>(criteria, named tests)"]
  TK --> AN["analyze<br/>(consistency)"]
  AN --> T["one task"]
  T --> G["gates: build (warnings = errors),<br/>format, all tests, vulnerabilities,<br/>mutation checks"]
  G --> RV{"engineer review"}
  RV -- "changes" --> T
  RV -- "approve<br/>(+ sign-off if high impact)" --> CM["commit + AI log entry"]
  CM --> T
  CM --> PR["PR at phase end<br/>CI + CodeQL"]
```

Sign-off points: database migrations, the redirect path, input validation and security controls, new
dependencies, data deletion. Details in [ADR-007](adr/007-spec-driven-development.md) and the
[AI usage log](ai-usage-log.md).

## Scaling path

The prototype is deliberately single-instance. To grow it:

| Today | Next step | Why |
|---|---|---|
| SQLite file | PostgreSQL on Amazon RDS / Aurora (EF Core provider switch, regenerated migrations) | Concurrent writers, replication, backups ([ADR-004](adr/004-sqlite.md)) |
| Indexed lookup per redirect | Distributed cache (Amazon ElastiCache for Redis/Valkey) for code → URL, invalidated on expiry/reclaim | Fewer database reads on the hot path |
| In-memory rate-limit budgets | Shared store (ElastiCache) plus AWS WAF rate-based rules at the edge | Budgets that hold across instances ([ADR-009](adr/009-rate-limiting.md)) |
| In-memory click queue (at-most-once) | Amazon SQS (or Kinesis at high volume) feeding a separate analytics worker and store | Durable, at-least-once analytics; independent scaling ([ADR-005](adr/005-async-click-recording.md)) |
| One container | Several ECS Fargate tasks behind an Application Load Balancer, trusted-proxy configuration, migrations as a release step | Availability and throughput ([ADR-010](adr/010-container-deployment.md)) |
| Redirects served by the API | Amazon CloudFront in front, with edge redirects for hot links | Latency close to users |
