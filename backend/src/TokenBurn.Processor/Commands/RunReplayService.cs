using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using TokenBurn.Contracts;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;

namespace TokenBurn.Processor.Commands;

/// <summary>
///     Re-publishes every durable run from <c>telemetry.agent_runs</c> onto
///     <c>telemetry.priced</c>. Agent runs are the replay source of truth, not
///     <c>ingest.envelopes</c>: envelopes hold only OTLP-originated runs
///     (~1148), while transcript imports write Postgres directly and bypass
///     envelopes entirely. Replaying envelopes could never reach the ~3055
///     document corpus. Replay is idempotent end to end — <c>agent_runs</c> is
///     never written here, and the ES index layer collapses repeats on
///     <c>_id = runId</c>.
/// </summary>
public sealed class RunReplayService(
    TelemetryDbContext db,
    IConfiguration configuration,
    ILogger<RunReplayService> logger)
{
    private const int PageSize = 500;

    public async Task<int> ReplayAsync(CancellationToken cancellationToken)
    {
        string bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured.");
        using IProducer<string, string> producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true
        }).Build();

        int published = 0;
        Guid? afterId = null;
        DateTimeOffset? afterStartedAt = null;
        while (true)
        {
            // Keyset over (started_at, id). Runs with a NULL started_at sort
            // first under ASC and can never be a page anchor, so they are
            // excluded from the walk and re-emitted by the final pass below.
            List<AgentRun> page = await PageNonNullRunsAsync(afterStartedAt, afterId, cancellationToken);

            if (page.Count == 0)
                break;

            await PublishAsync(producer, page, cancellationToken);
            published += page.Count;

            afterStartedAt = page[^1].StartedAt;
            afterId = page[^1].Id;
            if (page.Count < PageSize)
                break;
        }

        // NULL started_at runs: emit once, ordered by id, in bounded keyset
        // pages. The ES _id-overwrite makes any repeat harmless.
        Guid? afterNullId = null;
        while (true)
        {
            List<AgentRun> page = await PageNullRunsAsync(afterNullId, cancellationToken);
            if (page.Count == 0)
                break;

            await PublishAsync(producer, page, cancellationToken);
            published += page.Count;

            afterNullId = page[^1].Id;
            if (page.Count < PageSize)
                break;
        }

        producer.Flush(TimeSpan.FromSeconds(10));
        logger.LogInformation("Replay published {Published} runs to {Topic}.", published, KafkaTopics.Priced);
        return published;
    }

    private Task<List<AgentRun>> PageNonNullRunsAsync(
        DateTimeOffset? afterStartedAt, Guid? afterId, CancellationToken cancellationToken)
    {
        IQueryable<AgentRun> query = db.AgentRuns.AsNoTracking()
            .Where(run => run.StartedAt != null)
            .OrderBy(run => run.StartedAt)
            .ThenBy(run => run.Id);

        if (afterStartedAt is not null)
        {
            query = query.Where(run =>
                run.StartedAt > afterStartedAt ||
                (run.StartedAt == afterStartedAt && run.Id > afterId));
        }

        return query.Take(PageSize).ToListAsync(cancellationToken);
    }

    private Task<List<AgentRun>> PageNullRunsAsync(Guid? afterId, CancellationToken cancellationToken)
    {
        IQueryable<AgentRun> query = db.AgentRuns.AsNoTracking()
            .Where(run => run.StartedAt == null);
        if (afterId is not null)
            query = query.Where(run => run.Id > afterId);
        return query.OrderBy(run => run.Id).Take(PageSize).ToListAsync(cancellationToken);
    }

    private static async Task PublishAsync(
        IProducer<string, string> producer,
        IEnumerable<AgentRun> runs,
        CancellationToken cancellationToken)
    {
        foreach (AgentRun run in runs)
        {
            Contracts.PricedRun priced = PricedRunMapper.ToPricedRun(run);
            await producer.ProduceAsync(KafkaTopics.Priced, new Message<string, string>
            {
                Key = priced.SessionId,
                Value = KafkaJsonSerializer.Serialize(priced)
            }, cancellationToken);
        }
    }
}
