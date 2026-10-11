# Scenario 3: Ambiguous — "reliable and protected from abuse"

Feature `003-reliability-abuse-protection`. The requirement below is deliberately vague. This
document records how it was interpreted **before** any specification or code: what is ambiguous,
what a product owner would be asked, what was assumed because no product owner was available, and
the measurable problem that was actually built. Sections 3 onwards are completed as the work
proceeds.

## 1. Requirement (as given)

> "The service should be reliable and protected from abuse."

## 2. Interpretation

### 2.1 What is ambiguous

| Word | Open questions |
|---|---|
| **reliable** | Against what: traffic spikes, a slow or failing database, process crashes, restarts and deploys, bad input? Measured how: availability target, error rate, latency? For which endpoints: the public redirect, the create API, both? |
| **protected** | By what means: limiting request rates, blocking bad destinations, authentication, CAPTCHAs? Enforced where: in the service, at a proxy or CDN? |
| **abuse** | By whom and how: spammers mass-creating links to malicious sites, bots enumerating short codes or scraping statistics, a single client flooding redirects (denial of service), tools retrying aggressively? |
| **the service** | All endpoints, or only the public ones? One instance or several? Behind a reverse proxy? |
| (unstated) | Scale: requests per second to plan for, how many distinct clients, and what a legitimate heavy user looks like |

### 2.2 Questions for a product owner

1. Which failures matter most: traffic spikes, a database outage, crashes during deploys? Is there an availability or latency target?
2. Who are the abusers we worry about: link spammers, code enumerators and scrapers, or someone trying to take the redirect down?
3. What traffic should one instance handle, and what does a legitimate heavy user look like (for example an internal tool creating links in bulk)?
4. Should limits apply per client IP, per API key or account (none exist today), or globally?
5. What should a throttled client see, and may any clients be exempt (internal tools, health probes)?
6. Is blocking malicious **destinations** in scope (a maintained blocklist, an external reputation service), and who maintains such a list?
7. Do clients retry creates in a way that produces duplicate links, so that idempotency matters?
8. What does operations need from health checks (liveness vs readiness for an orchestrator)?
9. Will the service run behind a reverse proxy or CDN? That decides how a client is identified.
10. Are there privacy constraints on using client IP addresses for throttling? Feature 002 already forbids storing them.

### 2.3 Assumptions (no product owner available)

- **Primary abuse** is volume from a single client: mass link creation (spam, filling the code
  space) and floods against the public redirect and read endpoints (enumeration, scraping, denial of
  service). Malicious destinations matter too, but a credible defence needs a maintained reputation
  source, which is out of scope (below).
- **Reliable** here means the service keeps serving legitimate clients when one client misbehaves,
  and rejects the excess quickly and clearly instead of slowing down for everyone. Reliability
  against slow or failing analytics storage was already delivered in feature 002 (redirects never
  touch the database; p95 0.5 ms), as were consistent ProblemDetails errors without stack traces and
  a database health check (feature 001).
- **Clients are identified by IP address**, used in memory only as the rate-limiter key: never
  stored, never logged (consistent with feature 002 FR-010). There are no accounts or API keys.
- **Single instance**, exposed directly, with no reverse proxy rewriting client addresses. Behind a
  proxy, forwarded headers would have to be configured with the proxy's address first; otherwise every
  client would share one limit.
- **Limits are configuration, not code.** Defaults (to be confirmed in the spec): creating links
  much stricter than following them, because creation has lasting cost and is the spam vector, while
  a burst of legitimate redirects (a link shared in a chat) is normal.
- **Health probes are never throttled**, so an orchestrator never mistakes throttling for an outage.

### 2.4 Normalized problem statement

> Protect the public endpoints from single-client overuse so that one client cannot degrade the
> service for others: each client (identified by IP address, held in memory only) gets its own
> request budget per endpoint group — **create** (strict), **redirect** (generous, bursts allowed)
> and **read** (details and statistics) — and requests beyond the budget are rejected immediately
> with **429 Too Many Requests**, a `Retry-After` header and a ProblemDetails body. Limits are
> configurable; health checks are never limited; rejections are logged without client identity.

**Measurable acceptance criteria** (the spec turns these into numbered requirements):

1. A client that exceeds the create budget receives 429 with `Retry-After` (whole seconds, ≥ 1) and
   an `application/problem+json` body; no link is created by a rejected request.
2. Once the window has passed, the same client succeeds again.
3. Another client is unaffected while the first is throttled.
4. Redirect and read budgets are enforced separately from create: exhausting create does not block
   following links, and the reverse.
5. `/health` is never rate limited.
6. Rejections are logged with the policy name and no IP address; no IP address is stored.
7. Under the redirect budget, redirect latency is unchanged (p95 under 100 ms, SC-002 of feature 002).

### 2.5 Scope chosen, and what was deliberately left out

**Built:** rate limiting with ASP.NET Core's built-in rate limiter (named policies on the create,
redirect and read endpoints, 429 + `Retry-After` + ProblemDetails). It is the direct answer to
"protected from abuse", the constitution's Principle IV requires it ("Public endpoints MUST be rate
limited"), and it closes the deviation carried since feature 001.

**Left out, with reasons** (engineer decision on 2026-10-11, with the submission deadline the same day):

| Candidate | Why not now |
|---|---|
| Health liveness vs readiness | `/health` already checks the database; the split only matters to an orchestrator, which arrives with Docker in Phase 5. Next candidate |
| Destination domain blocklist | A static list in configuration gives a false sense of safety; a real defence needs a maintained reputation feed (an external dependency). The existing URL rules already block non-http(s) schemes, credentials in URLs and links back to the shortener |
| Idempotent create (`Idempotency-Key`) | No client is known to retry creates; doing it properly needs key storage, expiry and an ADR. Duplicate links are harmless apart from using codes |
| In-memory cache for hot redirects | Performance rather than reliability: redirects already take 0.5 ms at p95 |
| Structured logging with a correlation ID | ProblemDetails already carry a `traceId`, and logs are structured and PII-free; a request-wide correlation scope is polish |
| Graceful degradation of analytics | Already delivered in feature 002 (bounded queue, drop and log, redirect unaffected) |
| Global exception handler, no stack traces | Already delivered in feature 001 |
| Authentication and accounts | A product change, not hardening; out of scope for the whole prototype |
| Malware scanning through an external API | New external dependency and privacy review |
| Distributed cache or multi-instance rate limiting | Single instance (assumption above); would need a shared store such as Redis |
