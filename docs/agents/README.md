---
name: project-standards
description: Project-specific agent docs for TokenBurn — agent cost & waste radar
---

# Project Standards — TokenBurn

## Non-negotiables

1. **No raw prompt or response text leaves the system** — neither to a public-readable projection
   nor to a third-party model. The corpus contains private source code, absolute paths and
   possibly secrets. Only the redaction/aggregation stage may produce public-safe output, and any
   egress path is opt-in, off by default, and covered by a leak test.
2. **Every Kafka consumer is idempotent and replayable.** Deduplicate on a natural key or content
   hash. Replay is a routine operation, not incident recovery — a consumer that corrupts state on
   redelivery is a defect.
3. **Price against the versioned registry as of the run's timestamp, where price history exists;
   otherwise the price row is seeded at `-infinity` and the run records the multiplier actually
   applied.** Never re-price a historical run at current rates. An unresolvable model slug
   quarantines the run — a default price is never substituted.
4. **No `DateTime.UtcNow` in domain or handler logic** — inject `TimeProvider`.
5. **Every handler takes a `CancellationToken` as its last parameter and forwards it.**

Rules in this section trump everything else. Violating them is always a finding, regardless of
what any other doc or global skill says.

## Overrides

| Project doc | Overrides global skill | Notes |
|---|---|---|
| — | — | None yet. Add a row when a topic doc declares `overrides:` in its frontmatter |

## Doc map

| Doc | Description | Governs |
|---|---|---|
| `telemetry-pipeline.md` | Normalized envelope contract, one adapter per source format, idempotency and replay rules | `**/Adapters/**`, `**/Processor/**`, `**/Contracts/**` |
| `privacy-boundary.md` | What may cross into a public-readable projection, and the tests that prove it | `**/Redaction/**`, `web/app/(public)/**` |

## Load order

`telemetry-pipeline.md` then `privacy-boundary.md`. On conflict the later wins — but a privacy
rule never loses. When a project doc conflicts with a global skill, the project doc wins, except
that security-class rules (`dotnet-security-baseline` and successors) may only be tightened here,
never relaxed.
