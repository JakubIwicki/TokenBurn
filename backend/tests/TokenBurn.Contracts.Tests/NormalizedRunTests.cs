using System.Reflection;
using System.Runtime.CompilerServices;
using TokenBurn.Contracts;

namespace TokenBurn.Contracts.Tests;

public sealed class NormalizedRunTests
{
    [Fact]
    public void AgentId_DefaultsToEmptyString()
    {
        var run = CreateEnvelope();

        Assert.Equal("", run.AgentId);
    }

    [Fact]
    public void SessionId_IsARequiredMember()
    {
        PropertyInfo sessionId = typeof(NormalizedRun).GetProperty(nameof(NormalizedRun.SessionId))!;
        PropertyInfo agentId = typeof(NormalizedRun).GetProperty(nameof(NormalizedRun.AgentId))!;

        Assert.NotEmpty(sessionId.GetCustomAttributes<RequiredMemberAttribute>());
        Assert.Empty(agentId.GetCustomAttributes<RequiredMemberAttribute>());
    }

    [Fact]
    public void ExposesNoPricingFields()
    {
        string[] pricingFieldNames = ["CostUsd", "PriceMultiplier", "PricingStatus"];

        foreach (string name in pricingFieldNames)
        {
            Assert.Null(typeof(NormalizedRun).GetProperty(name));
        }
    }

    [Fact]
    public void CarriesTheFourTokenCounters()
    {
        var run = CreateEnvelope() with
        {
            InputTokens = 100,
            CacheReadTokens = 200,
            CacheWriteTokens = 300,
            OutputTokens = 400
        };

        Assert.Equal(100, run.InputTokens);
        Assert.Equal(200, run.CacheReadTokens);
        Assert.Equal(300, run.CacheWriteTokens);
        Assert.Equal(400, run.OutputTokens);
    }

    private static NormalizedRun CreateEnvelope()
        => new()
        {
            SessionId = "session-1",
            Source = "delegate-ledger",
            Status = RunStatus.Running
        };
}
