# ADR-007: Spec-driven development with Spec Kit and engineer-led AI

- **Status**: Accepted
- **Date**: 2026-10-09 (planning); applied to every feature
- **Related**: [.specify/memory/constitution.md](../../.specify/memory/constitution.md) ·
  [docs/ai-usage-log.md](../ai-usage-log.md) · scenario docs 01–03

## Context

The work was done with an AI coding agent under time pressure. The risk with AI-generated code is not
typing speed but unreviewed decisions: invented requirements, silent scope changes, untested edge
cases, and claims that are not true. The engineer had to stay accountable for every change.

## Decision

1. **A constitution first** (9 principles: engineer-led AI, test-first, clean architecture, security
   by default, redirect reliability, simplicity, observability, safe change management, quality
   gates), checked by every plan.
2. **GitHub Spec Kit per feature**: specify → clarify (engineer answers every question) → plan
   (research with decisions and rejected alternatives) → tasks (acceptance criteria and named tests)
   → analyze (consistency check) → implement. Applied to features 001 (greenfield), 002 (brownfield),
   003 (ambiguous) and 004 (web UI, compact cycle); tooling decisions (Docker, CI) went to ADRs.
3. **One task at a time**: the AI implements, runs the full gates (build with warnings as errors,
   format, all tests, vulnerability check), reports evidence (including mutation checks), and waits
   for the engineer's **approve** before committing.
4. **High-impact changes need explicit sign-off** (migrations, redirect path, input validation and
   security, new dependencies, data deletion), recorded in the commit message and the PR.
5. **Every AI interaction is logged** (`docs/ai-usage-log.md`) as accepted, edited or rejected, with
   what the engineer verified or changed.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Prompt-and-paste without specs | Fast, but decisions and requirements are implicit and unreviewable |
| AI writes everything, engineer reviews one big PR | Review at the end misses design errors early; large diffs hide mistakes |
| No AI | Slower; the goal was to show disciplined AI use, not avoid it |

## Consequences

- Traceability: every requirement maps to tasks, tests and commits; every decision has a reason and
  rejected alternatives.
- Real defects and wrong assumptions were caught before merge and recorded (scenario docs: AI
  correction tables), e.g. a task order that broke 26 tests, a flaky shutdown-logging bug exposed by
  CI, a sample file broken by rate limits, a day-of-week mistake that changed the schedule.
- The process costs time (reviews, sign-offs, logs); coarse tasks were used in later phases to fit
  the deadline without dropping review or tests.