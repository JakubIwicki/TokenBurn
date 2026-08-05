using TokenBurn.Processor.Domain;
using ContractsRunStatus = TokenBurn.Contracts.RunStatus;
using NormalizedRun = TokenBurn.Contracts.NormalizedRun;

namespace TokenBurn.Processor.Tests.Domain;

public sealed class AgentRunEnvelopeMapperTests
{
    [Fact]
    public void CarriesAllFields_FromEnvelopeToAggregate()
    {
        Guid parent = Guid.NewGuid();
        var started = new DateTimeOffset(2026, 7, 24, 21, 57, 51, 995, TimeSpan.Zero);
        var ended = new DateTimeOffset(2026, 7, 24, 21, 58, 11, 470, TimeSpan.Zero);
        var envelope = new NormalizedRun
        {
            SessionId = "sess-1",
            AgentId = "agent-9",
            Source = "claude-code-transcript",
            ExternalId = "ext-1",
            ParentRunId = parent,
            Workspace = "/home/jakub",
            Persona = "csharp-coder",
            ModelSlug = "deepseek-v4-flash",
            Service = "deepseek",
            Status = ContractsRunStatus.Failed,
            StartedAt = started,
            EndedAt = ended,
            InputTokens = 1,
            CacheReadTokens = 2,
            CacheWriteTokens = 3,
            OutputTokens = 4,
            ReportedCostUsd = 0.1234m
        };

        AgentRun run = AgentRunEnvelopeMapper.ToAgentRun(envelope);

        run.SessionId.Should().Be("sess-1");
        run.AgentId.Should().Be("agent-9");
        run.Source.Should().Be("claude-code-transcript");
        run.ExternalId.Should().Be("ext-1");
        run.ParentRunId.Should().Be(parent);
        run.Workspace.Should().Be("/home/jakub");
        run.Persona.Should().Be("csharp-coder");
        run.ModelSlug.Should().Be("deepseek-v4-flash");
        run.Service.Should().Be("deepseek");
        run.Status.Should().Be(RunStatus.Failed);
        run.StartedAt.Should().Be(started);
        run.EndedAt.Should().Be(ended);
        run.InputTokens.Should().Be(1);
        run.CacheReadTokens.Should().Be(2);
        run.CacheWriteTokens.Should().Be(3);
        run.OutputTokens.Should().Be(4);
        run.ReportedCostUsd.Should().Be(0.1234m);
        run.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(ContractsRunStatus.Running, RunStatus.Running)]
    [InlineData(ContractsRunStatus.Completed, RunStatus.Completed)]
    [InlineData(ContractsRunStatus.Failed, RunStatus.Failed)]
    [InlineData(ContractsRunStatus.Cancelled, RunStatus.Cancelled)]
    [InlineData(ContractsRunStatus.Unknown, RunStatus.Unknown)]
    public void MapsContractStatus_ToDomainStatus(ContractsRunStatus contractStatus, RunStatus expected)
    {
        var status = AgentRunEnvelopeMapper.ToRunStatus(contractStatus);

        status.Should().Be(expected);
    }

    [Fact]
    public void DefaultsAgentIdToEmpty_WhenBlank()
    {
        var envelope = new NormalizedRun
        {
            SessionId = "sess-1",
            Source = "delegate-ledger",
            Status = ContractsRunStatus.Running
        };

        AgentRun run = AgentRunEnvelopeMapper.ToAgentRun(envelope);

        run.AgentId.Should().Be("");
    }
}
