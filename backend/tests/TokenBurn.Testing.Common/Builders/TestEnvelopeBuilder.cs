using Api.TokenBurn.Ingest.Domain;
using TokenBurn.Testing.Common.Data;

namespace TokenBurn.Testing.Common.Builders;

public sealed class TestEnvelopeBuilder
{
    private readonly TestDb _db;
    private string _source = "otlp-traces";
    private string _payload = "{}";
    private string _contentHash = "hash";
    private DateTimeOffset _receivedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private TestEnvelopeBuilder(TestDb db) { _db = db; }

    public static TestEnvelopeBuilder Init(TestDb db) => new(db);

    public TestEnvelopeBuilder WithSource(string source) { _source = source; return this; }
    public TestEnvelopeBuilder WithPayload(string payload) { _payload = payload; return this; }
    public TestEnvelopeBuilder WithContentHash(string contentHash) { _contentHash = contentHash; return this; }

    public Envelope Build()
    {
        var envelope = Envelope.Create(_source, _payload, _contentHash, _receivedAt);
        _db.Store(envelope);
        return envelope;
    }
}
