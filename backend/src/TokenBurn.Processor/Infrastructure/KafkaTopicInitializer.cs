using Confluent.Kafka;
using Confluent.Kafka.Admin;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Infrastructure;

/// <summary>
///     Ensures the telemetry chain topics (priced, indexed) and the metrics
///     aggregate topic exist before any hosted consumer subscribes. Called
///     once at processor startup from
///     <c>InitializeProcessorAsync</c> — endpoint-authorization tests never
///     invoke it, so they stay broker-free. Mirrors the ingest outbox's
///     topic creation (3 partitions, RF 1, tolerate already-exists) with the
///     same retry/backoff posture: the broker must be reachable before the
///     consumers it feeds start.
/// </summary>
public sealed class KafkaTopicInitializer(IConfiguration configuration, ILogger<KafkaTopicInitializer> logger)
{
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    ];

    public async Task EnsureTopicsAsync(CancellationToken cancellationToken)
    {
        using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"]
                ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured.")
        }).Build();

        foreach (string topic in new[] { KafkaTopics.Priced, KafkaTopics.Indexed, KafkaTopics.Metrics })
        {
            await EnsureTopicAsync(admin, topic, cancellationToken);
        }
    }

    private async Task EnsureTopicAsync(IAdminClient admin, string topic, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await admin.CreateTopicsAsync(
                    [new TopicSpecification { Name = topic, NumPartitions = 3, ReplicationFactor = 1 }],
                    new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
                logger.LogInformation("Kafka topic {Topic} created.", topic);
                return;
            }
            catch (CreateTopicsException exception) when (
                exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                logger.LogInformation("Kafka topic {Topic} already exists.", topic);
                return;
            }
            catch (Exception exception) when (attempt < RetryBackoff.Length && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Failed to create Kafka topic {Topic}; retrying in {Delay}.", topic, RetryBackoff[attempt]);
                await Task.Delay(RetryBackoff[attempt], cancellationToken);
            }
        }
    }
}
