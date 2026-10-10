# Feature Specification: Link Expiry, Click Analytics, and Off-Path Click Recording

**Feature Branch**: `002-brownfield-analytics-expiry`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Enhance the existing URL shortener:
1. Links can optionally have an expiry date. Expired links must not redirect and should return 410 Gone.
2. Detailed analytics: record each click event (timestamp, referrer host, coarse user agent family) and expose GET /api/links/{code}/stats (new action on the existing LinksController) returning total clicks, clicks per day for the last 30 days, and top 5 referrers.
3. Refactor: click recording must be moved off the redirect hot path so redirects stay fast and never fail if analytics fails.
Do not store raw IP addresses."

**Builds on**: feature 001 (`specs/001-greenfield-core/`), merged. Impact analysis:
[docs/scenarios/02-brownfield.md](../../docs/scenarios/02-brownfield.md) section 3.

## Clarifications

### Session 2026-10-11

- Q: Should the total click count stay a stored number or be calculated from individual click records? → A: Keep the stored total; each saved batch of click records also increases it in the same transaction, so existing totals are preserved.
- Q: When a link with a custom alias expires, should the alias stay reserved or become available? → A: It becomes available immediately: a new custom-alias request may claim the code of an expired link; the expired link is retired.
- Q: How should clicks with no referrer appear in the statistics? → A: As an explicit `(direct)` entry ranked in the top 5 alongside real referrer hosts.
- Q: Should clicks from bots and link-preview crawlers count as clicks? → A: Yes; they are counted like any click and tagged with user-agent family `Bot`.
- Q: Which expiry time formats are accepted? → A: Any date-time with an explicit zone (`Z` or an offset such as `+05:30`), converted to and stored as UTC; date-times without a zone are rejected.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create a link that expires (Priority: P1)

A user creating a short link can optionally give it an expiry time. After that time the short link
stops redirecting and tells visitors the link is gone.

**Why this priority**: Expiry changes what visitors experience and is the most visible part of the
feature; time-limited links (campaigns, invitations) are a common need.

**Independent Test**: Create a link expiring shortly in the future, confirm it redirects, let it
expire, and confirm visitors get a "gone" response and no click is counted.

**Acceptance Scenarios**:

1. **Given** a valid URL and an expiry time in the future, **When** the user creates a link,
   **Then** the link is created and its details show that expiry time.
2. **Given** a link whose expiry time has not been reached, **When** a visitor opens the short URL,
   **Then** they are redirected as before.
3. **Given** a link whose expiry time has passed, **When** a visitor opens the short URL, **Then**
   they receive a "gone" response explaining the link has expired, are not redirected, and no click
   is recorded.
4. **Given** an expiry time that is now or in the past, **When** the user tries to create a link,
   **Then** the request is rejected with a validation error and no link is created.
5. **Given** a link created without an expiry time (including every link created before this
   feature), **When** any amount of time passes, **Then** it never expires.

---

### User Story 2 - Redirects stay fast and never fail because of analytics (Priority: P1)

Visitors are redirected without waiting for click data to be saved. If saving analytics is slow or
failing, redirects are unaffected.

**Why this priority**: The redirect is the core user-facing function; this resolves the
known deviation from the project constitution (Principle V) left by feature 001.

**Independent Test**: Make analytics storage fail or stall; confirm every redirect still succeeds
with normal speed, and that once storage recovers new clicks are counted again.

**Acceptance Scenarios**:

1. **Given** normal operation, **When** a visitor follows a short link, **Then** the redirect
   response does not wait for the click to be stored, and the click appears in the link's counts
   within 5 seconds.
2. **Given** analytics storage is failing, **When** visitors follow short links, **Then** every
   redirect still succeeds and the failures are logged.
3. **Given** analytics storage is slow, **When** visitors follow short links, **Then** redirect
   response times are unaffected.
4. **Given** the service is shutting down normally, **When** clicks are still waiting to be stored,
   **Then** the service attempts to store them before it stops.

---

### User Story 3 - View click statistics for a link (Priority: P2)

Anyone who knows a short code can see how a link has been used: total clicks, clicks per day for the
last 30 days, and the top 5 referring sites.

**Why this priority**: Valuable feedback for link creators, but it depends on clicks being recorded
(User Story 2) and is not needed for redirects to work.

