# ADR-008: Keep the stored click total alongside per-click events

- **Status**: Accepted
- **Date**: 2026-10-11
- **Deciders**: Engineer (feature 002 Clarification Q1); implemented in T005, T011, T014
- **Related**: spec FR-015, FR-017, Clarification Q1 · research R5, R7 · [ADR-005](005-async-click-recording.md)

## Context

Feature 001 stores each link's total as `ShortLinks.ClickCount`. Feature 002 adds a `ClickEvents`
table with one row per click. The total could now be **derived** (`COUNT(*)` of a link's events) or
**kept** as a stored number. Two facts decide it:

- Links created under feature 001 already have totals but no events: those clicks happened before
  per-click history existed. Deriving the total would silently reset them to zero (FR-015: existing
  totals must be preserved).
- The total is read on every details and stats request, while events grow without limit (no
  retention policy yet).

## Decision

**Keep `ClickCount` as the stored all-time total**, and keep it consistent with the events from now
on: the background writer stores a batch of events and increases each affected link's `ClickCount`
by that link's number of events **in the same transaction** (both or neither). Statistics report the
stored total as `totalClicks` (FR-017), while the daily and referrer breakdowns are aggregated from
the events (research R7).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Derive the total from `ClickEvents` | Loses every click counted before this feature (violates FR-015); the total query cost grows with history |
| Derive it, after back-filling synthetic events for old totals | Invents click times and referrers that never existed, so the daily and referrer statistics would be false |
| Stored total updated separately from the events (two transactions) | A failure between them lets the total and the events drift apart permanently |
| Database trigger on `ClickEvents` insert | Hides business logic in SQLite-specific DDL outside EF Core migrations' normal model; harder to test and to move to another database |

## Consequences

**Positive**

- Existing totals survive the migration unchanged (migration test on a seeded feature 001 database:
  `ClickCount` 7 is still 7).
- Reading the total is a single indexed row read.
- For clicks recorded after this feature, total and events agree by construction (one transaction).

**Negative / accepted**

- **For older links the breakdowns can sum to less than the total**: the difference is the clicks
  from before history existed. This is documented in the spec's assumptions and in the contract's stats endpoint
  description ("daily counts and referrers cover clicks recorded since").
- The total is denormalised: any future code path that writes `ClickEvents` must also increase
  `ClickCount` in the same transaction. Today `ClickEventStore` is the only writer.
- Reclaiming an expired alias deletes the old link's row (with its total) and its events together,
  so a reclaimed code starts from zero (`StatsTests`).

## Verification

`MigrationTests.ExistingLinkKeepsItsDataAndNeverExpires` (legacy total preserved),
`ClickEventWriterTests.BatchStoresEventsAndIncreasesTotalsTogether`,
`ClickStatsReaderTests.TotalIsTheStoredCountIncludingClicksFromBeforeHistoryWasKept`.