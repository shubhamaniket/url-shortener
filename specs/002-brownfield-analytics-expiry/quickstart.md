# Quickstart & Validation: Feature 002

Validates expiry, statistics, and off-path click recording end-to-end. Contract:
[contracts/http-api.md](contracts/http-api.md) and [contracts/openapi.yaml](contracts/openapi.yaml).
Build, test and run commands are unchanged from
[feature 001's quickstart](../001-greenfield-core/quickstart.md). Applying the new migration to an
existing development database keeps all existing links and counts.

## Manual validation scenarios

Run the API (`dotnet run --project src/UrlShortener.Api --launch-profile http`), then:

| # | Scenario | Steps | Expected |
|---|---|---|---|
| 1 | Existing links still work | Follow and read details of a link created before this feature | 302; details show `expiresAtUtc: null` and the previous `clickCount` |
| 2 | Expiring link | POST with `"expiresAt"` 2 minutes ahead using an offset (e.g. `+05:30`) | 201; `expiresAtUtc` is the UTC equivalent |
| 3 | Before expiry | `curl -i localhost:5058/<code>` | 302 |
| 4 | After expiry | Wait past the expiry, repeat #3 | 410 ProblemDetails, `Cache-Control: no-store`; `clickCount` unchanged |
| 5 | Expiry validation | POST with `"expiresAt":"2026-12-31T23:59:00"` (no zone), then with a past time | 400 `errors.expiresAt` for both |
| 6 | Reclaim | After #4, POST a new link with `customAlias` set to the expired code | 201; the code now redirects to the new URL; stats start from zero |
| 7 | Statistics | Follow a link 3× with `-H 'Referer: https://news.example/a?u=1'` and 2× without; GET `/api/links/<code>/stats` | `totalClicks` 5, today's entry 5, 30 daily entries, `topReferrers` `news.example` 3 then `(direct)` 2 |
| 8 | Eventual consistency | Read details immediately after a redirect, then again within 5 s | Count catches up within 5 seconds |
| 9 | Privacy | Inspect `ClickEvents` (`sqlite3 src/UrlShortener.Api/urlshortener.db "SELECT * FROM ClickEvents"`) | Only host, UA family, time and link id; no IP, path, query or raw user agent |
| 10 | Unknown stats | GET `/api/links/zzzzzzz/stats` | 404 ProblemDetails |

## Latency with analytics impaired (SC-002)

Re-run feature 001's SC-004 loop (200 redirects after a warm-up) in normal operation, and in the
automated test that blocks the click writer. Expected: p95 under 100 ms in both, with no
measurable difference caused by analytics.
