# Quickstart & Validation: Feature 003

Validates per-client rate limiting end to end. Contract: [contracts/http-api.md](contracts/http-api.md).
Build, test and run commands are unchanged from
[feature 001's quickstart](../001-greenfield-core/quickstart.md). No migration.

Run the API (`dotnet run --project src/UrlShortener.Api --launch-profile http`), then:

| # | Scenario | Steps | Expected |
|---|---|---|---|
| 1 | Create budget | `for i in $(seq 1 11); do curl -s -o /dev/null -w '%{http_code} ' -X POST localhost:5058/api/links -H 'Content-Type: application/json' -d '{"url":"https://example.com/'$i'"}'; done` | Ten `201`, then `429` |
| 2 | 429 details | Repeat one create with `curl -i` | `429`, `Retry-After: <1–60>`, `application/problem+json`, title "Too many requests" |
| 3 | Nothing created | Count links before/after (`sqlite3 src/UrlShortener.Api/urlshortener.db "SELECT COUNT(*) FROM ShortLinks"`) | Grew by exactly 10 |
| 4 | Separate budgets | While creates are rejected, follow a link and read its details | `302` and `200` |
| 5 | Redirect burst | `for i in $(seq 1 70); do curl -s -o /dev/null -w '%{http_code} ' localhost:5058/<code>; done` | About 60 `302` (plus refill), then `429` |
| 6 | Recovery | Wait `Retry-After` seconds, retry | Succeeds |
| 7 | Health exempt | `for i in $(seq 1 200); do curl -s -o /dev/null -w '%{http_code} ' localhost:5058/health; done` | All `200` |
| 8 | Logs | Look at the console after #1 | "Rate limit exceeded for policy create …"; no IP address anywhere |
| 9 | Invalid config | Start with `RateLimiting__Create__PermitLimit=0` | Startup fails naming the setting |

## Latency (SC-004)

Re-run feature 001's 200-redirect loop with `RateLimiting__Redirect__BurstLimit=1000` (so the loop
stays within budget): p95 under 100 ms, as in feature 002 (0.5 ms).
