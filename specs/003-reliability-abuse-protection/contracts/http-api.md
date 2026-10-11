# HTTP API Contract: Feature 003 changes

**Feature**: [../spec.md](../spec.md) | **Machine-readable**: [openapi.yaml](openapi.yaml) (OpenAPI
3.0.3, v0.3.0, Postman-importable)

Feature 003 extends [feature 002's contract](../../002-brownfield-analytics-expiry/contracts/http-api.md).
Only changes are listed. `openapi.yaml` here is the complete current contract and changes together
with this file and the OpenAPI drift test.

## Budgets (per client = per connection address; defaults, configurable)

| Group | Operations | Default budget |
|---|---|---|
| create | `POST /api/links` | 10 per minute |
| redirect | `GET /{code}` (any outcome: 302, 404, 410) | 120 per minute sustained, bursts up to 60 |
| read | `GET /api/links/{code}`, `GET /api/links/{code}/stats` | 60 per minute |
| — | `/health`, Swagger | never limited |

Every request in a group counts, whatever its outcome; rejected requests do not. Forwarded headers
(`X-Forwarded-For`) are ignored.

## New response on every limited operation: 429

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 42
Content-Type: application/problem+json

{ "type": "https://tools.ietf.org/html/rfc6585#section-4", "title": "Too many requests", "status": 429,
  "detail": "Request budget for this endpoint exceeded. Retry after 42 seconds.", "traceId": "00-…" }
```

`Retry-After` is in whole seconds, at least 1. A rejected request has no effect: no link created, no
click recorded.