**Independent Test**: Follow a link several times with different referring sites and browsers,
then request its statistics and confirm the totals, the per-day breakdown, and the referrer ranking.

**Acceptance Scenarios**:

1. **Given** a link followed 5 times today (3 from `news.example`, 2 with no referrer), **When**
   its statistics are requested, **Then** the response shows 5 total clicks, today's entry with 5,
   and the referrer ranking `news.example` 3, `(direct)` 2.
2. **Given** a link with no clicks, **When** its statistics are requested, **Then** the response
   shows 0 total clicks, 30 daily entries all 0, and an empty referrer ranking.
3. **Given** more than 5 distinct referring sites, **When** statistics are requested, **Then** only
   the 5 with the most clicks are listed, highest first.
4. **Given** an unknown code, **When** statistics are requested, **Then** a not-found error is
   returned.
5. **Given** an expired link, **When** statistics are requested, **Then** its statistics are still
   returned (expiry stops redirects, not reporting).

---

### Edge Cases

- A link expiring at an exact instant: at and after that instant it is expired; before it, it is not.
- `2026-10-12T18:00:00+05:30` and `2026-10-12T12:30:00Z` are the same expiry; `2026-10-12T18:00:00`
  (no zone) is rejected rather than guessed.
- The referrer is missing, malformed, or not an http(s) URL: the click is recorded with no referrer
  and counts towards the `(direct)` source in statistics.
- The referrer includes a path, query string, or credentials: only its host is kept
  (e.g. `https://news.example/a?u=42` → `news.example`).
- The user agent is missing or unrecognised: the click is recorded with family "Other".
- The same visitor follows a link many times: every successful redirect is a click (no
  de-duplication by visitor).
- Very high redirect volume fills the analytics queue: redirects continue; excess clicks may be
  dropped and are logged, never blocking the redirect.
- Clicks recorded before this feature exist only as a total count, with no per-click detail: their
  total is preserved, while the per-day breakdown and referrer ranking cover only clicks recorded
  after this feature.
- Days with no clicks still appear in the 30-day breakdown with 0.
- An expired link's code can be claimed by a new custom-alias request (in any letter case); from
  then on the code refers to the new link, and the expired link's details and statistics are no
  longer reachable through it.
- Two requests claim the same expired code at the same time: exactly one succeeds; the other gets
  the usual "alias already in use" conflict.
- A newly generated code that matches an expired link's code is still treated as a collision and
  regenerated; only explicit custom-alias requests reclaim codes.
- Requests for the details of an expired link still succeed and show the expiry time.

## Requirements *(mandatory)*

### Functional Requirements

**Expiry**

- **FR-001**: Users MAY supply an expiry time when creating a link; omitting it means the link never
  expires.
- **FR-002**: An expiry time MUST be strictly in the future at creation; otherwise the request is
  rejected with a validation error and nothing is stored.
- **FR-002a**: An expiry time MUST include an explicit time zone (`Z` or an offset such as
  `+05:30`); it is converted to UTC for storage and always returned in UTC. A date-time without a
  zone MUST be rejected with a validation error explaining that a zone is required.
- **FR-003**: A link MUST be treated as expired at and after its expiry time.
- **FR-004**: Following an expired link MUST return a "gone" response (not a redirect, not
  "not found") in the service's standard error format, and MUST NOT record a click.
- **FR-005**: Link details MUST include the expiry time (empty when the link never expires), for
  expired and non-expired links alike.
- **FR-006**: Links that existed before this feature MUST keep working exactly as before (no expiry).
- **FR-006a**: A custom-alias request MAY claim a code whose current link has expired (compared
  ignoring letter case, as for all aliases). The expired link is retired: afterwards the code's
  redirect, details and statistics all refer to the new link. Claiming MUST remain safe under
  concurrent requests (exactly one claimant wins). A code whose link has not expired stays taken.

**Click recording**

- **FR-007**: Each successful redirect MUST produce one click event with: the time of the click
  (UTC), the referrer host (or none), and a coarse user-agent family.
- **FR-008**: The referrer host MUST be derived from the referrer by keeping only the host name
  (lower-cased); the full referrer URL, its path, query and credentials MUST NOT be stored.
- **FR-009**: The user-agent family MUST be one of a small fixed set (e.g. Chrome, Edge, Firefox,
  Safari, Bot, Other); the raw user-agent string MUST NOT be stored. Requests from recognisable bots
  and link-preview crawlers MUST be classified as `Bot` and are counted like any other click.
