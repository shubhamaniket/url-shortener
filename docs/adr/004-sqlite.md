# ADR-004: SQLite with EF Core for the prototype, with a migration path

- **Status**: Accepted
- **Date**: 2026-10-10 (feature 001, research R9)
- **Related**: constitution VIII (migrations only) · ADR-005, ADR-010

## Context

Reviewers must run the service with no setup. The data model is small (links, click events), the
service is a single instance, and the schema had to evolve safely (feature 002 added a column and a
table to a database already holding data).

## Decision

- **SQLite through EF Core 8** (`Microsoft.EntityFrameworkCore.Sqlite`), database file configured by
  `ConnectionStrings:Default`; WAL journal mode (set by EF Core when it creates the database).
- **EF Core migrations are the only schema path** (`InitialCreate`, `AddExpiryAndClickEvents`),
  applied on startup in Development or with `Database:MigrateOnStartup=true` (tests, Docker);
  `dotnet-ef` pinned in a local tool manifest.
- Uniqueness and atomic updates are pushed into the database (unique index, `ClickCount = ClickCount + n`,
  conditional delete for reclaims), not into application locks.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| SQL Server / PostgreSQL in Docker | Production-grade, but a second service and setup for reviewers |
| In-memory provider | Not a real database: no unique-index or transaction semantics to test against |
| Dapper + hand-written SQL | No migrations; schema changes by hand (constitution VIII) |

## Consequences

- Zero setup; tests run against real SQLite files (one per test host), so constraints and
  transactions are exercised for real.
- **One writer at a time** and **one node**: fine for the prototype; the background click writer
  batches writes to suit it (ADR-005).
- **Migration path**: the EF Core model is provider-neutral apart from SQLite-specific error codes
  (2067 for unique violations) and DateTime-as-text conversions; moving to PostgreSQL or SQL Server
  means switching the provider package, regenerating migrations from the model, mapping the
  unique-violation code, and copying data.
- Feature 002's migration was tested on a database created by feature 001 (counts preserved), and
  both rollback paths were tested.