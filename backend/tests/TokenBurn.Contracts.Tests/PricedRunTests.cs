using TokenBurn.Contracts;

namespace TokenBurn.Contracts.Tests;

public sealed class PricedRunTests
{
    [Fact]
    public void CarriesPricingFields_DistinctFromEnvelope()
    {
        var priced = CreatePricedRun();

        Assert.NotNull(typeof(PricedRun).GetProperty(nameof(PricedRun.PricingStatus)));
        Assert.NotNull(typeof(PricedRun).GetProperty(nameof(PricedRun.CostUsd)));
        Assert.NotNull(typeof(PricedRun).GetProperty(nameof(PricedRun.ReportedCostUsd)));
        Assert.NotNull(typeof(PricedRun).GetProperty(nameof(PricedRun.PriceMultiplier)));
        Assert.NotNull(typeof(PricedRun).GetProperty(nameof(PricedRun.Version)));
    }

    [Fact]
    public void DefaultsAgentIdToEmptyString()
    {
        var priced = CreatePricedRun();

        Assert.Equal("", priced.AgentId);
    }

    [Fact]
    public void CarriesAllEnvelopeFields()
    {
        var started = new DateTimeOffset(2026, 7, 24, 21, 57, 51, TimeSpan.Zero);
        var priced = CreatePricedRun() with
        {
            SessionId = "sess-1",
            Source = "delegate-ledger",
            ExternalId = "ext-1",
            StartedAt = started,
            InputTokens = 100,
            CacheReadTokens = 200,
            CacheWriteTokens = 300,
            OutputTokens = 400,
            CostUsd = 0.5m,
            ReportedCostUsd = 0.4m,
            PriceMultiplier = 2m,
            Version = 3
        };

        Assert.Equal("sess-1", priced.SessionId);
        Assert.Equal("delegate-ledger", priced.Source);
        Assert.Equal("ext-1", priced.ExternalId);
        Assert.Equal(started, priced.StartedAt);
        Assert.Equal(100, priced.InputTokens);
        Assert.Equal(200, priced.CacheReadTokens);
        Assert.Equal(300, priced.CacheWriteTokens);
        Assert.Equal(400, priced.OutputTokens);
        Assert.Equal(0.5m, priced.CostUsd);
        Assert.Equal(0.4m, priced.ReportedCostUsd);
        Assert.Equal(2m, priced.PriceMultiplier);
        Assert.Equal(3, priced.Version);
    }

    [Fact]
    public void ExposesNoPersistenceAggregate()
    {
        Assert.Null(typeof(PricedRun).GetMethod("TryMarkPriced"));
        Assert.Null(typeof(PricedRun).GetMethod("TryTransitionTo"));
    }

    private static PricedRun CreatePricedRun()
        => new()
        {
            Id = Guid.NewGuid(),
            SessionId = "sess-1",
            Source = "delegate-ledger",
            Status = RunStatus.Completed,
            PricingStatus = PricingStatus.Priced
        };
}
