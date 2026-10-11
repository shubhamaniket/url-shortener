# Implementation Plan: Web UI for the URL Shortener

**Branch**: `phase-5-ci-docker-web` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

**Process note**: compact Spec Kit cycle by engineer decision (deadline): spec → plan (research folded
in below) → tasks → analyze; clarify skipped because the spec has no open markers. Docker and CI
decisions are recorded in an ADR, not here.

## Summary

A single static page (React + TypeScript, built with Vite) that calls the existing public API: create
a link (URL, optional alias, optional local-time expiry sent with the time-zone offset) and copy the
short URL; look up a code or pasted short URL to see details and 30-day statistics as a bar list; show
every API error in plain words. Built files are served by the API itself (one origin); during
development Vite's dev server proxies `/api` to the running API.

## Technical Context

**Language/Version**: TypeScript 5 (strict), React 18.3; Node 22 LTS in CI (24 locally works)

**Primary Dependencies** (all new, npm; **engineer sign-off required**, constitution VI):

| Package | Kind | Why | Alternative rejected |
|---|---|---|---|
| `react`, `react-dom` 18.3 | runtime | The plan's chosen UI library; small component model for three forms | Plain DOM scripts (more code for state and accessibility); larger frameworks |
| `vite` 5.4, `@vitejs/plugin-react` | build | Fast build, dev proxy, hashed static assets | Create React App (deprecated); webpack (configuration overhead) |
| `typescript` 5 | build | Typed API responses mirror the contract | Plain JS (no compile-time checks against the contract) |
| `vitest` 2, `@testing-library/react`, `@testing-library/user-event`, `jsdom` | test | Unit and component tests in the same toolchain | Jest (separate transform setup for Vite/TS) |
| `axe-core` | test | Automated accessibility check for SC-003 | Manual checks only |
| `eslint` 9, `typescript-eslint`, `eslint-plugin-react-hooks` | lint | CI lint gate | No linting |

No runtime dependency beyond React; no CSS framework, no chart library (spec assumption: bar list).

**Storage**: none (no accounts, no history)

**Testing**: Vitest + Testing Library in jsdom; `fetch` stubbed per test; axe-core on the rendered page

**Target Platform**: modern evergreen browsers; served by the API in the container

**Project Type**: web front end in `web/` alongside the .NET solution

**API Contract**: unchanged; the UI consumes [feature 003's contract](../003-reliability-abuse-protection/contracts/openapi.yaml) v0.3.0

**Performance Goals**: delivered size < 200 KB compressed (SC-004)

**Constraints**: no server behaviour change (FR-012); the only server change is hosting the built
files (static files + `index.html` at `/`)

## Research (decisions)

- **R1 Serving (FR-013)**: Vite builds into `src/UrlShortener.Api/wwwroot` (git-ignored; the Docker
  build produces it). The API adds `UseDefaultFiles()` + `UseStaticFiles()`. No SPA fallback route: the
  UI is one page at `/`, and a catch-all would collide with the redirect route `/{code}`. Static assets
  live under `/assets/`, and `assets` is already a reserved alias (feature 001), so no short code can
  shadow them.
- **R2 Dev proxy**: `vite.config.ts` proxies `/api` and `/health` to `http://localhost:5058`; short URLs
  shown by the UI come from the API's `shortUrl`, so they are correct in both modes.
- **R3 Expiry (FR-002)**: an `<input type="datetime-local">` value plus the browser's current offset
  (`Date#getTimezoneOffset`) becomes `yyyy-MM-ddTHH:mm:ss±HH:mm`, the exact format feature 002 requires.
  Results are shown with `toLocaleString()`.
- **R4 Errors (FR-007–FR-009)**: one function maps responses to UI errors: 400 with `errors` → per-field
  messages (keys `url`, `customAlias`, `expiresAt`); 409/404/410 → the ProblemDetails `detail` or
  `title`; 429 → "Too many requests. Try again in N seconds." from `Retry-After`; network failure →
  "The service is unavailable." Never a raw body.
- **R5 Accessibility (FR-010)**: native form controls with labels; errors linked with
  `aria-describedby`; an `aria-live="polite"` region for results and errors; focus moves to the first
  invalid field; visible focus styles.
- **R6 Lookup input (FR-004)**: trim; if it parses as a URL, use its last path segment.

## Constitution Check

| # | Principle | Status | Notes |
|---|---|---|---|
| I | Engineer-led | ✅ | Compact cycle chosen by the engineer; one task at a time |
| II | Test-first | ✅ | Unit tests for mapping/time/lookup parsing; component tests per story; a11y check; an API test that `/` serves the page |
| III | Clean architecture | ✅ | UI is a separate client of the public API; the server gains only static hosting |
| IV | Security | ✅ | No secrets; no `dangerouslySetInnerHTML`; links rendered as text/hrefs from API data with `rel="noopener noreferrer"` |
| VI | Simplicity | ⚠️ New dependencies | Justified in the table above; **sign-off required** |
| IX | Quality gates | ✅ | CI gains `npm ci`, lint, test, build; CodeQL adds JavaScript/TypeScript; Dependabot adds npm |

## Project Structure

```text
web/
├── package.json, package-lock.json, tsconfig.json, vite.config.ts, eslint.config.js, index.html
└── src/
    ├── main.tsx, App.tsx, styles.css
    ├── api.ts            # typed calls + error mapping (R4)
    ├── time.ts           # local expiry → zoned ISO (R3)
    ├── lookup.ts         # code from input or short URL (R6)
    ├── CreateLinkForm.tsx, LinkLookup.tsx, StatsView.tsx
    └── *.test.ts(x)
src/UrlShortener.Api/Program.cs   # UseDefaultFiles + UseStaticFiles
```
