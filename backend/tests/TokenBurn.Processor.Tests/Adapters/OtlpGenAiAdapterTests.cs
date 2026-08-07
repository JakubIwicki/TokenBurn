using System.Text.Json.Nodes;
using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Adapters;

public sealed class OtlpGenAiAdapterTests
{
    private static readonly string OtlpFixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures/delegate-ledger.otlp.json");
    private static readonly string SharedSessionOtlpPath = Path.Combine(AppContext.BaseDirectory, "fixtures/delegate-ledger-shared-session.otlp.json");

    [Theory]
    [InlineData("ok", RunStatus.Completed)]
    [InlineData("error", RunStatus.Failed)]
    [InlineData("timeout", RunStatus.Failed)]
    [InlineData("stopped", RunStatus.Cancelled)]
    [InlineData("orphaned", RunStatus.Unknown)]
    [InlineData("needs_input", RunStatus.Unknown)]
    public void MapsLedgerStatusVocabulary(string status, RunStatus expected)
    {
        string payload = BuildOtlp(ResourceSpan("sess-status", Span(handle: "h1", status: status)));

        var envelopes = CreateSut().Map(payload);

        envelopes.Should().ContainSingle().Which.Status.Should().Be(expected);
    }

    [Fact]
    public void ReturnsUnknown_WhenStatusIsAbsent()
    {
        string payload = BuildOtlp(ResourceSpan("sess-no-status", Span(handle: "h1")));

        var envelopes = CreateSut().Map(payload);

        envelopes.Should().ContainSingle().Which.Status.Should().Be(RunStatus.Unknown);
    }

    [Fact]
    public void MapsTokenCounters_FromSpanUsage()
    {
        string payload = BuildOtlp(ResourceSpan("sess-tokens",
            Span(handle: "h1", input: 4200, cacheRead: 1800, cacheWrite: 900, output: 640, cost: 0.25m)));

        NormalizedRun run = CreateSut().Map(payload).Single();

        run.InputTokens.Should().Be(4200);
        run.CacheReadTokens.Should().Be(1800);
        run.CacheWriteTokens.Should().Be(900);
        run.OutputTokens.Should().Be(640);
        run.ReportedCostUsd.Should().Be(0.25m);
        run.ExternalId.Should().Be("h1");
        run.AgentId.Should().Be("");
        run.Source.Should().Be("delegate-ledger");
    }

    [Fact]
    public void ReturnsZeroTokens_WhenSpanHasNoUsage()
    {
        string payload = BuildOtlp(ResourceSpan("sess-no-usage", Span(handle: "h1")));

        NormalizedRun run = CreateSut().Map(payload).Single();

        run.InputTokens.Should().Be(0);
        run.CacheReadTokens.Should().Be(0);
        run.CacheWriteTokens.Should().Be(0);
        run.OutputTokens.Should().Be(0);
    }

    [Fact]
    public void DropsTestHandles()
    {
        string payload = BuildOtlp(ResourceSpan("sess-test", Span(handle: "test-123", status: "ok")));

        var envelopes = CreateSut().Map(payload);

        envelopes.Should().BeEmpty();
    }

    [Fact]
    public void DropsOnlyTestSpan_WhenSessionHasBothHandles()
    {
        string payload = BuildOtlp(ResourceSpan("sess-mixed",
            Span(handle: "test-123", input: 9999),
            Span(handle: "real-456", input: 100, status: "ok")));

        NormalizedRun run = CreateSut().Map(payload).Should().ContainSingle().Which;

        run.InputTokens.Should().Be(100);
        run.ExternalId.Should().Be("real-456");
    }

    [Fact]
    public void SkipsSpan_WithNoSessionIdAndNoHandle()
    {
        string payload = BuildOtlp(ResourceSpan(null, Span(status: "ok")));

        var envelopes = CreateSut().Map(payload);

        envelopes.Should().BeEmpty();
    }

    [Fact]
    public void MapsRealOtlpFixture_ToThreeEnvelopes()
    {
        IReadOnlyList<NormalizedRun> envelopes = CreateSut().Map(File.ReadAllText(OtlpFixturePath));

        envelopes.Should().HaveCount(3);
        envelopes.Select(e => e.SessionId).Should().BeEquivalentTo(
            ["20260802-delegate-0001", "20260802-delegate-0002", "20260802-delegate-0003"]);
        envelopes.Should().OnlyContain(e => e.AgentId == "");

        NormalizedRun first = envelopes.Single(e => e.SessionId == "20260802-delegate-0001");
        first.ExternalId.Should().Be("20260802-delegate-abc123");
        first.InputTokens.Should().Be(4200);
        first.ReportedCostUsd.Should().Be(0.25m);
    }

