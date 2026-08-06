using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging;
using TokenBurn.Processor.Infrastructure.Embeddings;

namespace TokenBurn.Processor.Documents.Indexing;

/// <summary>
///     Idempotently PUTs the <c>documents</c> index template. The <c>documents</c> index has
///     exactly one writer — the documents import executor — so the template carries the
///     embedding field unconditionally (inert until a chunk is indexed with one), and
///     <see cref="EnsureVectorMappingAsync" /> migrates an index that predates the field
///     without a rebuild. ES being down fails the import command, which the durable
///     <c>import_commands</c> lifecycle retries.
/// </summary>
public sealed class DocumentIndexTemplateInitializer(
    Lazy<ElasticsearchClient> client,
    ILogger<DocumentIndexTemplateInitializer> logger,
    EmbeddingsOptions? embeddingsOptions = null)
{
    // The client is Lazy because the imports endpoint enumerates every IImportCommandExecutor
    // to validate a command's source — a host that never runs a documents import must still
    // answer /api/imports without Elasticsearch configured. The client's singleton factory
    // throws when Elasticsearch:Uri is absent, so it is resolved only on first use.
    private const string TemplateName = "documents";
    private const string IndexName = "documents";
    private const string EmbeddingField = "embedding";
    private const string ChunkTextField = "chunk_text";
    private const int DefaultEmbeddingDims = 384;
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];
    // Single-writer assumption: only the documents executor calls this, so a plain bool flag
    // suffices — no lock needed.
    private bool _vectorMappingEnsured;

    public async Task EnsureTemplateAsync(CancellationToken cancellationToken)
    {
        PutIndexTemplateRequest request = BuildRequest();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                PutIndexTemplateResponse response = await client.Value.Indices.PutIndexTemplateAsync(request, cancellationToken);
                if (response.IsValidResponse)
                    return;
                throw new InvalidOperationException($"Elasticsearch rejected the documents template: {response.DebugInformation}");
            }
            catch (Exception exception) when (attempt < RetryBackoff.Length && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Failed to ensure the documents index template; retrying in {Delay}.", RetryBackoff[attempt]);
                await Task.Delay(RetryBackoff[attempt], cancellationToken);
            }
        }
    }

    /// <summary>
    ///     Ensures a live <c>documents</c> index maps the <c>embedding</c> and <c>chunk_text</c>
    ///     fields, adding them via a mapping PUT when they are missing. dense_vector can be added
    ///     to a live index without a reindex, and the guard on the existing mapping keeps this
    ///     idempotent across replays. A missing index needs no migration — the template already
    ///     carries the fields for anything created from here on. Runs once per process.
    /// </summary>
    public async Task EnsureVectorMappingAsync(CancellationToken cancellationToken)
    {
        if (_vectorMappingEnsured)
            return;

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await EnsureVectorMappingOnceAsync(cancellationToken);
                _vectorMappingEnsured = true;
                return;
            }
            catch (Exception exception) when (attempt < RetryBackoff.Length && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Failed to ensure the documents vector mapping; retrying in {Delay}.", RetryBackoff[attempt]);
                await Task.Delay(RetryBackoff[attempt], cancellationToken);
            }
        }
    }

    private async Task EnsureVectorMappingOnceAsync(CancellationToken cancellationToken)
    {
        Elastic.Clients.Elasticsearch.IndexManagement.ExistsResponse exists = await client.Value.Indices.ExistsAsync(IndexName, cancellationToken);
        if (!exists.IsValidResponse)
            throw new InvalidOperationException($"Failed to check the documents index: {exists.DebugInformation}");
        if (!exists.Exists)
            return;

        GetMappingResponse get = await client.Value.Indices.GetMappingAsync(IndexName, cancellationToken);
        if (!get.IsValidResponse)
            throw new InvalidOperationException($"Failed to read the documents mapping: {get.DebugInformation}");
        TypeMapping? mappings = get.Mappings.GetValueOrDefault(IndexName)?.Mappings;
        if (mappings?.Properties is not null)
        {
            IDictionary<PropertyName, IProperty> properties = (IDictionary<PropertyName, IProperty>)mappings.Properties;
            if (properties.TryGetValue(EmbeddingField, out IProperty? existing))
            {
                if (existing is not DenseVectorProperty denseVector)
                    throw new InvalidOperationException(
                        $"The documents index maps {EmbeddingField} as {existing.Type}, not a dense_vector; reindex or remove the field.");

                // Fail loud at migrate time if dims disagree, instead of at every write.
                if (denseVector.Dims != EmbeddingDims)
                    throw new InvalidOperationException(
                        $"The documents index maps {EmbeddingField} with {denseVector.Dims} dims, but the configured embedding dims are {EmbeddingDims}; align Embeddings:Dims or reindex.");
                return;
            }
        }

        PutMappingResponse put = await client.Value.Indices.PutMappingAsync<DocumentChunkDocument>(
            IndexName,
            descriptor => descriptor.Properties(properties => properties
                .Text(ChunkTextField)
                .DenseVector(EmbeddingField, vector => vector
                    .Dims(EmbeddingDims)
                    .Similarity(DenseVectorSimilarity.Cosine)
                    .Index(true)
                    .IndexOptions(indexOptions => indexOptions
                        .Type(DenseVectorIndexOptionsType.Hnsw)
                        .M(16)
                        .EfConstruction(100)))),
            cancellationToken);
        if (!put.IsValidResponse)
            throw new InvalidOperationException($"Failed to add the documents vector mapping: {put.DebugInformation}");
    }

    private PutIndexTemplateRequest BuildRequest() => new(TemplateName)
    {
        IndexPatterns = new[] { "documents" },
        Priority = 200,
        Template = new IndexTemplateMapping
        {
            Settings = new IndexSettings
            {
                NumberOfShards = 1,
                NumberOfReplicas = 0
            },
            Mappings = new TypeMapping
            {
                Dynamic = DynamicMapping.False,
                Properties = new Properties
                {
                    ["id"] = new KeywordProperty(),
                    ["document_id"] = new LongNumberProperty(),
                    ["uri"] = new KeywordProperty(),
                    ["title"] = new KeywordProperty(),
                    ["source"] = new KeywordProperty(),
                    ["content_hash"] = new KeywordProperty(),
                    ["indexed_at"] = new DateProperty(),
                    ["ordinal"] = new IntegerNumberProperty(),
                    ["chunk_text"] = new TextProperty(),
                    [EmbeddingField] = BuildEmbeddingProperty()
                }
            }
        }
    };

    // Backward-compatible ctor: DI supplies the configured options; callers that construct
    // directly (the documents tests) fall back to the same default EmbeddingsOptions.Dims uses.
    private int EmbeddingDims => embeddingsOptions?.Dims ?? DefaultEmbeddingDims;

    private DenseVectorProperty BuildEmbeddingProperty() => new()
    {
        Dims = EmbeddingDims,
        Similarity = DenseVectorSimilarity.Cosine,
        Index = true,
        IndexOptions = new DenseVectorIndexOptions
        {
            Type = DenseVectorIndexOptionsType.Hnsw,
            M = 16,
            EfConstruction = 100
        }
    };
}
