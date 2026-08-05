using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Adapters;

public sealed class JiCachingAdapterTests
{
    private static readonly string JiCachingPath = Path.Combine(AppContext.BaseDirectory, "fixtures/jicaching-sample.jsonl");

    [Fact]
    public void MapsTwoEntries_ToUnknownEnvelopes()
    {
        IReadOnlyList<NormalizedRun> envelopes = CreateSut().Map(File.ReadAllText(JiCachingPath));

        envelopes.Should().HaveCount(2);
        envelopes.Should().OnlyContain(e => e.AgentId == "");
        envelopes.Should().OnlyContain(e => e.Source == "jicaching");
        envelopes.Should().OnlyContain(e => e.Status == RunStatus.Unknown);

        NormalizedRun first = envelopes.Single(e => e.SessionId == "00000000-0000-4000-8000-000000000001");
        first.ExternalId.Should().Be("20260804-080000-000001-deadbeef");
        first.ModelSlug.Should().Be("deepseek-v4-flash");
        first.InputTokens.Should().Be(1_250);
        first.CacheReadTokens.Should().Be(51_200);
        first.CacheWriteTokens.Should().Be(0);
        first.OutputTokens.Should().Be(240);
        first.ReportedCostUsd.Should().Be(0.000851m);

        NormalizedRun second = envelopes.Single(e => e.SessionId == "00000000-0000-4000-8000-000000000002");
        second.ExternalId.Should().Be("20260804-080210-000002-beef0001");
        second.ModelSlug.Should().Be("openai/gpt-5.6-luna");
        second.InputTokens.Should().Be(890);
        second.CacheReadTokens.Should().Be(204_800);
        second.CacheWriteTokens.Should().Be(512);
        second.OutputTokens.Should().Be(96);
        second.ReportedCostUsd.Should().Be(0.002437m);
    }

    [Fact]
    public void MergesSameSessionEntries_IntoOneEnvelope()
    {
        string payload = """
            {"record_type": "state", "capacity": 32}
            {"record_type": "entry", "ts": "2026-08-04T08:00:00Z", "session_id": "sess-jic", "handle": "h1", "model": "deepseek-v4-flash", "usage": {"input_tokens": 10, "cache_read_input_tokens": 20, "cache_creation_input_tokens": 30, "output_tokens": 40}, "cost_usd": 0.001}
            {"record_type": "entry", "ts": "2026-08-04T08:05:00Z", "session_id": "sess-jic", "handle": "h2", "model": "openai/gpt-5.6-luna", "usage": {"input_tokens": 100, "cache_read_input_tokens": 200, "cache_creation_input_tokens": 300, "output_tokens": 400}, "cost_usd": 0.002}
            """;

        NormalizedRun run = CreateSut().Map(payload).Should().ContainSingle().Which;

        run.InputTokens.Should().Be(110);
        run.CacheReadTokens.Should().Be(220);
        run.CacheWriteTokens.Should().Be(330);
        run.OutputTokens.Should().Be(440);
        run.ReportedCostUsd.Should().Be(0.003m);
        run.ExternalId.Should().Be("h2");
        run.ModelSlug.Should().Be("openai/gpt-5.6-luna");
    }

    [Fact]
    public void SkipsEntry_WithNoSessionIdAndNoHandle()
    {
        string payload = """
            {"record_type": "entry", "ts": "2026-08-04T08:00:00Z", "session_id": "", "handle": "", "model": "m", "usage": {"input_tokens": 1}, "cost_usd": 0.001}
            """;

        CreateSut().Map(payload).Should().BeEmpty();
    }

    private static JiCachingAdapter CreateSut() => new(MockLogger<JiCachingAdapter>.GetSuccessful().Object);
}
