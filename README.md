# TokenBurn

Agent cost & waste radar. Ingests LLM/agent telemetry over OTLP, normalizes heterogeneous log formats through adapters, prices every message against a versioned registry, indexes it in Elasticsearch for hybrid keyword+vector search, detects waste patterns, and answers grounded operator questions via RAG with citations.

## Why Kafka and Elasticsearch

Measured arrival rate is ~0.04 msg/s; a single Postgres with `tsvector` + `pgvector` + a job table would cover most of this functional surface. Kafka and Elasticsearch are deliberate choices to close portfolio gaps ("no finished event-driven system", "no search"), and because replayable stage isolation genuinely helps when pricing or detection rules change over a ~1 GB backfill. This is a deliberate purchase, not a throughput requirement.

## Architecture

<!-- TODO: Excalidraw container diagram -->

See [the architecture overview](docs/architecture.md).

## Quick start

These steps land in a later pass:

1. Copy `.env.example` to `.env`.
2. Run `docker/certs/generate.sh`.
3. Start the services with `docker compose up -d`.
