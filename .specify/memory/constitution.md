# URL Shortener Constitution

## Core Principles

### I. Engineer-Led AI Assistance

- AI proposes; the engineer reviews, edits, and approves. No AI output is committed without
  human review.
- Tasks MUST be implemented one at a time, each reviewed before the next begins. Running a whole
  task list in one unattended pass is prohibited.
- Every meaningful AI interaction (accepted, edited, or rejected) MUST be recorded in
  `docs/ai-usage-log.md`.
- Commits MUST carry an `AI-Assisted: generated | edited | manual` trailer.

**Rationale**: The engineer owns execution and quality; AI accelerates work within a task.
Traceability makes that ownership verifiable.

### II. Test-First Quality (NON-NEGOTIABLE)

- Every task MUST define acceptance criteria and the automated tests that prove them before it
  is implemented.
- A task is not done while any test is failing or any required test is missing.
- Tests MUST assert behavior (response bodies, headers, persisted state), not only status codes.
- Security-relevant rules (URL validation, alias rules) MUST include negative and malicious-input
  test cases.

**Rationale**: Tests are the acceptance evidence for AI-assisted code and the safety net for the
brownfield changes that follow.

### III. Clean Architecture with Thin Controllers

- Dependency direction: Api → Application → Domain. Infrastructure implements interfaces defined
  in Application. Domain MUST NOT reference ASP.NET Core, EF Core, or any other framework package.
- The Api layer MUST use attribute-routed controllers (`[ApiController]`); minimal API endpoints
  are not used for application features (the built-in `/health` endpoint is the sole exception).
- Controllers MUST stay thin: they map HTTP to application services and MUST NOT contain business
  logic or data access. Controllers MUST NOT inject `DbContext`.
- Business validation lives in Application/Domain; request DTO annotations cover shape only.

**Rationale**: Isolated business rules are unit-testable and infrastructure is swappable (e.g.
SQLite → PostgreSQL) without touching the domain.

### IV. Security by Default

- All external input MUST be validated. Destination URLs MUST be absolute and use the `http` or
  `https` scheme only; all other schemes (`javascript:`, `data:`, `file:`, …) are rejected.
- No secrets, credentials, or connection strings for real environments in code, config, prompts,
  logs, or commits. Only `.env.example`-style placeholders are committed.
- Least data collected: raw IP addresses MUST NOT be stored; logs MUST NOT contain PII.
- Public endpoints MUST be rate limited.

**Rationale**: A URL shortener lends its domain to arbitrary destinations; unsafe input or data
handling directly harms users.

### V. Redirect Path Reliability

- The redirect path is the critical path: it MUST NOT perform analytics writes synchronously.
- A failure in analytics or click recording MUST NOT cause a redirect to fail or change its
  response.
- Every change touching the redirect path is a high-impact change (see Principle VIII).

**Rationale**: Redirects are the core user-facing function; analytics is secondary and may
degrade without causing an outage.

### VI. Simplicity and Minimal Dependencies

- Prefer the .NET base class library and built-in ASP.NET Core features (rate limiting, health
  checks, ProblemDetails, DI, logging) over third-party packages.
- Every new dependency MUST be justified in its PR, with license and known-vulnerability checks.
- Build the smallest thing that meets the spec; a finished, tested feature beats a larger
  unfinished one.

**Rationale**: Fewer dependencies mean a smaller attack surface, less maintenance, and a codebase
reviewers can understand quickly.

### VII. Observability

- Logging MUST be structured (message templates, not string concatenation).
- The service MUST expose health checks, including database availability.
- All error responses MUST be RFC 7807 ProblemDetails, produced consistently through a global
  exception handler. Stack traces MUST NOT be returned outside Development.

**Rationale**: Consistent errors and structured telemetry make failures diagnosable without
leaking internals.

### VIII. Safe Change Management

- Database schema changes MUST be made through EF Core migrations only; no manual schema edits.
- High-impact changes require explicit engineer sign-off recorded in the PR checklist:
  DB schema/migrations, the redirect path, input validation and security code, and dependency
  additions.
- Schema changes to existing tables SHOULD be additive (nullable columns, new tables) to keep
  existing data and links working.

**Rationale**: Reviewable, versioned changes with a named approver keep risky changes deliberate
and reversible.

### IX. Quality Gates

Every change MUST pass, locally and in CI, before merge:

- Build with warnings treated as errors (analyzers at `latest-recommended`).
- `dotnet format --verify-no-changes`.
- Unit and integration tests.
- Vulnerable package check (`dotnet list package --vulnerable --include-transitive`).
- CodeQL analysis.

**Rationale**: Automated gates catch defects in AI-generated and human-written code alike,
independent of reviewer attention.

## Technology & Architecture Constraints

- Backend: .NET 8 (LTS) ASP.NET Core Web API with controllers; EF Core with SQLite.
- Frontend: React + Vite + TypeScript, deliberately minimal.
- Tests: xUnit and `WebApplicationFactory` integration tests. Use xUnit `Assert` or Shouldly;
  FluentAssertions v8+ is not permitted (commercial license).
- Delivery: runnable end-to-end locally via Docker Compose or the .NET CLI.

## Development Workflow & Review

- Spec-Driven Development with GitHub Spec Kit: specify → clarify → plan → tasks → analyze, with
  engineer review between each step, before implementation starts.
- One task per commit, using Conventional Commits that reference the task ID
  (e.g. `Implements T012 from specs/001-…/tasks.md`) plus the AI trailer.
- Each feature is delivered on its own branch through a PR using the repository PR template,
  self-reviewed against the quality-gate and sign-off checklist before merge.

## Governance

- This constitution supersedes other practices in this repository. Specs, plans, tasks, and PR
  reviews MUST verify compliance; deviations MUST be justified in the plan's complexity tracking
  or the PR description.
- Amendments are made by the engineer via PR, with the change rationale recorded and the version
  bumped per semantic versioning: MAJOR for removing or redefining a principle, MINOR for adding a
  principle or materially expanding guidance, PATCH for clarifications and wording.

**Version**: 1.0.0 | **Ratified**: 2026-10-10 | **Last Amended**: 2026-10-10
