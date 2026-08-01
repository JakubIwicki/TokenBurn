# TokenBurn — Execution Log

## 1. Current Status

| Field | Value |
|---|---|
| Active phase | **Phase 0 — Project scaffold** |
| Last updated | 2026-08-02 |
| Status | 🟢 **Phase 0 complete** (2026-08-02) — buildable skeleton + full acceptance: all 10 compose containers healthy, `dotnet test` green (6/6 convention tests), hello-world SSR page served over the TLS gateway |

Next action: Phase 1 — Identity on OpenIddict 7 (password + refresh grants, `client_credentials` for the collector, scopes, split-horizon issuer).

## 2. Implementation Checklist

### Phase 0 — Scaffold + CI + hello-world

- [x] `git init`, `.gitignore` (.NET + Node), `.editorconfig`, licence — 2026-08-02
- [x] `backend/TokenBurn.slnx`, `Directory.Packages.props` (central package management), `Directory.Build.props` (lock files, analyzers, `ArtifactsPath`) — 2026-08-02
- [x] src projects: `TokenBurn.Common`, `TokenBurn.Contracts`, `api.TokenBurn.Identity`, `api.TokenBurn.Ingest`, `api.TokenBurn.Insights`, `TokenBurn.Processor`, `TokenBurn.Collector` — 2026-08-02
- [x] test projects: `TokenBurn.Testing.Common`, one `.Tests` per src project, `TokenBurn.EndToEnd.Tests`, `TokenBurn.ArchitectureTests` — 2026-08-02
- [x] `TokenBurn.Common`: `Result<T>` + `ResultCategories` + `Result`→HTTP mapping, `BaseEntity<TKey>` — 2026-08-02
- [x] Thin `Program.cs` + endpoint registration as extension methods per service (delivered as `Extensions/ServiceHostExtensions.cs` rather than a `Startup` class) — 2026-08-02
- [x] MediatR with validation / logging / timing pipeline behaviours — 2026-08-02
- [x] `docker/certs/generate.sh` — dev TLS cert (JjChat has the reference script); mounted by the gateway and trusted by the Collector — 2026-08-02
- [x] `docker-compose.yml`: postgres, kafka (KRaft), elasticsearch, embeddings, identity, ingest, insights, processor, web, nginx — healthchecks, `${VAR:?}` fail-fast env vars, only the gateway publishing ports — 2026-08-02
- [x] `.env.example` committed; real `.env` gitignored — 2026-08-02
- [x] `docker/initdb/01-create-schemas.sh` — one `tokenburn` DB, schema + role per service, explicit `GRANT USAGE/SELECT` to `insights_role`; Identity gets its own database — 2026-08-02
- [x] Elasticsearch security configured (ES 9 ships it enabled) — credentials via env, TLS or documented dev-only exception — 2026-08-02
- [x] Kafka listener configuration stated; no anonymous external listener — 2026-08-02
- [x] `docker/nginx/nginx.conf` — TLS gateway, security headers, unknown-Host rejection — 2026-08-02
- [x] `/health` + `/health/ready` on every service — 2026-08-02
- [x] Next.js App Router app in `web/` with `(public)` / `(app)` route groups; hello-world SSR page — 2026-08-02
- [x] `.github/workflows/ci.yml`: backend build+test, frontend lint+test — 2026-08-02
- [x] Convention tests: **slice anatomy, endpoint authorization (allow-list: `/api/models` only), dependency direction** — 6/6 green (4 endpoint-auth × 4 hosts, slice anatomy, dependency direction), discovery-driven vacuity guards; `smart-auditor` passed — 2026-08-02
- [x] Record the host precondition `vm.max_map_count=262144` in the test project README — Testcontainers cannot set a host sysctl — 2026-08-02
- [x] `docs/architecture.md` (Mermaid topic-chain diagram, PLAN §3) and README (Excalidraw container diagram) — 2026-08-02
- [x] `docs/agents/` scaffolded — **done**
- [x] Verify: `docker compose up -d` → all healthy; `dotnet test` green; gateway serves the SSR page over TLS — **full acceptance passed 2026-08-02**: all 10 containers healthy (postgres, kafka, elasticsearch, embeddings, identity, ingest, insights, processor, web, nginx); `dotnet test` green (6/6 convention tests, 0 skips); `curl -k https://localhost/` → 200 with the hello-world SSR page. Notes: healthchecks need curl in the runtime images (added to all 5 Dockerfiles); root page `web/app/(public)/page.tsx` added during acceptance — the skeleton had only the `/models` placeholder

