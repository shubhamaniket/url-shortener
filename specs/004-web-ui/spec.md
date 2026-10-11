# Feature Specification: Web UI for the URL Shortener

**Feature Branch**: `phase-5-ci-docker-web`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "A minimal single-page web UI for the existing URL shortener API, for people who do not use curl or Swagger. On one page a user can: (1) create a short link from a long URL with an optional custom alias and an optional expiry time (entered in their local time, sent with their time zone), then see the short URL with a one-click copy button; (2) look up any code to see its details (original URL, created and expiry time, total clicks) and its statistics (clicks per day for the last 30 days as a simple table or bar list, top 5 referrers); (3) see the API's error messages in plain words (validation errors per field, alias taken, link expired, unknown code, too many requests with the retry time). No accounts. The UI only calls the existing public API; it adds no new server behaviour. It is served by the same service in the container (one origin), and during local development it talks to the API through a dev proxy. Keep it small, accessible (labels, keyboard use, readable errors) and fast to load."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Shorten a link in the browser (Priority: P1)

A person pastes a long URL, optionally types a custom alias and picks an expiry date and time, and
submits. They see the new short URL and copy it with one click.

**Why this priority**: Creating a link is the reason the service exists; without it the UI has no
value.

**Independent Test**: Open the page, enter `https://example.com/some/long/path`, submit, and copy the
short URL shown; pasting it in a new tab reaches the original page.

**Acceptance Scenarios**:

1. **Given** a valid URL, **When** the user submits, **Then** the short URL is shown with a copy
   button, and the copy button puts exactly that URL on the clipboard and confirms it ("Copied").
2. **Given** a custom alias, **When** the user submits, **Then** the short URL uses that alias.
3. **Given** an expiry picked in the user's local time, **When** the user submits, **Then** the link is
   created with that moment (local time plus the user's time zone), and the result shows when it
   expires in the user's local time.
4. **Given** an expiry in the past, **When** the user submits, **Then** the service's message ("must be
   in the future") is shown next to the expiry field and nothing is created.
5. **Given** an invalid URL or alias, **When** the user submits, **Then** each message is shown next to
   its own field, the entered values stay in place, and focus moves to the first field in error.
6. **Given** an alias that is already taken, **When** the user submits, **Then** a plain message says the
   alias is in use.

---

### User Story 2 - Look up a link's details and statistics (Priority: P2)

A person enters a code (or pastes a whole short URL) and sees the link's details and its click
statistics.

**Why this priority**: Seeing how a link performs is the second reason people use a shortener; it
depends on links existing (US1) but not on the creation form.

**Independent Test**: Look up a code that has clicks: the page shows the original URL, the created and
expiry times, the total clicks, 30 daily counts, and the top referrers.

**Acceptance Scenarios**:

1. **Given** an existing code, **When** the user looks it up, **Then** the original URL, the creation time,
   the expiry time (or "never") and the total clicks are shown.
2. **Given** that code has clicks, **When** its statistics are shown, **Then** the last 30 days appear in
   date order with a count and a proportional bar each, and up to 5 referrers appear with their counts,
   "(direct)" included.
3. **Given** the user pastes a full short URL instead of a code, **When** they look it up, **Then** the code
   is taken from it.
4. **Given** an expired link, **When** it is looked up, **Then** details and statistics are shown and the
   link is marked as expired.
5. **Given** an unknown code, **When** it is looked up, **Then** a plain "no link with this code" message
   is shown.
6. **Given** a link was just created in US1, **When** the user chooses to view it, **Then** the lookup
   fills in that code and shows its details.

---

### User Story 3 - Understand every failure (Priority: P2)

Whatever goes wrong, the user sees what happened and what to do, in plain words, never a raw error
dump or a silent failure.

**Why this priority**: The API already returns precise errors; an interface that hides them makes the
service feel broken.

**Independent Test**: Trigger each failure (invalid input, alias taken, unknown code, too many requests,
service unreachable) and read a clear message for each.

**Acceptance Scenarios**:

1. **Given** the user has sent too many requests, **When** the service refuses, **Then** the message says
   to try again and states the waiting time in seconds.
2. **Given** the service cannot be reached, **When** the user submits, **Then** a message says the service
   is unavailable and the user can retry.
3. **Given** any failure, **When** it is shown, **Then** it is announced to screen-reader users and does
   not erase what the user typed.

---

### Edge Cases

- A request is in progress: the submit button is disabled and shows progress, so a double click cannot
  create two links.
- Clipboard access is not available (older or locked-down browsers): the short URL stays selectable
  and the user is told to copy it manually.
- A code with surrounding spaces or a pasted short URL with a trailing slash still resolves.
- A link with no clicks: statistics show 30 zero days and "No referrers yet".
- Very long original URLs wrap instead of breaking the layout.
- Expiry field left empty: the link never expires.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The page MUST offer a form with a required URL field and optional alias and expiry fields,
  each with a visible label.
- **FR-002**: The expiry MUST be entered as a local date and time and sent with the user's time-zone
  offset; results MUST show times in the user's local time.
- **FR-003**: After a successful create, the page MUST show the short URL as a link with a copy button
  that confirms the copy.
- **FR-004**: The page MUST offer a lookup by code that also accepts a full short URL.
- **FR-005**: A lookup MUST show the original URL, creation time, expiry ("never" when absent), whether
  the link has expired, and total clicks.
- **FR-006**: A lookup MUST show 30 daily click counts (oldest first, each with a proportional bar) and
  up to 5 referrers with counts.
- **FR-007**: Field-level validation messages from the service MUST appear next to their fields; other
  errors MUST appear as a single message near the action that caused them.
- **FR-008**: "Too many requests" MUST be shown with the number of seconds to wait.
- **FR-009**: An unreachable service MUST produce a clear message, not a silent failure.
- **FR-010**: Errors and results MUST be announced to assistive technology, and every action MUST be
  usable with the keyboard alone.
- **FR-011**: While a request is in progress the triggering button MUST be disabled.
- **FR-012**: The UI MUST use only the existing public API and MUST NOT require any server behaviour
  change.
- **FR-013**: The UI MUST be served from the same address as the API in the packaged service, and MUST be
  usable during local development against a locally running API.

### Key Entities

- **Created link (view)**: code, short URL, original URL, created time, expiry, total clicks; as returned
  by the service.
- **Link statistics (view)**: total clicks, 30 daily counts, top referrers; as returned by the service.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time user creates and copies a short link in under 30 seconds without help.
- **SC-002**: Every API error case (invalid field, alias taken, expired, unknown code, too many
  requests, unreachable) produces a readable message; 0 raw error dumps.
- **SC-003**: All actions can be completed with the keyboard only, and an automated accessibility check
  of the page reports no critical violations.
- **SC-004**: The page is usable within 2 seconds on a typical laptop and local network, and its
  delivered size is under 200 KB compressed.
- **SC-005**: The API's behaviour is unchanged by this feature (all existing server tests pass
  unchanged); the server only additionally delivers the page itself.

## Assumptions

- **No accounts, no history**: the page does not remember the user's links between visits.
- **Same rate limits apply**: the browser is a client like any other; a demo can hit the create budget
  (10 per minute) and will see the "too many requests" message.
- **Modern evergreen browsers** (current Chrome, Edge, Firefox, Safari); no legacy browser support.
- **English only**; no localisation.
- **A simple bar list is enough** for daily clicks; no charting dependency.
- **The API documentation (Swagger) remains** the tool for developers; this page is for everyone else.
