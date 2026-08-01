# TokenBurn — Blueprint

> Agent cost & waste radar. Ingests LLM/agent telemetry, prices it, indexes it for hybrid
> search, detects waste, and answers grounded questions about it with citations.

**Revision 2.1 (2026-08-01)** — revision 2 corrected the plan against the real corpus after an
adversarial review; 2.1 re-measured every corpus figure line-by-line against the live sources
(see PROCESS.md §4). Revision 1 was written against an imagined dataset.

## 1. Project Overview

TokenBurn is an observability platform for LLM and autonomous-agent spend. It receives agent
telemetry over OTLP (GenAI semantic conventions), normalizes heterogeneous log formats through
adapters, prices every message against a versioned registry, indexes it in Elasticsearch for
hybrid keyword+vector search, detects waste patterns (context replay, cache collapse, runaway
loops, cost-threshold breaches), and answers operator questions via RAG grounded in real trace
evidence.

### The corpus — measured, not estimated

| Source | Measured | Notes |
|---|---|---|
| Claude Code transcripts | 1,764 `.jsonl` files, **726 MB**, **1,330 distinct sessions** (measured 2026-08-01) | 115,340 assistant messages, **100%** carrying `usage` (nested under `message`; there is no flat `message.usage` key). Zero unparseable lines |
| Delegate ledger | **1,168 rows**, ~1,091 unique handles, **935 distinct sessions + 103 session-less rows** (measured 2026-08-01) | 7 real handles appear twice (status progression: `timeout`/`stopped`/`error` → `orphaned`); the `test` fixture handle appears 71× (all `ok`, zero cost) and is excluded at the adapter; naive sum over rows still inflates cost ≈4.3% |
| Delegate raw run logs | 104 `.jsonl` files, **267 MB**, plus ~1,230 per-run `.json` job/result pairs | A second, different format; no run timestamp (dated by file mtime) |
| Pricing registry | 5 model slugs, 4 services | Covers the delegate providers only |
| Time window | **2026-07-02 → 2026-08-01 (30 days)** | Not a year |

**933 of the 935 real ledger sessions also appear in the transcript corpus** (2 ledger-only
sessions; the 103 session-less rows match only by `external_id`/handle), because delegate children
*are* Claude Code sessions. Those overlapping sessions hold roughly half of all transcript tokens
(estimate — the exact sum is pinned with the Phase 2 fixture). Run identity is therefore keyed on
`session_id`, not on the source that reported it.

Model attribution over the 115,340 assistant records — counting rule: assistant records by
`message.model`, measured 2026-08-01: `deepseek-v4-pro` 66,698 · `deepseek-v4-flash` 14,213 ·
`claude-opus-4-8` 10,703 · `claude-opus-5` 8,616 · `claude-fable-5` 7,139 · `openai/gpt-5.6-luna`
6,984 · `claude-sonnet-5` 163 · `claude-haiku-4-5-20251001` 20. ~600 records carry no model, 160
carry `<synthetic>`, and ~46 carry truncated/corrupted strings (e.g. `claude-odeepseek-v4-pro`) —
the last group is exactly the quarantine population. **Only two of these resolve against the
current registry.** The dominant ledger slug is `deepseek-v4-pro[1m]` — a tier suffix that
matches nothing.

That gap is the product's first real finding: existing tooling
(`~/.claude/scripts/delegate-report.py`) silently prices every unmatched model at flash rates via
a default fallback, so **most spend is currently invisible and the visible part is understated.**
TokenBurn exists to make that measurable.

### Key Decisions

- **Canonical run identity is `(session_id, agent_id)`**, with `source` demoted to provenance.
  Anything else double-counts the 933 overlapping sessions.
- **Unknown model slug ⇒ quarantine, never a default price.** An alias table and tier-suffix
  resolution handle `[1m]`, `-pro`, `luna` and friends; anything unresolved is flagged and
  reported as pricing-coverage, not silently billed.
- **Real data, private.** The corpus holds private source, absolute paths, business notes and
  plausibly secrets. Nothing derived from it is published. SEO is demonstrated on a separate
  anonymous surface generated from the pricing registry.
- **Privacy applies to egress, not just publication.** RAG retrieval ships private text to
  DeepSeek. That path is opt-in, off by default, and covered by a test asserting on the outbound
  request body.
- **Differentiation:** cost attribution per task and waste detection. Attribution resolves to
  session granularity plus sidechain sub-runs — stated up front, not discovered in Phase 2.
