using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Processor.WasteDetection;
using TokenBurn.Testing.Common.Assertions;

namespace TokenBurn.Processor.Tests.Persistence;

public sealed class FindingsUpsertTests : TelemetryHandlerTestBase
{
    private static readonly Guid RunId = new("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset DetectedAt = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly object Evidence = new
    {
        messageSequences = new[] { 1, 2 },
        modelSlug = "deepseek-v4-flash"
    };
    private static readonly object OtherEvidence = new
    {
        messageSequences = new[] { 3, 4 },
        modelSlug = "deepseek-v4-flash"
    };
    // Non-alphabetical key order on purpose: jsonb re-sorts keys on write, so this exercises the
    // normalization path instead of a fixture that is already alphabetically ordered.
    private static readonly object UnorderedEvidence = new
    {
        zebra = 1,
        alpha = 2
    };

    [Fact]
    public async Task UpsertsFinding_WithAllColumns_AndJsonbEvidenceRoundTrips()
    {
        FindingsUpserter upserter = new(Context);
        WasteFinding finding = WasteFinding.Create(RunId, WasteFindingKind.ContextReplay, WasteFindingSeverity.Major, Evidence, 0.42m, DetectedAt);

        await upserter.UpsertAsync([finding], CancellationToken.None);

        WasteFinding row = Context.WasteFindings.AsNoTracking().Single();
        row.Id.Should().Be(finding.Id);
        row.RunId.Should().Be(RunId);
        row.Kind.Should().Be(WasteFindingKind.ContextReplay);
        row.Severity.Should().Be(WasteFindingSeverity.Major);
        row.EvidenceHash.Should().Be(EvidenceHasher.Compute(Evidence));
        row.WastedCostUsd.Should().Be(0.42m);
        row.DetectedAt.Should().Be(DetectedAt);
        row.AcknowledgedAt.Should().BeNull();
        row.Version.Should().Be(1);
        // jsonb normalizes key order, so the round-trip is semantic: assert the parsed content.
        using JsonDocument document = JsonDocument.Parse(row.Evidence);
        JsonElement evidence = document.RootElement;
        evidence.GetProperty("modelSlug").GetString().Should().Be("deepseek-v4-flash");
        evidence.GetProperty("messageSequences").EnumerateArray().Select(sequence => sequence.GetInt32()).Should().Equal(1, 2);
    }

    [Fact]
    public async Task ReupsertingSameFinding_KeepsOneRow_AndPreservesDetectedAt()
    {
        FindingsUpserter upserter = new(Context);
        WasteFinding first = WasteFinding.Create(RunId, WasteFindingKind.ContextReplay, WasteFindingSeverity.Minor, Evidence, 0.01m, DetectedAt);
        await upserter.UpsertAsync([first], CancellationToken.None);

        // Same (run_id, kind, evidence_hash) — a replay may only mutate severity/cost. The
        // replayed finding also carries an acknowledgement, so a DO UPDATE that ever touches
        // acknowledged_at (or version) would fail the assertions below.
        WasteFinding replayed = WasteFinding.Create(RunId, WasteFindingKind.ContextReplay, WasteFindingSeverity.Critical, Evidence, 0.99m, DetectedAt.AddHours(1));
        replayed.TryAcknowledge(DetectedAt.AddDays(1)).AssertSuccess();
        await upserter.UpsertAsync([replayed], CancellationToken.None);

        List<WasteFinding> rows = Context.WasteFindings.AsNoTracking().ToList();
        rows.Should().HaveCount(1);
        rows[0].Id.Should().Be(first.Id);
        rows[0].DetectedAt.Should().Be(DetectedAt);
        rows[0].Severity.Should().Be(WasteFindingSeverity.Critical);
        rows[0].WastedCostUsd.Should().Be(0.99m);
        rows[0].AcknowledgedAt.Should().BeNull();
        rows[0].Version.Should().Be(1);
    }

    [Fact]
    public async Task UpsertingDifferentEvidence_InsertsNewRow()
    {
        FindingsUpserter upserter = new(Context);
        WasteFinding first = WasteFinding.Create(RunId, WasteFindingKind.ContextReplay, WasteFindingSeverity.Minor, Evidence, null, DetectedAt);
        WasteFinding differentEvidence = WasteFinding.Create(RunId, WasteFindingKind.ContextReplay, WasteFindingSeverity.Minor, OtherEvidence, null, DetectedAt);

        await upserter.UpsertAsync([first, differentEvidence], CancellationToken.None);

        List<WasteFinding> rows = Context.WasteFindings.AsNoTracking().ToList();
        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpsertingDifferentKind_InsertsNewRow()
    {
        FindingsUpserter upserter = new(Context);
        WasteFinding first = WasteFinding.Create(RunId, WasteFindingKind.ContextReplay, WasteFindingSeverity.Minor, Evidence, null, DetectedAt);
        WasteFinding differentKind = WasteFinding.Create(RunId, WasteFindingKind.Loop, WasteFindingSeverity.Minor, Evidence, null, DetectedAt);

        await upserter.UpsertAsync([first, differentKind], CancellationToken.None);

        List<WasteFinding> rows = Context.WasteFindings.AsNoTracking().ToList();
        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpsertingNonAlphabeticalEvidence_ReordersKeysInJsonb_AndDedupesOnReupsert()
    {
        FindingsUpserter upserter = new(Context);
        WasteFinding finding = WasteFinding.Create(RunId, WasteFindingKind.ContextReplay, WasteFindingSeverity.Minor, UnorderedEvidence, null, DetectedAt);

        await upserter.UpsertAsync([finding], CancellationToken.None);
        await upserter.UpsertAsync([finding], CancellationToken.None);

        List<WasteFinding> rows = Context.WasteFindings.AsNoTracking().ToList();
        rows.Should().HaveCount(1);
        // jsonb re-sorts keys alphabetically, so the stored text differs from the serialization
        // that was hashed; the semantic content must survive.
        rows[0].Evidence.Should().NotBe(EvidenceHasher.Serialize(UnorderedEvidence));
        using JsonDocument document = JsonDocument.Parse(rows[0].Evidence);
        JsonElement evidence = document.RootElement;
        evidence.GetProperty("zebra").GetInt32().Should().Be(1);
        evidence.GetProperty("alpha").GetInt32().Should().Be(2);
    }
}
