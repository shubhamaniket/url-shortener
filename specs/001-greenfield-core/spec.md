# Feature Specification: Greenfield Core URL Shortener

**Feature Branch**: `001-greenfield-core`

**Created**: 2026-10-10

**Status**: Draft

**Input**: User description: "Build the core of a URL shortener service.
Users can submit a long URL and receive a short code and a short URL. They may optionally request a custom alias.
Anyone visiting the short URL is redirected to the original URL.
Each redirect is counted, and the owner can view the total click count for a link.
The service exposes a health endpoint.
Out of scope for now: user accounts, link expiry, detailed analytics, link editing, custom domains."

## Clarifications

### Session 2026-10-10

- Q: If recording a click fails or lags, how accurate does the click count need to be? → A: Best-effort. Every redirect is counted in normal operation; if counting fails, the redirect still succeeds, the failure is logged, and that click may be lost.
- Q: Since there are no user accounts, who should be allowed to see a link's details and click count? → A: Anyone who knows the short code (public stats). Access control is deferred to a future authentication/authorization feature.
- Q: Should two custom aliases that differ only in letter case be allowed to exist as separate links? → A: No. Generated codes stay case-sensitive; custom aliases are unique ignoring case, keep the case they were created with, and resolve in any case.
- Q: How should the service know its own public address, used to build short URLs and to reject URLs that point back at itself? → A: A single configured public base URL; it builds every short URL, and destinations on its host are rejected. The incoming request's host is never used.
- Q: Should URLs that contain a username or password before the host (e.g. `https://google.com@evil.example/login`) be rejected? → A: Yes. Any URL containing user info is rejected with a clear error.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Shorten a long URL (Priority: P1)

A user submits a long web address and gets back a short code and a complete short URL they can
share.

**Why this priority**: Creating short links is the reason the service exists; nothing else
works without it.

**Independent Test**: Submit a valid long URL and confirm the response contains a short code, a
complete short URL built from that code, and the original URL, and that the link is persisted.

**Acceptance Scenarios**:

1. **Given** a valid `https` URL, **When** the user submits it without an alias, **Then** the
   service returns a newly generated short code, the full short URL, the original URL, and the
   creation time.
2. **Given** the same long URL submitted twice, **When** both requests succeed, **Then** each
   receives its own distinct short code.
3. **Given** a URL with an unsupported scheme (e.g. `javascript:alert(1)`, `data:…`, `file:///…`,
   `ftp://…`), **When** the user submits it, **Then** the request is rejected with an error
   explaining that only http and https URLs are accepted, and no link is created.
4. **Given** a relative or malformed URL (e.g. `example.com/page`, `not a url`), **When** the user
   submits it, **Then** the request is rejected with a validation error and no link is created.
5. **Given** a URL that points at the shortener's own host, **When** the user submits it,
   **Then** the request is rejected to prevent redirect loops.
6. **Given** an address without a scheme such as `www.example.com/page`, **When** the user
   submits it, **Then** the request is rejected with an error suggesting the user add `https://`,
   and no link is created.
7. **Given** a URL containing user info such as `https://google.com@evil.example/login` or
   `https://user:pass@site.example`, **When** the user submits it, **Then** the request is
   rejected with an error explaining that URLs with embedded credentials are not allowed, and no
   link is created.

---

### User Story 2 - Visit a short URL and be redirected (Priority: P1)

Anyone who opens a short URL is sent to the original long URL.

**Why this priority**: Redirection is the value delivered to the people who receive short links;
it is the most frequently used path.

**Independent Test**: Create a link, request its short URL, and confirm the response is a
temporary redirect whose target is exactly the original URL.

**Acceptance Scenarios**:

1. **Given** an existing short code, **When** a visitor requests its short URL, **Then** they are
   redirected (temporary redirect) to the original URL.
2. **Given** a short code that does not exist, **When** a visitor requests it, **Then** they
   receive a not-found response and are not redirected.

---

### User Story 3 - Choose a custom alias (Priority: P2)

A user may ask for a memorable alias (e.g. `team-offsite`) instead of a generated code.

**Why this priority**: Valuable for readability and sharing, but the service is usable without
it.

**Independent Test**: Create a link with a valid, unused alias and confirm the short URL uses that
alias and redirects correctly; then confirm a second request for the same alias is refused.

**Acceptance Scenarios**:

1. **Given** a valid, unused alias, **When** the user submits a URL with it, **Then** the link is
   created with the alias as its short code.
2. **Given** an alias already in use, **When** another user requests it, **Then** the request is
   rejected with a conflict error and the existing link is unchanged.
3. **Given** an alias that breaks the format rules (too short, too long, or containing characters
   other than letters, digits, `-` and `_`), **When** submitted, **Then** it is rejected with a
   validation error.
4. **Given** an alias that is a reserved word (e.g. `api`, `health`, `swagger`), **When**
   submitted, **Then** it is rejected so it cannot shadow service routes.

---

### User Story 4 - View a link's total click count (Priority: P2)

