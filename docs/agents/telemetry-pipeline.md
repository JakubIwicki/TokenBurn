---
name: telemetry-pipeline
description: Normalized envelope contract, one adapter per source format, idempotency and replay rules
governs: ["**/Adapters/**", "**/Processor/**", "**/Contracts/**", "**/Collector/**", "PLAN.md", "PROCESS.md"]
---

# Telemetry Pipeline

## What this doc governs

Every source-format adapter, every Kafka consumer in `TokenBurn.Processor`, the message contracts
in `TokenBurn.Contracts`, and the backfill paths in `TokenBurn.Collector`. It specialises the
global `cross-service-data-flow` and `async-command-processing` skills for this project's
ingest chain. For the durable command lifecycle itself, follow `async-command-processing`.

## Rules

1. **One adapter per source format, and no source format leaks past it.** `OtlpGenAiAdapter`,
   `ClaudeCodeTranscriptAdapter`, `DelegateLedgerAdapter` and `JiCachingAdapter` all emit the same
   normalized envelope. Nothing downstream may branch on `source` to reinterpret a field.
2. **Idempotency keys are database constraints, not handler conventions.** Ingest deduplicates on
   `content_hash`; runs on **`(session_id, agent_id)`**; messages on `(run_id, sequence)`;
   findings on `(run_id, kind, evidence_hash)`. Every participating column is `NOT NULL` —
   Postgres treats NULLs as distinct, so a nullable unique key constrains nothing. Handlers upsert
   on the constraint; they never `SELECT`-then-`INSERT`.
2a. **Run identity is the session, not the source.** Delegate children are themselves Claude Code
   sessions, so the same run arrives from two adapters. `source` is provenance; it is never part
   of the identity key. Every adapter must populate `session_id`.
3. **Replay must be safe and cheap.** Reprocessing a topic from the beginning must converge to the
   same state. Prefer upserts over inserts; never accumulate into a counter that a replay would
   double.
4. **Pricing is a pure function of `(usage, model, service, timestamp)`.** It reads the versioned
   registry and nothing else — no ambient clock, no current-price lookup, and the peak multiplier
   is applied from the run's own timestamp, never from `now`. Aliases and tier suffixes (`[1m]`,
   `-pro`) resolve through `model_aliases`. **An unresolved slug is an explicit categorised
   failure that quarantines the run.** A default price tier is never substituted — the existing
   prototype does exactly that and silently bills Opus traffic at flash rates.
4a. **Three token classes, not two.** `input`, `cache_read` and `cache_write` are separate price
   tiers and separate columns. Collapsing cache-write into input mis-prices the corpus
   irrecoverably, because the distinction cannot be reconstructed after ingest.
4b. **A reconciliation target is a pinned artifact with a stated tolerance**, never a live command.
   Commit the fixture, state the comparison subset, state the numeric tolerance, and report
   coverage for everything outside it.
5. **Ingest stays dumb.** The receiver validates shape, persists the raw envelope and writes the
   outbox row. Parsing, enrichment and pricing belong in the Processor, never on the request path.
6. **Contracts are versioned and additive.** Add fields; do not repurpose them. A breaking change
   needs a new topic.
7. **Ordering is a stated contract, not an accident.** Partition key is `session_id` on every topic
   in the chain, one consumer instance per partition, and the outbox drains single-threaded per
   key ordered by `(occurred_at, id)`. A stage that needs a different key needs a written
   justification.
8. **Two consumers never write the same document.** Indexing and embedding are chained
   (`priced → indexed → embedded`), and the embedder issues a partial `_update` with
   `doc_as_upsert`. Fanning both off `telemetry.priced` makes them race and clobber each other.
9. **Replay reads from `ingest.envelopes`, not from Kafka.** Broker retention is bounded and is not
   a durability guarantee. Any claim that "replay is routine" must name the store it replays from.

## Reference files

Populate as the canonical examples land:

- Envelope contract: `backend/src/TokenBurn.Contracts/` (Phase 2)
- Canonical adapter: `backend/src/TokenBurn.Processor/Adapters/DelegateLedgerAdapter.cs` (Phase 2)
- Consumer test pattern: `backend/tests/TokenBurn.Testing.Common/` (Phase 1)