- **FR-010**: The service MUST NOT store visitor IP addresses or any other personal data.
- **FR-011**: Redirects MUST NOT wait for click events to be stored; storing happens in the
  background.
- **FR-012**: Under normal operation every click MUST be reflected in the link's total and daily
  counts within 5 seconds.
- **FR-013**: If analytics storage fails or is overloaded, redirects MUST still succeed; affected
  click events MAY be lost, and losses MUST be logged (at-most-once delivery).
- **FR-014**: On normal shutdown the service MUST attempt to store click events still waiting.
- **FR-015**: The existing total click count for each link MUST be preserved and continue to
  increase with new clicks. It remains a stored total: storing a batch of click events and
  increasing the affected totals MUST happen together (both or neither), so totals and stored
  events never diverge for clicks recorded after this feature.

**Statistics**

- **FR-016**: The service MUST provide statistics for a link identified by its short code, using the
  same code-matching rules as redirects (custom aliases in any letter case, generated codes exactly).
- **FR-017**: Statistics MUST include the total click count (all-time).
- **FR-018**: Statistics MUST include clicks per calendar day (UTC) for the last 30 days including
  today, as exactly 30 entries in date order, with 0 for days without clicks.
- **FR-019**: Statistics MUST include up to 5 referrer sources ranked by all-time click count
  (highest first, ties broken alphabetically), consistent with the all-time total. Clicks without a referrer form one source labelled `(direct)`, which
  is ranked like any referrer host. The label cannot collide with a real host name.
- **FR-020**: Statistics MUST be available without credentials to anyone who knows the code
  (consistent with feature 001 link details) and MUST NOT expose any per-visitor data.
- **FR-021**: Statistics for an unknown code MUST return a not-found error.
- **FR-022**: Reading statistics or details MUST NOT record a click.

### Key Entities

- **Short Link** (existing): gains an optional expiry time. Its total click count stays a stored
  value, increased together with each stored batch of click events.
- **Click Event** (new): one successful redirect of a short link. Attributes: the link it belongs to,
  time of click (UTC), referrer host (optional), user-agent family. No visitor identity.
- **Link Statistics** (read model): total clicks, 30 daily counts, top 5 referrer sources (hosts or
  `(direct)`) with counts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With analytics storage made to fail, 100% of redirects to valid links still succeed.
- **SC-002**: With analytics storage made slow, 95% of redirects still complete in under 100 ms,
  measured end-to-end from a client on the same machine.
- **SC-003**: Under normal operation, N redirects are reflected in total and today's daily count as
  exactly N within 5 seconds (including concurrent redirects).
- **SC-004**: 100% of visits to expired links receive the "gone" response and add 0 clicks.
- **SC-005**: Every acceptance test from feature 001 passes, except where this spec intentionally
  changes behaviour (counts are now eventually consistent; details gain the expiry field).
- **SC-006**: No stored record contains an IP address, a full referrer URL, or a raw user-agent
  string (verified by inspecting stored click data after test traffic).

## Assumptions

- **Expiry is set at creation only**: changing or removing an expiry later is link editing, which
  remains out of scope.
- **Expiry input** is an absolute date-time with an explicit zone (Clarifications); no relative
  durations and no maximum horizon.
- **Expired codes are reclaimable** (Clarifications): the engineer chose availability over
  reservation, accepting that a previously shared short URL may later point to a new destination
  once its link has expired. Only custom-alias requests reclaim; the generator never targets
  expired codes.
- **Analytics is best-effort (at-most-once)**, consistent with feature 001 Clarification Q1: losing
  a few clicks under failure is acceptable; failing a redirect is not.
- **Days are UTC calendar days**; no per-viewer time zones.
- **Click events are retained indefinitely** for now; a retention policy is future work.
- **Legacy totals**: clicks counted before this feature have no per-click detail, so daily and
  referrer breakdowns may sum to less than the all-time total for older links.
- **Bot clicks are counted** (Clarifications) and tagged `Bot`; filtering can be added later without
  losing history. Totals therefore include link previews; bot detection by user agent is
  best-effort.
- **Out of scope**: link editing/deletion, per-visitor analytics or unique visitors, geography,
  device type beyond browser family, rate limiting (feature 003), authentication.