- **DeepSeek has no embeddings endpoint.** A CPU `text-embeddings-inference` container
  (`bge-small-en-v1.5`) produces vectors; DeepSeek is the generation model only.
- **Offline-demoable.** Fakes for chat and embeddings; no API keys needed to run the system.

## 2. Tech Stack

| Layer | Technology | Rationale |
|---|---|---|
| Backend | .NET 10, ASP.NET Core Minimal API, Vertical Slice Architecture | Strongest existing skill; VSA proven in JjChat and enforced by convention tests |
| In-process dispatch | MediatR + pipeline behaviours | Async command/query dispatch from a library — the deliberate contrast with JjChat's hand-rolled mediator |
| Messaging | Apache Kafka (KRaft, single broker) + `Confluent.Kafka` | The requested queue system, and the "no finished event-driven system" portfolio gap. **Bought deliberately** — see §3 |
| Durable dispatch | Postgres transactional outbox, single-threaded keyed drain | Atomic persist+publish without distributed transactions |
| Database | PostgreSQL 16 + EF Core 10 (Npgsql) | Requested. One database, schema-per-service |
| Search | Elasticsearch 9, BM25 + `dense_vector`, RRF hybrid | Requested; the single biggest portfolio gap |
| Embeddings | HF `text-embeddings-inference`, `bge-small-en-v1.5`, CPU | DeepSeek has no embeddings API; free and offline |
| LLM / RAG | DeepSeek via `Microsoft.Extensions.AI`, Polly retry, fake by default | Cheap as requested; the abstraction makes the fake the default |
| Identity | `api.TokenBurn.Identity` — OpenIddict 7 | Self-hosted authority; other services obtain tokens via `client_credentials` |
| Frontend | React 19 + Next.js App Router (SSR/ISR) | SEO requires server rendering |
| Frontend data | TanStack Query + `openapi-fetch` generated from OpenAPI | No hand-rolled HTTP client; end-to-end type safety |
| Frontend auth | `react-oidc-context` (`AuthProvider` / `useAuth`) | A real auth library instead of a hand-written AuthContext |
| Telemetry format | OTLP, GenAI semantic conventions **+ documented custom attributes** | See the honesty note in §3 |
| Hosting | Docker Compose, nginx TLS gateway | Single published origin, security headers |
| Testing | xUnit, Moq, FluentAssertions, Testcontainers (Postgres, Kafka), shared ES container per assembly, template-DB clone fixture / Vitest, MSW, Playwright | **One reset strategy: template-DB clone. No Respawn** |
| CI | GitHub Actions | Backend + frontend jobs, architecture conventions gate |

## 3. Architecture Vision

**Vertical Slice Architecture with an event-driven processing spine.**

Each API is organised by use case (`Features/<Slice>/`) rather than by technical layer. Behind the
APIs, a Kafka topic chain does the heavy work asynchronously: ingestion is deliberately dumb and
durable, while normalization, pricing, indexing, detection and embedding are independent consumers
that can be replayed and scaled separately.

**Honest rationale for Kafka.** Measured arrival rate is ~0.04 msg/s. A single Postgres instance
with `tsvector`, `pgvector` and a job table would cover most of this functional surface. Kafka and
Elasticsearch are here because "no finished event-driven system" and "no search" are the portfolio
gaps this project exists to close, and because replayable stage isolation genuinely helps when
pricing or detection rules change over a ~1 GB backfill. That is a deliberate purchase, not a
throughput requirement. Any reviewer will run this calculation in thirty seconds; the README says
so first.

**Honest rationale for OTLP.** The GenAI semantic conventions carry no cost attribute and no
agent-loop or iteration attribute, and the cache-token attributes are mid-migration to the
`semantic-conventions-genai` repo. TokenBurn therefore emits documented custom attributes
alongside the standard ones. OTLP buys a standard envelope and a real ingest interface — it does
not remove bespoke-schema work, and the README does not claim it does. Today every producer is a
file adapter; the OTLP endpoint is the forward-looking interface.

```
telemetry.raw        → normalized envelope (one adapter per source format)
telemetry.normalized → priced against the registry; unresolved slugs quarantined
telemetry.priced     ├→ indexed → telemetry.indexed → embedded  (chained, not fanned out)
                     ├→ waste detection → waste.detected → alerts
                     └→ redaction/aggregation → metrics.aggregate (public-safe projection)
```

### Ordering and replay contract

- **Partition key on every topic in the chain: `session_id`.** One key across the whole chain, so
  per-run ordering holds end to end.