### Phase 1 — Identity + ingest + outbox + first backfill

- [ ] `api.TokenBurn.Identity` on OpenIddict 7: password + refresh grants, `client_credentials` for the collector, scopes `telemetry.write` / `insights.read` / `ask.invoke` / `admin`, split-horizon issuer
- [ ] Resource APIs validate JWTs; **fail startup if no JWT authority is configured**
- [ ] OTLP/HTTP receiver `/v1/traces` + `/v1/logs`, protobuf + JSON, returning `200` + `Export*ServiceResponse`
- [ ] Rate-limit policies on `/v1/*` with the sustained-rate arithmetic recorded in the PR
- [ ] `ingest.envelopes` durable inbox, `UNIQUE(content_hash)`
- [ ] Transactional outbox + single-threaded keyed drain publishing to Kafka via `Confluent.Kafka`
- [ ] Topic creation with partition key `session_id`; retention explicitly configured
- [ ] `TokenBurn.Collector` CLI: `backfill --source delegate-ledger`, authenticating via `client_credentials`
- [ ] Verify: **1,168 ledger rows → ~1,091 runs** in `telemetry.agent_runs` (7 real handles appear twice and must upsert, not duplicate; the `test` fixture handle — 71 rows — is dropped at the adapter; 103 session-less rows get `session_id` derived from `external_id`); re-running the backfill leaves the row count unchanged

### Phase 2 — Normalization + pricing + durable commands

- [ ] Normalized envelope contract in `TokenBurn.Contracts`, including the canonical status enum
- [ ] Adapters: `DelegateLedgerAdapter`, `DelegateRunLogAdapter`, `ClaudeCodeTranscriptAdapter`, `JiCachingAdapter`, `OtlpGenAiAdapter` — one per format, each mapping its own status vocabulary
- [ ] Canonical identity `(session_id, agent_id)` with upsert-on-later-status; **cross-source dedupe test using the 933 overlapping sessions**
- [ ] `telemetry.model_prices` + `telemetry.model_aliases`; registry extended with Anthropic models, tier suffixes (`[1m]`), and service dimension
- [ ] Pricing engine: pure `(usage, model, service, timestamp)`; unresolved slug → `Quarantined`, never a default price
- [ ] `price_multiplier` recorded per run; peak windows applied at run time, not computation time
- [ ] `import_commands` lifecycle: status enum, handling lock, idempotent reprocessing, cooldown retry
- [ ] **Pin the reconciliation fixture** (committed file, not a live command, including the attribution counting rule: assistant records by `message.model`) before any pricing work
- [ ] **Profile container memory** before the transcript backfill
- [ ] Transcript backfill (726 MB, 115,340 messages)
- [ ] Verify: per-run |Δ| ≤ $0.000001 on the resolvable subset, zero rows outside; `pricingCoverage` reported; double-backfill leaves counts unchanged

### Phase 3 — Search + public SEO surface

- [ ] ES index templates: `traces`, `messages`
- [ ] Indexing consumer on `telemetry.priced`, emitting `telemetry.indexed`; replay re-published from `ingest.envelopes`
- [ ] `/api/search` keyword mode + filters + cursor pagination
- [ ] Public `(public)` routes: `/models`, `/models/[slug]`, `/guides/[slug]` — SSG/ISR
- [ ] `/api/models` explicit allow-list projection, rate limit, `Cache-Control`
- [ ] `generateMetadata`, JSON-LD, `sitemap.ts`, `robots.ts`
- [ ] Start the narrated walkthrough recording; refresh it each phase
- [ ] Verify: Lighthouse SEO ≥ 95; sitemap valid; search returns hits with highlights; unauthenticated access to `/api/runs` returns 401

### Phase 4 — Authed dashboard

- [ ] `openapi-typescript` + `openapi-fetch` client generated from the OpenAPI document
- [ ] `react-oidc-context` `AuthProvider` / `useAuth`; token injected via client middleware
- [ ] Dashboard: TanStack Query, `Suspense` streaming, `React.lazy` chart bundles
- [ ] Pricing-coverage indicator on every cost chart
- [ ] Verify: login works; dashboard shows real fleet data; Playwright smoke passes

### Phase 5 — Waste detection

