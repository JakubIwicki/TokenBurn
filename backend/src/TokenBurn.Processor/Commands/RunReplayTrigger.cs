using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TokenBurn.Processor.Aggregation;

namespace TokenBurn.Processor.Commands;

/// <summary>
///     Runs <see cref="RunReplayService.ReplayAsync" /> once at startup, gated
///     on <c>Processor:Replay:Enabled</c>. Off by default — the live pipeline
///     already publishes new runs; replay exists for backfilling Elasticsearch
///     from the durable store. Registered before the raw consumer so the index
///     consumer (Earliest) catches up on re-published runs.
/// </summary>
internal sealed class RunReplayTrigger(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ReplayCompletionNotifier replayCompletion,
    ILogger<RunReplayTrigger> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled())
        {
            logger.LogInformation("Run replay disabled (Processor:Replay:Enabled is not true).");
            return;
        }

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            RunReplayService replay = scope.ServiceProvider.GetRequiredService<RunReplayService>();
            int published = await replay.ReplayAsync(stoppingToken);
            logger.LogInformation("Run replay completed; {Published} runs re-published to {Topic}.", published, Contracts.KafkaTopics.Priced);
            // Only a successful replay unblocks the aggregate rebuild — on failure the process
            // crashes below and the notifier stays pending, so the rebuild never runs.
            replayCompletion.Complete();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Run replay failed at startup.");
            throw;
        }
    }

    private bool IsEnabled()
        => bool.TryParse(configuration["Processor:Replay:Enabled"], out bool enabled) && enabled;
}
