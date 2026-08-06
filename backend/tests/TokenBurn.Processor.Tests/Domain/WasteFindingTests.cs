using TokenBurn.Common.Primitives;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.WasteDetection;
using TokenBurn.Testing.Common.Assertions;

namespace TokenBurn.Processor.Tests.Domain;

public sealed class WasteFindingTests
{
    private static readonly Guid RunId = new("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset DetectedAt = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly object EvidenceObject = new
    {
        messageSequences = new[] { 1, 2 },
        modelSlug = "deepseek-v4-flash"
    };

    [Fact]
    public void Create_SetsIdentityAndPersistenceFields()
    {
        WasteFinding finding = WasteFinding.Create(RunId, WasteFindingKind.ContextReplay, WasteFindingSeverity.Major, EvidenceObject, 0.42m, DetectedAt);

        finding.Id.Should().NotBeEmpty();
        finding.RunId.Should().Be(RunId);
        finding.Kind.Should().Be(WasteFindingKind.ContextReplay);
        finding.Severity.Should().Be(WasteFindingSeverity.Major);
        finding.WastedCostUsd.Should().Be(0.42m);
        finding.DetectedAt.Should().Be(DetectedAt);
        finding.AcknowledgedAt.Should().BeNull();
        finding.Version.Should().Be(1);
    }

    [Fact]
    public void Create_SerializesEvidence_AndHashesOverTheSameJson()
    {
        WasteFinding finding = WasteFinding.Create(RunId, WasteFindingKind.Loop, WasteFindingSeverity.Minor, EvidenceObject, null, DetectedAt);

        finding.Evidence.Should().Be(EvidenceHasher.Serialize(EvidenceObject));
        finding.EvidenceHash.Should().Be(EvidenceHasher.Compute(EvidenceObject));
        finding.EvidenceHash.Should().HaveLength(64);
    }

    [Fact]
    public void TryAcknowledge_SetsOnce_AndSecondCallConflicts()
    {
        WasteFinding finding = WasteFinding.Create(RunId, WasteFindingKind.CostThreshold, WasteFindingSeverity.Critical, EvidenceObject, 5.00m, DetectedAt);
        DateTimeOffset ackAt = DetectedAt.AddDays(1);

        Result first = finding.TryAcknowledge(ackAt);
        Result second = finding.TryAcknowledge(ackAt.AddHours(1));

        first.AssertSuccess();
        second.AssertConflict();
        finding.AcknowledgedAt.Should().Be(ackAt);
    }
}
