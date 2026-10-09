# AI Usage Log

This log records how AI was used during the assignment: what was asked, what came back, and what I accepted, edited, or rejected, and why.
It supports the "AI-assisted execution and traceability" requirement.

## Summary
- **Tools used:** Claude (claude.ai chat) for planning, design review, and setup guidance; Claude Code via GitHub Spec Kit (`--integration claude`) for spec-driven implementation
- **Entries so far:** 13 | Accepted: 7 | Edited: 5 | Rejected: 1
- **Key lessons so far:**
  - AI knowledge of fast-moving CLI tools can be out of date. Verify flags against the tool's `--help` (Spec Kit `--ai` → `--integration`).
  - AI-generated shell commands may not account for the local shell (zsh globbing). Run, read the error, fix.
  - AI is useful for surfacing trade-offs (DB choice, deployment, uniqueness), but the final decision and its scope stay with the engineer.

## Secure AI usage practices
- No secrets, credentials, connection strings, or real user data shared in prompts
- All AI-suggested dependencies are checked for license and known vulnerabilities before adding
- Security-sensitive code (input validation, redirects, migrations) is reviewed line by line, never accepted blindly
- AI never commits or pushes. Every commit is made by me after review and a passing build/test run.

## Log

| # | Date/Time (IST) | Task ID | Activity | Prompt intent & constraints | AI output (summary) | Decision | Rationale / what I changed | Commit |
|---|---|---|---|---|---|---|---|---|
| 1 | 2026-10-09 21:30 | Planning | review | Analyse the assignment brief; validate my proposed stack (.NET 8 + SQLite + React) and SDD with GitHub Spec Kit | Confirmed the stack, mapped the brief's deliverables to concrete artifacts, proposed scenario split and clean-architecture layering | Edited | Kept the stack and the SDD approach; scoped down React to a minimal UI so effort goes to the API, tests, and docs | — |
| 2 | 2026-10-09 21:40 | Planning | design | Free deployment options; can GitHub host it? | Recommended Azure App Service F1; explained GitHub Pages is static-only and can't run the API or redirects | Edited | Decided on local run (Docker Compose + CLI) as the primary delivery, since the brief only requires "runnable end-to-end". Deployment moved to an optional phase. | — |
| 3 | 2026-10-09 21:45 | Planning | docs | Generate a self-contained execution plan usable by any AI agent, with a timeline to Sunday evening | Produced `url-shortner-plan.md`: ground rules, phases, traceability matrix, risk register, templates | Accepted | Reviewed against the brief's deliverables (section 5) and evaluation criteria; later refined (see #4, #7) | `<hash>` |
| 4 | 2026-10-09 21:55 | Planning | design | Change API style | Plan used ASP.NET Core minimal APIs | Edited | My decision: attribute-routed controllers. Clearer structure, familiar to .NET teams, and attribute-based rate limiting. Plan, constitution input, and ADR-001 updated. | `<hash>` |
| 5 | 2026-10-09 22:30 | Planning | design | How is short-code uniqueness guaranteed? | DB unique index as the real guarantee (not check-then-insert, which races); retry on collision for generated codes; 409 for taken aliases; explicit case-sensitivity decision; tests with a fake generator | Accepted | Will implement in Phase 2 with tests for collision retry and duplicate alias | — |
| 6 | 2026-10-09 23:35 | Planning | design | Is SQLite suitable for a URL shortener? | Fine for the prototype (zero setup, fast indexed reads, EF migrations); not for production (single writer, single node). Production path: Postgres/Azure SQL + Redis + queue-based analytics | Accepted | Kept SQLite deliberately to protect "runnable end-to-end" for reviewers. Trade-off and production path to be recorded in ADR-004; data access kept behind repository interfaces. | — |
| 7 | 2026-10-09 23:40 | Planning | docs | Add a "Why this tech stack" README section | Draft table: choice, reason, alternative considered, trade-off per area | Edited | Requested by me; to be adjusted to what is actually built before submission | `<hash>` |
| 8 | 2026-10-10 00:47 | P0-setup | setup | .NET 9 SDK installed; can I target .NET 8? | Yes: use `-f net8.0`, pin packages to 8.0.x, install .NET 8 runtime for running/tests, keep Docker/CI on 8.0 | Accepted | Followed as advised | — |
| 9 | 2026-10-10 00:53 | P0-setup | setup | Initialise Spec Kit | Suggested `specify init --here --ai claude` | **Rejected** | Command failed: `No such option: --ai`. The flag was renamed in newer Spec Kit versions. Used `--integration claude` instead; chose `sh` scripts for macOS. Lesson: verify CLI flags with `--help`. | `<hash>` |
| 10 | 2026-10-10 01:00 | P0-setup | setup | Default branch name | Suggested `main` (GitHub default; matches CI triggers and docs) | Accepted | Renamed `master` → `main` for consistency across CI and docs | — |
| 11 | 2026-10-10 01:21 | P0-setup | setup | Add `Microsoft.AspNetCore.Mvc.Testing` package | Command used `--version 8.0.*` unquoted | Edited | Failed in zsh (`no matches found`), because zsh treats `*` as a glob. Fixed by quoting: `--version "8.0.*"` | `<hash>` |
| 12 | 2026-10-10 01:31 | P0-setup | impl/test | Add `/health` endpoint and first integration test | `AddHealthChecks()` + `MapHealthChecks("/health")`, `public partial class Program {}`, `WebApplicationFactory` test | Accepted | Reviewed placement in `Program.cs`. Test named without underscores and uses the `Uri` overload to satisfy analyzers (warnings are errors). Test passes locally. | `<hash>` |
| 13 | 2026-10-10 01:37 | P0-setup | setup | Basic CI workflow + PR template | GitHub Actions restore → build → test; installs both .NET 8 and 9 SDKs; PR template with quality gates and sign-off checklist | Accepted | Both SDKs installed so CI uses the same analyzers as my local .NET 9 SDK build, avoiding local/CI drift | `<hash>` |