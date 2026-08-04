using Api.TokenBurn.Ingest.Domain;
using Api.TokenBurn.Ingest.Tests.Bases;
using Microsoft.Extensions.Time.Testing;
using TokenBurn.Common.Primitives;
using TokenBurn.Testing.Common.Assertions;

namespace Api.TokenBurn.Ingest.Tests.Domain;

public sealed class OutboxMessageTests : IngestHandlerTestBase
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void MarksPublished_WhenUnpublished()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var message = OutboxMessage.Create("telemetry.raw", "session-1", "{}", now);
        Db.Store(message);
        Db.Query<OutboxMessage>().Should().ContainSingle();

        var persisted = Db.FindFresh<OutboxMessage>(message.Id)!;
        Result result = persisted.TryMarkPublished(now);

        result.AssertSuccess();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<OutboxMessage>(message.Id)!;
        reloaded.PublishedAt.Should().Be(now);
    }

    [Fact]
    public void ReturnsConflict_WhenMarkingPublished_AlreadyPublished()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var message = OutboxMessage.Create("telemetry.raw", "session-1", "{}", now);
        Db.Store(message);
        Db.FindFresh<OutboxMessage>(message.Id)!.TryMarkPublished(now).AssertSuccess();
        Db.SaveChanges();

        var persisted = Db.FindFresh<OutboxMessage>(message.Id)!;
        Result result = persisted.TryMarkPublished(now.AddMinutes(1));

        result.AssertConflict();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<OutboxMessage>(message.Id)!;
        reloaded.PublishedAt.Should().Be(now);
    }

    [Fact]
    public void IncrementsAttempt()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var message = OutboxMessage.Create("telemetry.raw", "session-1", "{}", now);
        Db.Store(message);

        var persisted = Db.FindFresh<OutboxMessage>(message.Id)!;
        persisted.TryIncrementAttempt().AssertSuccess();
        persisted.TryIncrementAttempt().AssertSuccess();
        Db.SaveChanges();

        var reloaded = Db.FindFresh<OutboxMessage>(message.Id)!;
        reloaded.Attempts.Should().Be(2);
    }

    [Fact]
    public void IncrementsAttempt_AndPersists()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var message = OutboxMessage.Create("telemetry.raw", "session-1", "{}", now);
        Db.Store(message);

        var persisted = Db.FindFresh<OutboxMessage>(message.Id)!;
        persisted.TryIncrementAttempt().AssertSuccess();
        Db.SaveChanges();

        var reloaded = Db.FindFresh<OutboxMessage>(message.Id)!;
        reloaded.Attempts.Should().Be(1);
    }

    [Fact]
    public void ReturnsConflict_WhenIncrementingAttempt_AlreadyPublished()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var message = OutboxMessage.Create("telemetry.raw", "session-1", "{}", now);
        Db.Store(message);
        Db.FindFresh<OutboxMessage>(message.Id)!.TryMarkPublished(now).AssertSuccess();
        Db.SaveChanges();

        var persisted = Db.FindFresh<OutboxMessage>(message.Id)!;
        Result result = persisted.TryIncrementAttempt();

        result.AssertConflict();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<OutboxMessage>(message.Id)!;
        reloaded.Attempts.Should().Be(0);
        reloaded.PublishedAt.Should().Be(now);
    }

    [Fact]
    public void ReturnsConflict_WhenIncrementingAttempt_AlreadyDeadLettered()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var message = OutboxMessage.Create("telemetry.raw", "session-1", "{}", now);
        Db.Store(message);
        Db.FindFresh<OutboxMessage>(message.Id)!.TryDeadLetter(now).AssertSuccess();
        Db.SaveChanges();

        var persisted = Db.FindFresh<OutboxMessage>(message.Id)!;
        Result result = persisted.TryIncrementAttempt();

        result.AssertConflict();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<OutboxMessage>(message.Id)!;
        reloaded.Attempts.Should().Be(0);
        reloaded.DeadLetteredAt.Should().Be(now);
    }

    [Fact]
    public void DeadLetters_WhenAttemptsReachMax()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var message = OutboxMessage.Create("telemetry.raw", "session-1", "{}", now);
        Db.Store(message);
        var persisted = Db.FindFresh<OutboxMessage>(message.Id)!;

        for (int i = 0; i < OutboxMessage.MaxAttempts; i++)
        {
            persisted.TryIncrementAttempt().AssertSuccess();
        }

        Result result = persisted.TryDeadLetter(now.AddMinutes(1));

        result.AssertSuccess();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<OutboxMessage>(message.Id)!;
        reloaded.Attempts.Should().Be(OutboxMessage.MaxAttempts);
        reloaded.DeadLetteredAt.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void ReturnsConflict_WhenDeadLettering_AlreadyPublished()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var message = OutboxMessage.Create("telemetry.raw", "session-1", "{}", now);
        Db.Store(message);
        Db.FindFresh<OutboxMessage>(message.Id)!.TryMarkPublished(now).AssertSuccess();
        Db.SaveChanges();

        var persisted = Db.FindFresh<OutboxMessage>(message.Id)!;
        Result result = persisted.TryDeadLetter(now.AddMinutes(1));

        result.AssertConflict();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<OutboxMessage>(message.Id)!;
        reloaded.DeadLetteredAt.Should().BeNull();
        reloaded.PublishedAt.Should().Be(now);
    }

    [Fact]
    public void ReturnsConflict_WhenDeadLettering_AlreadyDeadLettered()
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var message = OutboxMessage.Create("telemetry.raw", "session-1", "{}", now);
        Db.Store(message);
        Db.FindFresh<OutboxMessage>(message.Id)!.TryDeadLetter(now).AssertSuccess();
        Db.SaveChanges();

        var persisted = Db.FindFresh<OutboxMessage>(message.Id)!;
        Result result = persisted.TryDeadLetter(now.AddMinutes(1));

        result.AssertConflict();
        Db.SaveChanges();
        var reloaded = Db.FindFresh<OutboxMessage>(message.Id)!;
        reloaded.DeadLetteredAt.Should().Be(now);
    }
}
