using Microsoft.Extensions.Configuration;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.WasteDetection;

namespace TokenBurn.Processor.Tests.WasteDetection;

public sealed class ContextReplayDetectorTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly WasteDetectionOptions Options = WasteDetectionOptions.FromConfiguration(new ConfigurationBuilder().Build());

    [Fact]
    public void WriteSpikeThenReadSpike_WithinWindow_FlagsContextReplay()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 5_000, 0, 150_000, 0, OccurredAt),
            AgentMessage.Create(run.Id, 2, "assistant", null, null, "deepseek-v4-flash", 0, 631_936, 0, 0, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = ContextReplayDetector.Detect(Options, run, messages, null, 1m);

        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        finding.Kind.Should().Be(WasteFindingKind.ContextReplay);
        finding.Severity.Should().Be(WasteFindingSeverity.Minor);
        Property<int[]>(finding.Evidence, "messageSequences").Should().Equal(1, 2);
        Property<string>(finding.Evidence, "kind").Should().Be("ContextReplay");
        Property<string>(finding.Evidence, "rule").Should().Be("context-replay");
        Property<string>(finding.Evidence, "modelSlug").Should().Be("deepseek-v4-flash");
        Property<long>(finding.Evidence, "cacheWriteTokens").Should().Be(150_000);
        Property<long>(finding.Evidence, "cacheReadTokens").Should().Be(631_936);
    }

    [Fact]
    public void WriteSpike_WithNoBigRead_NotFlagged()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 5_000, 0, 150_000, 0, OccurredAt),
            AgentMessage.Create(run.Id, 2, "assistant", null, null, "deepseek-v4-flash", 0, 10_000, 0, 0, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = ContextReplayDetector.Detect(Options, run, messages, null, 1m);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void BigRead_WithNoPriorWriteSpike_NotFlagged()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 5_000, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 2, "assistant", null, null, "deepseek-v4-flash", 0, 631_936, 0, 0, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = ContextReplayDetector.Detect(Options, run, messages, null, 1m);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void ReadAtWindowBoundary_WithinWindow_Flagged()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 5_000, 0, 150_000, 0, OccurredAt),
            AgentMessage.Create(run.Id, 2, "user", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 3, "user", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 4, "user", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 5, "user", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 6, "assistant", null, null, "deepseek-v4-flash", 0, 631_936, 0, 0, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = ContextReplayDetector.Detect(Options, run, messages, null, 1m);

        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        Property<int[]>(finding.Evidence, "messageSequences").Should().Equal(1, 6);
    }

    [Fact]
    public void ReadBeyondWindow_NotFlagged()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 5_000, 0, 150_000, 0, OccurredAt),
            AgentMessage.Create(run.Id, 2, "user", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 3, "user", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 4, "user", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 5, "user", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 6, "user", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 7, "assistant", null, null, "deepseek-v4-flash", 0, 631_936, 0, 0, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = ContextReplayDetector.Detect(Options, run, messages, null, 1m);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void RealCorpusReplayShape_WriteSpikeThenHugeRead_FlagsBothSequences()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 5_000, 0, 150_000, 0, OccurredAt),
            AgentMessage.Create(run.Id, 2, "assistant", null, null, "deepseek-v4-flash", 0, 0, 0, 0, OccurredAt),
            AgentMessage.Create(run.Id, 3, "user", null, null, "deepseek-v4-flash", 0, 631_936, 0, 0, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = ContextReplayDetector.Detect(Options, run, messages, null, 1m);

        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        Property<int[]>(finding.Evidence, "messageSequences").Should().Equal(1, 3);
        Property<long>(finding.Evidence, "cacheWriteTokens").Should().Be(150_000);
        Property<long>(finding.Evidence, "cacheReadTokens").Should().Be(631_936);
        object runTotals = Property<object>(finding.Evidence, "runTotals");
        Property<long>(runTotals, "input").Should().Be(1_000_000);
        Property<long>(runTotals, "cacheRead").Should().Be(500_000);
        Property<long>(runTotals, "cacheWrite").Should().Be(200_000);
        Property<long>(runTotals, "output").Should().Be(100_000);
    }

    [Fact]
    public void WriteSpike_WhenWriteUnpricedButPriceResolvable_UsesFullMessageCost()
    {
        AgentRun run = CreateRun();
        PriceRow deepseek = new("deepseek-v4-flash", "deepseek", 0.14m, 0.0028m, 0m, 0.28m, 1048576);
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 10_000, 0, 150_000, 5_000, OccurredAt),
            AgentMessage.Create(run.Id, 2, "assistant", null, null, "deepseek-v4-flash", 0, 631_936, 0, 0, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = ContextReplayDetector.Detect(Options, run, messages, deepseek, 1m);

        // Full message cost: (10_000 * 0.14 + 5_000 * 0.28) / 1_000_000 = 0.0028 — NOT the
        // cache-write-only cost of 0, so this pins the priced-path/unpriced-path parity.
        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        finding.WastedCostUsd.Should().Be(0.0028m);
    }

    private static AgentRun CreateRun(
        long? input = 1_000_000, long? cacheRead = 500_000, long? cacheWrite = 200_000, long? output = 100_000)
        => AgentRun.Create(
            "session-1", "agent-1", "test", null, null, "deepseek-v4-flash", RunStatus.Completed,
            OccurredAt, OccurredAt.AddMinutes(1), input, cacheRead, cacheWrite, output, null);

    private static T Property<T>(object evidence, string name)
        => (T)evidence.GetType().GetProperty(name)!.GetValue(evidence)!;
}
