# Feature Specification: Per-Client Request Budgets (Rate Limiting)

**Feature Branch**: `003-reliability-abuse-protection`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Protect the public endpoints of the existing URL shortener from single-client overuse, so one client cannot degrade the service for others. Each client (identified by IP address, held in memory only, never stored or logged) gets its own request budget per endpoint group: create (strict), redirect (generous, bursts allowed) and read (link details and statistics). Requests beyond the budget are rejected immediately with 429 Too Many Requests, a Retry-After header and a ProblemDetails body; rejected creates create nothing. Budgets are configurable; health checks are never limited; rejections are logged with the policy name and no client identity. Existing behaviour (features 001 and 002) is unchanged for clients within their budget, including redirect latency. Interpretation, assumptions and scope decisions: docs/scenarios/03-ambiguous.md."

**Origin**: the vague requirement "The service should be reliable and protected from abuse."
Its interpretation, the product-owner questions, the assumptions and the scope decision (rate
limiting only; everything else deliberately left out, with reasons) are in
[docs/scenarios/03-ambiguous.md](../../docs/scenarios/03-ambiguous.md) §2.

## Clarifications

### Session 2026-10-11

- Q: Should requests that fail (invalid input, unknown code) count against a client's budget? → A: Yes, every request counts whatever its outcome; enumeration and spam consist mostly of failing requests.
- Q: Should the service trust a forwarded client address from a reverse proxy (`X-Forwarded-For`) to identify clients? → A: No. Clients are identified by the connection's own address; forwarded headers are ignored, because trusting them would let any client fake its address and bypass the limits. Running behind a proxy requires explicit configuration later.
- Q: Are the default budgets right? → A: Yes: create 10 per minute, redirect 120 per minute with bursts up to 60, read (details and statistics) 60 per minute, per client; all configurable.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stop one client from mass-creating links (Priority: P1)

A spammer or a runaway script sends link-creation requests as fast as it can. The service accepts
creates from that client up to its budget, then rejects the rest immediately with a clear "too many
requests" answer that says when to try again. Nothing is created by a rejected request, and other
clients keep creating links normally.

**Why this priority**: Creating links has lasting cost (stored rows, used-up codes) and is the
spam vector; it is the most direct answer to "protected from abuse".

**Independent Test**: From one client, send more creates in one minute than the create budget
allows; the excess is rejected with "too many requests" and a retry time, no extra links exist, and
a second client can still create links.

**Acceptance Scenarios**:

1. **Given** a client that has used its whole create budget in the current window, **When** it
   sends another valid create request, **Then** the request is rejected as "too many requests" with
   the number of seconds to wait and an error body in the standard problem format, and no link is
   created.
2. **Given** that client was rejected, **When** the waiting time has passed, **Then** its next
   create succeeds.
3. **Given** one client is being rejected, **When** a different client creates a link, **Then** it
   succeeds.
4. **Given** a client sends invalid create requests (for example bad URLs), **When** it keeps
   sending them, **Then** they count against its create budget like any other create, and it is
   rejected once the budget is used up.

---

### User Story 2 - Keep links and statistics available under floods (Priority: P1)

A bot follows short links or reads link details and statistics at high speed, for example to
enumerate codes or scrape statistics. Each client may follow links generously, including short
bursts such as a link shared in a busy chat, and may read details and statistics at a moderate
rate; beyond that it is rejected immediately. Budgets for following links, reading, and creating
are separate.

**Why this priority**: Following links is the core public function; it must stay available to
everyone while a single client floods it.

**Independent Test**: From one client, follow a link more often than the redirect budget allows;
the excess is rejected with a retry time and records no click, while another client's redirects
still succeed, and the first client can still create links and read details.

**Acceptance Scenarios**:

1. **Given** a client within its redirect budget, **When** it follows a link, **Then** it is
   redirected exactly as before this feature (same response, click recorded).
2. **Given** a client sends a burst of redirects up to the burst allowance, **When** they arrive at
   once, **Then** all of them are redirected.
3. **Given** a client has exhausted its redirect budget, **When** it follows a link (valid, unknown,
   or expired), **Then** it is rejected as "too many requests" with a retry time, and no click is
   recorded.
4. **Given** a client has exhausted its create budget, **When** it follows a link or reads details,
   **Then** those succeed (separate budgets), and vice versa.
5. **Given** a client exceeds its read budget, **When** it requests link details or statistics,
   **Then** it is rejected as "too many requests" with a retry time.

---

### User Story 3 - Operators tune budgets and see rejections (Priority: P2)

An operator can change each budget without changing code, can see in the logs that and where
clients are being throttled (without learning who they are), and the health check keeps answering
during a flood so monitoring never mistakes throttling for an outage.

**Why this priority**: Limits that cannot be tuned or observed become either useless or harmful;
health probes must stay reliable.

**Independent Test**: Start the service with a changed budget and observe the new limit; flood an
endpoint and observe a log entry naming the endpoint group with no client address; flood the
health check and observe that it is never rejected.

