# HTTP API Contract: Feature 002 changes

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md) |
**Machine-readable**: [openapi.yaml](openapi.yaml) (OpenAPI 3.0.3, v0.2.0, Postman-importable)

Feature 002 extends [feature 001's contract](../../001-greenfield-core/contracts/http-api.md). Only
changes are listed; everything else is unchanged. `openapi.yaml` here is the complete current
contract and must change together with this file and the OpenAPI drift test.

## POST /api/links — optional expiry

New optional field `expiresAt`:

```json
{ "url": "https://example.com/flash-sale", "expiresAt": "2026-12-31T23:59:00+05:30" }
```

| Case | Result |
|---|---|
| Zone `Z` or offset, in the future | 201; `expiresAtUtc` = `2026-12-31T18:29:00Z` |
| No zone (`2026-12-31T23:59:00`) | 400 `errors.expiresAt`: a time zone is required |
| Now or in the past | 400 `errors.expiresAt`: must be in the future |
| Custom alias whose current link **has expired** | 201: the code is reclaimed; the expired link and its history are retired |
| Custom alias whose current link has not expired | 409 (unchanged) |

## GET /{code} — 410 for expired links

| Status | When |
|---|---|
| 302 | Link exists and has not expired (unchanged; click recorded in the background) |
| 404 | Unknown code (unchanged) |
| **410** | Link has expired: ProblemDetails "Link expired", `Cache-Control: no-store`, no click recorded |

## GET /api/links/{code} — expiry in details

`LinkResponse` gains `expiresAtUtc` (null when the link never expires). Works for expired links.
`clickCount` is eventually consistent (within 5 seconds of a redirect).

## GET /api/links/{code}/stats — new

| Status | Body |
|---|---|
| 200 | `LinkStatsResponse` (also for expired links) |
| 404 | ProblemDetails (unknown code) |

```json
{
  "code": "aZ3kP9q",
  "totalClicks": 5,
  "dailyClicks": [{ "date": "2026-09-12", "clicks": 0 }, "…28 more…", { "date": "2026-10-11", "clicks": 5 }],
  "topReferrers": [{ "source": "news.example", "clicks": 3 }, { "source": "(direct)", "clicks": 2 }]
}
```

`dailyClicks` always has 30 entries (UTC days, oldest first). `topReferrers` has up to 5 entries
by all-time clicks, highest first, ties alphabetical; `(direct)` means no referrer.
