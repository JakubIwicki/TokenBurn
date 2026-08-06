using Api.TokenBurn.Insights.Features.Ask.Chat;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Api.TokenBurn.Insights.Tests;

public sealed class AskBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AllowsUpToThePerHourLimit()
    {
        FakeTimeProvider clock = new(Start);
        AskBudget budget = new(3);

        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeTrue();
        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeTrue();
        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void Rejects_OnceThePerHourLimitIsReached()
    {
        FakeTimeProvider clock = new(Start);
        AskBudget budget = new(2);

        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeTrue();
        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeTrue();
        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeFalse();
    }

    [Fact]
    public void AllowsAgain_AfterTheWindowExpires()
    {
        FakeTimeProvider clock = new(Start);
        AskBudget budget = new(1);

        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeTrue();
        clock.Advance(TimeSpan.FromMinutes(61));
        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void DoesNotReject_WithinTheWindow()
    {
        FakeTimeProvider clock = new(Start);
        AskBudget budget = new(1);

        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeTrue();
        clock.Advance(TimeSpan.FromMinutes(59));
        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeFalse();
    }

    [Fact]
    public void IsolatesWindows_ByPrincipal()
    {
        FakeTimeProvider clock = new(Start);
        AskBudget budget = new(1);

        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeTrue();
        budget.TryCharge("sub-b", clock, CancellationToken.None).Should().BeTrue();
        budget.TryCharge("sub-a", clock, CancellationToken.None).Should().BeFalse();
    }
}
