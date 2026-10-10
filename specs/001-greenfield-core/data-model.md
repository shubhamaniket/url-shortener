# Data Model: Greenfield Core URL Shortener

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

## Entity: ShortLink (Domain)

A mapping from a short code to an original URL.

| Field | Type | Rules | Source |
|---|---|---|---|
| `Id` | `long` | Auto-increment primary key; internal only, never exposed | R11 |
| `Code` | `string` (3–30) | The code as created: a generated 7-char Base62 code or a custom alias in its original case | FR-006, FR-009, FR-012a |
| `NormalizedCode` | `string` (3–30) | `Code` lower-cased (invariant culture). **Unique.** Set by the entity, never by callers | R3 |
| `IsCustomAlias` | `bool` | `true` when created from a user alias; controls lookup case rules | FR-016 |
| `OriginalUrl` | `string` (≤ 2048) | Validated destination (see rules below), stored trimmed, otherwise unchanged | FR-003–FR-005 |
| `CreatedAtUtc` | `DateTime` (UTC) | Set once at creation from `TimeProvider` | FR-002 |
| `ClickCount` | `long` | Starts at 0; only increased by the click recorder, by exactly 1 per recorded click | FR-015 |

**Invariants (enforced in the Domain constructor/factory)**

- `Code` is either a valid generated code (7 chars, `[A-Za-z0-9]`) or a valid alias (see below).
- `NormalizedCode == Code.ToLowerInvariant()`.
- `OriginalUrl` has passed `DestinationUrl` validation.
- No public setters for `Code`, `OriginalUrl`, `CreatedAtUtc`; links are immutable apart from
  `ClickCount` (link editing is out of scope).

**Lookup rule (FR-016)**: find the row where `NormalizedCode == lower(input)`. It matches if
`IsCustomAlias` is true, or if `Code == input` with ordinal (case-sensitive) comparison.
Otherwise: not found.

## Value rules (Domain)

### DestinationUrl (FR-003, FR-003a, FR-004, FR-005)

Applied in this order to the trimmed input; first failure wins, reported on field `url`:

| # | Rule | Example rejected input |
|---|---|---|
| 1 | Required, not whitespace | `""`, `"   "` |
| 2 | Length ≤ 2048 | 2049-char URL |
| 3 | Parses as an absolute URI | `not a url`, `example.com/page`, `www.example.com/page` (hint: add `https://`) |
| 4 | Scheme is `http` or `https` | `javascript:alert(1)`, `data:text/html,…`, `file:///etc/passwd`, `ftp://x`, `www.example.com:8080/page`, `localhost:8080`, `/some/path` |
| 5 | Has a host | `http:///path` |
| 6 | No user info | `https://google.com@evil.example/login`, `https://user:pass@site.example` |
| 7 | Host ≠ configured public base host (case-insensitive, any scheme/port) | `HTTP://LOCALHOST:5058/x` when base is `http://localhost:5058` |

### CustomAlias (FR-010, FR-011)

Reported on field `customAlias`:

| # | Rule | Example rejected input |
|---|---|---|
| 1 | Length 3–30 | `ab`, 31 chars |
| 2 | Only `A–Z a–z 0–9 - _` | `my alias`, `über`, `a.b`, `a/b` |
| 3 | Not reserved (case-insensitive): `api`, `health`, `swagger`, `admin`, `static`, `assets` | `API`, `Health` |

Uniqueness (FR-012) is **not** a Domain rule; it is enforced by the unique index on
`NormalizedCode` and surfaced as a 409.

## Database schema (SQLite, via EF Core migration `InitialCreate`)

Table `ShortLinks`:

| Column | SQLite type | Constraints |
|---|---|---|
| `Id` | INTEGER | PK, autoincrement |
| `Code` | TEXT | NOT NULL, max 30 |
| `NormalizedCode` | TEXT | NOT NULL, max 30, **UNIQUE INDEX `IX_ShortLinks_NormalizedCode`** |
| `IsCustomAlias` | INTEGER | NOT NULL |
| `OriginalUrl` | TEXT | NOT NULL, max 2048 |
| `CreatedAtUtc` | TEXT | NOT NULL |
| `ClickCount` | INTEGER | NOT NULL (always written as 0 by the entity on insert; no database default) |

The unique index serves both uniqueness (R2/R3) and the redirect lookup, so no second index is
needed. SQLite does not enforce `TEXT` lengths; the 30 / 2048 limits are enforced by the
Domain entity and kept in the schema as documentation for a future move to another database.

`CreatedAtUtc` is stored as TEXT by the SQLite provider and read back with an unspecified
`DateTimeKind`; a value converter marks it as UTC so the API always returns an ISO 8601 value
ending in `Z`. The table deliberately has no column for visitor data such as IP address or user
agent (FR-021). The migration is a high-impact change and needs sign-off in the PR (constitution VIII).

## State transitions

None beyond creation and click counting. Expiry (feature 002) and editing/deletion (out of scope)
are not modelled yet; adding expiry later is an additive nullable column.

## Application DTOs

- `CreateLinkCommand(string Url, string? CustomAlias)`
- `LinkDetails(string Code, string ShortUrl, string OriginalUrl, DateTime CreatedAtUtc, long ClickCount)`
  — `ShortUrl` is built from the configured public base URL (FR-005a).
