---
name: privacy-boundary
description: What may cross into a public-readable projection, and the tests that prove it
governs: ["**/Redaction/**", "web/app/(public)/**", "**/*PublicProjection*", "**/metrics.aggregate*", "**/ModelDirectory/**", "**/Features/Ask/**", "**/Llm/**", "**/Embeddings/**", "backend/tests/TokenBurn.ArchitectureTests/**"]
---

# Privacy Boundary

## What this doc governs

The redaction/aggregation stage, the public-safe projection it emits, and every route under
`web/app/(public)/`. It tightens `dotnet-security-baseline` for this project — it may never relax
it.

## Why this exists

The ingested corpus is roughly 1 GB of real Claude Code and delegate telemetry. It contains
private source code, absolute filesystem paths, unpublished business notes, live-trading details
and plausibly API keys. Anything reachable without authentication is treated as published to the
internet permanently.

## Rules

1. **Default deny.** A field reaches the public projection only if it appears on an explicit
   allow-list. Never build the projection by removing fields from a private model.
2. **Aggregate-only.** Public output carries counts, sums, ratios and model metadata. No message
   text, no file paths, no workspace names, no repository names, no user identifiers.
3. **Minimum aggregation size.** Do not emit a public statistic derived from fewer than N runs
   (start at N=5); a single-run "aggregate" is the original record wearing a hat.
4. **The public route group is anonymous by construction.** Nothing under `web/app/(public)/` may
   call an authenticated API or read a private index. If a page needs a token, it is not public.
5. **Leak tests are mandatory and adversarial.** Every change to the redaction stage ships with a
   test that seeds a known secret-shaped string into the private corpus and asserts it cannot
   appear in the public projection or any public route response.
6. **Private by default in search too.** Private indices are never queried by an unauthenticated
   caller; scope enforcement lives at the endpoint, not in the query builder.
7. **Egress to a third-party model is a privacy boundary crossing.** RAG retrieval puts real trace
   text into a DeepSeek prompt — the same text this doc exists to protect. Therefore: the fake
   chat client is the default in every environment; the real provider is opt-in per environment
   and never the fallback; the retrieved context is redacted before it enters the prompt; and the
   mandatory leak test asserts on the **outbound request body**, not only on the API response.
   The same rule applies to any hosted embeddings provider — which is one reason the embedder runs
   locally.
8. **The anonymous endpoint projects an allow-list.** `/api/models` returns exactly
   `slug, provider, context_window, per-Mtok prices`. Never `SELECT *`: the registry it derives
   from also holds credential env-var names, upstream hostnames and internal ports. It is
   rate-limited and cached, and it is the single commented entry in the endpoint-authorization
   convention test's allow-list.

## Reference files

Populate as the canonical examples land:

- Redaction stage: `backend/src/TokenBurn.Processor/Redaction/` (Phase 7)
- Leak test: `backend/tests/TokenBurn.EndToEnd.Tests/` (Phase 7)
- Public route group: `web/app/(public)/` (Phase 3)