- **Consumer concurrency:** one instance per partition. The outbox drain is single-threaded per
  key, ordered by `(occurred_at, id)`.
- **Indexing and embedding are chained, not parallel.** Two consumers writing the same
  Elasticsearch document race and clobber each other; the embedder issues a partial `_update` with
  `doc_as_upsert` on the document the indexer created.
- **`ingest_envelopes` (Postgres) is the replay source of truth**, not Kafka. Kafka retention is
  explicitly bounded; a full replay re-publishes from the envelope table. This keeps the "replay is
  routine" guarantee from depending on a broker retention setting.

### Database topology

**One `tokenburn` database, one schema per service, explicit grants.** Postgres cannot query
across databases, so a database-per-service split (JjChat's approach) would leave
`api.TokenBurn.Insights` unable to read the tables it serves. Identity keeps its own database and
role — that split is correct and is lifted as-is.

| Schema | Owner (write) | Readers |
|---|---|---|
| `ingest` | `api.TokenBurn.Ingest` | processor |
| `telemetry` | `TokenBurn.Processor` | `insights_role` (SELECT) |
| `search` | `TokenBurn.Processor` | `insights_role` (SELECT) |
| *(separate DB)* | `api.TokenBurn.Identity` | — |

### Anti-corruption layer

`OtlpGenAiAdapter`, `ClaudeCodeTranscriptAdapter`, `DelegateLedgerAdapter`, `DelegateRunLogAdapter`
(the raw `logs/*.jsonl` are a second format and get their own adapter) and `JiCachingAdapter` all
emit the same normalized envelope. Each maps its own status vocabulary
(`ok`/`orphaned`/`error`/`needs_input`/`timeout`/`stopped`) onto the canonical enum. No source
format leaks past the adapter boundary.

> Diagram: Mermaid `flowchart LR` of the topic chain in `docs/architecture.md`; Excalidraw
> container diagram in the README. Both are Phase 0 deliverables — see PROCESS.md Phase 0.

## 4. Folder Structure

```
TokenBurn/
├── backend/
│   ├── TokenBurn.slnx
│   ├── Directory.Packages.props        # central package management
│   ├── Directory.Build.props
│   ├── src/
│   │   ├── TokenBurn.Common/           # Result<T>, BaseEntity, HTTP mapping
│   │   ├── TokenBurn.Contracts/        # Kafka message contracts
│   │   ├── api.TokenBurn.Identity/     # OpenIddict authority
│   │   ├── api.TokenBurn.Ingest/       # OTLP receiver, outbox writer
│   │   ├── api.TokenBurn.Insights/     # query, search, RAG
│   │   ├── TokenBurn.Processor/        # Kafka consumer chain
│   │   └── TokenBurn.Collector/        # backfill CLI
│   └── tests/
│       ├── TokenBurn.Testing.Common/   # fixtures, builders, doubles
│       ├── <per-project>.Tests/
│       ├── TokenBurn.EndToEnd.Tests/
│       └── TokenBurn.ArchitectureTests/
├── web/
│   ├── app/
│   │   ├── (public)/                   # models, guides — SSG/ISR, indexed
│   │   └── (app)/                      # dashboard, traces, search, ask — authed
│   ├── src/{api,features,ui}/          # generated client, feature hooks, primitives
│   └── e2e/                            # Playwright
├── docker/
│   ├── initdb/                         # schemas + roles + grants
│   ├── certs/                          # generate.sh (dev TLS)
│   └── nginx/
├── docs/
│   ├── architecture.md
│   └── agents/                         # project-layer standards
├── .github/workflows/
├── .env.example
├── docker-compose.yml
├── PLAN.md
└── PROCESS.md
```

## 5. Database Schema

All cost columns are `numeric(20,10)` — the ledger carries ten significant decimals and a
cache-read line prices at ~$2.8e-9 per token, so six decimals would make
`SUM(agent_messages.cost_usd) ≠ agent_runs.cost_usd` by construction. Rounding happens at the API
boundary, never in storage. **Every column participating in a UNIQUE constraint is `NOT NULL`** —
Postgres treats NULLs as distinct, so a nullable unique key constrains nothing.

| Table | Columns | Keys, constraints, indexes |
|---|---|---|
| `ingest.envelopes` | `id` uuid PK, `source` text NOT NULL, `payload` jsonb NOT NULL, `content_hash` text NOT NULL, `received_at` timestamptz NOT NULL, `status` text NOT NULL | `UNIQUE(content_hash)`; index `(status, received_at)`. **The replay source of truth** |
| `ingest.outbox_messages` | `id` uuid PK, `topic` text NOT NULL, `key` text NOT NULL, `payload` jsonb NOT NULL, `occurred_at` timestamptz NOT NULL, `published_at` timestamptz NULL, `attempts` int NOT NULL | Partial index `WHERE published_at IS NULL` ordered `(occurred_at, id)`; drained single-threaded per key |
| `telemetry.agent_runs` | `id` uuid PK, `session_id` text NOT NULL, `agent_id` text NOT NULL DEFAULT `''`, `source` text NOT NULL, `external_id` text NULL, `parent_run_id` uuid NULL, `workspace` text, `persona` text, `model_slug` text, `service` text, `status` text NOT NULL, `pricing_status` text NOT NULL, `started_at`/`ended_at` timestamptz, `input_tokens`/`cache_read_tokens`/`cache_write_tokens`/`output_tokens` bigint, `cost_usd`/`reported_cost_usd` numeric(20,10), `price_multiplier` numeric(6,3), `version` int | **`UNIQUE(session_id, agent_id)`** — canonical identity, upsert on later status. `CHECK (status IN …)`, `CHECK (pricing_status IN ('Priced','Quarantined','Unpriceable'))`, `CHECK (parent_run_id <> id)`. Self-FK `DEFERRABLE INITIALLY DEFERRED`. Indexes `(started_at DESC, id)`, `(model_slug, started_at)`, `(persona, started_at)` |
| `telemetry.agent_messages` | `id` uuid PK, `run_id` uuid NOT NULL, `sequence` int NOT NULL, `role` text NOT NULL, `content` text, `tool_name` text NULL, `model_slug` text, four token columns bigint, `cost_usd` numeric(20,10), `occurred_at` timestamptz NOT NULL | FK → `agent_runs`; `UNIQUE(run_id, sequence)`; index `(run_id, occurred_at)` |
| `telemetry.model_prices` | `slug` text NOT NULL, `service` text NOT NULL, `input_per_mtok`/`cache_read_per_mtok`/`cache_write_per_mtok`/`output_per_mtok` numeric(20,10), `context_window` int, `effective_from` timestamptz NOT NULL, `effective_to` timestamptz NULL | `PK(slug, service, effective_from)`; `EXCLUDE USING gist (slug WITH =, service WITH =, tstzrange(effective_from, effective_to) WITH &&)` so validity ranges cannot overlap; index `(slug, service, effective_from DESC)`. Backfill seeds `effective_from = '-infinity'` |
| `telemetry.model_aliases` | `alias` text PK, `service` text NOT NULL, `slug` text NOT NULL | Seeded from the registry's alias arrays; resolves `[1m]`, `-pro`, `luna`. Unresolved ⇒ `pricing_status = 'Quarantined'` |
| `telemetry.waste_findings` | `id` uuid PK, `run_id` uuid NOT NULL, `kind` text NOT NULL, `severity` text NOT NULL, `evidence` jsonb NOT NULL, `evidence_hash` text NOT NULL, `wasted_cost_usd` numeric(20,10), `detected_at`, `acknowledged_at` NULL | FK → `agent_runs`; **`UNIQUE(run_id, kind, evidence_hash)`** so a replay upserts instead of double-counting; index `(kind, severity, detected_at DESC)` |
| `ingest.import_commands` | `id` uuid PK, `type` text NOT NULL, `payload` jsonb, `status` text NOT NULL, `attempts` int, `handling_started_at` NULL, `cooldown_until` NULL, `last_error` NULL, `created_at`/`completed_at` | `CHECK` on status; index `(status, cooldown_until)` for the poller |
| `search.documents` | `id` uuid PK, `uri` text NOT NULL, `title` text, `source` text, `content_hash` text NOT NULL, `indexed_at` | `UNIQUE(content_hash)` |
| `search.document_chunks` | `id` uuid PK, `document_id` uuid NOT NULL, `ordinal` int NOT NULL, `text` text NOT NULL, `token_count` int | FK → `documents`; `UNIQUE(document_id, ordinal)` |

**Identity rule for session-less ledger rows.** `session_id` is NOT NULL, but the delegate ledger
carries 103 rows (8.8%) without one — 71 are the `test` fixture handle (dropped by
`DelegateLedgerAdapter`; the dedupe test asserts it never reappears), 32 are real runs that
predate stable session IDs. The adapter derives `session_id` from the run handle (`external_id`)
for those; a row whose derived identity still resolves to nothing is quarantined
(`pricing_status = 'Quarantined'`), never dropped.

`cache_hit_rate` is **not** stored — it is computed on read from the token columns, so a replay
cannot leave a derived column disagreeing with its inputs.

Example row — `telemetry.agent_runs`:

```
id                 : 0f2c9b6e-1f4a-4d5b-9a7e-2b0c9d8e7f61
session_id         : c3de2e5f-7351-423e-99f6-03fb47c56300
agent_id           : ''                       -- main thread; sidechains carry their agent id
source             : delegate-ledger          -- provenance only, not identity
external_id        : 20260801-201957-139297-84926b
parent_run_id      : null
workspace          : /home/jakub/JjChat
persona            : explore
model_slug         : deepseek-v4-flash
service            : deepseek
status             : Completed                -- mapped by the adapter from 'ok'
pricing_status     : Priced
started_at         : 2026-08-01T20:19:57Z
ended_at           : 2026-08-01T20:21:44Z
input_tokens       : 53695
cache_read_tokens  : 631936
cache_write_tokens : 0
output_tokens      : 10219
cost_usd           : 0.0121480408
reported_cost_usd  : 0.0121480408
price_multiplier   : 1.000                    -- 2.000 during Shanghai peak windows
version            : 1
```

## 6. API Contract

Auth: bearer JWT from `api.TokenBurn.Identity`. Scopes: `telemetry.write` (collector service
account), `insights.read`, `ask.invoke` (separate — it spends provider money), `admin`. Errors are
RFC 9457 Problem Details. **Every collection endpoint is cursor-paginated on `(started_at, id)`**;
`started_at` alone is not unique and a keyset on it skips rows at page boundaries.

| Method | Path | Auth | Request | Response | Errors |
|---|---|---|---|---|---|
| POST | `/v1/traces` | `telemetry.write` | OTLP/HTTP protobuf or JSON | **`200` + `ExportTraceServiceResponse`** (partial failure reported in-band via `partial_success`) | 400, 401, 413, 429 |
| POST | `/v1/logs` | `telemetry.write` | OTLP/HTTP | `200` + `ExportLogsServiceResponse` | 400, 401, 413, 429 |
| POST | `/api/imports` | `admin` | `{ source, path, since? }` | `202` + `{ commandId }` | 400, 401, 409, 429 |
| GET | `/api/imports/{id}` | `admin` | — | `{ id, status, progress, error? }` | 401, 404 |
| GET | `/api/runs` | `insights.read` | `from,to,model,persona,minCost,cursor,limit` | paged run summaries | 400, 401, 429 |
| GET | `/api/runs/{id}` | `insights.read` | — | run + messages + findings | 401, 404, 429 |
| GET | `/api/search` | `insights.read` | `q,mode=keyword\|hybrid,filters,cursor,limit` | hits + highlights | 400, 401, 429 |
| GET | `/api/costs/summary` | `insights.read` | `groupBy=day\|model\|persona,from,to` | series + `pricingCoverage` | 400, 401, 429 |
| GET | `/api/findings` | `insights.read` | `kind,severity,ack,cursor,limit` | paged findings | 400, 401, 429 |
| POST | `/api/ask` | `ask.invoke` | `{ question, filters? }` | answer + citations | 400, 401, 403, 429, 503 |
| GET | `/api/models` | anonymous | — | public model directory | 429 |

**`202` on OTLP would be a protocol breach** — OTLP/HTTP requires 200 with a response body, and
real exporters treat a bodyless non-200 as a failure and retry. Durable-inbox semantics live behind
the 200.

**`/api/models` is the only anonymous endpoint.** It projects an explicit allow-list —
`slug, provider, context_window, per-Mtok prices` — never `SELECT *`, because the registry also
holds `key_env` names, upstream hostnames and ports. It is rate-limited and `Cache-Control`-d; an
uncached anonymous DB-backed endpoint is a free DoS surface. It is the single commented entry in
the endpoint-authorization convention test's allow-list.

`/api/costs/summary` returns `pricingCoverage` alongside every series: the share of tokens that
resolved to a price. A dashboard that cannot say how much it *couldn't* price is lying.

Concrete example:

```http
POST /api/ask
Authorization: Bearer <jwt>
Content-Type: application/json

{ "question": "Which persona burned the most output tokens last week, and why?",
  "filters": { "from": "2026-07-25", "to": "2026-08-01" } }
```

```json
{
  "answer": "The `explore` persona accounted for 61% of output tokens. Two runs dominate: both
             re-sent the full working set after a cache miss, replaying ~630k input tokens each.",
  "citations": [
    { "type": "trace", "runId": "0f2c9b6e-…", "sessionId": "c3de2e5f-…" },
    { "type": "document", "uri": "docs/architecture.md", "chunk": 3 }
  ],
  "retrieval": { "mode": "hybrid", "traceHits": 12, "docHits": 4 },
  "pricingCoverage": 0.62
}
```

Responses are validated at the frontend boundary with Zod, on top of the generated OpenAPI types.

## 7. Phase Roadmap

Identity moved to Phase 1: Phases 1-3 all ship scoped endpoints, and the privacy doc's "scope
enforcement lives at the endpoint" rule needs an authority to exist before ~1 GB of private data
is indexed.

| Phase | Goal | Dependencies | Est. Effort* |
|---|---|---|---|
| 0 | Scaffold: solution, compose (10 containers), dev certs, CI, health endpoints, hello-world SSR, endpoint-authorization + dependency-direction convention tests, `.env.example` | — | 3–5 days |
| 1 | Identity (OpenIddict) + Ingest OTLP receiver + outbox → Kafka + Collector backfill of `ledger.jsonl` | 0 | 4–6 days |
| 2 | Normalization adapters, registry extension (aliases, tier suffixes, Anthropic prices), pricing engine with quarantine, durable import commands, transcript backfill, reconciliation harness | 1 | 6–9 days |
| 3 | Elasticsearch indexing + search API + public model directory (SSR/ISR/SEO) | 1, 2 | 4–6 days |
| 4 | `react-oidc-context` wiring + authed dashboard (TanStack Query, Suspense, `React.lazy` charts) | 1, 3 | 4–6 days |
| 5 | Waste detection: context-replay / cache-collapse, loop, cost-threshold | 1, 2 | 3–5 days |
| 6 | Document loader + embeddings + hybrid RRF + RAG with citations (fake chat client default) | 1, 3, 5 | 5–7 days |
| 7 | Redaction/aggregation boundary, egress controls, self-instrumentation | 1, 6 | 3–4 days |

\* Full-time senior-engineer days. At part-time pace this is a **3–6 month** project. The control
case is snipapp, which stalled at phase 1 of 10. Phases 0–5 constitute a complete, demonstrable
system; 6 and 7 are additive. If the pace slips, cut from the end, not the middle.

### Acceptance criteria

**Reconciliation (Phase 2)** — the Revision-1 criterion ("totals match `delegate-report.py`") was
unachievable: the prototype applies the 2× peak multiplier at computation time rather than run
time, 402 of 1,168 ledger rows predate the `peak` flag, and 86% of rows carry an unresolvable
slug. Replaced with:

1. Ingest the ledger's own cost into `reported_cost_usd`; recompute into `cost_usd`.
2. Compare **per run**, restricted to runs where the slug resolves **and** `peak` is present and
   false. Tolerance: **|Δ| ≤ $0.000001 per run**, zero rows outside it.
3. Pin the comparison set as a committed fixture at Phase 2 start — a file, not a live command.
4. Report `pricingCoverage` for everything outside that set. Coverage is a number to improve, not
   a failure.

**Dedupe invariant (every phase from 1 on)** — running any backfill twice leaves
`COUNT(*) FROM telemetry.agent_runs` unchanged. Asserted in CI against a fixture corpus.

**Waste detection (Phase 5)** — ground truth is cache collapse and context replay measured
directly from `cache_read_input_tokens` / `cache_creation_input_tokens`, present on 100% of the
115,340 transcript messages. Validated against manually confirmed runs plus synthetic injected
cases for edge behaviour. (The JiCaching keepalive incident is *not* usable: it lives in
`~/.jicaching/state.json` as lifetime accumulators with no epoch and a 500-entry ring buffer.)

## 8. Open Questions

1. **Transcript storage depth** — full message text (private, enables the search story) or
   metadata only? Plan assumes full text, private, behind auth.
2. **Alert destination for `waste.detected`** (Phase 5) — in-app only, or push to email/Slack?
3. **Retention** — the corpus grows continuously. Time-based ES index rollover, or keep everything?
4. **Anthropic price history** — prices can be added going forward, but historical rates for the
   30-day backfill must be entered by hand or seeded at `-infinity`. Which?
5. **Recruiter-facing artifact** — nothing here is publicly demoable, so a narrated walkthrough
   recording is the only channel to the audience. Which phase produces it? (Recommendation: start
   at Phase 3, refresh each phase.)
