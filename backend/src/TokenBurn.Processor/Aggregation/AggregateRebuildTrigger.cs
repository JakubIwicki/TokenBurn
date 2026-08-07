using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TokenBurn.Processor.Aggregation;

/// <summary>
///     Runs the aggregate recompute once at startup, gated on <c>Processor:Aggregate:Enabled</c>
///     (default-off). When replay is enabled it awaits <see cref="ReplayCompletionNotifier.Completion" />
///     first so the aggregate includes every replayed run; otherwise it rebuilds immediately.
///     Registered after <see cref="RunReplayTrigger" /> so it starts after replay's start (the
///     notifier makes order-tolerant anyway). A failed rebuild propagates out of ExecuteAsync — it is
///     a host failure, never swallowed.
/// </summary>
internal sealed class AggregateRebuildTrigger(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    AggregateOptions options,
    ReplayCompletionNotifier replayCompletion,
    ILogger<AggregateRebuildTrigger> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
            return;

        try
        {
            if (IsReplayEnabled())
                await replayCompletion.Completion.WaitAsync(stoppingToken);

            using IServiceScope scope = scopeFactory.CreateScope();
            AggregateRebuildService rebuild = scope.ServiceProvider.GetRequiredService<AggregateRebuildService>();
            int count = await rebuild.RebuildAsync(stoppingToken);
            logger.LogInformation("Aggregate rebuild completed; {BucketCount} buckets rebuilt and published.", count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Aggregate rebuild failed at startup.");
            throw;
        }
    }

    private bool IsReplayEnabled()
        => bool.TryParse(configuration["Processor:Replay:Enabled"], out bool enabled) && enabled;
}