    [Fact]
    public void MergesTwoSpansInOneSession_IntoOneEnvelope()
    {
        IReadOnlyList<NormalizedRun> envelopes = CreateSut().Map(File.ReadAllText(SharedSessionOtlpPath));

        NormalizedRun run = envelopes.Should().ContainSingle().Which;
        run.SessionId.Should().Be("shared-sess-1");
        run.AgentId.Should().Be("");
        run.ExternalId.Should().Be("20260802-delegate-shared-h2");
        run.Persona.Should().Be("research");
        run.ModelSlug.Should().Be("deepseek-v4-flash");
        run.Status.Should().Be(RunStatus.Completed);
        run.InputTokens.Should().Be(5700);
        run.CacheReadTokens.Should().Be(2700);
        run.CacheWriteTokens.Should().Be(900);
        run.OutputTokens.Should().Be(850);
        run.ReportedCostUsd.Should().Be(0.75m);
        run.StartedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1_785_628_800_000));
        run.EndedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1_785_629_100_000));
    }

    [Fact]
    public void Maps_SourceFromResource_WhenTokenburnSourceSet()
    {
        string payload = BuildOtlp(ResourceSpanWithSource("sess-self", "tokenburn-self", Span(handle: "h1")));

        NormalizedRun run = CreateSut().Map(payload).Single();

        run.Source.Should().Be("tokenburn-self");
    }

    [Fact]
    public void Defaults_SourceToDelegateLedger_WhenTokenburnSourceAbsent()
    {
        string payload = BuildOtlp(ResourceSpanWithSource("sess-default", null, Span(handle: "h1")));

        NormalizedRun run = CreateSut().Map(payload).Single();

        run.Source.Should().Be("delegate-ledger");
    }

    [Fact]
    public void Merge_UsesMaxCostSpanSource()
    {
        string payload = BuildOtlp(
            ResourceSpanWithSource("shared-sess", "delegate-ledger", Span(handle: "h1", input: 100, cost: 0.25m, status: "ok")),
            ResourceSpanWithSource("shared-sess", "tokenburn-self", Span(handle: "h2", input: 4200, cost: 0.5m, status: "ok")));

        NormalizedRun run = CreateSut().Map(payload).Single();

        run.Source.Should().Be("tokenburn-self");
    }

    [Fact]
    public void ReturnsEmpty_WhenPayloadHasNoResourceSpans()
    {
        var envelopes = CreateSut().Map("{}");

        envelopes.Should().BeEmpty();
    }

    private static OtlpGenAiAdapter CreateSut() => new(MockLogger<OtlpGenAiAdapter>.GetSuccessful().Object);

    private static string BuildOtlp(params JsonObject[] resourceSpans)
        => new JsonObject { ["resourceSpans"] = new JsonArray(resourceSpans) }.ToJsonString();

    private static JsonObject ResourceSpan(string? sessionId, params JsonObject[] spans)
    {
        JsonArray attributes = sessionId is null
            ? new JsonArray(StringAttr("tokenburn.source", "delegate-ledger"))
            : new JsonArray(StringAttr("session.id", sessionId), StringAttr("tokenburn.source", "delegate-ledger"));
        return new JsonObject
        {
            ["resource"] = new JsonObject { ["attributes"] = attributes },
            ["scopeSpans"] = new JsonArray(
                new JsonObject
                {
                    ["scope"] = new JsonObject { ["name"] = "delegate-ledger" },
                    ["spans"] = new JsonArray(spans)
                })
        };
    }

    private static JsonObject ResourceSpanWithSource(string? sessionId, string? source, params JsonObject[] spans)
    {
        JsonArray attributes = new();
        if (sessionId is not null) attributes.Add(StringAttr("session.id", sessionId));
        if (source is not null) attributes.Add(StringAttr("tokenburn.source", source));
        return new JsonObject
        {
            ["resource"] = new JsonObject { ["attributes"] = attributes },
            ["scopeSpans"] = new JsonArray(
                new JsonObject
                {
                    ["scope"] = new JsonObject { ["name"] = "delegate-ledger" },
                    ["spans"] = new JsonArray(spans)
                })
        };
    }

    private static JsonObject Span(
        string? handle = null, string? status = null, string? model = "deepseek-v4-flash",
        string? persona = "engineering", long? input = null, long? cacheRead = null,
        long? cacheWrite = null, long? output = null, decimal? cost = null)
    {
        var attributes = new JsonArray();
        if (handle is not null) attributes.Add(StringAttr("tokenburn.handle", handle));
        if (persona is not null) attributes.Add(StringAttr("tokenburn.persona", persona));
        if (model is not null) attributes.Add(StringAttr("gen_ai.request.model", model));
        if (status is not null) attributes.Add(StringAttr("tokenburn.status", status));
        if (input is not null) attributes.Add(IntAttr("gen_ai.usage.input_tokens", input.Value));
        if (cacheRead is not null) attributes.Add(IntAttr("gen_ai.usage.cache_read_tokens", cacheRead.Value));
        if (cacheWrite is not null) attributes.Add(IntAttr("gen_ai.usage.cache_write_tokens", cacheWrite.Value));
        if (output is not null) attributes.Add(IntAttr("gen_ai.usage.output_tokens", output.Value));
        if (cost is not null) attributes.Add(DoubleAttr("tokenburn.cost_usd", (double)cost.Value));
        return new JsonObject
        {
            ["traceId"] = "AAAAAAAAAAAAAAAAAAAAAA==",
            ["spanId"] = "AAAAAAAAAAA=",
            ["name"] = "delegate child",
            ["startTimeUnixNano"] = "1785628800000000000",
            ["endTimeUnixNano"] = "1785629100000000000",
            ["attributes"] = attributes
        };
    }

    private static JsonObject StringAttr(string key, string value)
        => new() { ["key"] = key, ["value"] = new JsonObject { ["stringValue"] = value } };

    private static JsonObject IntAttr(string key, long value)
        => new() { ["key"] = key, ["value"] = new JsonObject { ["intValue"] = value } };

    private static JsonObject DoubleAttr(string key, double value)
        => new() { ["key"] = key, ["value"] = new JsonObject { ["doubleValue"] = value } };
}
