# Engineering Summary

A URL shortener built end to end in about two days, as an exercise in **engineer-led, AI-assisted
delivery**: a working service, plus the evidence of how every requirement was understood, decomposed,
built, validated and reviewed.

## 1. Problem and approach

**Problem.** Build a URL shortener through three scenarios: a greenfield core service; a brownfield
change to that merged code (link expiry, click analytics, moving click recording off the redirect
path, no IP storage); and an ambiguous requirement ("The service should be reliable and protected
from abuse").

**Approach.**

- **Process first.** A [constitution](../.specify/memory/constitution.md) of nine principles
  (engineer-led AI, test-first, clean architecture, security by default, redirect reliability,
  simplicity, observability, safe change management, quality gates), then **GitHub Spec Kit** per
  feature: specify → clarify → plan → tasks → analyze → implement
  ([ADR-007](adr/007-spec-driven-development.md)).
- **One task at a time.** The AI agent implemented one task, ran every gate (build with warnings as
  errors, formatting, the full test suite, vulnerability checks, mutation checks) and waited for the
  engineer's approval before committing. High-impact changes (migrations, the redirect path,
  validation and security, new dependencies, data deletion) needed explicit sign-off.
- **Simple, built-in technology.** ASP.NET Core 8 with clean architecture and controllers, EF Core +
  SQLite, the framework's rate limiter and background services, a one-page React UI, one Docker image
  (rationale per choice in the [README](../README.md#why-this-tech-stack)).

**Rationale.** The risk with AI-generated code is unreviewed decisions, not typing speed. Specs make
requirements explicit, clarifications force decisions into the open, small reviewed tasks keep the
engineer accountable, and tests plus mutation checks make claims verifiable.

## 2. Artifacts produced

| Artifact | Where |
|---|---|
| Running service (API, redirects, statistics, web UI), one-command start | [README quick start](../README.md#quick-start), [docker-compose.yml](../docker-compose.yml) |
| Specifications for four features (spec with clarifications, plan with research, data model, OpenAPI contract, tasks, quickstart) | [specs/](../specs/) (001 greenfield, 002 brownfield, 003 ambiguous, 004 web UI) |
| Scenario write-ups (requirement → interpretation → impact → decomposition → execution → validation → risks) | [01 greenfield](scenarios/01-greenfield.md), [02 brownfield](scenarios/02-brownfield.md), [03 ambiguous](scenarios/03-ambiguous.md) |
| Architecture and decisions | [architecture.md](architecture.md), [10 ADRs](adr/README.md) |
| Tests and coverage | 244 unit + 141 integration (.NET), 38 web; 98% line coverage of hand-written code ([testing.md](testing.md)) |
| Risks, trade-offs, limitations | [risks-and-tradeoffs.md](risks-and-tradeoffs.md) |
| Deployment design (AWS) | [deployment.md](deployment.md) |
| AI usage record (98+ entries) | [ai-usage-log.md](ai-usage-log.md) |
| Quality gates and oversight | [CI](../.github/workflows/ci.yml), [CodeQL](../.github/workflows/codeql.yml), [Dependabot](../.github/dependabot.yml), [PR template](../.github/pull_request_template.md); over 110 small commits and 20 merged pull requests at the time of writing |

## 3. AI usage

**Where it helped.** Turning requirements into complete specs and testable task lists quickly;
tracing every usage of affected code for the brownfield impact analysis; surfacing trade-offs with
alternatives (uniqueness design, click delivery semantics, rate-limit algorithms, deployment options);
writing thorough tests, including races, migrations on seeded data and accessibility checks; running
mutation checks and validation scripts against the running service.

**Where it was wrong** (each caught before merge, recorded in the scenario docs' correction tables):

- Outdated tool knowledge: a renamed Spec Kit flag; SQLite error code 19 instead of 2067 for unique
  violations; a plan claiming no package was needed for `BackgroundService` in a class library.
- Plans that broke code: a task order that broke 26 integration tests; rate limits that broke the
  sample requests file.
- Weak or wrong evidence: a "no click recorded" check that proved nothing once storage became
  asynchronous; a test fake that gave every link id 0; a coverage script that double-counted files
  (54% instead of 98%); a miscounted summary; a flaky CI run that hid a real shutdown-logging bug.
- Environment slips: zsh globbing, macOS lacking `timeout`, CRLF files rewritten with LF, a
  day-of-week mistake in the schedule.

**How quality was controlled.** Engineer approval per task and sign-off for high-impact changes;
warnings as errors and analyzers; the full suite on every task; mutation checks with survivors
reported; CI on every pull request (format, coverage, vulnerable packages, web lint/tests, Docker
build) and CodeQL (its first run found five non-security issues in test code, all fixed); an AI usage
log in which roughly half of all AI output was edited before use (current counts in the log's summary).

**Decisions the engineer made against or beyond the AI's proposal:** controllers instead of minimal
APIs; reclaimable expired aliases (the AI recommended keeping them reserved); rate limiting as the
scope of the ambiguous scenario and coarser tasks under the deadline; a specification for the web UI
and an ADR for Docker; AWS for the deployment design; and refusing scope cuts in the final phases.

## 4. Risks, trade-offs and validation

**Main risks and mitigations** (full register in [risks-and-tradeoffs.md](risks-and-tradeoffs.md)):
malicious destinations (scheme allowlist, no credentials in URLs, no self-links), enumeration
(cryptographic codes, per-client budgets), races (database unique constraint, conditional
reclaim), analytics breaking redirects (in-memory queue, background writer), abuse (per-client rate
limits, forwarded headers ignored), privacy (no IPs, referrer host only, browser family only).

**Key trade-offs:** fast, failure-proof redirects over exact analytics (at-most-once, eventually
consistent counts); a stored click total alongside events; one index for all case rules (a smaller
but ample code space); SQLite and one container for zero setup over scalability.

**Validation performed:**

- Automated: 244 unit, 141 integration and 38 web tests, all green in CI; mutation checks per task.
- Concurrency: 20 parallel redirects counted exactly; 10 parallel alias claims and reclaims give
  exactly one winner (repeated runs).
- Brownfield safety: the full feature 001 suite as a regression baseline; the migration tested on a
  seeded database and on a database created by the previous release; both rollback paths tested.
- Performance: redirect p95 0.9 ms on the running service (0.5 ms before rate limiting); redirects
  still 302 with analytics storage blocked or failing.
- Running service: every quickstart scenario, all 34 sample requests with default rate limits, the
  container (non-root, persistence, seeded data), and the engineer's manual checks in Swagger, Postman
  and the browser.

## 5. Assumptions

Prototype scale on one machine; no accounts (statistics public to anyone with the code, aggregates
only); losing a few clicks under failure is acceptable, failing a redirect is not; expiry times always
carry a time zone and statistics use UTC days; clients reach the service directly or through a proxy
that will be configured; modern browsers for the UI. Details: each spec's Assumptions section and the
[ambiguous scenario's interpretation](scenarios/03-ambiguous.md#23-assumptions-no-product-owner-available).

## 6. Limitations and next steps

**Limitations:** not deployed (design only); single instance (in-memory queue and budgets, SQLite
single writer); at-most-once analytics; clients identified by IP address; no deduplication or
idempotency keys; no destination blocklist; a single health endpoint.

**With more time, in order:**

1. Deploy the AWS design ([deployment.md](deployment.md)): ECS Fargate behind an ALB with trusted-proxy
   configuration, RDS PostgreSQL with migrations as a release step.
2. Liveness and readiness health checks; response compression for the UI; quieter logging of expected
   duplicates and sampled rate-limit warnings.
3. Durable analytics through SQS with idempotent events; a retention policy for click events.
4. Shared rate-limit budgets and a redirect cache in ElastiCache; IPv6 /64 grouping.
5. A maintained destination reputation check; idempotency keys if retrying clients appear.
6. Accounts, so statistics and links can be private.
