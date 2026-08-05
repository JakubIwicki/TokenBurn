using TokenBurn.Processor.Domain;
using ContractsRunStatus = TokenBurn.Contracts.RunStatus;
using ContractsPricingStatus = TokenBurn.Contracts.PricingStatus;

namespace TokenBurn.Processor.Tests.Domain;

public sealed class PricedRunMapperTests
{
    [Fact]
    public void CarriesAllFields_FromAggregateToTransport()
    {
        Guid parent = Guid.NewGuid();
        var started = new DateTimeOffset(2026, 7, 24, 21, 57, 51, 995, TimeSpan.Zero);
        var ended = new DateTimeOffset(2026, 7, 24, 21, 58, 11, 470, TimeSpan.Zero);
        AgentRun run = AgentRun.Create(
            "sess-1", "agent-9", "claude-code-transcript", "ext-1", "csharp-coder",
            "deepseek-v4-flash", RunStatus.Completed, started, ended, 1, 2, 3, 4, 0.4m,
            service: "deepseek", workspace: "/home/jakub", parentRunId: parent);
        run.TryMarkPriced(0.5m, 2m);

        TokenBurn.Contracts.PricedRun priced = PricedRunMapper.ToPricedRun(run);

        priced.Id.Should().Be(run.Id);
        priced.SessionId.Should().Be("sess-1");
        priced.AgentId.Should().Be("agent-9");
        priced.Source.Should().Be("claude-code-transcript");
        priced.ExternalId.Should().Be("ext-1");
        priced.ParentRunId.Should().Be(parent);
        priced.Workspace.Should().Be("/home/jakub");
        priced.Persona.Should().Be("csharp-coder");
        priced.ModelSlug.Should().Be("deepseek-v4-flash");
        priced.Service.Should().Be("deepseek");
        priced.Status.Should().Be(ContractsRunStatus.Completed);
        priced.StartedAt.Should().Be(started);
        priced.EndedAt.Should().Be(ended);
        priced.InputTokens.Should().Be(1);
        priced.CacheReadTokens.Should().Be(2);
        priced.CacheWriteTokens.Should().Be(3);
        priced.OutputTokens.Should().Be(4);
        priced.PricingStatus.Should().Be(ContractsPricingStatus.Priced);
        priced.CostUsd.Should().Be(0.5m);
        priced.ReportedCostUsd.Should().Be(0.4m);
        priced.PriceMultiplier.Should().Be(2m);
        priced.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(RunStatus.Running, ContractsRunStatus.Running)]
    [InlineData(RunStatus.Completed, ContractsRunStatus.Completed)]
    [InlineData(RunStatus.Failed, ContractsRunStatus.Failed)]
    [InlineData(RunStatus.Cancelled, ContractsRunStatus.Cancelled)]
    [InlineData(RunStatus.Unknown, ContractsRunStatus.Unknown)]
    public void MapsRunStatus_ToTransportStatus(RunStatus domain, ContractsRunStatus expected)
    {
        PricedRunMapper.ToRunStatus(domain).Should().Be(expected);
    }

    [Theory]
    [InlineData(PricingStatus.Priced, ContractsPricingStatus.Priced)]
    [InlineData(PricingStatus.Quarantined, ContractsPricingStatus.Quarantined)]
    [InlineData(PricingStatus.Unpriceable, ContractsPricingStatus.Unpriceable)]
    public void MapsPricingStatus_ToTransportStatus(PricingStatus domain, ContractsPricingStatus expected)
    {
        PricedRunMapper.ToPricingStatus(domain).Should().Be(expected);
    }

    [Fact]
    public void MapsQuarantined_WhenUnpriced()
    {
        AgentRun run = AgentRun.Create(
            "sess-1", "", "delegate-ledger", null, null, "deepseek-v4-pro[1m]",
            RunStatus.Completed, null, null, 1, 0, 0, 0, null);

        var priced = PricedRunMapper.ToPricedRun(run);

        priced.PricingStatus.Should().Be(ContractsPricingStatus.Quarantined);
        priced.CostUsd.Should().BeNull();
    }
}
