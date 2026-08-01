# Architecture

The telemetry pipeline uses `session_id` as the partition key on every topic.

```mermaid
flowchart LR
    raw[telemetry.raw] --> normalize[Normalized envelope<br/>one adapter per source format]
    normalize --> normalized[telemetry.normalized]
    normalized --> price[Priced against versioned registry<br/>unresolved slugs quarantined]
    price --> priced[telemetry.priced]
    priced --> index[indexed]
    index --> indexed[telemetry.indexed]
    indexed --> embed[embedded]
    priced --> detect[waste detection]
    detect --> detected[waste.detected]
    detected --> alerts[alerts]
    priced --> redact[redaction / aggregation]
    redact --> aggregate[metrics.aggregate<br/>public-safe projection]
```

Indexing and embedding are CHAINED (not fanned out) because two consumers writing the same Elasticsearch document would race.
