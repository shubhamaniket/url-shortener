# Research: Link Expiry, Click Analytics, and Off-Path Click Recording

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Impact analysis**:
[docs/scenarios/02-brownfield.md](../../docs/scenarios/02-brownfield.md) §3 | **Date**: 2026-10-11

Each entry: Decision / Rationale / Alternatives considered. Builds on feature 001's research
(`specs/001-greenfield-core/research.md`, "001-R<n>"). No open `NEEDS CLARIFICATION` items remain.

## R1. Expiry input and storage (FR-001–FR-003, FR-002a)

- **Decision**: The create request takes `expiresAt` as an **ISO 8601 string with a mandatory zone**,
  checked by a Domain rule (`ExpiryTime`): it must match
  `yyyy-MM-ddTHH:mm[:ss[.fffffff]](Z|±HH:mm)`, parse as a `DateTimeOffset`, and be strictly later
  than "now" from `TimeProvider`. Stored as `ShortLink.ExpiresAtUtc` (`DateTime?`, UTC); a link is
  expired when `ExpiresAtUtc <= now`. Responses return `expiresAtUtc` in UTC (`Z`).
- **Rationale**: Binding straight to `DateTimeOffset` would silently turn a zone-less
  `2026-10-12T18:00:00` into server-local time, the exact bug Clarification Q5 rules out. Reading
  the raw string lets the Domain reject it with a clear message. The request field is `expiresAt`
  (it may carry any offset); the response field is `expiresAtUtc` (always UTC), matching
  `createdAtUtc`.
- **Alternatives**: `DateTimeOffset` binding (loses "no zone" information); epoch seconds
  (unfriendly, and it still needs a timezone story for users); relative durations (out of scope).

## R2. Distinguishing "expired" from "not found" on the redirect (FR-004)

- **Decision**: `ResolveForRedirectAsync` returns a `RedirectResolution` (`Found(url)`,
  `NotFound`, `Expired`) instead of `string?`. `RedirectController` maps `Expired` to a
  **410 ProblemDetails** ("Link expired") with `Cache-Control: no-store`, and records no click.
  `GetDetailsAsync` and the stats query ignore expiry (FR-005, US3-5).
- **Rationale**: A nullable string cannot carry three outcomes. A small result type keeps the
  controller thin and the rule in the Application layer.
- **Alternatives**: An `ExpiredLinkException` mapped by the global handler (exceptions on the hot
  path for a normal outcome; slower and less explicit); returning the link and letting the
  controller decide (business rule leaks into the Api layer, constitution III).

## R3. Click context and privacy (FR-007–FR-010)

