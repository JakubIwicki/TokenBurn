using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Assertions;
using TokenBurn.Testing.Common.Builders;

namespace TokenBurn.Processor.Tests.Persistence;

public sealed class AgentMessageUpsertTests : TelemetryHandlerTestBase
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task PersistsMessages_KeyedToStoredRunId()
    {
        AgentRunUpserter upserter = new(Context);
        AgentMessageUpserter messageUpserter = new(Context);
        AgentRun run = TestAgentRunBuilder.Init(Db).WithAgentId("").WithModelSlug("deepseek-v4-flash").BuildWithoutDatabase();
        Guid storedId = (await upserter.UpsertAsync(run, CancellationToken.None)).StoredId;

        AgentMessage first = AgentMessage.Create(storedId, 1, "user", "hello", null, "deepseek-v4-flash", 10, 5, 0, 2, OccurredAt);
        first.AttachCost(0.01m).AssertSuccess();
        AgentMessage second = AgentMessage.Create(storedId, 2, "assistant", "world", null, "deepseek-v4-flash", 20, 10, 0, 4, OccurredAt);
        second.AttachCost(0.02m).AssertSuccess();
        await messageUpserter.UpsertAsync(storedId, [first, second], CancellationToken.None);

        List<AgentMessage> rows = await Context.AgentMessages.AsNoTracking().OrderBy(m => m.Sequence).ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(m => m.RunId == storedId);
        rows[0].Sequence.Should().Be(1);
        rows[0].Role.Should().Be("user");
        rows[0].Content.Should().Be("hello");
        rows[0].InputTokens.Should().Be(10);
        rows[0].CacheReadTokens.Should().Be(5);
        rows[0].CacheWriteTokens.Should().Be(0);
        rows[0].OutputTokens.Should().Be(2);
        rows[0].CostUsd.Should().Be(0.01m);
        rows[0].OccurredAt.Should().Be(OccurredAt);
        rows[0].Version.Should().Be(1);
        rows[1].Sequence.Should().Be(2);
        rows[1].Role.Should().Be("assistant");
        rows[1].Content.Should().Be("world");
        rows[1].InputTokens.Should().Be(20);
        rows[1].CostUsd.Should().Be(0.02m);
    }

    [Fact]
    public async Task ReupsertingSameMessages_KeepsOneRowPerSequence()
    {
        AgentRunUpserter upserter = new(Context);
        AgentMessageUpserter messageUpserter = new(Context);
        AgentRun run = TestAgentRunBuilder.Init(Db).WithAgentId("").BuildWithoutDatabase();
        Guid storedId = (await upserter.UpsertAsync(run, CancellationToken.None)).StoredId;
        AgentMessage[] messages =
        [
            AgentMessage.Create(storedId, 1, "user", "hello", null, null, 10, 0, 0, 0, OccurredAt),
            AgentMessage.Create(storedId, 2, "assistant", "world", null, null, 20, 0, 0, 0, OccurredAt)
        ];

        await messageUpserter.UpsertAsync(storedId, messages, CancellationToken.None);
        await messageUpserter.UpsertAsync(storedId, messages, CancellationToken.None);

        List<AgentMessage> rows = await Context.AgentMessages.AsNoTracking().OrderBy(m => m.Sequence).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(m => m.Sequence).Should().Equal(1, 2);
    }
}
