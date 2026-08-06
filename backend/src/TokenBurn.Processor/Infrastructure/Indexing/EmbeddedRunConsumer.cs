using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     Consumes <c>telemetry.indexed</c> and writes the embedding fields onto each
///     indexed run. Chained off the indexed topic — telemetry-pipeline rule 8:
///     priced → indexed → embedded, never fanned off one topic. Gated on
///     <c>Processor:Embeddings:Enabled</c> (off by default), mirroring how the
///     replay trigger is registered unconditionally and no-ops when its flag is
///     unset — a host without an embeddings endpoint still boots. The
///     <c>_id</c>-scoped partial update makes redelivery idempotent: a crash
///     between update and commit recomputes and overwrites the same two fields.
/// </summary>
internal sealed class EmbeddedRunConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<EmbeddedRunConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled())
        {
            logger.LogInformation("Embeddings disabled (Processor:Embeddings:Enabled is not true).");
            return;
        }

        string bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured.");
        ConsumerConfig consumerConfig = new()
        {
            BootstrapServers = bootstrapServers,
            GroupId = "processor-telemetry-embedded",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(KafkaTopics.Indexed);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result = null!;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    using IServiceScope scope = scopeFactory.CreateScope();
                    RunEmbedder embedder = scope.ServiceProvider.GetRequiredService<RunEmbedder>();
                    IndexedRun indexed = KafkaJsonSerializer.Deserialize<IndexedRun>(result.Message.Value)
                        ?? throw new InvalidOperationException("IndexedRun payload deserialized to null.");
                    await embedder.EmbedAsync(indexed.RunId, stoppingToken);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // Same crash-on-error posture as the other consumers: never commit past an
                    // un-processed offset, because the only recovery would be a full replay. The
                    // idempotent partial update makes reprocessing safe.
                    logger.LogError(exception, "Failed to embed telemetry.indexed message at {TopicPartitionOffset}.", result?.TopicPartitionOffset);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            consumer.Close();
        }
    }

    private bool IsEnabled()
        => bool.TryParse(configuration["Processor:Embeddings:Enabled"], out bool enabled) && enabled;
}
