using Microsoft.Extensions.Configuration;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.WasteDetection;
using TokenBurn.Testing.Common.Assertions;

namespace TokenBurn.Processor.Tests.WasteDetection;

public sealed class CostThresholdDetectorTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly WasteDetectionOptions Options = WasteDetectionOptions.FromConfiguration(new ConfigurationBuilder().Build());

    [Fact]
    public void RunOverThreshold_ReturnsFindingWithOverage()
    {
        AgentRun run = CreateRunWithCost(1.50m);

        IReadOnlyList<WasteFindingDraft> findings = CostThresholdDetector.Detect(Options, run, [], null, 1m);

        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        finding.Kind.Should().Be(WasteFindingKind.CostThreshold);
        finding.WastedCostUsd.Should().Be(0.50m);
        finding.Severity.Should().Be(WasteFindingSeverity.Critical);
        Property<string>(finding.Evidence, "kind").Should().Be("CostThreshold");
        Property<string>(finding.Evidence, "rule").Should().Be("cost-threshold");
        Property<decimal>(finding.Evidence, "runCostUsd").Should().Be(1.50m);
        Property<decimal>(finding.Evidence, "maxRunCostUsd").Should().Be(1.00m);
        Property<decimal>(finding.Evidence, "overageUsd").Should().Be(0.50m);
    }

    [Fact]
    public void RunExactlyAtThreshold_ReturnsFindingWithZeroOverage()
    {
        AgentRun run = CreateRunWithCost(1.00m);

        IReadOnlyList<WasteFindingDraft> findings = CostThresholdDetector.Detect(Options, run, [], null, 1m);

        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        finding.WastedCostUsd.Should().Be(0m);
        finding.Severity.Should().Be(WasteFindingSeverity.Minor);
    }

    [Fact]
    public void RunUnderThreshold_NoFinding()
    {
        AgentRun run = CreateRunWithCost(0.50m);

        IReadOnlyList<WasteFindingDraft> findings = CostThresholdDetector.Detect(Options, run, [], null, 1m);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void SeverityTiers_FollowMajorAndCriticalBoundaries()
    {
        WasteSeverity.For(0.0099m, Options).Should().Be(WasteFindingSeverity.Minor);
        WasteSeverity.For(0.01m, Options).Should().Be(WasteFindingSeverity.Major);
        WasteSeverity.For(0.10m, Options).Should().Be(WasteFindingSeverity.Critical);
        WasteSeverity.For(null, Options).Should().Be(WasteFindingSeverity.Minor);
    }

    private static AgentRun CreateRunWithCost(decimal costUsd)
    {
        AgentRun run = AgentRun.Create(
            "session-1", "agent-1", "test", null, null, "deepseek-v4-flash", RunStatus.Completed,
            OccurredAt, OccurredAt.AddMinutes(1), 1_000_000, 0, 0, 0, null);
        run.TryMarkPriced(costUsd, 1.0m).AssertSuccess();
        return run;
    }

    private static T Property<T>(object evidence, string name)
        => (T)evidence.GetType().GetProperty(name)!.GetValue(evidence)!;
}