Someone who knows a short code can look up the link's details, including how many times it has
been followed.

**Why this priority**: Gives link creators basic feedback; secondary to creating and following
links.

**Independent Test**: Create a link, follow it N times, then look it up and confirm the click
count equals N.

**Acceptance Scenarios**:

1. **Given** a newly created link, **When** its details are requested, **Then** the response shows
   the short code, short URL, original URL, creation time, and a click count of 0.
2. **Given** a link that has been followed 3 times, **When** its details are requested, **Then**
   the click count is 3.
3. **Given** a short code that does not exist, **When** its details are requested, **Then** a
   not-found error is returned.
4. **Given** a link, **When** its details are requested, **Then** the click count does not
   increase (looking up details is not a click).

---

### User Story 5 - Check service health (Priority: P3)

An operator or orchestrator checks whether the service and its data store are available.

**Why this priority**: Operational need; does not deliver user value directly, but required for
reliable running.

**Independent Test**: Call the health check while the service is running normally and confirm it
reports healthy.

**Acceptance Scenarios**:

1. **Given** the service and its data store are available, **When** health is checked, **Then** it
   reports healthy.
2. **Given** the data store is unavailable, **When** health is checked, **Then** it reports
   unhealthy.

---

### Edge Cases

- A URL longer than 2048 characters is rejected with a validation error.
- An empty or missing URL is rejected with a validation error.
- A URL with leading/trailing whitespace is trimmed before validation.
- Inputs without an `http`/`https` scheme that may still parse as some other kind of URL are
  rejected, e.g. `www.example.com:8080/page` (read as scheme `www.example.com`), `localhost:8080`
  (read as scheme `localhost`), and `/some/path` (read as a local file path on some platforms).
  Acceptance is decided by an explicit `http`/`https` allowlist, not by "parses as a URL".
- A generated code that collides with an existing code is regenerated transparently; the user
  never sees the collision. If a unique code still cannot be produced after a bounded number of
  attempts, the request fails with a server error rather than overwriting a link.
- Two simultaneous requests for the same custom alias: exactly one succeeds; the other receives
  the conflict error (never a server error or a duplicate).
- A create request with a forged host (e.g. `Host: evil.example`) still returns a short URL on
  the configured base URL.
- A destination on the service's own host is rejected in any variation, e.g. different letter
  case (`HTTP://LOCALHOST:8080/x`), scheme, or port.
- Generated codes are case-sensitive: `Abc123x` and `abc123x` are different codes.
- Custom aliases are case-insensitive for uniqueness and lookup: once `team-offsite` exists,
  requesting `Team-Offsite` is a conflict, and visiting `/Team-Offsite` redirects to the
  `team-offsite` link.
- A custom alias that matches an existing generated code ignoring case is treated as taken.
- Requests for paths that are reserved service routes (e.g. the health check) are never treated as
  short codes.
- Concurrent redirects of the same link are all counted under normal operation (no lost
  increments from races).
- If recording a click fails (e.g. the data store is briefly unavailable for the counter update),
  the visitor is still redirected to the original URL; the failure is logged and the click is not
  counted.

## Requirements *(mandatory)*

### Functional Requirements

**Link creation**

- **FR-001**: System MUST accept a long URL and create a short link with a unique short code.
- **FR-002**: System MUST return the short code, the full short URL, the original URL, and the
  creation time on successful creation.
- **FR-003**: System MUST accept only absolute URLs with the `http` or `https` scheme and a host;
  all other schemes and relative URLs MUST be rejected.
- **FR-003a**: System MUST reject URLs that contain user info (a username and/or password before
  the host, e.g. `https://google.com@evil.example`), because it disguises the real destination
  and can carry credentials.
- **FR-004**: System MUST reject URLs longer than 2048 characters.
- **FR-005**: System MUST reject URLs whose host is the shortener's own host, i.e. the host of the
  configured public base URL, compared case-insensitively and regardless of scheme or port.
- **FR-005a**: Short URLs MUST be built from a single configured public base URL. The host supplied
  by an incoming request MUST NOT be used to build short URLs or for the self-host check, so a
  forged request host cannot change the returned short URL.
- **FR-006**: Generated short codes MUST be 7 characters from the 62-character alphabet
  (`A–Z`, `a–z`, `0–9`) and MUST be produced from a cryptographically secure random source so
  codes are not guessable or sequential.
- **FR-007**: System MUST guarantee code uniqueness; on a collision it MUST retry generation a
  bounded number of times (max 5) and MUST NOT overwrite an existing link.
- **FR-008**: Submitting the same long URL more than once MUST create a new, distinct short link
  each time.

**Custom alias**

- **FR-009**: Users MAY supply a custom alias; when supplied it MUST be used as the short code.
- **FR-010**: A custom alias MUST be 3–30 characters long and contain only letters, digits, `-`
  and `_`.
- **FR-011**: System MUST reject aliases that are reserved words (at minimum `api`, `health`,
  `swagger`), compared case-insensitively.