- **Decision**: `RedirectController` passes only the raw `Referer` and `User-Agent` header values
  (never the connection's IP) to the Application layer as a `ClickContext`. Pure Domain functions
  reduce them before anything is stored:
  - `ReferrerHost.From(referer)`: absolute `http`/`https` URI → `IdnHost` lower-cased (≤ 253
    chars); anything else → `null` (shown as `(direct)` in stats).
  - `UserAgentFamily.From(userAgent)`: ordered, case-insensitive substring rules → one of
    **`Bot`, `Edge`, `Firefox`, `Chrome`, `Safari`, `Other`**. `Bot` first (`bot`, `crawler`,
    `spider`, `slurp`, `facebookexternalhit`, `whatsapp`, `preview`), then `Edg/`, `Firefox/`,
    `Chrome/` or `CriOS/`, then `Safari/`; missing or unmatched → `Other`.
- **Rationale**: Reducing data at the edge of the Domain means the raw values never reach
  storage or logs (constitution IV). Ordered rules handle the well-known overlaps (Edge and Chrome
  both contain `Chrome/`; Chrome contains `Safari/`).
- **Alternatives**: A UA-parsing package (new dependency for six buckets, constitution VI);
  storing raw values and reducing them at query time (stores personal data, violates FR-008/FR-009).

## R4. Off-path click recording (FR-011, FR-013) — resolves constitution Principle V

- **Decision**: `IClickRecorder` becomes **non-blocking**: `bool TryRecord(ClickEvent clickEvent)`.
  The Infrastructure implementation `ChannelClickRecorder` (singleton) wraps
  `Channel.CreateBounded<ClickEvent>(capacity 10 000, FullMode = DropOldest, SingleReader = true)`
  with the `itemDropped` callback counting drops. The redirect path builds the event and calls
  `TryRecord` inside the existing failure isolation; it never awaits the database.
- **Rationale**: A bounded channel caps memory under load; `DropOldest` keeps the most recent
  clicks when overloaded, which matter most for the "last 30 days" view; a single reader matches
  SQLite's single writer. The redirect cost becomes one in-memory enqueue.
- **Alternatives**: `Task.Run` per click (unbounded, unobserved failures, scoped `DbContext`
  lifetime bug, rejected in 001-R6); an unbounded channel (memory grows without limit under a
  stuck writer); `DropWrite`/`DropNewest` (loses the freshest data); an external queue (new
  infrastructure, out of scope for a single-node prototype). Trade-off recorded in **ADR-005**.
- **Known limitation**: drops are reported by the writer, so while the writer is completely
  blocked, drop logs appear only when it resumes (FR-013 is still met; noted in ADR-005).

## R5. Background writer and batching (FR-012, FR-014, FR-015)

- **Decision**: `ClickEventWriter : BackgroundService` reads the channel. It waits for the first
  event, then drains up to **500** waiting events into a batch and writes it in **one database
  transaction**: insert the `ClickEvents` rows and `UPDATE ShortLinks SET ClickCount = ClickCount
  + n` per affected link. Before inserting, it keeps only events whose link still exists (one
  `SELECT Id … WHERE Id IN (…)` per batch): a link retired by a reclaim (R6) may still have clicks
  queued, and a foreign-key failure would otherwise discard the whole batch, including other
  links' clicks. Dropped orphan events are counted and logged. On any other failure it logs the
  batch size and the error, discards the batch (at-most-once), and continues. Drop counts from R4 are logged by the writer (not per click).
  On shutdown, it drains and writes the remaining events with a 5-second budget.
- **Rationale**: Writing as soon as events arrive keeps counts fresh (well within FR-012's
  5 seconds) while batching naturally under load. One transaction per batch keeps the stored
  total and the events consistent (Clarification Q1). The writer catches its own exceptions, so
  one bad batch cannot stop the service.
- **Alternatives**: A fixed flush interval (adds latency for no benefit at this scale);
  per-event writes (one transaction per click; SQLite contention); deriving `ClickCount` from
  events (loses legacy totals, rejected in Q1). The stored-total decision is recorded in
  **ADR-008**.

## R6. Reclaiming an expired code (FR-006a, Clarification Q2)

- **Decision**: The alias path stays "insert first". When `TryAddAsync` reports a duplicate, the
  service loads the existing link; if it has **expired**, the repository's
  `TryReplaceExpiredAsync(expired, replacement, now)` runs one transaction:
  `DELETE FROM ShortLinks WHERE Id = @old AND ExpiresAtUtc <= @now` (its `ClickEvents` go with it
  through `ON DELETE CASCADE`), then inserts the replacement. If the delete affects 0 rows or the
  insert hits the unique index, it rolls back and returns `false`, giving the usual 409. A code
  whose link has not expired still returns 409 straight away.
- **Rationale**: The conditional delete makes concurrent claims safe: only the transaction that
  actually removes the expired row can insert, and the unique index rejects any other. Deleting
  rather than renaming the old row is data minimisation: once retired, its history is unreachable
  by design (spec edge cases). Clicks for the old link still queued at that moment are discarded
  by the writer's existence filter (R5), never attached to the new link.
- **Alternatives**: Rename the old row's code to a tombstone (keeps unreachable data; tombstones
  need their own uniqueness scheme); a pre-check before insert (racy); also reclaiming in the
  code generator (rejected in the spec).

## R7. Statistics query (FR-016–FR-022)

