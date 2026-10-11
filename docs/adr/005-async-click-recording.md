# ADR-005: Asynchronous click recording with a bounded channel (at-most-once)

- **Status**: Accepted
- **Date**: 2026-10-11
- **Deciders**: Engineer (approved in feature 002 plan, decision 1); implemented in T010–T013, T022
- **Related**: spec FR-011–FR-015, SC-001, SC-002 · research R4, R5, R11 · constitution Principle V ·
  [ADR-008](008-stored-click-count.md)

## Context

Feature 001 counted a click by awaiting `UPDATE ShortLinks SET ClickCount = ClickCount + 1` inside
the redirect. Failures were caught, so a broken database never failed a redirect, but a slow one
still slowed it: the database sat on the hot path. Feature 001 recorded this as its one deviation
from constitution Principle V ("the redirect path must be fast and must not fail because analytics
fails").

Feature 002 adds per-click analytics (time, referrer host, browser family), so each redirect now
writes more, not less. The redirect must stay fast (p95 under 100 ms, SC-002) and must succeed when
analytics storage is slow, failing or overloaded (SC-001). Clicks must appear within 5 seconds in
normal operation (FR-012). It is a single-node prototype on SQLite, which allows one writer at a time.

## Decision

1. **The redirect only enqueues.** It builds a privacy-reduced `ClickEvent` (only the referrer host
   and browser family survive `ClickEvent.Create`; the client IP never reaches the Application layer)
   and calls `IClickRecorder.TryRecord`, a non-blocking write to an in-memory
   `Channel<ClickEvent>`. No database I/O happens on the redirect path.
2. **The channel is bounded**: capacity 10 000 (`ClickRecording:QueueCapacity`), `DropOldest` when
   full, single reader. A full queue drops its oldest event and counts the drop; the redirect never
   waits.
3. **A hosted `ClickEventWriter` stores events** as soon as they arrive, in batches of up to 500. Each
   batch is one transaction: skip events whose link no longer exists, insert the events, and
   increase each link's `ClickCount` by its number of events ([ADR-008](008-stored-click-count.md)).
4. **Delivery is at-most-once.** A batch that fails to store is logged (count and exception, never
   click data) and discarded; the writer carries on with the next batch. Nothing is retried.
5. **Shutdown flushes with a budget.** A stop request does not abort the batch in progress or the
   queue: the writer keeps storing for up to `ClickRecording:ShutdownFlushTimeout` (5 s), then logs
   once how many clicks were not stored.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep the synchronous update (feature 001) | Database latency stays on the redirect path; violates Principle V and SC-002 with per-click writes |
| `Task.Run` (fire-and-forget) per click | Unbounded concurrency, unobserved failures, and a request-scoped `DbContext` used after the request ends (rejected in feature 001, R6) |
| Unbounded channel | Memory grows without limit while storage is stuck |
| `DropNewest` / `DropWrite` when full | Loses the freshest clicks, which matter most for the "last 30 days" view |
| `Wait` when full | Puts storage back on the redirect path under load: the exact failure being removed |
| Retry failed batches (at-least-once) | Needs idempotent events or deduplication and a retry store; duplicates would inflate totals. Out of scope for a prototype where losing a few clicks is acceptable (spec assumption, feature 001 Clarification Q1) |
| External queue (Redis, RabbitMQ, Kafka) | New infrastructure and dependencies for a single-node prototype (constitution VI) |

## Consequences

**Positive**

- Redirects do no analytics I/O. With the writer blocked in storage and the queue full, 49 further
  redirects all returned 302 while nothing was stored; in-process p95 was 0.35–0.73 ms over five
  runs (`AnalyticsResilienceTests`). With storage failing on every batch, 50 of 50 redirects
  succeeded (SC-001).
- Memory is bounded: at most 10 000 queued events.
- Batching turns many small writes into few transactions, which suits SQLite's single writer.
- Constitution Principle V's deviation from feature 001 is closed (scenario doc §5).

**Negative / accepted risks**

- **Clicks can be lost**, and losses are logged as counts:
  - events dropped while the queue is full ("N queued clicks were dropped…");
  - a batch whose storage fails ("Storing a batch of N clicks failed…");
  - clicks still queued or in flight when the shutdown budget runs out ("N clicks were not stored
    before shutdown");
  - everything in memory on a crash or a kill without graceful shutdown (not logged).
- **Counts are eventually consistent** (normally well under the 5 seconds of FR-012). Tests that read
  a count right after redirecting must wait for it (`Eventually`), and "no click was recorded" is
  checked by waiting for a later marker click, since the queue is first-in, first-out.
- **Drop logs are delayed while the writer is blocked**: drops are counted at once but reported after
  the next batch completes.
- **Clicks for a retired link are skipped.** When an expired alias is reclaimed, its old link and
  history are deleted; clicks for it still queued are filtered out by the writer rather than
  failing the batch or being attached to the new link.
- **Single instance only.** The queue lives in one process. Scaling out would need a shared queue or
  per-instance writers against a database that allows concurrent writers.

## Verification

`ChannelClickRecorderTests` (ordering, newest kept, never blocks), `ClickEventWriterTests` (batches of
500, deleted-link filter, failure recovery without click data in logs, shutdown flush, flush budget
with a single loss report), `ClickRecordingTests` (stored row holds only host, family and time; 302
when storage throws), `AnalyticsResilienceTests` (blocked writer, failing storage).