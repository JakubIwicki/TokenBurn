using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.SelfTelemetry;
using TokenBurn.Processor.Tests.Bases;
using RunStatus = TokenBurn.Processor.Domain.RunStatus;

namespace TokenBurn.Processor.Tests.SelfTelemetry;

public sealed class SelfTelemetryEmitterTests : TelemetryHandlerTestBase
{
    private const string SelfSource = "tokenburn-self";
    private const string TestModel = "deepseek-v4-flash";
    private const string TestClientId = "tokenburn-self-test";
    private static readonly DateTimeOffset Start = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EmitsSelfSourceSpan_WithExpectedIdentityAttributes()
    {
        Fixture fx = Fixture.Init(Start);
        SelfTelemetryEmitter sut = fx.CreateSut(Context);

        await sut.EmitOnceAsync(CancellationToken.None);

        string body = fx.Requests.Single(r => r.Path == "/v1/traces").Body;
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement resource = json.RootElement.GetProperty("resourceSpans")[0].GetProperty("resource");
        ReadString(resource, "tokenburn.source").Should().Be(SelfSource);
        ReadString(resource, "session.id").Should().NotBeNullOrWhiteSpace();

        JsonElement span = json.RootElement.GetProperty("resourceSpans")[0]
            .GetProperty("scopeSpans")[0].GetProperty("spans")[0];
        span.GetProperty("name").GetString().Should().Be(SelfSource);
        ReadString(span, "tokenburn.handle").Should().Be(SelfSource);
        ReadString(span, "tokenburn.persona").Should().Be("processor");
        ReadString(span, "tokenburn.status").Should().Be("ok");
        ReadString(span, "gen_ai.request.model").Should().Be(TestModel);
    }

    [Fact]
    public async Task ProducesDistinctSessionIds_ForConsecutiveTicks()
    {
        Fixture fx = Fixture.Init(Start);
        SelfTelemetryEmitter sut = fx.CreateSut(Context);

        await sut.EmitOnceAsync(CancellationToken.None);
        await sut.EmitOnceAsync(CancellationToken.None);

        var traceBodies = fx.Requests.Where(r => r.Path == "/v1/traces").Select(r => r.Body).ToList();
        traceBodies.Should().HaveCount(2);
        SessionId(traceBodies[0]).Should().NotBe(SessionId(traceBodies[1]));
    }

    [Fact]
    public async Task ReportsWindowActivity_AsSyntheticUsageCounters()
    {
        Fixture fx = Fixture.Init(Start);
        SelfTelemetryEmitter sut = fx.CreateSut(Context);
        DateTimeOffset window = Start.AddMinutes(5);
        AgentRun runA = SeedRun("s1", window);
        AgentRun runB = SeedRun("s2", window);
        SeedRun("s3", window);
        SeedMessages(runA, 2, window);
        SeedFinding(runA, window);
        fx.AdvanceClock(TimeSpan.FromHours(1));

        await sut.EmitOnceAsync(CancellationToken.None);

        string body = fx.Requests.Single(r => r.Path == "/v1/traces").Body;
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement span = json.RootElement.GetProperty("resourceSpans")[0]
            .GetProperty("scopeSpans")[0].GetProperty("spans")[0];
        ReadLong(span, "gen_ai.usage.input_tokens").Should().Be(3);
        ReadLong(span, "gen_ai.usage.cache_read_tokens").Should().Be(2);
        ReadLong(span, "gen_ai.usage.cache_write_tokens").Should().Be(1);
        ReadLong(span, "gen_ai.usage.output_tokens").Should().Be(0);
    }

    [Fact]
    public async Task EmittedTrace_RoundTripsThroughAdapter_AsSelfCompletedRun()
    {
        Fixture fx = Fixture.Init(Start);
        SelfTelemetryEmitter sut = fx.CreateSut(Context);
        DateTimeOffset window = Start.AddMinutes(5);
        AgentRun runA = SeedRun("s1", window);
        SeedRun("s2", window);
        SeedRun("s3", window);
        SeedMessages(runA, 2, window);
        SeedFinding(runA, window);
        fx.AdvanceClock(TimeSpan.FromHours(1));

        await sut.EmitOnceAsync(CancellationToken.None);

        string body = fx.Requests.Single(r => r.Path == "/v1/traces").Body;
        OtlpGenAiAdapter adapter = new(NullLogger<OtlpGenAiAdapter>.Instance);
        NormalizedRun run = adapter.Map(body).Should().ContainSingle().Which;

        run.Source.Should().Be(SelfSource);
        run.ModelSlug.Should().Be(TestModel);
        run.Status.Should().Be(TokenBurn.Contracts.RunStatus.Completed);
        run.SessionId.Should().NotBeNullOrWhiteSpace();
        run.InputTokens.Should().Be(3);
        run.CacheReadTokens.Should().Be(2);
        run.CacheWriteTokens.Should().Be(1);
        run.OutputTokens.Should().Be(0);
    }

    [Fact]
    public async Task RequestsClientCredentialsToken_WithConfiguredClientId()
    {
        Fixture fx = Fixture.Init(Start);
        SelfTelemetryEmitter sut = fx.CreateSut(Context);

        await sut.EmitOnceAsync(CancellationToken.None);

        CapturedRequest token = fx.Requests.Single(r => r.Path == "/connect/token");
        token.Body.Should().Contain("grant_type=client_credentials");
        token.Body.Should().Contain("scope=telemetry.write");
        token.Body.Should().Contain($"client_id={TestClientId}");

        CapturedRequest traces = fx.Requests.Single(r => r.Path == "/v1/traces");
        traces.Authorization.Should().Be("Bearer test-token");
    }

