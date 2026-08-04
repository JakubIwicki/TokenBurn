using Microsoft.Extensions.Time.Testing;
using TokenBurn.Common.Primitives;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Assertions;
using TokenBurn.Testing.Common.Builders;

namespace TokenBurn.Processor.Tests.Domain;

public sealed class AgentRunTests : TelemetryHandlerTestBase
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void CreatesRun_WithQuarantinedPricing_ByDefault()
    {
        Db.Query<AgentRun>().Should().BeEmpty();
        var run = TestAgentRunBuilder.Init(Db).Running().Build();

        var persisted = Db.FindFresh<AgentRun>(run.Id);

        persisted.Should().NotBeNull();
        persisted!.PricingStatus.Should().Be(PricingStatus.Quarantined);
        persisted.Version.Should().Be(1);
    }

    [Fact]
    public void PersistsTokenCounters_WhenCreatingRun()
    {
        Db.Query<AgentRun>().Should().BeEmpty();
        var run = TestAgentRunBuilder.Init(Db).Running().WithCacheWriteTokens(300).Build();

        var persisted = Db.FindFresh<AgentRun>(run.Id);

        persisted.Should().NotBeNull();
        persisted!.InputTokens.Should().Be(100);
        persisted.CacheReadTokens.Should().Be(0);
        persisted.CacheWriteTokens.Should().Be(300);
        persisted.OutputTokens.Should().Be(50);
    }

    [Fact]
    public void Transitions_FromRunningToCompleted()
    {
        var run = TestAgentRunBuilder.Init(Db).Running().Build();
        DateTimeOffset now = Clock.GetUtcNow();

        var persisted = Db.FindFresh<AgentRun>(run.Id)!;
        Result result = persisted.TryTransitionTo(RunStatus.Completed, now);

        result.AssertSuccess();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.Status.Should().Be(RunStatus.Completed);
        reloaded.EndedAt.Should().Be(now);
    }

    [Fact]
    public void ReturnsConflict_WhenTransitioningFromTerminalToRunning()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var run = TestAgentRunBuilder.Init(Db).Completed(now).Build();

        var persisted = Db.FindFresh<AgentRun>(run.Id)!;
        Result result = persisted.TryTransitionTo(RunStatus.Running, now);

        result.AssertConflict();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.Status.Should().Be(RunStatus.Completed);
    }

    [Fact]
    public void ReturnsConflict_WhenTransitioningTerminalToDifferentTerminal()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var run = TestAgentRunBuilder.Init(Db).Completed(now).Build();

        var persisted = Db.FindFresh<AgentRun>(run.Id)!;
        Result result = persisted.TryTransitionTo(RunStatus.Failed, now);

        result.AssertConflict();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.Status.Should().Be(RunStatus.Completed);
    }

    [Fact]
    public void Succeeds_WhenTransitioningToSameStatus()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var run = TestAgentRunBuilder.Init(Db).Completed(now).Build();

        var persisted = Db.FindFresh<AgentRun>(run.Id)!;
        Result result = persisted.TryTransitionTo(RunStatus.Completed, now);

        result.AssertSuccess();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.Status.Should().Be(RunStatus.Completed);
        reloaded.EndedAt.Should().Be(now);
    }

    [Theory]
    [InlineData(RunStatus.Unknown)]
    [InlineData(RunStatus.Running)]
    public void AcceptsAnyNonTerminalOrigin_ForTerminalTarget(RunStatus origin)
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var run = TestAgentRunBuilder.Init(Db).WithStatus(origin).Build();

        var persisted = Db.FindFresh<AgentRun>(run.Id)!;
        Result result = persisted.TryTransitionTo(RunStatus.Completed, now);

        result.AssertSuccess();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.Status.Should().Be(RunStatus.Completed);
        reloaded.EndedAt.Should().Be(now);
    }

    [Fact]
    public void MarksPriced_WhenQuarantined()
    {
        var run = TestAgentRunBuilder.Init(Db).Running().Build();

        var persisted = Db.FindFresh<AgentRun>(run.Id)!;
        Result result = persisted.TryMarkPriced(1.23m, 1.0m);

        result.AssertSuccess();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.PricingStatus.Should().Be(PricingStatus.Priced);
        reloaded.CostUsd.Should().Be(1.23m);
    }

    [Fact]
    public void ReturnsConflict_WhenMarkingPriced_AlreadyPriced()
    {
        var run = TestAgentRunBuilder.Init(Db).Running().Build();
        Db.FindFresh<AgentRun>(run.Id)!.TryMarkPriced(1.23m, 1.0m).AssertSuccess();
        Db.SaveChanges();

        var persisted = Db.FindFresh<AgentRun>(run.Id)!;
        Result result = persisted.TryMarkPriced(2.50m, 1.0m);

        result.AssertConflict();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.PricingStatus.Should().Be(PricingStatus.Priced);
        reloaded.CostUsd.Should().Be(1.23m);
    }
}
