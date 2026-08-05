namespace TokenBurn.Contracts;

/// <summary>
///     Acknowledgment published to <c>telemetry.indexed</c> once a
///     <see cref="PricedRun" /> has been indexed into Elasticsearch. Carries
///     only the identifiers the Phase 5 embedder needs as its
///     <c>doc_as_upsert</c> handle.
/// </summary>
public sealed record IndexedRun
{
    public required Guid RunId { get; init; }
    public required string SessionId { get; init; }
}
