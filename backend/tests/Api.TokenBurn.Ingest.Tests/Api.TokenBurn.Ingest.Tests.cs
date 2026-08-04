using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Api.TokenBurn.Ingest;
using Google.Protobuf;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using Testcontainers.PostgreSql;

namespace Api.TokenBurn.Ingest.Tests;

public sealed class ApiTokenBurnIngestTests : IAsyncLifetime
{
    private const string NoAuthHeader = "X-Test-No-Auth";
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder().Build();
    private WebApplicationFactory<IngestDbContext> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await _database.StartAsync(timeout.Token);

        _factory = new WebApplicationFactory<IngestDbContext>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Ingest", _database.GetConnectionString());
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            // The ingest host's OutboxPublisher fails fast at startup without this key; the tests
            // assert envelope/outbox persistence, not publishing, so a placeholder broker suffices.
            builder.UseSetting("Kafka:BootstrapServers", "localhost:9092");
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddSingleton<IConfigureOptions<AuthenticationOptions>>(
                    new ConfigureNamedOptions<AuthenticationOptions>(Options.DefaultName, options =>
                    {
                        options.DefaultScheme = "Test";
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultForbidScheme = "Test";
                    }));
            });
        });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Post_LogsJson_Returns200_AndPersistsEnvelopeAndOutbox()
    {
        ExportLogsServiceRequest request = BuildLogsRequest("s-json");
        string body = JsonFormatter.Default.Format(request);
        using HttpResponseMessage response = await PostAsync("/v1/logs", body, "application/json");

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ExportLogsServiceResponse parsedResponse = JsonParser.Default.Parse<ExportLogsServiceResponse>(await response.Content.ReadAsStringAsync());
        Assert.NotNull(parsedResponse);
        await AssertPersistedAsync("otlp-logs", Encoding.UTF8.GetBytes(body), "s-json");
    }

    [Fact]
    public async Task Post_LogsJson_Twice_DeduplicatesToOneEnvelopeAndOneOutbox()
    {
        ExportLogsServiceRequest request = BuildLogsRequest("s-dedupe");
        string body = JsonFormatter.Default.Format(request);
        using HttpResponseMessage first = await PostAsync("/v1/logs", body, "application/json");
        using HttpResponseMessage second = await PostAsync("/v1/logs", body, "application/json");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await AssertPersistedAsync("otlp-logs", Encoding.UTF8.GetBytes(body), "s-dedupe");
    }

    [Fact]
    public async Task Post_LogsProtobuf_Returns200_AndPersists()
    {
        ExportLogsServiceRequest request = BuildLogsRequest("s-protobuf");
        byte[] body = request.ToByteArray();
        using HttpResponseMessage response = await PostBytesAsync("/v1/logs", body, "application/x-protobuf");

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ExportLogsServiceResponse parsedResponse = ExportLogsServiceResponse.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());
        Assert.NotNull(parsedResponse);
        await AssertPersistedAsync("otlp-logs", body, "s-protobuf");
    }

    [Fact]
    public async Task Post_Traces_Returns200_AndPersists()
    {
        ExportTraceServiceRequest request = BuildTracesRequest("s-traces");
        byte[] body = request.ToByteArray();
        using HttpResponseMessage response = await PostBytesAsync("/v1/traces", body, "application/x-protobuf");

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ExportTraceServiceResponse parsedResponse = ExportTraceServiceResponse.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());
        Assert.NotNull(parsedResponse);
        await AssertPersistedAsync("otlp-traces", body, "s-traces");
    }

    [Fact]
    public async Task Post_Logs_NoAuth_Returns401()
    {
        ExportLogsServiceRequest request = BuildLogsRequest("s-no-auth");
        using HttpRequestMessage message = new(HttpMethod.Post, "/v1/logs")
        {
            Content = new ByteArrayContent(request.ToByteArray())
        };
        message.Content.Headers.ContentType = new("application/x-protobuf");
        message.Headers.Add(NoAuthHeader, "true");

        using HttpResponseMessage response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNotPersistedAsync("otlp-logs", request.ToByteArray(), "s-no-auth");
    }

    [Fact]
    public async Task Post_Logs_MalformedBody_Returns400()
    {
        byte[] body = [1, 2, 3, 4];
        using HttpResponseMessage response = await PostBytesAsync("/v1/logs", body, "application/x-protobuf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNotPersistedAsync("otlp-logs", body, string.Empty);
    }

    [Fact]
    public async Task Post_Logs_OversizeBody_Returns413()
    {
        byte[] body = new byte[10 * 1024 * 1024 + 1];
        using HttpResponseMessage response = await PostBytesAsync("/v1/logs", body, "application/x-protobuf");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await AssertNotPersistedAsync("otlp-logs", body, string.Empty);
    }

    private Task<HttpResponseMessage> PostAsync(string path, string body, string contentType)
        => PostBytesAsync(path, Encoding.UTF8.GetBytes(body), contentType);

    private async Task<HttpResponseMessage> PostBytesAsync(string path, byte[] body, string contentType)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(body)
        };
        message.Content.Headers.ContentType = new(contentType);
        return await _client.SendAsync(message);
    }

    private async Task AssertPersistedAsync(string source, byte[] body, string sessionId)
    {
        string contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant();
        using IServiceScope scope = _factory.Services.CreateScope();
        IngestDbContext db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        int envelopeCount = await db.Envelopes.CountAsync(x => x.ContentHash == contentHash && x.Source == source);
        int outboxCount = await db.OutboxMessages.CountAsync(x => x.Key == sessionId && x.Topic == "telemetry.raw" && x.PublishedAt == null);
        Assert.Equal(1, envelopeCount);
        Assert.Equal(1, outboxCount);
    }

    private async Task AssertNotPersistedAsync(string source, byte[] body, string sessionId)
    {
        string contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant();
        using IServiceScope scope = _factory.Services.CreateScope();
        IngestDbContext db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        int envelopeCount = await db.Envelopes.CountAsync(x => x.ContentHash == contentHash && x.Source == source);
        int outboxCount = await db.OutboxMessages.CountAsync(x => x.Key == sessionId && x.Topic == "telemetry.raw");
        Assert.Equal(0, envelopeCount);
        Assert.Equal(0, outboxCount);
    }

    private static ExportLogsServiceRequest BuildLogsRequest(string sessionId)
        => new()
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

    private static ExportTraceServiceRequest BuildTracesRequest(string sessionId)
        => new()
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

    private static Resource BuildResource(string sessionId)
        => new()
        {
            Attributes =
            {
                new KeyValue { Key = "session.id", Value = new AnyValue { StringValue = sessionId } }
            }
        };

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.ContainsKey(NoAuthHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            ClaimsIdentity identity = new(
                [new Claim("scope", "telemetry.write"), new Claim("sub", "test-client")],
                Scheme.Name);
            ClaimsPrincipal principal = new(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