- **FR-012**: System MUST reject an alias that is already in use with a conflict error; uniqueness
  MUST hold under concurrent requests. "In use" is compared case-insensitively against all
  existing codes (aliases and generated codes): if `team-offsite` exists, `Team-Offsite` is taken.
- **FR-012a**: A custom alias MUST be stored and displayed with the letter case it was created
  with.

**Redirect**

- **FR-013**: Requesting a short URL for an existing code MUST redirect to the original URL using a
  temporary redirect, so browsers do not cache it permanently and every visit reaches the service
  and is counted.
- **FR-014**: Requesting an unknown code MUST return a not-found response.
- **FR-015**: Under normal operation, each successful redirect MUST increase the link's click count
  by exactly one, visible in the link's details within 5 seconds.
- **FR-015a**: Click counting is best-effort: if a click cannot be recorded, the redirect MUST still
  succeed unchanged, the failure MUST be logged, and that click MAY be lost.
- **FR-016**: Generated codes MUST be matched case-sensitively. Custom aliases MUST be matched
  in any letter case (`/TEAM-OFFSITE` resolves `team-offsite`). An exact-case match always takes
  precedence over a case-insensitive alias match.
- **FR-016a**: A newly generated code that matches an existing custom alias ignoring case MUST be
  treated as a collision and regenerated, so any-case alias lookups are never ambiguous.

**Link details**

- **FR-017**: System MUST let anyone who knows a short code retrieve that link's details: short
  code, short URL, original URL, creation time, and total click count.
- **FR-018**: Retrieving link details MUST NOT change the click count.
- **FR-018a**: Link details MUST be retrievable without credentials; knowing the short code is
  sufficient. Details MUST contain only the fields listed in FR-017 (no visitor data).

**Health**

- **FR-019**: System MUST expose a health check that reports healthy only when the service can
  reach its data store.

**Errors and privacy**

- **FR-020**: All error responses MUST use one consistent, machine-readable error format with a
  human-readable title and detail, and MUST NOT expose internal details such as stack traces.
- **FR-021**: System MUST NOT store visitor IP addresses or other personal data for this feature.

### Key Entities

- **Short Link**: A mapping from a short code to an original URL. Attributes: short code (unique,
  either generated or a custom alias), whether the code is a custom alias (it determines the
  matching rules in FR-016), original URL, creation time (UTC), total click count.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can turn a long URL into a working short URL with a single request and no
  account.
- **SC-002**: 100% of the malicious and malformed URL cases listed in this spec are rejected, and
  none results in a stored link.
- **SC-003**: Under normal operation, for a link followed N times, its reported click count is
  exactly N (verified for sequential and concurrent visits).
- **SC-003a**: When click recording is made to fail, 100% of redirects still reach the original
  URL.
- **SC-004**: Under local, single-user conditions, 95% of redirects complete in under 100 ms,
  measured end-to-end from a client on the same machine.
- **SC-005**: Concurrent requests for the same custom alias never produce two links or a server
  error; exactly one succeeds.
- **SC-006**: Every error response a user can trigger (invalid URL, invalid alias, alias taken,
  unknown code) has the same structure and a clear explanation.

## Assumptions

- **No accounts**: Because user accounts are out of scope, the "owner" of a link is anyone who
  knows its short code; link details and click counts are public to anyone with the code (see
  Clarifications). This is acceptable because generated codes are random and hard to guess and
  the exposed data is low-sensitivity. Ownership and access control are deferred to a future
  authentication/authorization feature.
- **No deduplication**: The same long URL creates a new short link each time (FR-008); dedupe or
  idempotency may be revisited later.
- **Letter case**: Generated codes use the 62-character alphabet and are matched case-sensitively,
  but no two codes may differ only in letter case; a generated code that would is regenerated
  (the remaining code space is still far beyond prototype needs; see plan research R3). Custom
  aliases are human-typed, so they are unique and resolvable regardless of case, which also blocks
  look-alike aliases (see Clarifications). Reserved-word checks are case-insensitive so `API` and
  `Health` are also blocked.
- **Click counting**: A click is any successful redirect, counted best-effort (see
  Clarifications): redirect availability takes priority over count accuracy. Bot filtering, unique visitors, and
  per-click details are out of scope (detailed analytics is deferred).
- **No scheme guessing**: The service never adds a missing scheme itself; an address like
  `www.example.com` is rejected with a hint. Any convenience prefixing of `https://` belongs in a
  user interface, not in the service.
- **Short URL base**: The short URL is built from a configured public base URL (e.g.
  `http://localhost:8080` locally), never from the incoming request (see Clarifications).
- **Rate limiting**: Required by the constitution for public endpoints but delivered in a later
  feature (reliability and abuse protection); not part of this spec's acceptance.
- **Out of scope**: user accounts, link expiry, detailed analytics, link editing or deletion,
  custom domains, URL safety blocklists beyond the scheme, user-info, and self-host checks.