**Acceptance Scenarios**:

1. **Given** a create budget configured to a different value, **When** the service starts, **Then**
   that value is enforced.
2. **Given** a budget is configured with an invalid value (zero, negative), **When** the service
   starts, **Then** it refuses to start with a message naming the setting.
3. **Given** a request is rejected, **When** an operator reads the logs, **Then** there is an entry
   naming the endpoint group, and no log entry contains the client's address.
4. **Given** any number of health-check requests from one client, **When** they arrive, **Then** none
   is rejected for exceeding a budget.

---

### Edge Cases

- **Requests whose client address cannot be determined** share one common budget per group, so they
  cannot bypass limits.
- **Rejected requests do not use up budget**: a client that keeps hammering after being rejected is
  served again as soon as the window allows.
- **Every request in a group counts**, whatever its outcome (success, invalid input, conflict, not
  found, gone), because failing requests are also abuse (for example enumerating unknown codes).
- **A rejected redirect for an expired or unknown code** is still answered "too many requests"
  (the budget check comes first; nothing about the code is revealed).
- **Service restarts** reset all budgets; budgets are not persisted.
- **Many clients behind one shared address** (an office network) share one budget; accepted for this
  prototype (Assumptions).
- **The API documentation pages** (development only) are not limited.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST give each client its own request budget for each of three endpoint
  groups: **create** (creating links), **redirect** (following short links), and **read** (link
  details and statistics).
- **FR-002**: The default create budget MUST be **10 requests per minute** per client.
- **FR-003**: The default redirect budget MUST allow a sustained **120 requests per minute** per
  client, with bursts of up to **60 requests at once**.
- **FR-004**: The default read budget MUST be **60 requests per minute** per client.
- **FR-005**: A request beyond its budget MUST be rejected immediately (not queued or delayed) with a
  "too many requests" status, a retry-after value in whole seconds of at least 1, and an error body
  in the same problem format as the service's other errors.
- **FR-006**: A rejected request MUST have no effect: no link created, no click recorded.
- **FR-007**: Budgets MUST be independent per client and per group.
- **FR-008**: Every request to a limited group MUST count against the budget, whatever its outcome
  (success, invalid input, conflict, not found, gone); rejected requests MUST NOT count.
- **FR-009**: Health checks MUST never be rate limited.
- **FR-010**: Each budget (limit, window, and for redirects the burst size) MUST be configurable
  without code changes; invalid values MUST stop the service at startup with a clear message.
- **FR-011**: The client's network address MAY be used only in memory to tell clients apart; it MUST
  NOT be stored or written to logs, and nothing about a client MUST be kept once its window has
  passed. Requests without a determinable address share one budget per group.
- **FR-011a**: The client address MUST be the address of the network connection itself; addresses
  claimed in request headers (such as forwarded-for headers) MUST be ignored, so a client cannot
  escape its budget by claiming another address.
- **FR-012**: Each rejection MUST be logged with the endpoint group, and without the client address.
- **FR-013**: For clients within their budgets, all existing behaviour from features 001 and 002
  MUST be unchanged.
- **FR-014**: The published API contract MUST document the "too many requests" response on every
  limited operation.

### Key Entities

- **Request budget**: per client and per endpoint group; holds how many requests remain in the
  current window (and, for redirects, the burst allowance). In memory only; never persisted.
- **Endpoint group**: create, redirect, or read; each with its own configured limit and window.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A client that sends 11 creates within one minute has exactly 10 accepted and the 11th
  rejected with a retry time; 0 links are created by rejected requests.
- **SC-002**: While one client is being rejected, 100% of another client's requests within its own
  budget succeed.
- **SC-003**: After the indicated retry time has passed, the rejected client's next request succeeds.
- **SC-004**: For clients within budget, 95% of redirects still complete in under 100 ms (unchanged
  from feature 002), measured on the running service.
- **SC-005**: During a flood from one client, 100% of health-check requests succeed.
- **SC-006**: All feature 001 and 002 acceptance tests still pass; any test changed because it sends
  more requests than a default budget is listed with its reason.
- **SC-007**: No stored data and no log entry contains a client address.

## Assumptions

- **Single instance, directly exposed.** Budgets are kept in the process's memory. Forwarded
  headers are ignored (Clarification 2); behind a reverse proxy all clients would share the proxy's
  budget until trusted-proxy handling is configured, which is future work. Multi-instance (shared)
  budgets are out of scope.
- **Clients are identified by network address.** There are no accounts or API keys; clients sharing
  an address share a budget.
- **Default numbers are starting points** (confirmed in Clarification 3), chosen so normal use (a person creating a few links,
  following links shared in a busy chat) is never throttled; they are configuration.
- **Automated test environments may use larger budgets** so existing functional tests (which send
  many requests quickly) are not throttled; dedicated tests use small budgets to exercise limiting.
- **Out of scope** (scenario doc §2.5): liveness/readiness health split, destination blocklist,
  idempotent create, caching, request correlation IDs, authentication, malware scanning, distributed
  rate limiting.
