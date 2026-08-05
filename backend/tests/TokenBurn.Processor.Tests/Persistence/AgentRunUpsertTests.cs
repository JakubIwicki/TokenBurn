using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Adapters;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Builders;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Persistence;

public sealed class AgentRunUpsertTests : TelemetryHandlerTestBase
{
    // Shanghai 08:30 — off-peak, so the multiplier is 1.0 and the recomputed cost is exact.
    private static readonly DateTimeOffset OffPeakStart = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OffPeakEnd = new(2026, 1, 1, 0, 31, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreezesPricedCost_WhenQuarantinedReplayArrives()
    {
        (PricingEngine engine, AgentRunUpserter upserter) = await CreateSeededPipelineAsync();
        AgentRun first = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(1_000_000).WithCacheReadTokens(0).WithCacheWriteTokens(0).WithOutputTokens(0)
            .WithTime(OffPeakStart).Completed(OffPeakEnd)
            .BuildWithoutDatabase();

        await upserter.UpsertAsync(first, CancellationToken.None);
        await engine.PriceRunAsync(first, CancellationToken.None);
        await upserter.UpsertAsync(first, CancellationToken.None);

        AgentRun priced = LoadByKey(first.SessionId, first.AgentId);
        priced.PricingStatus.Should().Be(PricingStatus.Priced);
        priced.CostUsd.Should().Be(0.14m);
        priced.PriceMultiplier.Should().Be(1.0m);

        AgentRun second = TestAgentRunBuilder.Init(Db)
            .WithSessionId(first.SessionId).WithAgentId(first.AgentId)
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(2_000_000).WithCacheReadTokens(0).WithCacheWriteTokens(0).WithOutputTokens(0)
            .WithTime(OffPeakStart).Completed(OffPeakEnd)
            .BuildWithoutDatabase();

        await upserter.UpsertAsync(second, CancellationToken.None);

        AgentRun reloaded = LoadByKey(first.SessionId, first.AgentId);
        reloaded.PricingStatus.Should().Be(PricingStatus.Priced);
        reloaded.CostUsd.Should().Be(0.14m);
        reloaded.InputTokens.Should().Be(2_000_000);
    }

    [Fact]
    public async Task MarksPriced_WhenQuarantinedRunPricedThenReupserted()
    {
        (PricingEngine engine, AgentRunUpserter upserter) = await CreateSeededPipelineAsync();
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(1_000_000).WithCacheReadTokens(0).WithCacheWriteTokens(0).WithOutputTokens(0)
            .WithTime(OffPeakStart).Completed(OffPeakEnd)
            .BuildWithoutDatabase();

        await upserter.UpsertAsync(run, CancellationToken.None);
        AgentRun quarantined = LoadByKey(run.SessionId, run.AgentId);
        quarantined.PricingStatus.Should().Be(PricingStatus.Quarantined);

        await engine.PriceRunAsync(quarantined, CancellationToken.None);
        await upserter.UpsertAsync(quarantined, CancellationToken.None);

        AgentRun priced = LoadByKey(run.SessionId, run.AgentId);
        priced.PricingStatus.Should().Be(PricingStatus.Priced);
        priced.CostUsd.Should().Be(0.14m);
    }

    [Fact]
    public async Task RejectsReplay_WithOlderEndedAt()
    {
        (PricingEngine _, AgentRunUpserter upserter) = await CreateSeededPipelineAsync();
        var t2 = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        AgentRun newer = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("deepseek-v4-flash")
            .WithTime(t2).Completed(t2)
            .BuildWithoutDatabase();
        AgentRun older = TestAgentRunBuilder.Init(Db)
            .WithSessionId(newer.SessionId).WithAgentId(newer.AgentId)
            .WithModelSlug("deepseek-v4-flash")
            .WithTime(t1).Failed()
            .BuildWithoutDatabase();

        await upserter.UpsertAsync(newer, CancellationToken.None);
        await upserter.UpsertAsync(older, CancellationToken.None);

        AgentRun row = LoadByKey(newer.SessionId, newer.AgentId);
        row.Status.Should().Be(RunStatus.Completed);
        row.EndedAt.Should().Be(t2);
    }

    [Fact]
    public async Task Dedupes_CorpusBackfill_WhenRunTwice()
    {
        (PricingEngine engine, AgentRunUpserter upserter) = await CreateSeededPipelineAsync();
        IReadOnlyList<AgentRun> runs = new DelegateLedgerAdapter(MockLogger<DelegateLedgerAdapter>.GetSuccessful().Object)
            .Map(File.ReadAllText(CorpusPath))
            .Select(AgentRunEnvelopeMapper.ToAgentRun)
            .ToList();
        runs.Should().HaveCount(282);

        await BackfillAsync(runs, engine, upserter);
        (await Context.AgentRuns.CountAsync()).Should().Be(282);

        await BackfillAsync(runs, engine, upserter);
        (await Context.AgentRuns.CountAsync()).Should().Be(282);
    }

    private static string CorpusPath => Path.Combine(AppContext.BaseDirectory, "fixtures/reconciliation-ledger.jsonl");

    private async Task<(PricingEngine Engine, AgentRunUpserter Upserter)> CreateSeededPipelineAsync()
    {
        await new PricingSeeder(Context).SeedAsync();
        return (new PricingEngine(Context), new AgentRunUpserter(Context));
    }

    private AgentRun LoadByKey(string sessionId, string agentId)
    {
        Context.ChangeTracker.Clear();
        return Context.AgentRuns.Single(r => r.SessionId == sessionId && r.AgentId == agentId);
    }

    private static async Task BackfillAsync(IReadOnlyList<AgentRun> runs, PricingEngine engine, AgentRunUpserter upserter)
    {
        foreach (AgentRun run in runs)
        {
            await engine.PriceRunAsync(run, CancellationToken.None);
            await upserter.UpsertAsync(run, CancellationToken.None);
        }
    }
}
