using Api.TokenBurn.Ingest.Features.Logs;
using Api.TokenBurn.Ingest.Infrastructure;
using Google.Protobuf;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using TokenBurn.Common.Primitives;
using TokenBurn.Testing.Common.Assertions;
using TokenBurn.Testing.Common.Mocking;

namespace Api.TokenBurn.Ingest.Tests.Features.Logs;

public sealed class ExportLogsHandlerTests
{
    private const string SessionId = "session-1";
    private const string Source = "otlp-logs";

    [Fact]
    public async Task WritesEnvelope_WhenValidProtobuf()
    {
        Fixture fixture = Fixture.Create();
        byte[] body = BuildLogsRequest(SessionId).ToByteArray();

        Result result = await Act(fixture, new ExportLogsCommand(body, "application/x-protobuf"));

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

        Result result = await Act(fixture, new ExportLogsCommand(body, "application/x-protobuf"));

        result.AssertInvalid();
        fixture.Inbox.Mock.Verify(
            x => x.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WritesEnvelope_WhenValidJson()
    {
        Fixture fixture = Fixture.Create();
        byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(BuildLogsRequest(SessionId)));

        Result result = await Act(fixture, new ExportLogsCommand(body, "application/json"));

        result.AssertSuccess();
        fixture.Inbox.Mock.Verify(
            x => x.WriteAsync(Source, It.IsAny<string>(), It.IsAny<string>(), SessionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Task<Result> Act(Fixture fixture, ExportLogsCommand command)
        => fixture.CreateSut().HandleAsync(command, CancellationToken.None);

    private static ExportLogsServiceRequest BuildLogsRequest(string sessionId) => new()
    {
        ResourceLogs =
        {
            new ResourceLogs
            {
                Resource = BuildResource(sessionId),
                ScopeLogs = { new ScopeLogs { LogRecords = { new LogRecord { Body = new AnyValue { StringValue = "test log" } } } } }
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

        public ExportLogsHandler CreateSut() => new(Inbox.Object);
    }

    private sealed class MockEnvelopeInbox : MockObject<IEnvelopeInbox>
    {
    }
}
