using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Processor.WasteDetection;
using TokenBurn.Testing.Common.Assertions;
using TokenBurn.Testing.Common.Builders;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.WasteDetection;

public sealed class WasteDetectionServiceTests : TelemetryHandlerTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 1, 1, 0, 31, 0, TimeSpan.Zero);
    private static readonly WasteDetectionOptions Options = WasteDetectionOptions.FromConfiguration(new ConfigurationBuilder().Build());
    // Instance, not static: the idempotency test advances this clock, and xUnit builds a fresh
    // class instance per test method, so a sibling test never sees a clock another test moved.
    private readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task DetectRun_WithWriteThenReadSpike_ReturnsContextReplayFinding()
    {
        (Guid storedId, AgentMessage[] messages) = await SeedPricedContextReplayRunAsync();

        IReadOnlyList<WasteFindingDraft> findings = await CreateSut().DetectRunAsync(storedId, CancellationToken.None);

        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        finding.RunId.Should().Be(storedId);
        finding.Kind.Should().Be(WasteFindingKind.ContextReplay);
        finding.EvidenceHash.Should().NotBeNullOrWhiteSpace();
        finding.WastedCostUsd.Should().Be(messages[0].CostUsd);
        Property<int[]>(finding.Evidence, "messageSequences").Should().Equal(1, 2);
    }

    [Fact]
    public async Task DetectRun_WhenRunIsRunning_ReturnsEmptyEvenWithFlaggingMessages()
    {
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(5_000).WithCacheReadTokens(631_936).WithCacheWriteTokens(150_000).WithOutputTokens(0)
            .Running()
            .BuildWithoutDatabase();
        Guid storedId = (await new AgentRunUpserter(Context).UpsertAsync(run, CancellationToken.None)).StoredId;
        AgentMessage[] messages =
        [
            AgentMessage.Create(storedId, 1, "user", null, null, "deepseek-v4-flash", 5_000, 0, 150_000, 0, Start),
            AgentMessage.Create(storedId, 2, "assistant", null, null, "deepseek-v4-flash", 0, 631_936, 0, 0, Start)
        ];
        await new AgentMessageUpserter(Context).UpsertAsync(storedId, messages, CancellationToken.None);

        IReadOnlyList<WasteFindingDraft> findings = await CreateSut().DetectRunAsync(storedId, CancellationToken.None);

        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectRun_WhenRunHasNoMessages_ReturnsEmpty()
    {
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithAgentId("").WithModelSlug("deepseek-v4-flash").WithTime(Start).Completed(End)
            .BuildWithoutDatabase();
        Guid storedId = (await new AgentRunUpserter(Context).UpsertAsync(run, CancellationToken.None)).StoredId;

        IReadOnlyList<WasteFindingDraft> findings = await CreateSut().DetectRunAsync(storedId, CancellationToken.None);

        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectRun_WhenRunDoesNotExist_ReturnsEmpty()
    {
        IReadOnlyList<WasteFindingDraft> findings = await CreateSut().DetectRunAsync(Guid.NewGuid(), CancellationToken.None);

        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectRun_WhenRunIsUnpriced_ReturnsFindingWithNullCostAndHash()
    {
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("no-such-model")
            .WithInputTokens(5_000).WithCacheReadTokens(0).WithCacheWriteTokens(150_000).WithOutputTokens(0)
            .WithTime(Start).Completed(End)
            .BuildWithoutDatabase();
        Guid storedId = (await new AgentRunUpserter(Context).UpsertAsync(run, CancellationToken.None)).StoredId;
        AgentMessage[] messages =
        [
            AgentMessage.Create(storedId, 1, "user", null, null, "no-such-model", 5_000, 0, 150_000, 0, Start),
            AgentMessage.Create(storedId, 2, "assistant", null, null, "no-such-model", 0, 631_936, 0, 0, Start)
        ];
        await new AgentMessageUpserter(Context).UpsertAsync(storedId, messages, CancellationToken.None);

        IReadOnlyList<WasteFindingDraft> findings = await CreateSut().DetectRunAsync(storedId, CancellationToken.None);

        WasteFindingDraft finding = findings.Should().ContainSingle().Subject;
        finding.WastedCostUsd.Should().BeNull();
        finding.EvidenceHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DetectRun_Twice_ProducesSameEvidenceHash()
    {
        (Guid storedId, _) = await SeedPricedContextReplayRunAsync();
        WasteDetectionService service = CreateSut();

        string first = (await service.DetectRunAsync(storedId, CancellationToken.None)).Single().EvidenceHash;
        string second = (await service.DetectRunAsync(storedId, CancellationToken.None)).Single().EvidenceHash;

        second.Should().Be(first);
    }

    [Fact]
    public async Task DetectRun_PersistsFinding_AndReDetectKeepsOneRowWithSameDetectedAt()
    {
        (Guid storedId, _) = await SeedPricedContextReplayRunAsync();
        WasteDetectionService service = CreateSut();

        await service.DetectRunAsync(storedId, CancellationToken.None);
        DateTimeOffset firstDetectedAt = Context.WasteFindings.AsNoTracking().Single().DetectedAt;

        Clock.Advance(TimeSpan.FromMinutes(5));
        await service.DetectRunAsync(storedId, CancellationToken.None);

        List<WasteFinding> rows = Context.WasteFindings.AsNoTracking().ToList();
        rows.Should().HaveCount(1);
        rows[0].DetectedAt.Should().Be(firstDetectedAt);
        rows[0].Kind.Should().Be(WasteFindingKind.ContextReplay);
        rows[0].EvidenceHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DetectRun_PersistsNothing_WhenRunHasNoMessages()
    {
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithAgentId("").WithModelSlug("deepseek-v4-flash").WithTime(Start).Completed(End)
            .BuildWithoutDatabase();
        Guid storedId = (await new AgentRunUpserter(Context).UpsertAsync(run, CancellationToken.None)).StoredId;

        await CreateSut().DetectRunAsync(storedId, CancellationToken.None);

        List<WasteFinding> rows = Context.WasteFindings.AsNoTracking().ToList();
        rows.Should().BeEmpty();
    }

    private WasteDetectionService CreateSut()
        => new(Context, Options, new PricingEngine(Context), new FindingsUpserter(Context), Clock,
            MockLogger<WasteDetectionService>.GetSuccessful().Object);

    private async Task<(Guid StoredId, AgentMessage[] Messages)> SeedPricedContextReplayRunAsync()
    {
        await new PricingSeeder(Context).SeedAsync();
        PricingEngine engine = new(Context);
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(5_000).WithCacheReadTokens(631_936).WithCacheWriteTokens(150_000).WithOutputTokens(0)
            .WithTime(Start).Completed(End)
            .BuildWithoutDatabase();
        (await engine.PriceRunAsync(run, CancellationToken.None)).AssertSuccess();
        Guid storedId = (await new AgentRunUpserter(Context).UpsertAsync(run, CancellationToken.None)).StoredId;

        AgentMessage[] messages =
        [
            AgentMessage.Create(storedId, 1, "user", "prompt", null, "deepseek-v4-flash", 5_000, 0, 150_000, 0, Start),
            AgentMessage.Create(storedId, 2, "assistant", "response", null, "deepseek-v4-flash", 0, 631_936, 0, 0, Start)
        ];
        (await engine.PriceMessagesAsync(run, messages, CancellationToken.None)).AssertSuccess();
        await new AgentMessageUpserter(Context).UpsertAsync(storedId, messages, CancellationToken.None);
        return (storedId, messages);
    }

    private static T Property<T>(object evidence, string name)
        => (T)evidence.GetType().GetProperty(name)!.GetValue(evidence)!;
}
