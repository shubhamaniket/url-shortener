# Quickstart & Validation: Greenfield Core

Validates the feature end-to-end. Contract details: [contracts/http-api.md](contracts/http-api.md)
and [contracts/openapi.yaml](contracts/openapi.yaml).

## Prerequisites

- .NET 8 SDK (the repo also builds with the .NET 9 SDK targeting `net8.0`)
- No database install: SQLite is bundled with the EF Core provider

## Build, test, run

```bash
dotnet tool restore                       # installs the pinned dotnet-ef
dotnet build -c Release                   # warnings are errors
dotnet format --verify-no-changes
dotnet test
dotnet run --project src/UrlShortener.Api --launch-profile http   # http://localhost:5058
```

In Development the database file is created and migrated on startup. With `dotnet run --project`
the working directory is the project folder, so the file is `src/UrlShortener.Api/urlshortener.db`
(gitignored). Delete it to start with an empty database.
Swagger UI: http://localhost:5058/swagger

## Manual validation scenarios

| # | Scenario | Command | Expected |
|---|---|---|---|
| 1 | Create (generated code) | `curl -i -X POST localhost:5058/api/links -H 'Content-Type: application/json' -d '{"url":"https://example.com"}'` | 201, 7-char `code`, `shortUrl` on `http://localhost:5058`, `clickCount` 0, `Location` header |
| 2 | Redirect | `curl -i localhost:5058/<code>` | 302, `Location: https://example.com/` (stored normalized form), `Cache-Control: no-store` |
| 3 | Click count | Repeat #2 twice more, then `curl localhost:5058/api/links/<code>` | `clickCount` 3 |
| 4 | Custom alias | POST with `"customAlias":"team-offsite"`, then `curl -i localhost:5058/TEAM-OFFSITE` | 201, then 302 |
| 5 | Alias conflict | POST again with `"customAlias":"Team-Offsite"` | 409 ProblemDetails |
| 6 | Bad scheme | POST `{"url":"javascript:alert(1)"}` | 400, `errors.url` |
| 7 | No scheme | POST `{"url":"www.example.com/page"}` | 400, hint to add `https://` |
| 8 | User info | POST `{"url":"https://google.com@evil.example"}` | 400 |
| 9 | Self host | POST `{"url":"http://LOCALHOST:5058/x"}` | 400 |
| 10 | Forged host | Scenario 1 with `-H 'Host: evil.example'` | `shortUrl` still on `http://localhost:5058` |
| 11 | Unknown code | `curl -i localhost:5058/zzzzzzz` | 404 ProblemDetails |
| 12 | Health | `curl -i localhost:5058/health` | 200 `Healthy` |

## Testing with Postman

1. Postman → **Import** → select `specs/001-greenfield-core/contracts/openapi.yaml`
   → *Generate collection from imported APIs*. Each endpoint becomes a request, and the named
   request examples (generated code, custom alias, invalid scheme, missing scheme, user info,
   self host, reserved alias, alias case conflict) are available as example bodies.
2. Set the collection variable `baseUrl` to `http://localhost:5058` (or `http://localhost:8080`
   for Docker).
3. For `GET /{code}`, turn off **Settings → Automatically follow redirects** on that request to
   see the `302` and its `Location` header instead of the destination page.
4. Walk through the manual validation scenarios above.

The OpenAPI file is validated with:

```bash
uvx openapi-spec-validator specs/001-greenfield-core/contracts/openapi.yaml
```

## SC-004 latency check (manual)

```bash
for i in $(seq 1 200); do curl -s -o /dev/null -w '%{time_total}\n' localhost:5058/<code>; done \
  | sort -n | awk '{a[NR]=$1} END {print "p95:", a[int(NR*0.95)]}'
```

Expected: p95 under 0.100 s on a local machine. Record the measured value in
`docs/scenarios/01-greenfield.md`.
