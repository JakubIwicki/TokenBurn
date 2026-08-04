using Api.TokenBurn.Ingest.Features.Traces;
using Api.TokenBurn.Ingest.Infrastructure;
using Google.Protobuf;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using TokenBurn.Common.Primitives;
using TokenBurn.Testing.Common.Assertions;
using TokenBurn.Testing.Common.Mocking;

namespace Api.TokenBurn.Ingest.Tests.Features.Traces;

public sealed class ExportTracesHandlerTests
{
    private const string SessionId = "session-1";
    private const string Source = "otlp-traces";

    [Fact]
    public async Task WritesEnvelope_WhenValidProtobuf()
    {
        Fixture fixture = Fixture.Create();
        byte[] body = BuildTracesRequest(SessionId).ToByteArray();

        Result result = await Act(fixture, new ExportTracesCommand(body, "application/x-protobuf"));

        result.AssertSuccess();
        fixture.Inbox.Mock.Verify(
            x => x.WriteAsync(Source, It.IsAny<string>(), It.IsAny<string>(), SessionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReturnsInvalid_WhenPayloadMalformed()
    {
        Fixture fixture = Fixture.Create();
        byte[] body = [1, 2, 3, 4];

        Result result = await Act(fixture, new ExportTracesCommand(body, "application/x-protobuf"));

        result.AssertInvalid();
        fixture.Inbox.Mock.Verify(
            x => x.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WritesEnvelope_WhenValidJson()
    {
        Fixture fixture = Fixture.Create();
        byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(BuildTracesRequest(SessionId)));

        Result result = await Act(fixture, new ExportTracesCommand(body, "application/json"));

        result.AssertSuccess();
        fixture.Inbox.Mock.Verify(
            x => x.WriteAsync(Source, It.IsAny<string>(), It.IsAny<string>(), SessionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Task<Result> Act(Fixture fixture, ExportTracesCommand command)
        => fixture.CreateSut().HandleAsync(command, CancellationToken.None);

    private static ExportTraceServiceRequest BuildTracesRequest(string sessionId) => new()
    {
        ResourceSpans =
        {
            new ResourceSpans
            {
                Resource = BuildResource(sessionId),
                ScopeSpans = { new ScopeSpans { Spans = { new Span { Name = "test span" } } } }
            }
        }
    };

    private static Resource BuildResource(string sessionId) => new()
    {
        Attributes =
        {
            new KeyValue { Key = "session.id", Value = new AnyValue { StringValue = sessionId } }
        }
    };

    private sealed class Fixture
    {
        public MockEnvelopeInbox Inbox { get; } = new();
        public FakeTimeProvider TimeProvider { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        private Fixture() { }

        public static Fixture Create() => new();

        public ExportTracesHandler CreateSut() => new(Inbox.Object);
    }

    private sealed class MockEnvelopeInbox : MockObject<IEnvelopeInbox>
    {
    }
}
