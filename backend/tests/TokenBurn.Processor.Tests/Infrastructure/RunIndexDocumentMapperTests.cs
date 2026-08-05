using TokenBurn.Contracts;
using TokenBurn.Processor.Infrastructure.Indexing;

namespace TokenBurn.Processor.Tests.Infrastructure;

public sealed class RunIndexDocumentMapperTests
{
    [Fact]
    public void MapsAllFields_FromPricedRun()
    {
        Guid id = Guid.NewGuid();
        Guid parent = Guid.NewGuid();
        var started = new DateTimeOffset(2026, 7, 24, 21, 57, 51, TimeSpan.Zero);
        var ended = started.AddMinutes(2);
        PricedRun priced = new()
        {
            Id = id,
            SessionId = "sess-1",
            AgentId = "agent-9",
            Source = "delegate-ledger",
            ExternalId = "ext-1",
            ParentRunId = parent,
            Workspace = "/home/jakub/JjChat",
            Persona = "explore",
            ModelSlug = "deepseek-v4-flash",
            Service = "deepseek",
            Status = RunStatus.Completed,
            StartedAt = started,
            EndedAt = ended,
            InputTokens = 53695,
            CacheReadTokens = 631936,
            CacheWriteTokens = 0,
            OutputTokens = 10219,
            PricingStatus = PricingStatus.Priced,
            CostUsd = 0.0121480408m,
            ReportedCostUsd = 0.0121480408m,
            PriceMultiplier = 1m,
            Version = 1
        };

        RunIndexDocument doc = RunIndexDocumentMapper.FromPricedRun(priced);

        doc.Id.Should().Be(id);
        doc.SessionId.Should().Be("sess-1");
        doc.AgentId.Should().Be("agent-9");
        doc.Source.Should().Be("delegate-ledger");
        doc.ExternalId.Should().Be("ext-1");
        doc.ParentRunId.Should().Be(parent);
        doc.Workspace.Should().Be("/home/jakub/JjChat");
        doc.Persona.Should().Be("explore");
        doc.ModelSlug.Should().Be("deepseek-v4-flash");
        doc.Service.Should().Be("deepseek");
        doc.Status.Should().Be("Completed");
        doc.PricingStatus.Should().Be("Priced");
        doc.StartedAt.Should().Be(started);
        doc.EndedAt.Should().Be(ended);
        doc.InputTokens.Should().Be(53695);
        doc.CacheReadTokens.Should().Be(631936);
        doc.CacheWriteTokens.Should().Be(0);
        doc.OutputTokens.Should().Be(10219);
        doc.CostUsd.Should().Be(0.0121480408m);
        doc.ReportedCostUsd.Should().Be(0.0121480408m);
        doc.PriceMultiplier.Should().Be(1m);
        doc.Version.Should().Be(1);
    }

    [Fact]
    public void SearchableText_JoinsWorkspacePersonaExternalIdSessionId()
    {
        PricedRun priced = new()
        {
            Id = Guid.NewGuid(),
            SessionId = "sess-1",
            Source = "delegate-ledger",
            ExternalId = "20260801-201957",
            Workspace = "/home/jakub/JjChat",
            Persona = "explore",
            Status = RunStatus.Completed,
            PricingStatus = PricingStatus.Priced
        };

        RunIndexDocument doc = RunIndexDocumentMapper.FromPricedRun(priced);

        doc.SearchableText.Should().Be("/home/jakub/JjChat explore 20260801-201957 sess-1");
    }

    [Fact]
    public void SearchableText_ToleratesNullMembers()
    {
        PricedRun priced = new()
        {
            Id = Guid.NewGuid(),
            SessionId = "sess-2",
            Source = "otlp",
            Status = RunStatus.Completed,
            PricingStatus = PricingStatus.Priced
        };

        RunIndexDocument doc = RunIndexDocumentMapper.FromPricedRun(priced);

        doc.SearchableText.Should().Be("   sess-2");
    }

    [Theory]
    [InlineData(RunStatus.Running, "Running")]
    [InlineData(RunStatus.Completed, "Completed")]
    [InlineData(RunStatus.Failed, "Failed")]
    [InlineData(RunStatus.Cancelled, "Cancelled")]
    [InlineData(RunStatus.Unknown, "Unknown")]
    public void ConvertsRunStatus_ToPascalString(RunStatus status, string expected)
    {
        RunIndexDocumentMapper.ToString(status).Should().Be(expected);
    }

    [Theory]
    [InlineData(PricingStatus.Priced, "Priced")]
    [InlineData(PricingStatus.Quarantined, "Quarantined")]
    [InlineData(PricingStatus.Unpriceable, "Unpriceable")]
    public void ConvertsPricingStatus_ToPascalString(PricingStatus status, string expected)
    {
        RunIndexDocumentMapper.ToString(status).Should().Be(expected);
    }
}
