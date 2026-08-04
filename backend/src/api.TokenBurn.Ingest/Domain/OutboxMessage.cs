using TokenBurn.Common.Primitives;

namespace Api.TokenBurn.Ingest.Domain;

public sealed class OutboxMessage : BaseEntity<Guid>
{
    public const int MaxAttempts = 5;

    private OutboxMessage() { }

    public string Topic { get; private init; } = string.Empty;
    public string Key { get; private init; } = string.Empty;
    public string Payload { get; private init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private init; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? DeadLetteredAt { get; private set; }

    public static OutboxMessage Create(string topic, string key, string payload, DateTimeOffset occurredAt)
        => new()
        {
            Id = Guid.NewGuid(), Topic = topic, Key = key, Payload = payload,
            OccurredAt = occurredAt, PublishedAt = null, DeadLetteredAt = null, Attempts = 0
        };

    /// <summary>
    /// Spec-of-record transition for publishing: a message may be marked published once.
    /// OutboxPublisher enforces the same invariant atomically with a conditional
    /// UPDATE ... WHERE published_at IS NULL, so a replica racing on an already-published row
    /// affects zero rows and skips instead of double-publishing.
    /// </summary>
    public Result TryMarkPublished(DateTimeOffset now)
    {
        if (PublishedAt is not null)
            return Result.Conflict("Outbox message is already published.");
        PublishedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Spec-of-record transition for a failed publish attempt: records one more attempt while the
    /// message is still unpublished and not dead-lettered.
    /// OutboxPublisher applies the same guards atomically with the conditional UPDATE ... WHERE
    /// published_at IS NULL AND dead_lettered_at IS NULL, and sets dead_lettered_at once attempts
    /// reach MaxAttempts.
    /// </summary>
    public Result TryIncrementAttempt()
    {
        if (PublishedAt is not null)
            return Result.Conflict("Outbox message is already published.");
        if (DeadLetteredAt is not null)
            return Result.Conflict("Outbox message is already dead-lettered.");
        Attempts++;
        return Result.Success();
    }

    /// <summary>
    /// Spec-of-record transition for dead-lettering: a message may be dead-lettered only while
    /// unpublished and not already dead-lettered.
    /// OutboxPublisher enforces the same invariant atomically via the WHERE clauses on the
    /// attempt-increment UPDATE.
    /// </summary>
    public Result TryDeadLetter(DateTimeOffset now)
    {
        if (PublishedAt is not null)
            return Result.Conflict("Outbox message is already published.");
        if (DeadLetteredAt is not null)
            return Result.Conflict("Outbox message is already dead-lettered.");
        DeadLetteredAt = now;
        return Result.Success();
    }
}
