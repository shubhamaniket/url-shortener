# HTTP API Contract: Greenfield Core

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

All errors are RFC 7807 ProblemDetails (`application/problem+json`). Validation errors use the
`ValidationProblemDetails` shape with an `errors` object keyed by field name. No stack traces
outside Development.

The machine-readable version of this contract is [openapi.yaml](openapi.yaml) (OpenAPI 3.0.3,
importable into Postman). This file and `openapi.yaml` are the reviewed source of truth and must
change together; the Swagger UI at `/swagger` is generated from the code and must match them
(research R14).

## POST /api/links — create a short link

**Request** (`application/json`):

```json
{ "url": "https://example.com/some/long/path?x=1", "customAlias": "team-offsite" }
```

| Field | Required | Notes |
|---|---|---|
| `url` | yes | Rules in data-model `DestinationUrl` |
| `customAlias` | no | Rules in data-model `CustomAlias`; omit or `null` for a generated code |

**Responses**

| Status | When | Body |
|---|---|---|
| 201 Created | Link created | `LinkResponse`; `Location: /api/links/{code}` |
| 400 Bad Request | Invalid/missing `url` or invalid `customAlias` | ValidationProblemDetails |
| 409 Conflict | Alias already in use (ignoring case) | ProblemDetails |
| 500 Internal Server Error | Could not generate a unique code after 5 attempts, or unexpected error | ProblemDetails, generic detail |

`LinkResponse`:

```json
{
  "code": "aZ3kP9q",
  "shortUrl": "http://localhost:5058/aZ3kP9q",
  "originalUrl": "https://example.com/some/long/path?x=1",
  "createdAtUtc": "2026-10-10T13:45:00Z",
  "clickCount": 0
}
```

400 example:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "url": ["The URL must start with http:// or https://. Did you mean https://www.example.com/page?"] }
}
```

409 example:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Alias already in use",
  "status": 409,
  "detail": "The alias 'team-offsite' is already taken."
}
```

## GET /{code} — redirect

| Status | When | Headers / body |
|---|---|---|
| 302 Found | Code exists (generated: exact case; alias: any case) | `Location: <originalUrl>`, `Cache-Control: no-store` |
| 404 Not Found | Unknown code, or a path that fails the route constraint `^[A-Za-z0-9_-]{3,30}$` | ProblemDetails |

A click-recording failure never changes this response (FR-015a). Not shown in Swagger.

## GET /api/links/{code} — link details

| Status | When | Body |
|---|---|---|
| 200 OK | Code exists (same matching rules as redirect) | `LinkResponse` |
| 404 Not Found | Unknown code | ProblemDetails |

No credentials required (FR-018a). Does not change `clickCount` (FR-018).

## GET /health — health check

| Status | When | Body |
|---|---|---|
| 200 OK | Service up and database reachable | `Healthy` (text/plain) |
| 503 Service Unavailable | Database unreachable | `Unhealthy` (text/plain) |

## Not in this feature

Rate limiting (429) is deferred to feature 003; expiry (410) and `/api/links/{code}/stats` to
feature 002.
