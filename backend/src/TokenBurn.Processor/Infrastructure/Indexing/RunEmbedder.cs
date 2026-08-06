using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Infrastructure.Embeddings;
using TokenBurn.Processor.Persistence;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     Computes and stores the embedding for one already-indexed run: reads the
///     indexed document, builds <c>embedding_text</c> from the run's agent
///     messages, embeds it, then merges exactly the two embedding fields onto the
///     traces document with a partial <c>_update</c> (<c>doc_as_upsert</c>). The
///     <c>_id</c>-scoped overwrite makes redelivery idempotent — replaying an
///     <c>IndexedRun</c> recomputes and overwrites the same two fields, leaving the
///     rest of the document untouched (telemetry-pipeline rule 8).
/// </summary>
public sealed class RunEmbedder(
    ElasticsearchClient client,
    TelemetryDbContext db,
    IEmbeddingClient embeddingClient,
    RunEmbeddingTextBuilder textBuilder,
    EmbeddingsOptions options,
    SearchIndexTemplateInitializer templateInitializer,
    ILogger<RunEmbedder> logger)
{
    private const string IndexName = "traces";

    public async Task EmbedAsync(Guid runId, CancellationToken cancellationToken)
    {
        await templateInitializer.EnsureVectorMappingAsync(cancellationToken);

        GetResponse<RunIndexDocument> existing = await client.GetAsync<RunIndexDocument>(IndexName, runId.ToString("D"), cancellationToken);
        if (!existing.IsValidResponse || !existing.Found)
            throw new InvalidOperationException($"Failed to load indexed run {runId}: {existing.DebugInformation}");

        RunIndexDocument document = existing.Source!;
        List<AgentMessage> messages = await db.AgentMessages.AsNoTracking()
            .Where(message => message.RunId == runId)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);
        string embeddingText = textBuilder.Build(messages, options.MaxRunChars, document.SearchableText);

        IReadOnlyList<float> embedding = await embeddingClient.EmbedAsync([embeddingText], cancellationToken);
        RunEmbeddingPartial partial = new()
        {
            Embedding = embedding.ToArray(),
            EmbeddingText = embeddingText
        };

        UpdateResponse<RunIndexDocument> update = await client.UpdateAsync<RunIndexDocument, RunEmbeddingPartial>(
            IndexName, runId.ToString("D"),
            descriptor => descriptor.Doc(partial).DocAsUpsert(true),
            cancellationToken);
        if (!update.IsValidResponse)
            throw new InvalidOperationException($"Failed to write embeddings for run {runId}: {update.DebugInformation}");

        // doc_as_upsert forging a Created document means the run's indexed document vanished
        // between the Get above and this update (a deletion race). The forged fragment would
        // carry only the two embedding fields — fail loud so redelivery re-indexes first.
        if (update.Result == Result.Created)
            throw new InvalidOperationException(
                $"Run {runId} had no indexed document when embedding, so doc_as_upsert forged a fragment; the run must be re-indexed before embedding.");

        logger.LogDebug("Embedded run {RunId}.", runId);
    }
}
