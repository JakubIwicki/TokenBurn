using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Adapters;

public sealed class DelegateLedgerAdapterTests
{
    private const string Session084aea47 = "084aea47-5ae6-48dd-a5ca-c3d8c626c46a";
    private static readonly string LedgerPath = Path.Combine(AppContext.BaseDirectory, "fixtures/reconciliation-ledger.jsonl");

    [Fact]
    public void MapsReconciliationCorpus_To282Envelopes()
    {
        IReadOnlyList<NormalizedRun> envelopes = CreateSut().Map(File.ReadAllText(LedgerPath));

        envelopes.Should().HaveCount(282);
        envelopes.Should().OnlyContain(e => e.AgentId == "");
        envelopes.Should().OnlyContain(e => e.Source == "delegate-ledger");
        envelopes.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.SessionId));
        envelopes.Select(e => e.SessionId).Distinct().Should().HaveCount(282);
    }

    [Fact]
    public void CollapsesFourHandleRows_ForSession084aea47()
    {
        NormalizedRun run = CreateSut().Map(File.ReadAllText(LedgerPath)).Single(e => e.SessionId == Session084aea47);

        run.AgentId.Should().Be("");
        run.ExternalId.Should().Be("20260731-222106-450333-9049f7");
        run.Persona.Should().Be("csharp-coder");
        run.ModelSlug.Should().Be("openai/gpt-5.6-luna");
        run.Status.Should().Be(RunStatus.Completed);
        run.InputTokens.Should().Be(580_972);
        run.CacheReadTokens.Should().Be(9_629_379);
        run.CacheWriteTokens.Should().Be(0);
        run.OutputTokens.Should().Be(22_128);
        run.ReportedCostUsd.Should().BeApproximately(0.16766779m, 0.000001m);
    }

    [Fact]
    public void DropsTestHandleRows()
    {
        string payload = """
            {"handle": "test-123", "session_id": "sess-test", "status": "ok", "ts": "2026-08-01T00:00:00Z", "duration_s": 1, "miss_tokens": 10, "hit_tokens": 5, "output_tokens": 2, "cost_usd": 0.001}
            """;

        var envelopes = CreateSut().Map(payload);

        envelopes.Should().BeEmpty();
    }

    [Fact]
    public void SkipsRow_WithNoSessionIdAndNoHandle()
    {
        string payload = """
            {"handle": "", "session_id": "", "status": "ok", "ts": "2026-08-01T00:00:00Z", "duration_s": 1, "miss_tokens": 10, "hit_tokens": 5, "output_tokens": 2, "cost_usd": 0.001}
            """;

        var envelopes = CreateSut().Map(payload);

        envelopes.Should().BeEmpty();
    }

    [Fact]
    public void MapsRow_WithNullDurationSeconds_AsZeroDuration()
    {
        string payload = """
            {"handle": "20260801-000000-000000-abcd34", "session_id": "", "status": "ok", "ts": "2026-08-01T00:00:00Z", "duration_s": null, "miss_tokens": 10, "hit_tokens": 5, "output_tokens": 2, "cost_usd": 0.001}
            """;

        NormalizedRun run = CreateSut().Map(payload).Should().ContainSingle().Which;

        run.StartedAt.Should().Be(run.EndedAt);
    }

    [Fact]
    public void FallsBackToHandle_WhenSessionIdIsBlank()
    {
        string payload = """
            {"handle": "20260801-000000-000000-abcd12", "session_id": "", "status": "ok", "ts": "2026-08-01T00:00:00Z", "duration_s": 1, "miss_tokens": 10, "hit_tokens": 5, "output_tokens": 2, "cost_usd": 0.001}
            """;

        NormalizedRun run = CreateSut().Map(payload).Should().ContainSingle().Which;

        run.SessionId.Should().Be("20260801-000000-000000-abcd12");
        run.ExternalId.Should().Be("20260801-000000-000000-abcd12");
    }

    private static DelegateLedgerAdapter CreateSut() => new(MockLogger<DelegateLedgerAdapter>.GetSuccessful().Object);
}
