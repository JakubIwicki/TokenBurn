using Api.TokenBurn.Ingest.Domain;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Api.TokenBurn.Ingest.Infrastructure;

public sealed class OutboxPublisher : BackgroundService
{
    private const string TopicName = "telemetry.raw";
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<OutboxPublisher> logger;
    private readonly IAdminClient adminClient;
    private readonly IProducer<string, string> producer;

    public OutboxPublisher(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<OutboxPublisher> logger)
    {
        this.scopeFactory = scopeFactory;
        this.timeProvider = timeProvider;
        this.logger = logger;
        (adminClient, producer) = CreateClients(configuration);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool topicReady = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!topicReady)
                {
                    await EnsureTopicAsync(stoppingToken);
                    topicReady = true;
                }

                bool foundRows = await DrainBatchAsync(stoppingToken);
                if (!foundRows)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The outbox publisher tick failed; retrying.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task EnsureTopicAsync(CancellationToken cancellationToken)
    {
        try
        {
            await adminClient.CreateTopicsAsync(
                [new TopicSpecification { Name = TopicName, NumPartitions = 3, ReplicationFactor = 1 }],
                new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
        }
        catch (CreateTopicsException exception) when (
            exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            logger.LogInformation("Kafka topic {Topic} already exists.", TopicName);
        }
    }

    private async Task<bool> DrainBatchAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IngestDbContext db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        List<OutboxMessage> messages = await db.OutboxMessages
            .AsNoTracking()
            .Where(message => message.PublishedAt == null && message.DeadLetteredAt == null && message.Attempts < OutboxMessage.MaxAttempts)
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (OutboxMessage message in messages)
        {
            try
            {
                await producer.ProduceAsync(
                    message.Topic,
                    new Message<string, string> { Key = message.Key, Value = message.Payload },
                    cancellationToken);

                int affected = await MarkPublishedAsync(db, message.Id, timeProvider.GetUtcNow(), cancellationToken);
                if (affected == 0)
                {
                    logger.LogDebug("Outbox message {MessageId} was already published by another replica; skipping.", message.Id);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                await IncrementAttemptsAsync(db, message.Id, timeProvider.GetUtcNow(), cancellationToken);
                logger.LogWarning(exception, "Failed to publish outbox message {MessageId}; retrying in order.", message.Id);
                break;
            }
        }

        return messages.Count > 0;
    }

    private static Task<int> MarkPublishedAsync(
        IngestDbContext db,
        Guid messageId,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE ingest.outbox_messages SET published_at = {0} WHERE id = {1} AND published_at IS NULL",
            [
                new NpgsqlParameter("p_published_at", NpgsqlDbType.TimestampTz) { Value = publishedAt },
                new NpgsqlParameter("p_id", NpgsqlDbType.Uuid) { Value = messageId }
            ],
            cancellationToken);

    private static Task<int> IncrementAttemptsAsync(
        IngestDbContext db,
        Guid messageId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE ingest.outbox_messages SET attempts = attempts + 1, dead_lettered_at = CASE WHEN attempts + 1 >= {0} THEN {1} END WHERE id = {2} AND published_at IS NULL AND dead_lettered_at IS NULL",
            [
                new NpgsqlParameter("p_max_attempts", NpgsqlDbType.Integer) { Value = OutboxMessage.MaxAttempts },
                new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz) { Value = now },
                new NpgsqlParameter("p_id", NpgsqlDbType.Uuid) { Value = messageId }
            ],
            cancellationToken);

    public override void Dispose()
    {
        producer.Dispose();
        adminClient.Dispose();
        base.Dispose();
    }

    private static (IAdminClient AdminClient, IProducer<string, string> Producer) CreateClients(IConfiguration configuration)
    {
        string bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured.");

        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException("Kafka:BootstrapServers must be configured.");
        }

        IAdminClient adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();
        IProducer<string, string> producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true
        }).Build();
        return (adminClient, producer);
    }
}
