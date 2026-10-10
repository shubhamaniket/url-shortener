# Research: Greenfield Core URL Shortener

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-10

Each entry: Decision / Rationale / Alternatives considered. No open `NEEDS CLARIFICATION` items
remain.

## R1. Short code generation

- **Decision**: 7 characters from the Base62 alphabet, generated with
  `RandomNumberGenerator.GetString(alphabet, 7)` (.NET 8 BCL, unbiased, cryptographically secure),
  behind an Application interface `IShortCodeGenerator` implemented in Infrastructure.
- **Rationale**: Meets FR-006 (not guessable or sequential) with no dependency. 62^7 ≈ 3.5 × 10^12
  raw combinations (≈ 7.8 × 10^10 distinct case-insensitive forms, see R3), so collisions are rare
  at prototype scale and handled by R2.
- **Alternatives**: `System.Random` (predictable, rejected by FR-006); counter + Base62 encoding
  (sequential, enumerable); hash of the URL (collisions on truncation, and the same URL must give
  distinct codes per FR-008).

## R2. Uniqueness and collision retry

- **Decision**: The database unique index is the only uniqueness guarantee. Creation attempts an
  insert; Infrastructure translates a SQLite unique-constraint violation (`SqliteException`,
  `SqliteErrorCode == 19`) into a "duplicate" result. For generated codes, the Application layer
  retries with a new code, up to 5 attempts in total, then fails with a 500 (FR-007). For custom
  aliases, a duplicate is a 409 conflict (FR-012); no retry.
- **Rationale**: Check-then-insert races under concurrency (two requests both see "free"). Letting
  the constraint decide makes the alias race (SC-005) correct by construction.
- **Alternatives**: Pre-check with `AnyAsync` before inserting (racy, and an extra round trip);
  application-level locks (single-process only, break with more instances).

## R3. Case rules for aliases vs generated codes (Clarification Q3)

- **Decision**: Store `Code` (as created) and `NormalizedCode` (`Code` lower-cased, invariant
  culture). A **single unique index on `NormalizedCode`** covers all rows, generated and alias.
  Lookup is one indexed query on `NormalizedCode = lower(input)`; the row matches if it is a
  custom alias, or if it is a generated code and `Code` equals the input exactly (ordinal).
- **Rationale**: One database constraint enforces all three rules atomically, including under
  concurrency: alias vs alias ignoring case (FR-012), alias vs generated code (FR-012), and
  generated code vs existing alias (FR-016a). Generated-code lookup stays case-sensitive (FR-016).
- **Trade-off (approved by engineer, 2026-10-10)**: Two *generated* codes that differ only in case
  (`Abc123x`, `abc123x`) cannot both exist; the second is treated as a collision and regenerated.
  This shrinks the distinct generated-code space from ≈3.5 × 10^12 to ≈7.8 × 10^10, which is
  still far beyond prototype scale (at 1M links a new code collides about 1 in 78,000 times and
  is silently retried). The spec's behaviour is unchanged (visiting `abc123x` never resolves
  `Abc123x`); the spec's "Letter case" assumption was reworded accordingly.
- **Alternatives**: Unique index on `Code` plus a partial unique index on `NormalizedCode` for
  aliases only: keeps the full space, but alias-vs-generated and generated-vs-alias collisions
  then need application checks that race. SQLite `COLLATE NOCASE` on `Code`: same effect as the
  chosen design but hides the rule in a collation and makes exact-case generated lookups awkward.

## R4. URL validation (FR-003, FR-003a, FR-004, FR-005, FR-005a)

- **Decision**: A Domain rule (`DestinationUrl`) applied after trimming:
  1. not empty; length ≤ 2048;
  2. `Uri.TryCreate(value, UriKind.Absolute, out uri)` succeeds;
  3. `uri.Scheme` is exactly `http` or `https` (**allowlist**, checked after parsing);
  4. `uri.Host` is not empty;
  5. `uri.UserInfo` is empty;
  6. `uri.Host` is not equal (case-insensitive) to the configured public base URL's host,
     regardless of scheme or port.
  If step 2 or 3 fails and the input has no `://`, the error detail suggests adding `https://`.
