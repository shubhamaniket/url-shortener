# Architecture Decision Records

Short records of the decisions that shape this service: context, decision, alternatives considered
and consequences.

| ADR | Decision |
|---|---|
| [001](001-architecture.md) | Clean architecture (Api → Application → Domain, Infrastructure) with attribute-routed controllers |
| [002](002-short-code-generation.md) | Random 7-character Base62 codes; uniqueness and case rules enforced by one database index |
| [003](003-redirect-status.md) | 302 redirects with `Cache-Control: no-store` (410 for expired links) |
| [004](004-sqlite.md) | SQLite with EF Core migrations for the prototype, with a migration path |
| [005](005-async-click-recording.md) | Clicks recorded off the redirect path through a bounded in-memory queue (at-most-once) |
| [006](006-idempotency.md) | No URL deduplication or idempotency keys for now |
| [007](007-spec-driven-development.md) | Spec-driven development with Spec Kit and engineer-led AI |
| [008](008-stored-click-count.md) | Keep the stored click total alongside per-click events |
| [009](009-rate-limiting.md) | Per-client rate limiting with the built-in ASP.NET Core limiter |
| [010](010-container-deployment.md) | One container serving the API and web UI, SQLite on a volume |