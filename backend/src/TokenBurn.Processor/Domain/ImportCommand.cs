using TokenBurn.Common.Primitives;

namespace TokenBurn.Processor.Domain;

public enum ImportCommandStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

/// <summary>
///     A durable, replayable import command: a payload (path, since, ...) executed
///     exactly-once per claim by <c>ImportCommandWorker</c>. The worker enforces the
///     claim/completion transitions atomically in SQL under a <c>status='Running'</c>
///     ownership guard; the guarded methods here are the spec-of-record for those
///     transitions, exercised by the domain tests and used when the aggregate is
///     materialized through EF.
/// </summary>
public sealed class ImportCommand : BaseEntity<Guid>
{
    public string Type { get; private init; } = null!;
    public string? Payload { get; private init; }
    public ImportCommandStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? HandlingStartedAt { get; private set; }
    public DateTimeOffset? CooldownUntil { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private ImportCommand() { }

    public static ImportCommand Create(string type, string? payloadJson, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return new ImportCommand
        {
            Id = Guid.NewGuid(),
            Type = type,
            Payload = payloadJson,
            Status = ImportCommandStatus.Queued,
            Attempts = 0,
            CreatedAt = now
        };
    }

    /// <summary>Queued→Running; refused while the command is not Queued or is in cooldown.</summary>
    public Result TryStart(DateTimeOffset now)
    {
        if (Status != ImportCommandStatus.Queued)
            return Result.Conflict($"Cannot start command in '{Status}' state.");
        if (CooldownUntil is { } cooldown && cooldown > now)
            return Result.Conflict($"Command is in cooldown until {cooldown:o}.");

        Status = ImportCommandStatus.Running;
        HandlingStartedAt = now;
        return Result.Success();
    }

    /// <summary>Running→Completed; refused from any other state.</summary>
    public Result TryComplete(DateTimeOffset now)
    {
        if (Status != ImportCommandStatus.Running)
            return Result.Conflict($"Cannot complete command in '{Status}' state.");

        Status = ImportCommandStatus.Completed;
        CompletedAt = now;
        HandlingStartedAt = null;
        return Result.Success();
    }

    /// <summary>
    ///     Running→Queued+cooldown (retry) or Running→Failed (terminal). The worker applies
    ///     exponential backoff between retries; the single <paramref name="backoff"/> here is
    ///     the linear spec used by the domain tests and EF-materialized aggregates.
    /// </summary>
    public Result TryFail(DateTimeOffset now, string error, int maxAttempts, TimeSpan backoff)
    {
        if (Status != ImportCommandStatus.Running)
            return Result.Conflict($"Cannot fail command in '{Status}' state.");

        Attempts += 1;
        LastError = error;
        HandlingStartedAt = null;
        if (Attempts >= maxAttempts)
        {
            Status = ImportCommandStatus.Failed;
            CompletedAt = now;
        }
        else
        {
            Status = ImportCommandStatus.Queued;
            CooldownUntil = now.Add(backoff);
        }
        return Result.Success();
    }
}
