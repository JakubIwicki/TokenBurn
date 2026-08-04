using TokenBurn.Common.Primitives;

namespace Api.TokenBurn.Ingest.Domain;

public enum EnvelopeStatus
{
    Received = 0
}

public sealed class Envelope : BaseEntity<Guid>
{
    private Envelope() { }

    public string Source { get; private init; } = string.Empty;
    public string Payload { get; private init; } = string.Empty;
    public string ContentHash { get; private init; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private init; }
    public EnvelopeStatus Status { get; private init; }

    public static Envelope Create(string source, string payload, string contentHash, DateTimeOffset receivedAt)
        => new()
        {
            Id = Guid.NewGuid(), Source = source, Payload = payload, ContentHash = contentHash,
            ReceivedAt = receivedAt, Status = EnvelopeStatus.Received
        };
}
