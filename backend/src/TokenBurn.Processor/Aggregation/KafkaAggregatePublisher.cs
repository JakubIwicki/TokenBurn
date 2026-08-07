using Confluent.Kafka;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Aggregation;

/// <summary>
///     Publishes recomputed aggregate buckets to <c>metrics.aggregate</c>, keyed per bucket day.
/// </summary>
public sealed class KafkaAggregatePublisher(IConfiguration configuration) : IAggregatePublisher
{
    public async Task PublishAsync(PublicAggregate aggregate, DateOnly bucketDay, CancellationToken cancellationToken)
    {
        string bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured.");
        // The rebuild is a startup-time one-shot with a small bucket volume, so a producer built per
        // publish adds nothing over a long-lived one (RunReplayService's build-per-operation posture).
        using IProducer<string, string> producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true
        }).Build();

        await producer.ProduceAsync(KafkaTopics.Metrics, new Message<string, string>
        {
            // Partition key = bucket day. Justification (telemetry-pipeline rule 7): aggregate rows
            // are whole-corpus summaries with no session identity, so the session_id key used on
            // every other topic is meaningless here; per-day keying keeps replay idempotent (the
            // same day always lands on the same partition) and lets Phase 8 consumers fan out per day.
            Key = bucketDay.ToString("yyyy-MM-dd"),
            Value = KafkaJsonSerializer.Serialize(aggregate)
        }, cancellationToken);
    }
}
