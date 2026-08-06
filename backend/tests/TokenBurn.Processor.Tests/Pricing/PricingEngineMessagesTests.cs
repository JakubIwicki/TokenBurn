using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Assertions;
using TokenBurn.Testing.Common.Builders;

namespace TokenBurn.Processor.Tests.Pricing;

public sealed class PricingEngineMessagesTests : TelemetryHandlerTestBase
{
    // Shanghai 09:30 (UTC+8) = 01:30 UTC — inside the 09:00-12:00 peak window, multiplier 2.0.
    private static readonly DateTimeOffset PeakStart = new(2026, 1, 1, 1, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeakEnd = new(2026, 1, 1, 1, 31, 0, TimeSpan.Zero);
    // Shanghai 13:30 (UTC+8) = 05:30 UTC — off-peak: priced at its own occurred_at this
    // message would get multiplier 1.0 and the per-message sum would break the run total.
    private static readonly DateTimeOffset OffPeakOccurredAt = new(2026, 1, 1, 5, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task SumOfMessageCosts_EqualsRunCost_WhenRunIsInPeakWindow()
    {
        await new PricingSeeder(Context).SeedAsync();
        PricingEngine engine = new(Context);
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(1_000_000).WithCacheReadTokens(500_000).WithCacheWriteTokens(100_000).WithOutputTokens(200_000)
            .WithTime(PeakStart).Completed(PeakEnd)
            .BuildWithoutDatabase();

        (await engine.PriceRunAsync(run, CancellationToken.None)).AssertSuccess();
        run.PricingStatus.Should().Be(PricingStatus.Priced);
        run.PriceMultiplier.Should().Be(2.0m);

        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", "first", null, "deepseek-v4-flash", 400_000, 200_000, 0, 80_000, PeakStart),
            AgentMessage.Create(run.Id, 2, "assistant", "second", null, "deepseek-v4-flash", 600_000, 300_000, 100_000, 120_000, OffPeakOccurredAt)
        ];
        (await engine.PriceMessagesAsync(run, messages, CancellationToken.None)).AssertSuccess();

        messages.Sum(m => m.CostUsd).Should().Be(run.CostUsd);
    }

    [Fact]
    public async Task LeavesMessageCostsNull_WhenRunIsQuarantined()
    {
        await new PricingSeeder(Context).SeedAsync();
        PricingEngine engine = new(Context);
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("no-such-model")
            .WithInputTokens(1_000_000).WithCacheReadTokens(0).WithCacheWriteTokens(0).WithOutputTokens(0)
            .WithTime(PeakStart).Completed(PeakEnd)
            .BuildWithoutDatabase();
        (await engine.PriceRunAsync(run, CancellationToken.None)).AssertSuccess();
        run.PricingStatus.Should().Be(PricingStatus.Quarantined);

        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", "hello", null, "no-such-model", 1_000_000, 0, 0, 0, PeakStart)
        ];
        (await engine.PriceMessagesAsync(run, messages, CancellationToken.None)).AssertSuccess();

        messages[0].CostUsd.Should().BeNull();
    }

    [Fact]
    public async Task LeavesMessageCostsNull_WhenRunHasNoUsage()
    {
        await new PricingSeeder(Context).SeedAsync();
        PricingEngine engine = new(Context);
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(0).WithCacheReadTokens(0).WithCacheWriteTokens(0).WithOutputTokens(0)
            .WithReportedCostUsd(0.01m)
            .WithTime(PeakStart).Completed(PeakEnd)
            .BuildWithoutDatabase();
        (await engine.PriceRunAsync(run, CancellationToken.None)).AssertSuccess();
        run.PricingStatus.Should().Be(PricingStatus.Quarantined);

        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", "hello", null, "deepseek-v4-flash", 0, 0, 0, 0, PeakStart)
        ];
        (await engine.PriceMessagesAsync(run, messages, CancellationToken.None)).AssertSuccess();

        messages[0].CostUsd.Should().BeNull();
    }

    [Fact]
    public async Task LeavesMessageCostsNull_WhenRunHasNoEndedAt()
    {
        PricingEngine engine = new(Context);
        AgentRun run = TestAgentRunBuilder.Init(Db).Running().WithModelSlug("deepseek-v4-flash").BuildWithoutDatabase();

        AgentMessage[] messages =
        [
            AgentMessage.Create(run.Id, 1, "user", "hello", null, "deepseek-v4-flash", 1_000_000, 0, 0, 0, PeakStart)
        ];
        (await engine.PriceMessagesAsync(run, messages, CancellationToken.None)).AssertSuccess();

        messages[0].CostUsd.Should().BeNull();
    }
}
