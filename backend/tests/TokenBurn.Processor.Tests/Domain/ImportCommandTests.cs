using TokenBurn.Common.Primitives;
using TokenBurn.Processor.Domain;
using TokenBurn.Testing.Common.Assertions;

namespace TokenBurn.Processor.Tests.Domain;

public sealed class ImportCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_DefaultsToQueuedZeroAttempts()
    {
        ImportCommand command = ImportCommand.Create("claude-code-transcript", "{}", Now);

        command.Status.Should().Be(ImportCommandStatus.Queued);
        command.Attempts.Should().Be(0);
        command.CreatedAt.Should().Be(Now);
        command.HandlingStartedAt.Should().BeNull();
        command.CooldownUntil.Should().BeNull();
        command.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void TryStart_WhenQueuedAndNoCooldown_MovesToRunning()
    {
        ImportCommand command = ImportCommand.Create("claude-code-transcript", "{}", Now);

        Result result = command.TryStart(Now);

        result.AssertSuccess();
        command.Status.Should().Be(ImportCommandStatus.Running);
        command.HandlingStartedAt.Should().Be(Now);
    }

    [Fact]
    public void TryStart_Rejected_WhenRunning()
    {
        ImportCommand command = ImportCommand.Create("claude-code-transcript", "{}", Now);
        command.TryStart(Now).AssertSuccess();

        Result result = command.TryStart(Now);

        result.AssertConflict();
        command.Status.Should().Be(ImportCommandStatus.Running);
    }

    [Fact]
    public void TryStart_Rejected_WhenCooldownInFuture()
    {
        ImportCommand command = ImportCommand.Create("claude-code-transcript", "{}", Now);
        command.TryStart(Now).AssertSuccess();
        command.TryFail(Now, "boom", maxAttempts: 3, backoff: TimeSpan.FromMinutes(1)).AssertSuccess();

        Result result = command.TryStart(Now.AddMinutes(1).AddSeconds(-1));

        result.AssertConflict();
        command.Status.Should().Be(ImportCommandStatus.Queued);
    }

    [Fact]
    public void TryComplete_RequiresRunning()
    {
        ImportCommand command = ImportCommand.Create("claude-code-transcript", "{}", Now);

        Result result = command.TryComplete(Now);

        result.AssertConflict();
        command.Status.Should().Be(ImportCommandStatus.Queued);

        command.TryStart(Now).AssertSuccess();
        command.TryComplete(Now).AssertSuccess();

        command.Status.Should().Be(ImportCommandStatus.Completed);
        command.CompletedAt.Should().Be(Now);
        command.HandlingStartedAt.Should().BeNull();
    }

    [Fact]
    public void TryFail_BelowMax_ReturnsToQueuedWithCooldown()
    {
        ImportCommand command = ImportCommand.Create("claude-code-transcript", "{}", Now);
        command.TryStart(Now).AssertSuccess();
        var backoff = TimeSpan.FromMinutes(5);

        Result result = command.TryFail(Now, "boom", maxAttempts: 3, backoff);

        result.AssertSuccess();
        command.Attempts.Should().Be(1);
        command.LastError.Should().Be("boom");
        command.CooldownUntil.Should().Be(Now.Add(backoff));
        command.Status.Should().Be(ImportCommandStatus.Queued);
        command.HandlingStartedAt.Should().BeNull();
    }

    [Fact]
    public void TryFail_AtMax_MarksFailed()
    {
        ImportCommand command = ImportCommand.Create("claude-code-transcript", "{}", Now);
        command.TryStart(Now).AssertSuccess();

        Result result = command.TryFail(Now, "boom", maxAttempts: 1, backoff: TimeSpan.FromMinutes(1));

        result.AssertSuccess();
        command.Status.Should().Be(ImportCommandStatus.Failed);
        command.Attempts.Should().Be(1);
        command.CompletedAt.Should().Be(Now);
        command.CooldownUntil.Should().BeNull();
    }

    [Fact]
    public void TryFail_OnlyFromRunning()
    {
        ImportCommand command = ImportCommand.Create("claude-code-transcript", "{}", Now);

        Result result = command.TryFail(Now, "boom", maxAttempts: 1, backoff: TimeSpan.FromMinutes(1));

        result.AssertConflict();
        command.Attempts.Should().Be(0);
        command.Status.Should().Be(ImportCommandStatus.Queued);
    }
}
