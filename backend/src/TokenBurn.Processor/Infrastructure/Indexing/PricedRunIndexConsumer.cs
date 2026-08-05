using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     Consumes <c>telemetry.priced</c>, indexes each run into Elasticsearch,
///     then publishes an acknowledgment to <c>telemetry.indexed</c> keyed by
///     session. Crash-safe order: index → publish → commit. A crash between
///     publish and commit re-delivers the PricedRun; the <c>_id</c>-overwrite
///     makes the re-index idempotent, so the doc count stays distinct.
/// </summary>
internal sealed class PricedRunIndexConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PricedRunIndexConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured.");
        ConsumerConfig consumerConfig = new()
        {
            BootstrapServers = bootstrapServers,
            GroupId = "processor-telemetry-priced",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        using IProducer<string, string> producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true
        }).Build();
        consumer.Subscribe(KafkaTopics.Priced);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result = null!;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    using IServiceScope scope = scopeFactory.CreateScope();
                    IRunIndexer indexer = scope.ServiceProvider.GetRequiredService<IRunIndexer>();
                    PricedRun priced = KafkaJsonSerializer.Deserialize<PricedRun>(result.Message.Value)
                        ?? throw new InvalidOperationException("PricedRun payload deserialized to null.");
                    await indexer.IndexAsync(priced, stoppingToken);
                    await producer.ProduceAsync(KafkaTopics.Indexed, new Message<string, string>
                    {
                        Key = priced.SessionId,
                        Value = KafkaJsonSerializer.Serialize(new IndexedRun { RunId = priced.Id, SessionId = priced.SessionId })
                    }, stoppingToken);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // Same crash-on-error posture as the raw consumer: never commit past an
                    // un-processed offset, because the only recovery would be a full replay.
                    logger.LogError(exception, "Failed to process telemetry.priced message at {TopicPartitionOffset}.", result?.TopicPartitionOffset);
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
}