- [ ] Context-replay / cache-collapse detector over `cache_read` vs `cache_write` per message
- [ ] Loop detector (repeated near-identical requests within a run)
- [ ] Cost-threshold breach → `waste.detected`
- [ ] `waste_findings` upsert on `(run_id, kind, evidence_hash)`; `/api/findings`; dashboard surface
- [ ] Verify: detector flags manually confirmed replay runs in the real corpus; synthetic injected cases pass; a full replay does not double-count findings

### Phase 6 — Documents + embeddings + hybrid search + RAG

- [ ] Document loader populating `search.documents` / `document_chunks`
- [ ] Embedding consumer chained after indexing, partial `_update` with `doc_as_upsert`
- [ ] RRF hybrid retrieval over traces + documents
- [ ] `/api/ask` on `ask.invoke` scope with a per-principal budget; `Microsoft.Extensions.AI` + Polly
- [ ] **`FakeChatClient` is the default**; the real provider is opt-in per environment
- [ ] Egress leak test asserting on the outbound request body
- [ ] Verify: answers cite real session IDs and document chunks; full offline run with fakes

### Phase 7 — Redaction boundary + self-instrumentation

- [ ] Redaction/aggregation stage → `metrics.aggregate`
- [ ] Leak tests: seed a secret-shaped string into the private corpus, assert absence from the public projection, public routes, and outbound LLM requests
- [ ] TokenBurn emits its own OTLP telemetry into itself
- [ ] Verify: leak tests pass; TokenBurn appears in its own dashboard

## 3. Decision Log