    [Fact]
    public void Throws_WhenEnabledWithBlankClientSecret()
    {
        IConfiguration configuration = ConfigurationFor(enabled: "true", clientSecret: "");

        Action act = () => SelfTelemetryOptions.FromConfiguration(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SelfTelemetry:ClientSecret*");
    }

    [Fact]
    public void DoesNotThrow_WhenDisabledWithBlankClientSecret()
    {
        IConfiguration configuration = ConfigurationFor(enabled: "false", clientSecret: "");

        SelfTelemetryOptions options = SelfTelemetryOptions.FromConfiguration(configuration);

        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Throws_WhenEnabledWithIntervalMinutesZero()
    {
        IConfiguration configuration = ConfigurationFor(enabled: "true", clientSecret: "secret", intervalMinutes: "0");

        Action act = () => SelfTelemetryOptions.FromConfiguration(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SelfTelemetry:IntervalMinutes*");
    }

    [Fact]
    public void DoesNotThrow_WhenDisabledWithIntervalMinutesZero()
    {
        IConfiguration configuration = ConfigurationFor(enabled: "false", clientSecret: "", intervalMinutes: "0");

        SelfTelemetryOptions options = SelfTelemetryOptions.FromConfiguration(configuration);

        options.Enabled.Should().BeFalse();
    }

    private AgentRun SeedRun(string sessionId, DateTimeOffset startedAt)
    {
        AgentRun run = AgentRun.Create(sessionId, "", "delegate-ledger", null, null,
            TestModel, RunStatus.Completed, startedAt, startedAt.AddMinutes(1),
            inputTokens: 1000, cacheReadTokens: 100, cacheWriteTokens: 200, outputTokens: 300,
            reportedCostUsd: null, service: "delegate-ledger");
        Db.Store(run);
        return run;
    }

    private void SeedMessages(AgentRun run, int count, DateTimeOffset occurredAt)
    {
        for (int sequence = 0; sequence < count; sequence++)
            Db.Store(AgentMessage.Create(run.Id, sequence, "user", null, null, run.ModelSlug,
                inputTokens: 10, cacheReadTokens: 0, cacheWriteTokens: 0, outputTokens: 5, occurredAt: occurredAt));
    }

    private void SeedFinding(AgentRun run, DateTimeOffset detectedAt)
    {
        WasteFinding finding = WasteFinding.Create(run.Id, WasteFindingKind.Loop, WasteFindingSeverity.Major,
            new { description = "self-telemetry window test" }, wastedCostUsd: 0.01m, detectedAt: detectedAt);
        Db.Store(finding);
    }

    private static IConfiguration ConfigurationFor(string enabled, string clientSecret, string intervalMinutes = "60")
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SelfTelemetry:Enabled"] = enabled,
                ["SelfTelemetry:IntervalMinutes"] = intervalMinutes,
                ["SelfTelemetry:ClientSecret"] = clientSecret
            })
            .Build();

    private static string SessionId(string otlpJson)
    {
        using JsonDocument body = JsonDocument.Parse(otlpJson);
        return ReadString(body.RootElement.GetProperty("resourceSpans")[0].GetProperty("resource"), "session.id");
    }

    private static string ReadString(JsonElement owner, string key)
        => owner.GetProperty("attributes").EnumerateArray()
            .Single(attribute => attribute.GetProperty("key").GetString() == key)
            .GetProperty("value").GetProperty("stringValue").GetString()!;

    private static long ReadLong(JsonElement owner, string key)
    {
        string value = owner.GetProperty("attributes").EnumerateArray()
            .Single(attribute => attribute.GetProperty("key").GetString() == key)
            .GetProperty("value").GetProperty("intValue").GetString()!;
        return long.Parse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class Fixture
    {
        private readonly RoutingHandler _handler;
        private readonly FakeTimeProvider _clock;

        private Fixture(RoutingHandler handler, FakeTimeProvider clock)
        {
            _handler = handler;
            _clock = clock;
        }

        public static Fixture Init(DateTimeOffset start) => new(new RoutingHandler(), new FakeTimeProvider(start));

        public Fixture AdvanceClock(TimeSpan by)
        {
            _clock.Advance(by);
            return this;
        }

        public SelfTelemetryOptions Options { get; } = new(
            Enabled: true,
            IntervalMinutes: 60,
            IdentityUrl: "http://identity.test",
            IngestUrl: "http://ingest.test",
            ClientId: TestClientId,
            ClientSecret: "test-secret");

        public IReadOnlyList<CapturedRequest> Requests => _handler.Requests;

        public SelfTelemetryEmitter CreateSut(TelemetryDbContext db)
        {
            IServiceScopeFactory scopeFactory = new ServiceCollection().AddSingleton(db)
                .BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            SelfTelemetryTokenClient tokenClient = new(
                new HttpClient(_handler) { BaseAddress = new Uri("http://identity.test") }, Options);
            return new SelfTelemetryEmitter(
                scopeFactory, Options, tokenClient, new StubHttpClientFactory(_handler), _clock,
                NullLogger<SelfTelemetryEmitter>.Instance);
        }
    }

    private sealed record CapturedRequest(string Method, string Path, string Body, string? Authorization);

    private sealed class RoutingHandler : DelegatingHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method.ToString(),
                request.RequestUri!.AbsolutePath,
                body,
                request.Headers.Authorization?.ToString()));
            return request.RequestUri!.AbsolutePath switch
            {
                "/connect/token" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"test-token"}""", Encoding.UTF8, "application/json")
                },
                "/v1/traces" => new HttpResponseMessage(HttpStatusCode.OK),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }
    }

    private sealed class StubHttpClientFactory(RoutingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("http://self-telemetry.test") };
    }
}
