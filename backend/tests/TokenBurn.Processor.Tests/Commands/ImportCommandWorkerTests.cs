using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TokenBurn.Processor.Commands;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Assertions;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Commands;

public sealed class ImportCommandWorkerTests : TelemetryHandlerTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaimsQueuedCommand_Executes_AndCompletes()
    {
        await SeedCommandAsync("claude-code-transcript", """{"path":"/tmp/transcripts"}""", ImportCommandStatus.Queued, Now);
        var clock = new FakeTimeProvider(Now);
        var executor = new FakeImportExecutor("claude-code-transcript", callProgress: true);

        ImportCommandWorker worker = CreateWorker(clock, executor);
        await worker.ProcessPendingAsync(CancellationToken.None);

        ImportCommand row = LoadSingle("claude-code-transcript");
        row.Status.Should().Be(ImportCommandStatus.Completed);
        row.CompletedAt.Should().Be(Now);
        row.HandlingStartedAt.Should().BeNull();
        row.Payload.Should().Contain("\"progress\"");
        executor.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SkipsCommand_WhoseCooldownIsInFuture()
    {
        await SeedCommandAsync("claude-code-transcript", "{}", ImportCommandStatus.Queued, Now, cooldownUntil: Now.AddMinutes(15));
        var clock = new FakeTimeProvider(Now);
        var executor = new FakeImportExecutor("claude-code-transcript");

        ImportCommandWorker worker = CreateWorker(clock, executor);
        await worker.ProcessPendingAsync(CancellationToken.None);

        ImportCommand row = LoadSingle("claude-code-transcript");
        row.Status.Should().Be(ImportCommandStatus.Queued);
        row.HandlingStartedAt.Should().BeNull();
        executor.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ClaimsCommand_OnceCooldownElapses()
    {
        await SeedCommandAsync("claude-code-transcript", "{}", ImportCommandStatus.Queued, Now, cooldownUntil: Now.AddMinutes(10));
        var clock = new FakeTimeProvider(Now);
        var executor = new FakeImportExecutor("claude-code-transcript");

        ImportCommandWorker worker = CreateWorker(clock, executor);
        await worker.ProcessPendingAsync(CancellationToken.None);
        executor.CallCount.Should().Be(0);

        clock.Advance(TimeSpan.FromMinutes(11));
        await worker.ProcessPendingAsync(CancellationToken.None);

        ImportCommand row = LoadSingle("claude-code-transcript");
        row.Status.Should().Be(ImportCommandStatus.Completed);
        executor.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ReclaimsStaleRunning_AndReexecutes()
    {
        await SeedCommandAsync("claude-code-transcript", "{}", ImportCommandStatus.Running, Now.AddHours(-1), handlingStartedAt: Now.AddMinutes(-31));
        await SeedCommandAsync("fresh-running", "{}", ImportCommandStatus.Running, Now.AddMinutes(-30), handlingStartedAt: Now.AddMinutes(-5));
        var clock = new FakeTimeProvider(Now);
        var executor = new FakeImportExecutor("claude-code-transcript");

        ImportCommandWorker worker = CreateWorker(clock, executor);
        await worker.ProcessPendingAsync(CancellationToken.None);

        ImportCommand reclaimed = LoadSingle("claude-code-transcript");
        reclaimed.Status.Should().Be(ImportCommandStatus.Completed);
        reclaimed.HandlingStartedAt.Should().BeNull();

        ImportCommand fresh = LoadSingle("fresh-running");
        fresh.Status.Should().Be(ImportCommandStatus.Running);
        fresh.HandlingStartedAt.Should().Be(Now.AddMinutes(-5));
        executor.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RetriesWithBackoff_OnFailure()
    {
        await SeedCommandAsync("claude-code-transcript", "{}", ImportCommandStatus.Queued, Now);
        var clock = new FakeTimeProvider(Now);
        var executor = new FakeImportExecutor("claude-code-transcript", toThrow: new InvalidOperationException("boom"));

        ImportCommandWorker worker = CreateWorker(clock, executor);
        await worker.ProcessPendingAsync(CancellationToken.None);

        ImportCommand row = LoadSingle("claude-code-transcript");
        row.Status.Should().Be(ImportCommandStatus.Queued);
        row.Attempts.Should().Be(1);
        row.CooldownUntil.Should().Be(Now.AddMinutes(1));
        row.HandlingStartedAt.Should().BeNull();
        row.LastError.Should().Be("boom");
    }

    [Fact]
    public async Task MarksFailed_AfterMaxAttempts()
    {
        await SeedCommandAsync("claude-code-transcript", "{}", ImportCommandStatus.Queued, Now);
        var clock = new FakeTimeProvider(Now);
        var executor = new FakeImportExecutor("claude-code-transcript", toThrow: new InvalidOperationException("boom"));
        var config = new Dictionary<string, string?> { ["ImportCommand:MaxAttempts"] = "2" };

        ImportCommandWorker worker = CreateWorker(clock, executor, config);
        await worker.ProcessPendingAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(3));
        await worker.ProcessPendingAsync(CancellationToken.None);

        ImportCommand row = LoadSingle("claude-code-transcript");
        row.Status.Should().Be(ImportCommandStatus.Failed);
        row.Attempts.Should().Be(2);
        row.CompletedAt.Should().Be(clock.GetUtcNow());
        row.CooldownUntil.Should().BeNull();
    }

    [Fact]
    public async Task MarksFailed_WhenTypeUnknown()
    {
        await SeedCommandAsync("no-such-executor", "{}", ImportCommandStatus.Queued, Now);
        var clock = new FakeTimeProvider(Now);
        var executor = new FakeImportExecutor("claude-code-transcript");
        var config = new Dictionary<string, string?> { ["ImportCommand:MaxAttempts"] = "1" };

        ImportCommandWorker worker = CreateWorker(clock, executor, config);
        await worker.ProcessPendingAsync(CancellationToken.None);

        ImportCommand row = LoadSingle("no-such-executor");
        row.Status.Should().Be(ImportCommandStatus.Failed);
        row.Attempts.Should().Be(1);
        row.CompletedAt.Should().Be(Now);
        row.LastError.Should().Contain("no-such-executor");
    }

    [Fact]
    public async Task DoesNotDoubleExecute_WhenRowWasReclaimed()
    {
        await SeedCommandAsync("claude-code-transcript", """{"path":"/tmp/transcripts"}""", ImportCommandStatus.Queued, Now);
        var clock = new FakeTimeProvider(Now);
        // The executor simulates a competing worker that reclaimed the row and re-queued it with a
        // backoff while this execution was in flight: the completion must be a no-op under the
        // `status='Running'` guard, and the cooldown keeps the row from being claimed twice.
        var executor = new FakeImportExecutor("claude-code-transcript", onExecute: () =>
        {
            ImportCommand row = Context.ImportCommands.Single(c => c.Type == "claude-code-transcript");
            row.TryFail(clock.GetUtcNow(), "lease reclaimed by another worker", maxAttempts: 999, backoff: TimeSpan.FromMinutes(10)).AssertSuccess();
            Db.SaveChanges();
        });

        ImportCommandWorker worker = CreateWorker(clock, executor);
        await worker.ProcessPendingAsync(CancellationToken.None);
        executor.CallCount.Should().Be(1);

        ImportCommand row = LoadSingle("claude-code-transcript");
        row.Status.Should().Be(ImportCommandStatus.Queued);
        row.CompletedAt.Should().BeNull();

        await worker.ProcessPendingAsync(CancellationToken.None);
        executor.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshesLease_OnWallClockInterval_IndependentOfProgressCalls()
    {
        await SeedCommandAsync("claude-code-transcript", "{}", ImportCommandStatus.Queued, Now);
        var clock = new FakeTimeProvider(Now);
        var config = new Dictionary<string, string?> { ["ImportCommand:HeartbeatInterval"] = "00:00:01" };
        DateTimeOffset? refreshedHandlingStartedAt = null;
        var executor = new FakeImportExecutor("claude-code-transcript", onExecuteAsync: async () =>
        {
            // A batch with no progress call at all, for far longer than a 25-file boundary would
            // ever take to reach: only the wall-clock heartbeat, not file-count-driven progress,
            // can keep the lease alive here.
            clock.Advance(TimeSpan.FromMinutes(20));
            for (int attempt = 0; attempt < 40 && refreshedHandlingStartedAt is null; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                Context.ChangeTracker.Clear();
                DateTimeOffset? current = Context.ImportCommands.Single(c => c.Type == "claude-code-transcript").HandlingStartedAt;
                if (current > Now)
                    refreshedHandlingStartedAt = current;
            }
        });

        ImportCommandWorker worker = CreateWorker(clock, executor, config);
        await worker.ProcessPendingAsync(CancellationToken.None);

        refreshedHandlingStartedAt.Should().NotBeNull();
        refreshedHandlingStartedAt.Should().BeCloseTo(Now.AddMinutes(20), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LogsWarning_WhenCompletingCommandWhoseLeaseWasLost()
    {
        await SeedCommandAsync("claude-code-transcript", "{}", ImportCommandStatus.Queued, Now);
        var clock = new FakeTimeProvider(Now);
        // Simulates another worker reclaiming (and re-queueing) the lease while this execution is
        // still in flight: the completion UPDATE will affect 0 rows since status is no longer Running.
        var executor = new FakeImportExecutor("claude-code-transcript", onExecute: () =>
        {
            ImportCommand row = Context.ImportCommands.Single(c => c.Type == "claude-code-transcript");
            row.TryFail(clock.GetUtcNow(), "lease reclaimed by another worker", maxAttempts: 999, backoff: TimeSpan.FromMinutes(10)).AssertSuccess();
            Db.SaveChanges();
        });
        MockLogger<ImportCommandWorker> logger = MockLogger<ImportCommandWorker>.GetSuccessful();

        ImportCommandWorker worker = CreateWorker(clock, executor, logger: logger);
        await worker.ProcessPendingAsync(CancellationToken.None);

        logger.Mock.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("lease was lost")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private ImportCommandWorker CreateWorker(
        FakeTimeProvider clock, FakeImportExecutor executor, Dictionary<string, string?>? overrides = null,
        MockLogger<ImportCommandWorker>? logger = null)
    {
        // Scoped (not singleton) and backed by the real connection string, matching production
        // DI: `scopeFactory.CreateScope()` inside the worker (main flow and heartbeat alike) must
        // get its own DbContext instance, since a single EF Core DbContext is never safe to use
        // concurrently from two in-flight operations.
        string connectionString = Context.Database.GetConnectionString()!;
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddDbContext<TelemetryDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IConfiguration>(BuildConfig(overrides));
        services.AddSingleton<ILogger<ImportCommandWorker>>((logger ?? MockLogger<ImportCommandWorker>.GetSuccessful()).Object);
        services.AddScoped<IImportCommandExecutor>(_ => executor);
        ServiceProvider provider = services.BuildServiceProvider();
        return new ImportCommandWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<ILogger<ImportCommandWorker>>());
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?>? overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ImportCommand:MaxAttempts"] = "3",
            ["ImportCommand:BackoffBase"] = "00:01:00",
            ["ImportCommand:MaxBackoff"] = "00:30:00",
            ["ImportCommand:LeaseTimeout"] = "00:30:00",
            ["ImportCommand:PollDelay"] = "00:00:05"
        };
        if (overrides is not null)
        {
            foreach ((string key, string? value) in overrides)
                values[key] = value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private async Task SeedCommandAsync(
        string type,
        string payload,
        ImportCommandStatus status,
        DateTimeOffset createdAt,
        int attempts = 0,
        DateTimeOffset? handlingStartedAt = null,
        DateTimeOffset? cooldownUntil = null,
        string? lastError = null)
    {
        const string sql = """
            INSERT INTO telemetry.import_commands
                (id, type, payload, status, attempts, handling_started_at, cooldown_until, last_error, created_at, completed_at)
            VALUES
                (@id, @type, @payload, @status, @attempts, @handling_started_at, @cooldown_until, @last_error, @created_at, NULL)
            """;
        await Context.Database.ExecuteSqlRawAsync(sql, [
            new NpgsqlParameter("id", Guid.NewGuid()),
            new NpgsqlParameter("type", type),
            new NpgsqlParameter("payload", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = payload },
            new NpgsqlParameter("status", status.ToString()),
            new NpgsqlParameter("attempts", attempts),
            new NpgsqlParameter("handling_started_at", (object?)handlingStartedAt ?? DBNull.Value),
            new NpgsqlParameter("cooldown_until", (object?)cooldownUntil ?? DBNull.Value),
            new NpgsqlParameter("last_error", (object?)lastError ?? DBNull.Value),
            new NpgsqlParameter("created_at", createdAt)
        ]);
    }

    private ImportCommand LoadSingle(string type)
    {
        Context.ChangeTracker.Clear();
        return Context.ImportCommands.Single(c => c.Type == type);
    }

    private sealed class FakeImportExecutor : IImportCommandExecutor
    {
        private readonly Exception? _toThrow;
        private readonly bool _callProgress;
        private readonly Action? _onExecute;
        private readonly Func<Task>? _onExecuteAsync;

        public FakeImportExecutor(
            string commandType, Exception? toThrow = null, bool callProgress = false,
            Action? onExecute = null, Func<Task>? onExecuteAsync = null)
        {
            CommandType = commandType;
            _toThrow = toThrow;
            _callProgress = callProgress;
            _onExecute = onExecute;
            _onExecuteAsync = onExecuteAsync;
        }

        public string CommandType { get; }
        public int CallCount { get; private set; }

        public async Task ExecuteAsync(ImportCommand command, Func<string, CancellationToken, Task> updateProgress, CancellationToken ct)
        {
            CallCount++;
            _onExecute?.Invoke();
            if (_onExecuteAsync is not null)
                await _onExecuteAsync();
            if (_callProgress)
                await updateProgress("""{"progress":{"filesProcessed":1,"runsUpserted":1}}""", ct);
            if (_toThrow is not null)
                throw _toThrow;
        }
    }
}
