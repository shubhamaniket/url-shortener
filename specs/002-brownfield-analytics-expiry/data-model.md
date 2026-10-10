# Data Model: Link Expiry, Click Analytics, and Off-Path Click Recording

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md) | Extends
[feature 001's data model](../001-greenfield-core/data-model.md); only changes are listed here.

## Entity: ShortLink (changed)

| Field | Type | Change | Rules | Source |
|---|---|---|---|---|
| `ExpiresAtUtc` | `DateTime?` (UTC) | **New** | Null = never expires. Set only at creation, strictly after creation time. Expired when `ExpiresAtUtc <= now` | FR-001–FR-003 |
| `ClickCount` | `long` | Semantics clarified | Stored total; increased by the background writer in the same transaction that stores the click events | FR-015, Q1 |

All other fields and invariants are unchanged. New behaviour: `IsExpiredAt(DateTime nowUtc)`.
Factories gain an optional `expiresAtUtc` argument.

## Entity: ClickEvent (new)

One successful redirect. No visitor identity.

| Field | Type | Rules | Source |
|---|---|---|---|
| `Id` | `long` | Auto-increment primary key | — |
| `ShortLinkId` | `long` | FK → `ShortLinks.Id`, cascade delete (retired links take their events) | FR-007, FR-006a |
| `OccurredAtUtc` | `DateTime` (UTC) | Time of the redirect, from `TimeProvider` | FR-007 |
| `ReferrerHost` | `string?` (≤ 253) | Host only, lower-cased; null = no or invalid referrer | FR-008 |
| `UserAgentFamily` | `string` (≤ 16) | One of `Bot`, `Edge`, `Firefox`, `Chrome`, `Safari`, `Other` | FR-009 |

## Value rules (Domain)

### ExpiryTime (FR-002, FR-002a), field `expiresAt`

| # | Rule | Rejected example |
|---|---|---|
| 1 | ISO 8601 date-time with an explicit zone: `yyyy-MM-ddTHH:mm[:ss[.fffffff]](Z\|±HH:mm)` | `2026-10-12T18:00:00` (no zone), `12/10/2026`, `tomorrow` |
| 2 | Strictly after "now" | `2020-01-01T00:00:00Z`, the current instant |

Accepted values are converted to UTC: `2026-10-12T18:00:00+05:30` → `2026-10-12T12:30:00Z`.

### ReferrerHost (FR-008)

| Input `Referer` | Stored |
|---|---|
| `https://News.Example/a?u=42` | `news.example` |
| `https://user:pass@site.example/x` | `site.example` |
| (missing), `""`, `not a url`, `android-app://com.x`, `ftp://f.example` | `null` → shown as `(direct)` |

### UserAgentFamily (FR-009)

Ordered, case-insensitive substring rules (first match wins): `Bot` (`bot`, `crawler`, `spider`,
`slurp`, `facebookexternalhit`, `whatsapp`, `preview`) → `Edge` (`Edg/`) → `Firefox` (`Firefox/`)
→ `Chrome` (`Chrome/`, `CriOS/`) → `Safari` (`Safari/`) → otherwise `Other` (also for missing).

## Database schema (EF Core migration `AddExpiryAndClickEvents`, sign-off required)

`ShortLinks`: add `ExpiresAtUtc` TEXT **NULL** (existing rows: NULL = never expire).

`ClickEvents` (new):

| Column | SQLite type | Constraints |
|---|---|---|
| `Id` | INTEGER | PK, autoincrement |
| `ShortLinkId` | INTEGER | NOT NULL, FK → `ShortLinks(Id)` ON DELETE CASCADE |
| `OccurredAtUtc` | TEXT | NOT NULL (UTC converter, as for `CreatedAtUtc`) |
| `ReferrerHost` | TEXT | NULL, max 253 |
| `UserAgentFamily` | TEXT | NOT NULL, max 16 |

Index `IX_ClickEvents_ShortLinkId_OccurredAtUtc` on (`ShortLinkId`, `OccurredAtUtc`): serves the
daily aggregation and the FK. Down: drop `ClickEvents`, drop `ExpiresAtUtc`.

## State transitions

```text
Active ──(now >= ExpiresAtUtc)──▶ Expired ──(alias request claims the code)──▶ Retired (row + events deleted)
```

Links without an expiry stay Active forever. Expiry is evaluated at request time; nothing runs
in the background to mark links expired.

## Application DTOs

- `CreateLinkCommand(string Url, string? CustomAlias, string? ExpiresAt)`
- `LinkDetails(..., DateTime? ExpiresAtUtc)` (field added)
- `ClickContext(string? Referrer, string? UserAgent)`: raw header values, reduced by Domain rules
- `RedirectResolution`: `Found(string Url)` | `NotFound` | `Expired`
- `LinkStatistics(string Code, long TotalClicks, IReadOnlyList<DailyClicks> Daily, IReadOnlyList<ReferrerClicks> TopReferrers)`
