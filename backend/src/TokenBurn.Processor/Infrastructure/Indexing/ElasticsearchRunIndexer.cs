using Elastic.Clients.Elasticsearch;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     Indexes a <see cref="PricedRun" /> into the <c>traces</c> index under
///     <c>_id = run.Id</c> — the overwrite makes redelivery idempotent and the
///     document count stays distinct across replays. The template is ensured
///     once per process before the first index.
/// </summary>
public sealed class ElasticsearchRunIndexer(
    ElasticsearchClient client,
    SearchIndexTemplateInitializer templateInitializer) : IRunIndexer
{
    private const string IndexName = "traces";
    private bool _templateEnsured;

    public async Task IndexAsync(PricedRun run, CancellationToken cancellationToken)
    {
        if (!_templateEnsured)
        {
            await templateInitializer.EnsureTemplateAsync(cancellationToken);
            _templateEnsured = true;
        }

        RunIndexDocument document = RunIndexDocumentMapper.FromPricedRun(run);
        // The full-document overwrite drops the embedder's embedding/embedding_text fields; the
        // chained telemetry.indexed consumer re-writes them via partial update. Order (priced →
        // indexed → embedded), not field ownership, is what keeps telemetry-pipeline rule 8.
        IndexResponse response = await client.IndexAsync(document, IndexName, run.Id.ToString("D"), cancellationToken);
        if (!response.IsValidResponse)
            throw new InvalidOperationException(
                $"Failed to index run {run.Id}: {response.DebugInformation}");
    }
}