| Date | Decision | Rationale | Alternatives Considered |
|---|---|---|---|
| 2026-08-01 | Build TokenBurn (agent cost/waste radar) | Fills the three portfolio gaps — search, finished event-driven system, SSR/SEO — on a real corpus that already exists | GhostJob Radar (job-board ToS + defamation exposure), PostMortem AI (better enterprise signal, no personal data), GitDrama PR Roast (defamation risk) |
| 2026-08-01 | New repo rather than extending JjChat | Clean domain; JjChat stays a finished artifact | Extending JjChat closes the same gaps for ~40% of the work with a public demo already live and a stronger "one system evolved" narrative. Raised by review, considered, **rejected deliberately** |
| 2026-08-01 | Next.js App Router | SEO requires SSR; metadata API, sitemap, ISR, streaming; widest recognition | React Router v7, TanStack Start, Vite SPA + prerender |
| 2026-08-01 | Self-hosted OpenIddict Identity service | The platform owns its token authority; other services obtain tokens via `client_credentials`; JjChat's setup is proven | Keycloak (zero code, external authority — review recommended it as a time saver), Auth.js, Duende BFF |
| 2026-08-01 | `react-oidc-context` for frontend auth | A library supplying `AuthProvider` + `useAuth` with PKCE and silent renew | Hand-rolled context (the thing being avoided), NextAuth v5 |
| 2026-08-01 | Accept a client-rendered authed island | Token lives in the browser; the public SEO surface is anonymous and the dashboard must never be indexed | BFF token-exchange proxy for full SSR |
| 2026-08-01 | MediatR instead of a hand-rolled mediator | Requested contrast; removes a kernel to maintain | Hand-rolled (JjChat), Wolverine |
| 2026-08-01 | Polling outbox, not Debezium CDC | Same guarantee, one fewer container | Debezium (deferred), MassTransit Kafka Rider |
| 2026-08-01 | Separate embeddings container | DeepSeek has no embeddings endpoint | ES ELSER (licence tier), OpenRouter embeddings |
| 2026-08-01 | Real data private; SEO on an anonymous surface | The corpus holds private source, paths and plausibly secrets | Publishing scrubbed aggregates (needs the redaction stage first) |
| 2026-08-01 | Ingest via OTLP + documented custom attributes | OTLP buys a standard envelope and a real interface. It carries **no** cost or agent-loop attribute, so custom attributes are unavoidable — the plan no longer claims otherwise | Bespoke schema, file-watching only |
| **2026-08-01** | **Canonical run identity `(session_id, agent_id)`** | All 928 ledger sessions also exist in the transcript corpus, so a `(source, external_id)` key would double-count ~50% of tokens | `(source, external_id)` (Revision 1 — actively guaranteed the duplication), content hash (differs per source) |
| **2026-08-01** | **Unknown model slug quarantines the run** | 86% of ledger rows carry `deepseek-v4-pro[1m]`; the registry has no Anthropic models. The prototype silently defaults to flash rates, mispricing Opus | Default price (silent mispricing), reject the row (loses token data) |
| **2026-08-01** | **Reconciliation redefined as a pinned fixture with a stated tolerance** | The prototype applies the peak multiplier at computation time and 35% of rows predate the `peak` flag; matching it exactly would certify against a known-wrong oracle | Match totals exactly (unachievable), skip reconciliation (loses the gate) |
| **2026-08-01** | **Identity moved to Phase 4 → Phase 1** | Phases 1–3 ship scoped endpoints and index ~1 GB of private data; scope enforcement needs an authority to exist | Keep at Phase 4 with endpoints unenforced |
| **2026-08-01** | **One database, schema per service** | Postgres cannot query across databases; a DB-per-service split leaves Insights unable to read the tables it serves | DB per service (JjChat's split — correct there, wrong here); shared schema, no isolation |
| **2026-08-01** | **Waste ground truth = cache collapse in transcripts** | 100% of transcript messages carry `cache_read` / `cache_creation` tokens. The keepalive incident lives in a fourth system with lifetime counters and no event log | Ingest the JiCaching ring buffer (N=500, no history), synthetic fixtures only (never confirmed against real waste) |
| **2026-08-01** | **Kafka and ES stated as deliberate purchases** | Measured arrival is ~0.04 msg/s; a single Postgres would cover most of it. Reviewers will run that calculation — better to state the tradeoff than rationalise a throughput requirement | Manufacture a scale narrative (Revision 1 did this and contradicted itself one paragraph later) |

## 4. Change History

| Date | What Changed | Impact | Author |
|---|---|---|---|
| 2026-08-01 | **Revision 2** — plan rewritten after adversarial review (critic + smart-auditor) against the real corpus | Corpus figures corrected (1.1 GB → 758 MB of JSONL; "160 runs" → 1,159 ledger rows; "a year" → 30 days); run identity re-keyed; pricing gains aliases, services and quarantine; costs widened to `numeric(20,10)`; `cache_write_tokens` added; all UNIQUE participants `NOT NULL`; indexes specified; OTLP fixed to 200; Identity moved to Phase 1; DB topology changed to schema-per-service; partition keys, retention and the indexer→embedder chain specified; egress privacy rule added; effort estimates raised to 32–48 days | Claude |
| 2026-08-01 | **Revision 2.1** — every corpus figure re-measured against the live sources after a line-by-line review | Attribution re-pinned with its counting rule (66,698/14,213/10,703/8,616/7,139/6,984/163/20); size figures corrected (758 → 726 MB; raw logs ~160 files/~340 MB → 104 files/267 MB; "1.4 GB" → ~1 GB in PLAN §3/§6 and privacy-boundary.md); null-`session_id` identity rule added (103 rows, 71 of them the `test` fixture handle — dropped at the adapter); `docs/architecture.md` + README added as Phase 0 deliverables | Claude |

## 5. Blockers & Risks

| Item | Type | Owner | Resolution plan |
|---|---|---|---|
| Pricing coverage — 88% of corpus tokens unpriceable today | Risk | Jakub | Phase 2 registry extension + alias table; coverage reported on every cost API so the gap is visible rather than hidden |
| Cross-source double counting | Risk (mitigated) | Jakub | Canonical `(session_id, agent_id)` key + a dedupe invariant asserted in CI from Phase 1 |
| Corpus contains secrets and private code | Risk | Jakub | Auth-gated from Phase 1; egress off by default in Phase 6; redaction boundary with leak tests in Phase 7 |
| Ten containers on one WSL2 box | Risk | Jakub | Memory-capped containers; **profile before the Phase 2 backfill (now a checklist item)**; shared ES container per test assembly, embeddings always faked in tests |
| Schedule — 32–48 full-time-equivalent days at part-time pace | Risk | Jakub | Phases 0–5 are a complete system; 6–7 are additive. Cut from the end. Control case: snipapp stalled at phase 1 of 10 |
| Delegation reliability | Risk | Jakub | 69 of 1,168 historical delegate runs (5.9%) did not complete cleanly. Review every diff; keep tasks small enough to re-run |
| Nothing is publicly demoable | Risk | Jakub | A narrated walkthrough recording starting at Phase 3 is the only channel to a recruiter — treated as a deliverable, not a nice-to-have |

No active blockers.