- **Rationale**: The allowlist after parsing is what rejects inputs that parse with a bogus scheme
  (`www.example.com:8080/page` → scheme `www.example.com`; `localhost:8080` → scheme `localhost`;
  `/some/path` → `file` on macOS/Linux). `System.Uri` is BCL, so the Domain stays framework-free.
- **Alternatives**: Regex validation (hard to get right, misses parser edge cases); a URL
  validation package (new dependency, constitution VI).

## R5. Public base URL (Clarification Q4)

- **Decision**: `ShortLinks:PublicBaseUrl` setting bound to an options class with
  `ValidateDataAnnotations` + a custom check (absolute http/https) and `ValidateOnStart()`; the app
  fails fast on a bad value. Development default `http://localhost:5058` (the existing launch
  profile); Docker sets `http://localhost:8080` (Phase 5). Short URL = base URL + `/` + `Code`.
  The request `Host` header is never read.
- **Rationale**: Prevents host-header injection; identical output behind proxies and in tests.
- **Alternatives**: `Request.Host` (spoofable, rejected in Q4); forwarded headers middleware
  (still trusts client-influenced data, needs proxy configuration).

## R6. Click counting (Clarification Q1) — temporary synchronous recorder

- **Decision**: Application interface `IClickRecorder.RecordClickAsync(linkId)`. The greenfield
  implementation performs one atomic SQL update,
  `ExecuteUpdateAsync(s => s.SetProperty(l => l.ClickCount, l => l.ClickCount + 1))`, awaited
  inside the redirect use case but wrapped in `try/catch` that logs and swallows every failure
  except cancellation. The redirect result never depends on it.
- **Rationale**: The atomic `UPDATE … SET ClickCount = ClickCount + 1` cannot lose increments under
  concurrent redirects (SC-003); the `try/catch` meets FR-015a/SC-003a now. The interface is the
  seam that feature 002 replaces with a `Channel<T>`-based background recorder.
- **Constitution note**: Principle V forbids synchronous analytics writes on the redirect path.
  This is a deliberate, time-boxed deviation recorded in the plan's Complexity Tracking; it is the
  starting point for the brownfield refactor in feature 002.
