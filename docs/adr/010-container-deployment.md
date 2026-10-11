# ADR-010: One container serving the API and the web UI, with SQLite on a volume

- **Status**: Accepted
- **Date**: 2026-10-11
- **Deciders**: Engineer (Phase 5: Docker and CI decisions recorded as an ADR rather than a spec)
- **Related**: [ADR-009](009-rate-limiting.md) (client identity behind proxies) · feature 004 plan R1
  (the API serves the built UI) · constitution IV (no secrets), VI (simplicity), VIII (migrations)

## Context

Reviewers should be able to run the whole system with one command, without installing .NET or Node.
The system is an ASP.NET Core 8 API with a SQLite database and a static React page (feature 004) that
calls only the public API. It is a single-instance prototype: in-memory click queue (ADR-005) and
in-memory rate-limit budgets (ADR-009).

## Decision

1. **One image, three build stages** (`Dockerfile`):
   - `node:22-alpine` builds the web UI (`npm ci`, `npm run build`) into the API's `wwwroot`;
   - `mcr.microsoft.com/dotnet/sdk:8.0` restores and publishes the API (Release) including that
     `wwwroot`;
   - `mcr.microsoft.com/dotnet/aspnet:8.0` runs it. Base images are pinned to a major version and
     updated by Dependabot (`docker` ecosystem).
2. **One origin**: the API serves the page at `/` (feature 004 R1), so there is no CORS, no second
   web server and no proxy configuration.
3. **Non-root**: the runtime stage runs as the image's built-in unprivileged `app` user (`APP_UID`);
   the only writable path is the data directory.
4. **SQLite on a named volume** (`/data/urlshortener.db`): data survives `docker compose down` /
   `up`; `docker compose down -v` resets it.
5. **Migrations on start** (`Database__MigrateOnStartup=true`, the existing switch): EF Core migrations
   remain the only way the schema changes (constitution VIII); a fresh volume gets the full schema.
6. **Configuration by environment variables** in `docker-compose.yml` (port 8080, public base URL,
   connection string); no secrets exist, and none are baked into the image.
7. **Demo data through the public API**: a one-shot `seed` service (a small shell script in a `curl`
   image) waits for `/health`, then creates a few links and follows them with different `Referer`
   headers, so statistics are not empty on first look. It goes through the real create, redirect,
   queue and writer path, and it is idempotent (skips if the demo alias already exists).
8. **CI builds the image** on every pull request (build only, no push), so a broken Dockerfile is
   caught before merge.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Two containers (API + nginx for the UI) | Second server and a proxy or CORS setup for a one-page client; more moving parts for no benefit at this scale |
| Serve the UI from a CDN / separate host | Cross-origin configuration, and the UI would need a configured API address |
| Run as root | Unnecessary privilege; the official images ship an unprivileged user |
| PostgreSQL container | A second service and a provider change for a prototype; ADR-004 keeps SQLite with a documented migration path |
| Seed by SQL inserts or a seeding endpoint | Bypasses validation and the click pipeline (or adds server code only for demos); the API is the honest way in |
| Apply migrations with a separate `dotnet ef` step | Needs the SDK at runtime or an extra container; the existing startup switch already does it safely for one instance |
| Image `HEALTHCHECK` | The ASP.NET runtime image has no `curl`/`wget`; adding one enlarges the image. The seed service and orchestrators can call `/health` directly |

## Consequences

**Positive**

- `docker compose up --build` gives the full system at `http://localhost:8080` with data that
  persists, demo statistics, and no local .NET or Node install.
- The image contains only the published app and its static files, running unprivileged.
- The same build is checked in CI on every pull request.

**Negative / accepted**

- **Single instance only**: the click queue and rate-limit budgets are in memory, and SQLite allows
  one writer; scaling out would need a shared queue, shared budgets and another database.
- **Client addresses inside Docker**: with Docker Desktop's port forwarding, all browser requests can
  appear to come from one gateway address, so local users share one rate-limit budget. Harmless for
  a demo; a real deployment behind a reverse proxy needs trusted-proxy configuration first (ADR-009).
- **Demo statistics are all "today"**: clicks cannot be back-dated through the API; the 30-day view
  fills in over time.
- **Migrations on start** suit one instance; several instances starting together would need a
  separate migration step.
- **No TLS** in the container: HTTPS belongs to the hosting proxy (feature 001, research R13).