- **Decision**: `IClickStatsReader.GetAsync(linkId, todayUtc)` (Infrastructure, EF Core):
  - **Total**: the link's stored `ClickCount`.
  - **Daily**: `ClickEvents` for the link with `OccurredAtUtc >= today − 29 days`, grouped by UTC
    date, then padded in memory to exactly 30 entries, oldest first, `0` for missing days.
  - **Top referrers**: all-time, `GROUP BY ReferrerHost` (`NULL` → `(direct)`), ordered by count
    descending then source ascending, `Take(5)`.
  The service resolves the code with the shared `FindAsync` (same matching rules) and ignores
  expiry; unknown code → `null` → 404.
- **Rationale**: Both aggregations run in the database and use the
  `(ShortLinkId, OccurredAtUtc)` index; padding to 30 days in memory is trivial. "Today" comes
  from `TimeProvider`, so tests are deterministic.
- **Risk to verify in implementation**: EF Core's SQLite translation of grouping by
  `OccurredAtUtc.Date`. Fallback: select `OccurredAtUtc` values within the 30-day window and group
  in memory (bounded by the window), with a test either way.
- **Alternatives**: A pre-aggregated daily table (premature for the prototype); returning raw
  events (exposes per-click data, violates FR-020).

## R8. Response and contract changes (impact analysis §3.3)

- **Decision**: `LinkResponse` gains `expiresAtUtc` (nullable). New `LinkStatsResponse`:
  `code`, `totalClicks`, `dailyClicks[]` (`date` as `yyyy-MM-dd`, `clicks`), `topReferrers[]`
  (`source`, `clicks`). `GET /api/links/{code}/stats` is a new action on `LinksController`.
  Feature 002 owns its contract (`contracts/openapi.yaml`, version 0.2.0); the OpenAPI drift test
  switches to it.
- **Rationale**: Additive changes only; feature 001 clients keep working (FR-006, SC-005).

## R9. Migration `AddExpiryAndClickEvents` (constitution VIII, sign-off)

- **Decision**: Additive EF migration: `ShortLinks.ExpiresAtUtc TEXT NULL`; new `ClickEvents`
  (`Id`, `ShortLinkId` FK → `ShortLinks.Id` **ON DELETE CASCADE**, `OccurredAtUtc`,
  `ReferrerHost` NULL ≤ 253, `UserAgentFamily` ≤ 16) with index
  `IX_ClickEvents_ShortLinkId_OccurredAtUtc`. Down drops the table and the column (EF rebuilds
  the SQLite table for the column drop). A **migration test on a seeded v1 database** migrates to
  `InitialCreate`, inserts a link with clicks, migrates to latest, and checks the link, its
  `ClickCount`, and a null expiry are preserved (plan risk register: "Migration test on seeded DB").
- **Alternatives**: Recreating the database (data loss); manual SQL (constitution VIII).

## R10. Tests that change by design (impact analysis §3.5)

- **Decision**: Tests that read a click count right after redirects now **poll until the expected
  count appears, with a 5-second timeout** (a shared `Eventually` helper), matching FR-012. The
  exact-count guarantees (sequential and 20 concurrent redirects) stay. `ClickRecorderIncrementsStoredCount`
  is replaced by writer tests (batch writes events and totals together; a failing batch does not
  stop the writer; shutdown flushes). The "exactly five fields" details test becomes six fields.
  Every other feature 001 test must pass **unchanged** (regression baseline: 131 unit + 64
  integration on `main`).
- **Rationale**: Changing a test is justified only where the spec changes behaviour (SC-005);
  each such change is listed in tasks.md.

## R11. Shutdown and test hosts

- **Decision**: The writer is a hosted service, so `WebApplicationFactory` test hosts run it too;
  integration tests therefore exercise the real asynchronous path. For the "analytics fails"
  test, the test host replaces the writer's data access with one that throws, and asserts the
  redirect is still 302 and the failure is logged.
- **Rationale**: Testing the real pipeline is what proves FR-011/FR-013, not a synchronous fake.
