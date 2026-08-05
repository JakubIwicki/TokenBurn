using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Common.Primitives;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;

namespace TokenBurn.Processor.Commands;

/// <summary>
///     Drives the durable <c>import_commands</c> lifecycle. Each tick (in its own DI scope)
///     reclaims stale Running rows whose lease expired, atomically claims one eligible Queued
///     row (<c>FOR UPDATE SKIP LOCKED</c> — the cross-instance serialization), dispatches its
///     executor, and lands the terminal state under a <c>status='Running'</c> ownership guard.
///     The claim's RETURNING row is the persistence authority (id, attempts); the in-memory
///     aggregate is materialized through the factory + <c>TryStart</c> so the executor sees a
///     correctly-stateful command, but every retry/fail decision is enforced by the raw SQL
///     guard, never by in-memory values.
/// </summary>
public sealed class ImportCommandWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<ImportCommandWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Import command worker tick failed.");
            }
            await Task.Delay(ReadSettings(configuration).PollDelay, timeProvider, stoppingToken);
        }
    }

    public async Task ProcessPendingAsync(CancellationToken ct)
    {
        WorkerSettings settings = ReadSettings(configuration);
        using IServiceScope scope = scopeFactory.CreateScope();
        TelemetryDbContext db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
        Dictionary<string, IImportCommandExecutor> executors = scope.ServiceProvider
            .GetServices<IImportCommandExecutor>()
            .ToDictionary(executor => executor.CommandType);
        DateTimeOffset now = timeProvider.GetUtcNow();

        await ReclaimStaleAsync(db, now, settings.LeaseTimeout, ct);

        ClaimedCommand? claimed = await ClaimOneAsync(db, now, ct);
        if (claimed is null)
            return;

        if (!executors.TryGetValue(claimed.Type, out IImportCommandExecutor? executor))
        {
            logger.LogWarning("No executor registered for import command type '{Type}'.", claimed.Type);
            await FailAsync(db, claimed.Id, $"No executor for type '{claimed.Type}'.", claimed.Attempts, settings, now, logger, ct);
            return;
        }

        ImportCommand command = ImportCommand.Create(claimed.Type, claimed.Payload, now);
        Result started = command.TryStart(now);
        if (!started.IsSuccess)
            throw new InvalidOperationException($"Cannot start claimed command {claimed.Id}: {started.ErrorMessage}");

        Func<string, CancellationToken, Task> updateProgress = CreateProgressUpdater(db, claimed.Id, timeProvider);

        // The executor only refreshes the lease when it calls updateProgress, which for a
        // file-count-driven executor may not happen for a long stretch (or at all, for a batch
        // smaller than its reporting threshold). A wall-clock heartbeat, running against its own
        // scope/DbContext so it never touches `db` concurrently with the main flow, keeps the
        // lease alive independent of any executor's internal progress cadence.
        using CancellationTokenSource heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task heartbeatTask = RunHeartbeatAsync(scopeFactory, claimed.Id, timeProvider, settings.HeartbeatInterval, heartbeatCts.Token);
        try
        {
            await executor.ExecuteAsync(command, updateProgress, ct);
            await CompleteAsync(db, claimed.Id, timeProvider.GetUtcNow(), logger, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: leave the row Running; the lease reclaim picks it up after LeaseTimeout.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Import command {CommandId} of type '{Type}' failed.", claimed.Id, claimed.Type);
            await FailAsync(db, claimed.Id, exception.Message, claimed.Attempts, settings, timeProvider.GetUtcNow(), logger, ct);
        }
        finally
        {
            heartbeatCts.Cancel();
            await heartbeatTask;
        }
    }

    private static async Task RunHeartbeatAsync(
        IServiceScopeFactory scopeFactory, Guid commandId, TimeProvider timeProvider, TimeSpan interval, CancellationToken ct)
    {
        using PeriodicTimer timer = new(interval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                TelemetryDbContext heartbeatDb = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
                await RefreshLeaseAsync(heartbeatDb, commandId, timeProvider.GetUtcNow(), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The main execution finished (or the host is shutting down); no further heartbeat needed.
        }
    }

    private static async Task RefreshLeaseAsync(TelemetryDbContext db, Guid commandId, DateTimeOffset now, CancellationToken ct)
    {
        // `status='Running'` ownership guard, as on CompleteAsync/FailAsync: a row already
        // reclaimed by another worker must not have its lease extended by the worker that no
        // longer owns it.
        const string sql = """
            UPDATE telemetry.import_commands
            SET handling_started_at=@now
            WHERE id=@id AND status='Running'
            """;
        await db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("id", commandId), new NpgsqlParameter("now", now)], ct);
    }

    private static async Task ReclaimStaleAsync(TelemetryDbContext db, DateTimeOffset now, TimeSpan leaseTimeout, CancellationToken ct)
    {
        const string sql = """
            UPDATE telemetry.import_commands
            SET status='Queued', handling_started_at=NULL
            WHERE status='Running' AND handling_started_at IS NOT NULL AND handling_started_at < @cutoff
            """;
        await db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("cutoff", now - leaseTimeout)], ct);
    }

    private static async Task<ClaimedCommand?> ClaimOneAsync(TelemetryDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            UPDATE telemetry.import_commands AS c
            SET status='Running', handling_started_at=@now
            FROM (SELECT id FROM telemetry.import_commands
                  WHERE status='Queued' AND (cooldown_until IS NULL OR cooldown_until <= @now)
                  ORDER BY created_at ASC LIMIT 1 FOR UPDATE SKIP LOCKED) AS chosen
            WHERE c.id=chosen.id
            RETURNING c.id, c.type, c.payload, c.attempts
            """;

        NpgsqlConnection connection = await OpenConnectionAsync(db, ct);
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("now", now);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new ClaimedCommand(
            reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3));
    }

    private static Func<string, CancellationToken, Task> CreateProgressUpdater(
        TelemetryDbContext db, Guid commandId, TimeProvider timeProvider)
        => async (progressJson, token) =>
        {
            // jsonb `||` merges the progress object into the payload; both jsonb operands are bound
            // as NpgsqlDbType.Jsonb parameters (a literal `'{}'` in the raw SQL would be passed
            // through string.Format and break), and COALESCE guards a NULL payload (jsonb `||` with
            // a NULL operand yields NULL and would drop the progress).
            const string sql = """
                UPDATE telemetry.import_commands
                SET payload = COALESCE(payload, @empty) || @progress,
                    handling_started_at = @now
                WHERE id = @id
                """;
            await db.Database.ExecuteSqlRawAsync(sql, [
                new NpgsqlParameter("id", commandId),
                new NpgsqlParameter("empty", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = "{}" },
                new NpgsqlParameter("progress", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = progressJson },
                new NpgsqlParameter("now", timeProvider.GetUtcNow())
            ], token);
        };

    private static async Task CompleteAsync(
        TelemetryDbContext db, Guid commandId, DateTimeOffset now, ILogger<ImportCommandWorker> logger, CancellationToken ct)
    {
        // `status='Running'` ownership guard: a row reclaimed (lease expired) and re-claimed by
        // another worker must never be completed by the worker that no longer owns it.
        const string sql = """
            UPDATE telemetry.import_commands
            SET status='Completed', completed_at=@now, handling_started_at=NULL
            WHERE id=@id AND status='Running'
            """;
        int rowsAffected = await db.Database.ExecuteSqlRawAsync(
            sql, [new NpgsqlParameter("id", commandId), new NpgsqlParameter("now", now)], ct);
        if (rowsAffected == 0)
            logger.LogWarning(
                "Import command {CommandId} finished but could not be marked Completed: its lease was lost to another worker.",
                commandId);
    }

    private static async Task FailAsync(
        TelemetryDbContext db, Guid commandId, string error, int attempts, WorkerSettings settings, DateTimeOffset now,
        ILogger<ImportCommandWorker> logger, CancellationToken ct)
    {
        int newAttempts = attempts + 1;
        bool terminal = newAttempts >= settings.MaxAttempts;
        DateTimeOffset? cooldownUntil = terminal ? null : now.Add(ExponentialBackoff(newAttempts, settings.BackoffBase, settings.MaxBackoff));
        DateTimeOffset? completedAt = terminal ? now : null;

        // `status='Running'` ownership guard, as on CompleteAsync: a reclaimed row must not have
        // its attempts inflated or be terminalized by a worker that no longer owns it.
        const string sql = """
            UPDATE telemetry.import_commands
            SET attempts=attempts+1, last_error=@error, handling_started_at=NULL,
                status=@status,
                cooldown_until=@cooldown_until,
                completed_at=@completed_at
            WHERE id=@id AND status='Running'
            """;
        int rowsAffected = await db.Database.ExecuteSqlRawAsync(sql, [
            new NpgsqlParameter("id", commandId),
            new NpgsqlParameter("error", error),
            new NpgsqlParameter("status", terminal ? "Failed" : "Queued"),
            new NpgsqlParameter("cooldown_until", (object?)cooldownUntil ?? DBNull.Value),
            new NpgsqlParameter("completed_at", (object?)completedAt ?? DBNull.Value)
        ], ct);
        if (rowsAffected == 0)
            logger.LogWarning(
                "Import command {CommandId} failed but could not be marked {Status}: its lease was lost to another worker.",
                commandId, terminal ? "Failed" : "Queued");
    }

    private static TimeSpan ExponentialBackoff(int attempt, TimeSpan backoffBase, TimeSpan maxBackoff)
    {
        TimeSpan backoff = backoffBase;
        for (int i = 1; i < attempt; i++)
        {
            if (backoff >= maxBackoff)
                break;
            backoff += backoff;
        }
        return backoff > maxBackoff ? maxBackoff : backoff;
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync(TelemetryDbContext db, CancellationToken ct)
    {
        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
        return connection;
    }

    private static WorkerSettings ReadSettings(IConfiguration configuration) => new(
        configuration.GetValue("ImportCommand:MaxAttempts", 3),
        configuration.GetValue("ImportCommand:BackoffBase", TimeSpan.FromMinutes(1)),
        configuration.GetValue("ImportCommand:MaxBackoff", TimeSpan.FromMinutes(30)),
        configuration.GetValue("ImportCommand:LeaseTimeout", TimeSpan.FromMinutes(30)),
        configuration.GetValue("ImportCommand:PollDelay", TimeSpan.FromSeconds(5)),
        configuration.GetValue("ImportCommand:HeartbeatInterval", TimeSpan.FromMinutes(5)));

    private sealed record WorkerSettings(
        int MaxAttempts, TimeSpan BackoffBase, TimeSpan MaxBackoff, TimeSpan LeaseTimeout, TimeSpan PollDelay, TimeSpan HeartbeatInterval);

    private sealed record ClaimedCommand(Guid Id, string Type, string? Payload, int Attempts);
}
