using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Assertions;
using TokenBurn.Testing.Common.Builders;

namespace TokenBurn.Processor.Tests.Persistence;

public sealed class AgentRunIdStabilityTests : TelemetryHandlerTestBase
{
    private static readonly DateTimeOffset T1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReturnsStoredId_OnInsert()
    {
        AgentRunUpserter upserter = new(Context);
        AgentRun run = TestAgentRunBuilder.Init(Db).WithAgentId("").WithTime(T2).Completed(T2).BuildWithoutDatabase();

        (Guid storedId, bool applied) = await upserter.UpsertAsync(run, CancellationToken.None);

        storedId.Should().Be(run.Id);
        applied.Should().BeTrue();
        Context.AgentRuns.Should().ContainSingle(r => r.Id == storedId);
    }

    [Fact]
    public async Task ReturnsOriginalStoredId_NotApplied_WhenOlderEndedAtReplay()
    {
        AgentRunUpserter upserter = new(Context);
        AgentRun newer = TestAgentRunBuilder.Init(Db)
            .WithAgentId("").WithModelSlug("deepseek-v4-flash").WithTime(T3).Completed(T3)
            .BuildWithoutDatabase();
        Guid original = (await upserter.UpsertAsync(newer, CancellationToken.None)).StoredId;

        AgentRun older = TestAgentRunBuilder.Init(Db)
            .WithSessionId(newer.SessionId).WithAgentId(newer.AgentId)
            .WithModelSlug("deepseek-v4-flash").WithTime(T1).Failed()
            .BuildWithoutDatabase();
        (Guid storedId, bool applied) = await upserter.UpsertAsync(older, CancellationToken.None);

        storedId.Should().Be(original);
        storedId.Should().NotBe(older.Id);
        applied.Should().BeFalse();
    }

    [Fact]
    public async Task ReturnsOriginalStoredId_Applied_WhenNewerEndedAtReplay()
    {
        AgentRunUpserter upserter = new(Context);
        AgentRun first = TestAgentRunBuilder.Init(Db)
            .WithAgentId("").WithModelSlug("deepseek-v4-flash").WithTime(T2).Completed(T2)
            .BuildWithoutDatabase();
        Guid original = (await upserter.UpsertAsync(first, CancellationToken.None)).StoredId;

        AgentRun replay = TestAgentRunBuilder.Init(Db)
            .WithSessionId(first.SessionId).WithAgentId(first.AgentId)
            .WithModelSlug("deepseek-v4-flash").WithTime(T3).Completed(T3)
            .BuildWithoutDatabase();
        (Guid storedId, bool applied) = await upserter.UpsertAsync(replay, CancellationToken.None);

        storedId.Should().Be(original);
        applied.Should().BeTrue();
        AgentRun row = await Context.AgentRuns.AsNoTracking().SingleAsync(r => r.SessionId == first.SessionId);
        row.Id.Should().Be(original);
    }

    [Fact]
    public async Task MessagesSurviveReimport_KeyedToOriginalId()
    {
        await new PricingSeeder(Context).SeedAsync();
        AgentRunUpserter upserter = new(Context);
        AgentMessageUpserter messageUpserter = new(Context);
        PricingEngine engine = new(Context);
        DateTimeOffset start = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
        DateTimeOffset end = new(2026, 1, 1, 0, 31, 0, TimeSpan.Zero);

        AgentRun first = TestAgentRunBuilder.Init(Db)
            .WithAgentId("")
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(1_000_000).WithCacheReadTokens(0).WithCacheWriteTokens(0).WithOutputTokens(0)
            .WithTime(start).Completed(end)
            .BuildWithoutDatabase();
        await engine.PriceRunAsync(first, CancellationToken.None);
        (Guid storedId, bool appliedFirst) = await upserter.UpsertAsync(first, CancellationToken.None);
        appliedFirst.Should().BeTrue();

        AgentMessage[] firstMessages =
        [
            AgentMessage.Create(storedId, 1, "user", "hello", null, "deepseek-v4-flash", 500_000, 0, 0, 0, start),
            AgentMessage.Create(storedId, 2, "assistant", "world", null, "deepseek-v4-flash", 500_000, 0, 0, 0, end)
        ];
        (await engine.PriceMessagesAsync(first, firstMessages, CancellationToken.None)).AssertSuccess();
        await messageUpserter.UpsertAsync(storedId, firstMessages, CancellationToken.None);

        AgentRun reimport = TestAgentRunBuilder.Init(Db)
            .WithSessionId(first.SessionId).WithAgentId(first.AgentId)
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(1_000_000).WithCacheReadTokens(0).WithCacheWriteTokens(0).WithOutputTokens(0)
            .WithTime(start).Completed(end.AddMinutes(1))
            .BuildWithoutDatabase();
        await engine.PriceRunAsync(reimport, CancellationToken.None);
        (Guid reimportedId, bool appliedSecond) = await upserter.UpsertAsync(reimport, CancellationToken.None);

        reimportedId.Should().Be(storedId);
        appliedSecond.Should().BeTrue();

        AgentMessage[] reimportMessages =
        [
            AgentMessage.Create(reimportedId, 1, "user", "hello", null, "deepseek-v4-flash", 500_000, 0, 0, 0, start),
            AgentMessage.Create(reimportedId, 2, "assistant", "world", null, "deepseek-v4-flash", 500_000, 0, 0, 0, end)
        ];
        (await engine.PriceMessagesAsync(reimport, reimportMessages, CancellationToken.None)).AssertSuccess();
        await messageUpserter.UpsertAsync(reimportedId, reimportMessages, CancellationToken.None);

        List<AgentMessage> rows = await Context.AgentMessages.AsNoTracking().OrderBy(m => m.Sequence).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(m => m.RunId == storedId);
        AgentRun storedRun = await Context.AgentRuns.AsNoTracking().SingleAsync(r => r.Id == storedId);
        storedRun.CostUsd.Should().NotBeNull();
        rows.Sum(m => m.CostUsd).Should().Be(storedRun.CostUsd);
    }
}
