# ADR-001: Clean architecture with attribute-routed controllers

- **Status**: Accepted
- **Date**: 2026-10-09 (decided in planning); implemented in feature 001
- **Deciders**: Engineer (changed the AI's initial proposal of minimal APIs; AI usage log #4)
- **Related**: constitution III ("Clean architecture, thin controllers") · feature 001 plan

## Context

A URL shortener is small, but this one had to grow through three scenarios (greenfield, brownfield,
ambiguous) without rewrites, be reviewed by people unfamiliar with it, and keep business rules
testable without HTTP or a database. The AI's first plan used ASP.NET Core minimal APIs.

## Decision

- **Four projects with one dependency direction**: `Api → Application → Domain`; `Infrastructure`
  implements interfaces declared in `Application` (`IShortLinkRepository`, `IShortCodeGenerator`,
  `IClickRecorder`, `IClickStatsReader`). `Domain` has no package dependencies.
- **Domain** holds the rules as small types: `DestinationUrl`, `CustomAlias`, `ExpiryTime`,
  `ReferrerHost`, `UserAgentFamily`, entities `ShortLink` and `ClickEvent`.
- **Application** holds the use cases (`LinkService`): create, resolve for redirect, details,
  statistics.
- **Api uses attribute-routed controllers** (`[ApiController]`), kept thin: map HTTP to the
  application service and back; no business logic or data access. Cross-cutting concerns use the
  framework: global exception handler → ProblemDetails, rate-limit attributes, model metadata.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Minimal APIs (AI's first proposal) | Less ceremony, but route groups and filters scatter structure; controllers are familiar to most .NET reviewers and attribute-based rate limiting and response metadata read naturally |
| Single project | Fastest start, but rules leak into HTTP and EF code and become hard to unit test |
| Vertical slices / MediatR | Extra dependency and indirection for four use cases |

## Consequences

- Domain and Application are unit-tested with hand-written fakes (244 unit tests, no HTTP, no database).
- Brownfield changes stayed local: expiry rules in Domain, the off-path recorder in Infrastructure,
  rate limiting only in Api (feature 003 touched no other layer).
- Slightly more ceremony (interfaces, DTOs, mapping) than a single-project minimal API.