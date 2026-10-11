---

description: "Task list for feature 004 — web UI"
---

# Tasks: Web UI for the URL Shortener

**Input**: [spec.md](spec.md), [plan.md](plan.md) (research R1–R6 inside); API contract unchanged
(feature 003 v0.3.0)

**Tests**: REQUIRED. **Regression rule**: the .NET suite (244 unit + 138 integration) stays green and
unchanged except the one new hosting test (T004).

**Granularity**: coarse tasks (engineer decision), one capability end to end each.

## Definition of Done (every task)

1. `npm run lint`, `npm test`, `npm run build` pass in `web/`; .NET build, format and full suite pass
2. One commit per task with `Implements Txxx from specs/004-web-ui/tasks.md` and the `AI-Assisted:` trailer
3. Task ticked here; entry in `docs/ai-usage-log.md`

---

- [ ] T001 Scaffold `web/` (Vite + React + TypeScript strict, ESLint, Vitest), dev proxy to the API (R2), shared modules `api.ts` (typed calls, error mapping R4), `time.ts` (R3), `lookup.ts` (R6); CI job: `npm ci`, lint, test, build; CodeQL adds `javascript-typescript`; Dependabot adds npm
  - **AC**: dependency list exactly as plan.md; build output goes to `src/UrlShortener.Api/wwwroot` (git-ignored)
  - **Tests**: `api.test.ts` (400 field errors, 409, 404, 410, 429 with Retry-After seconds, network failure, success parsing), `time.test.ts` (local value + offsets like +05:30, -08:00, Z), `lookup.test.ts` (code, spaces, full short URL, trailing slash)
  - **Sign-off**: new dependencies
- [ ] T002 [US1] Create form: URL (required), alias, expiry (datetime-local); disabled while sending; field errors next to fields with focus on the first; result with short URL link, copy button ("Copied"; manual-copy fallback), local expiry; "View details" fills the lookup (US2-6)
  - **Tests**: `CreateLinkForm.test.tsx`: success shows short URL and copy writes it; expiry sent with offset; field errors shown and values kept; 409 message; button disabled while pending
- [ ] T003 [US2][US3] Lookup and statistics: details (original URL, created, expiry or "never", expired badge, total clicks) and stats (30 days with proportional bars, up to 5 referrers, "No referrers yet"); unknown code, 429 and unreachable messages; live region
  - **Tests**: `LinkLookup.test.tsx`: details + stats rendered from stubbed responses; pasted short URL resolved; expired badge; 404 message; 429 with seconds; axe-core finds no violations on the whole page (SC-003)
- [ ] T004 Serve the built UI from the API (R1): `UseDefaultFiles` + `UseStaticFiles` in Program.cs; quickstart check in the browser (dev proxy and served build) and engineer's manual check
  - **AC**: `/` serves the page when built; redirects, API, health unchanged; built assets under 200 KB compressed (SC-004), measured from the build output
  - **Tests**: integration test: with an `index.html` in the web root, `GET /` returns it; `GET /assets` is not treated as a short code
  - **Changed tests**: none

## Dependencies

T001 → T002 → T003 → T004 (T002 and T003 both edit `App.tsx`)
