using Api.TokenBurn.Ingest.Domain;
using Api.TokenBurn.Ingest.Tests.Bases;
using TokenBurn.Testing.Common.Builders;

namespace Api.TokenBurn.Ingest.Tests.Domain;

public sealed class EnvelopeTests : IngestHandlerTestBase
{
    [Fact]
    public void CreatesEnvelope_WithReceivedStatus()
    {
        Db.Query<Envelope>().Should().BeEmpty();

        var envelope = TestEnvelopeBuilder.Init(Db).Build();

        var persisted = Db.FindFresh<Envelope>(envelope.Id);

        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(EnvelopeStatus.Received);
        persisted.Source.Should().Be("otlp-traces");
    }

    [Fact]
    public void CreatesEnvelope_WithContentHash()
    {
        Db.Query<Envelope>().Should().BeEmpty();

        var envelope = TestEnvelopeBuilder.Init(Db).WithContentHash("sha256-abc").Build();

        var persisted = Db.FindFresh<Envelope>(envelope.Id);

        persisted.Should().NotBeNull();
        persisted!.ContentHash.Should().Be("sha256-abc");
    }
}
