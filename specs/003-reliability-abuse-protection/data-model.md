# Data Model: Per-Client Request Budgets

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

**No schema change and no migration.** All state is in process memory and disappears on restart.

## Request budget (in memory, per client × group)

| Group | Algorithm (research R2) | Settings (section `RateLimiting`) | Defaults |
|---|---|---|---|
| create | Fixed window | `Create:PermitLimit`, `Create:WindowSeconds` | 10, 60 |
| redirect | Token bucket | `Redirect:BurstLimit`, `Redirect:TokensPerSecond` | 60, 2 (= 120/min) |
| read | Fixed window | `Read:PermitLimit`, `Read:WindowSeconds` | 60, 60 |

**Validation (startup, FR-010)**: every value is an integer ≥ 1; otherwise startup fails with a
message naming the setting, e.g. `RateLimiting:Create:PermitLimit must be at least 1.`

**Partition key (FR-011, FR-011a)**: the connection's remote address as text, or `unknown` when
absent. Held only by the limiter; never stored, never logged. Forwarded headers are not read.

**Lifecycle**: a partition's counters reset at each window (fixed window) or refill continuously
(token bucket); idle partitions are released by the limiter. Nothing is persisted.
