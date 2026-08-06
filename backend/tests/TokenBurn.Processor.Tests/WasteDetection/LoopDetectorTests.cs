using Microsoft.Extensions.Configuration;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.WasteDetection;

namespace TokenBurn.Processor.Tests.WasteDetection;

public sealed class LoopDetectorTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly WasteDetectionOptions Options = WasteDetectionOptions.FromConfiguration(new ConfigurationBuilder().Build());

    [Fact]
    public void NearIdenticalMessages_WithinWindow_FlaggedWithCorrectEvidence()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 50_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 2, "user", null, null, "deepseek-v4-flash", 51_000, 10_200, 5_100, 2_000, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = LoopDetector.Detect(Options, run, messages, null, 1m);

        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        finding.Kind.Should().Be(WasteFindingKind.Loop);
        Property<int[]>(finding.Evidence, "sequences").Should().Equal(1, 2);
        Property<int>(finding.Evidence, "occurrences").Should().Be(2);
        object tokens = Property<object>(finding.Evidence, "tokens");
        Property<long>(tokens, "input").Should().Be(50_000);
        Property<long>(tokens, "cacheRead").Should().Be(10_000);
        Property<long>(tokens, "cacheWrite").Should().Be(5_000);
        Property<long>(tokens, "output").Should().Be(2_000);
    }

    [Fact]
    public void RepeatedChain_AcrossSeveralMessages_ReportsAllSequences()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 50_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 2, "assistant", null, null, "deepseek-v4-flash", 99_999, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 3, "user", null, null, "deepseek-v4-flash", 51_000, 10_200, 5_100, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 4, "assistant", null, null, "deepseek-v4-flash", 88_888, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 5, "user", null, null, "deepseek-v4-flash", 49_000, 9_800, 4_900, 2_000, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = LoopDetector.Detect(Options, run, messages, null, 1m);

        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        Property<int[]>(finding.Evidence, "sequences").Should().Equal(1, 3, 5);
        Property<int>(finding.Evidence, "occurrences").Should().Be(3);
        object tokens = Property<object>(finding.Evidence, "tokens");
        Property<long>(tokens, "input").Should().Be(50_000);
    }

    [Fact]
    public void NearIdenticalMessages_WithDifferentRole_NotFlagged()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 50_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 2, "assistant", null, null, "deepseek-v4-flash", 51_000, 10_200, 5_100, 2_000, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = LoopDetector.Detect(Options, run, messages, null, 1m);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void NearIdenticalMessages_WithDifferentModel_NotFlagged()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 50_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 2, "user", null, null, "claude-opus-5", 51_000, 10_200, 5_100, 2_000, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = LoopDetector.Detect(Options, run, messages, null, 1m);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void NearIdenticalMessages_BelowMinInputTokens_NotFlagged()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 5_000, 1_000, 500, 200, OccurredAt),
            AgentMessage.Create(run.Id, 2, "user", null, null, "deepseek-v4-flash", 5_100, 1_020, 510, 200, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = LoopDetector.Detect(Options, run, messages, null, 1m);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void NearIdenticalMessages_BeyondWindowApart_NotFlagged()
    {
        AgentRun run = CreateRun();
        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", null, null, "deepseek-v4-flash", 50_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 2, "assistant", null, null, "deepseek-v4-flash", 90_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 3, "assistant", null, null, "deepseek-v4-flash", 85_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 4, "assistant", null, null, "deepseek-v4-flash", 80_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 5, "assistant", null, null, "deepseek-v4-flash", 75_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 6, "assistant", null, null, "deepseek-v4-flash", 70_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(run.Id, 7, "user", null, null, "deepseek-v4-flash", 51_000, 10_200, 5_100, 2_000, OccurredAt)
        ];

        IReadOnlyList<WasteFindingDraft> findings = LoopDetector.Detect(Options, run, messages, null, 1m);

        findings.Should().BeEmpty();
    }

    private static AgentRun CreateRun()
        => AgentRun.Create(
            "session-1", "agent-1", "test", null, null, "deepseek-v4-flash", RunStatus.Completed,
            OccurredAt, OccurredAt.AddMinutes(1), 1_000_000, 500_000, 200_000, 100_000, null);

    private static T Property<T>(object evidence, string name)
        => (T)evidence.GetType().GetProperty(name)!.GetValue(evidence)!;
}