- **Alternatives**: Read-modify-write via the tracked entity (loses increments under concurrency);
  fire-and-forget `Task.Run` (unbounded, unobserved exceptions, scoped `DbContext` disposed after
  the request); building the channel now (pulls feature 002 forward and removes the brownfield
  scenario's main change).

## R7. Error handling and ProblemDetails (FR-020, SC-006)

- **Decision**: `AddProblemDetails()` + one `IExceptionHandler`:
  - `LinkValidationException(field, message)` → 400 `ValidationProblemDetails` with `errors[field]`,
    the same shape `[ApiController]` produces for model-binding errors;
  - `AliasAlreadyExistsException` → 409 ProblemDetails;
  - anything else → 500 ProblemDetails with a generic detail (no stack trace outside Development).
  Not-found is **not** an exception: services return `null`, controllers return `NotFound()`, which
  `[ApiController]` turns into a 404 ProblemDetails. This keeps exceptions off the redirect path.
- **Rationale**: Controllers stay free of `try/catch` (constitution III); one consistent error
  shape for every user-triggered error (SC-006).
- **Alternatives**: A `Result<T>` type (more code at every call site, no benefit at this size);
  exception filters (MVC-only, miss errors outside controllers).

## R8. Routing

- **Decision**: `LinksController` at `api/links`; `RedirectController` at the root with
  `[HttpGet("{code:regex(^[[A-Za-z0-9_-]]{{3,30}}$)}")]` and `[ApiExplorerSettings(IgnoreApi = true)]`.
  Literal routes (`/health`, `/api/...`) take precedence over the parameter route; `/swagger` is
  handled by middleware before routing. The redirect response is a 302 with
  `Cache-Control: no-store` so every visit reaches the service (FR-013).
- **Rationale**: The route constraint rejects malformed paths before any database lookup. Because
  a path that matches no endpoint never reaches a controller, `UseStatusCodePages()` (with
  `AddProblemDetails()`) is added so those 404s are ProblemDetails too (SC-006).
- **Reserved aliases** (case-insensitive): `api`, `health`, `swagger`, `admin`, `static`,
  `assets` (`assets` reserved for the Phase 5 frontend bundle).

## R9. Persistence and migrations

- **Decision**: EF Core 8 with `Microsoft.EntityFrameworkCore.Sqlite` (Infrastructure) and
  `Microsoft.EntityFrameworkCore.Design` (Api, `PrivateAssets=all`, migrations only). `dotnet-ef`
  pinned via a local tool manifest (`.config/dotnet-tools.json`). Migrations are applied at
  startup when the environment is Development or when `Database:MigrateOnStartup=true` (used by
  Docker and tests). Connection string `ConnectionStrings:Default`, default
  `Data Source=urlshortener.db`; the `.db` file is gitignored.
- **Rationale**: Migrations are the only schema path (constitution VIII); a local tool manifest
  gives every reviewer the same `dotnet-ef` version via `dotnet tool restore`.
- **Dependency justification**: both packages are first-party Microsoft, MIT-licensed; there is no
  BCL alternative for an ORM with migrations. Checked with `dotnet list package --vulnerable`
  when added.

## R10. Health check (FR-019)

- **Decision**: A small custom `IHealthCheck` in Infrastructure calling
  `DbContext.Database.CanConnectAsync()`, registered with the existing `AddHealthChecks()`.
  `MapHealthChecks` already returns 200 for healthy and 503 for unhealthy.
- **Rationale**: Avoids adding `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`
  for a one-line check (constitution VI). Liveness vs readiness split is deferred to feature 003.

## R11. Time and IDs

- **Decision**: `TimeProvider` (.NET 8 BCL) injected for `CreatedAtUtc`, so tests can fix the
  clock with a small hand-written fake (no extra package). `Id` is an auto-increment `long`, internal only; the
  public identifier is the code.

## R12. Testing strategy

- **Decision**:
  - **Unit tests** (`UrlShortener.UnitTests`): Domain rules (URL validation including every
    malicious/tricky input in the spec; alias rules; reserved words), Application use cases with
    hand-written fakes (collision retry with a scripted generator, retry exhaustion, alias
    conflict, click-recorder failure isolation), and `Base62CodeGenerator` (length, alphabet). The
    unit test project gains a reference to Infrastructure for the generator.
  - **Integration tests** (`UrlShortener.IntegrationTests`): a custom `WebApplicationFactory` per
    test class using a **temporary SQLite file** (not `:memory:`, because a shared in-memory
    connection is not safe for the concurrent-request tests), migrations applied, base URL
    overridden, and an option to swap in a throwing `IClickRecorder`. Covers create → redirect →
    count, 400/404/409 bodies, forged `Host` header, alias race (exactly one 201), concurrent
    redirect counting, health 200.
  - Assertions check bodies and headers (`Location`, ProblemDetails fields), not only status
    codes (constitution II). Plain xUnit `Assert`; no new test packages.
- **SC-004 (redirect latency)**: verified manually via the quickstart (timed loop); not an
  automated test because timing tests are flaky on shared CI runners.

## R13. Existing-code observations (Phase 0 skeleton)

- `Program.cs` calls `UseHttpsRedirection()`. In Docker (HTTP only on 8080) it does nothing but
  log a warning, and behind a TLS-terminating proxy it is the proxy's job. Decision: remove it;
  HTTPS enforcement is documented as a deployment concern.
- `UseAuthorization()` is present without any auth. Harmless; left as is.
- `HealthTests` uses the default factory (no database configured). It will move to the custom
  factory once the database health check exists.

## R14. Contract-first OpenAPI document

- **Decision**: Hand-write `contracts/openapi.yaml` (OpenAPI 3.0.3) before implementation, next to
  the readable `http-api.md`. It includes the redirect endpoint, which the generated Swagger UI
  hides. The runtime Swagger document (Swashbuckle, already referenced) must match it: controllers
  declare every documented status with `[ProducesResponseType]`, and an integration test fetches
  `/swagger/v1/swagger.json` and asserts the documented API paths and status codes are present.
  The file is validated with `openapi-spec-validator` (run via `uvx`, not a project dependency).
- **Rationale**: Gives a reviewed, testable contract before code exists; importable into Postman
  for manual testing; the drift test stops code and contract silently diverging.
- **Alternatives**: Only the generated Swagger document (exists only after the code, so it cannot
  be reviewed up front, and it omits the redirect endpoint); code generation from the spec
  (NSwag etc.: new dependency and tooling for four endpoints, constitution VI).
