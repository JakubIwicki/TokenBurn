# Standards proposals — human-gated queue

Appended by `smart-auditor`. Process with `/doc-lesson`. Never applied autonomously.

**All six applied 2026-08-01** — the four project-layer items into `docs/agents/privacy-boundary.md`
and `docs/agents/telemetry-pipeline.md`; the two global items into
`~/.claude/skills/cross-service-data-flow/SKILL.md` (§2 → "Ordering in an event chain") and
`~/.claude/skills/integration-testing/SKILL.md` ("Containerise the store; fake the model").

- [x] **LLM egress is a privacy boundary, not just the public projection** (layer: project, target: `docs/agents/privacy-boundary.md`)

      Add as rule 7:

      7. **Egress to a third-party model is a boundary too.** The rules above govern what
         reaches a *public reader*; they also govern what reaches an *external API*. Any text
         placed into a prompt sent off-host (RAG retrieval context, few-shot examples, error
         payloads) is subject to the same default-deny allow-list as the public projection.
         A RAG answer endpoint may not ship raw prompt/response text, file paths or workspace
         names to a provider. The offline fake is the default in every environment that has
         not explicitly opted in. The mandatory leak test asserts on the **outbound request
         body**, not only on the response returned to the caller.

- [x] **Reconcile against a pinned artifact with a stated tolerance, never a live prototype** (layer: project, target: `docs/agents/telemetry-pipeline.md`)

      Add as rule 7:

      7. **A reconciliation target is a pinned file with a tolerance, not a command.** "Totals
         match `<script>`" is not an acceptance criterion: the script re-prices with current
         rates, applies approximations we do not reproduce, and its output moves. Pin a dated
         artifact (per-run `(external_id, reported_cost_usd)`), state the comparison set
         (which rows are excluded and why), and state an absolute and relative tolerance.
         Ingest the source's own reported cost as `reported_cost_usd` alongside the
         recomputed `cost_usd`; the reconciliation test asserts on the delta between the two
         columns, per run.

- [x] **Unknown model / alias resolution is an explicit failure, never a default price** (layer: project, target: `docs/agents/telemetry-pipeline.md`)

      Extend rule 4:

      4. …It reads the versioned registry and nothing else. **A model slug with no registry
         row is a categorised failure (`Result` → quarantine the run), never a fallback to a
         default tier.** Alias and tier-suffix resolution happens in one place, against a
         stored alias table, before the price lookup — the registry's canonical slugs and the
         slugs that appear in real telemetry are different vocabularies.

- [x] **Idempotency keys are database constraints, not conventions** (layer: project, target: `docs/agents/telemetry-pipeline.md`)

      Extend rule 2:

      2. …A consumer without a stated key is incomplete. **The key must be enforced by a
         UNIQUE constraint on the target table with every participating column `NOT NULL`
         (Postgres treats NULLs as distinct, so a nullable column silently disables the
         constraint), and the write must be an upsert on that constraint.** A key that exists
         only in the handler's `if` is not an idempotency key.

- [x] **Ordering and partition keys are part of a topic-chain contract** (layer: global, target: `~/.claude/skills/cross-service-data-flow/SKILL.md`)

      Add to §2 (style selection) as a new subsection:

      ### Ordering in an event chain

      A chain of topics has an ordering contract or it has a race. State, per topic: the
      **partition key**, the **ordering guarantee it buys**, and the **consumer concurrency
      that preserves it**. Ordering holds only within a partition, so entities that must be
      processed in sequence share one key across *every* topic in the chain — a key change
      mid-chain silently drops the guarantee. A polled outbox dispatcher is part of this
      contract: drain ordered by `(occurred_at, id)` and single-threaded per key, or the
      publish order does not match the commit order.

      **Two consumers must not write the same downstream document.** When a fan-out has two
      branches that both land in one search/read model, the second writer clobbers the first
      unless it uses a partial update. Prefer chaining (`priced → indexed → embedded`) over
      fan-out onto a shared document.

      Retention is part of the replay guarantee. "Replay from the beginning" is false under
      default broker retention. Either name the durable store (not the topic) as the replay
      source and specify the re-publish path, or set unbounded retention explicitly and size
      the disk for it.

- [x] **Which infrastructure gets a container and which gets a fake** (layer: global, target: `~/.claude/skills/integration-testing/SKILL.md`)

      Add as a new section:

      ## Containerise the store; fake the model

      A real ephemeral container is mandatory for anything whose *semantics* the test depends
      on — the relational store, the broker, the search engine. It is the wrong tool for a
      dependency whose only contribution is an expensive deterministic transform: an
      embeddings server, an OCR service, a local model runner. Those get a deterministic fake
      producing stable vectors/outputs, so a test asserts on retrieval logic rather than on
      model weights.

      Scope containers by cost: per-test for a cheap store (template-DB clone), **per-assembly
      shared with per-test namespace isolation** (index-per-test, topic-per-test) for a heavy
      one. Record the host preconditions a container needs but cannot set for itself
      (kernel sysctls, memory floors) in the test project's README — a suite that only runs
      on a machine someone tuned by hand is not a gate.

      Pick **one** database-reset strategy per solution and say which. Template-clone and
      truncate-based reset are both fine; two of them in one repo means half the suite is
      isolated the other way. Neither resets a broker or a search index — those need their own
      per-test namespace.

- [ ] **privacy-boundary.md governs globs must cover the endpoint-authorization convention test** (layer: project, target: docs/agents/privacy-boundary.md)
      Rule 8's enforcement point is "the endpoint-authorization convention test's allow-list", but the doc's
      `governs:` list (["**/Redaction/**", "web/app/(public)/**", "**/*PublicProjection*", "**/metrics.aggregate*",
      "**/ModelDirectory/**", "**/Features/Ask/**", "**/Llm/**", "**/Embeddings/**"]) does not include the test path,
      so `match-agents-docs.py` reports no governing doc for `backend/tests/TokenBurn.ArchitectureTests/**` and future
      auditors are not routed to the rule the allow-list enforces. Add
      `"backend/tests/TokenBurn.ArchitectureTests/**"` to the `governs:` list.
