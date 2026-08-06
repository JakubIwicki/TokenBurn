using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging;
using TokenBurn.Processor.Infrastructure.Embeddings;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     Idempotently PUTs the <c>traces</c> index template. ES being down
///     degrades only indexing — the raw consumer publishes to
///     <c>telemetry.priced</c> regardless; the index consumer retries the
///     template until the cluster answers or the host is stopped. The template
///     carries the two embedding fields unconditionally (they are inert metadata
///     until the embedder writes them), so indices created at any time after this
///     deploy already map them; <see cref="EnsureVectorMappingAsync" /> migrates an
///     index that predates them without a rebuild.
/// </summary>
public sealed class SearchIndexTemplateInitializer(
    ElasticsearchClient client,
    ILogger<SearchIndexTemplateInitializer> logger,
    EmbeddingsOptions? embeddingsOptions = null)
{
    private const string TemplateName = "traces";
    private const string IndexName = "traces";
    private const string EmbeddingField = "embedding";
    private const string EmbeddingTextField = "embedding_text";
    private const int DefaultEmbeddingDims = 384;
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];
    // Single-writer assumption: only the embed consumer calls EnsureVectorMappingAsync today, so a
    // plain bool flag suffices — no lock needed.
    private bool _vectorMappingEnsured;

    public async Task EnsureTemplateAsync(CancellationToken cancellationToken)
    {
        PutIndexTemplateRequest request = BuildRequest();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                PutIndexTemplateResponse response = await client.Indices.PutIndexTemplateAsync(request, cancellationToken);
                if (response.IsValidResponse)
                    return;
                throw new InvalidOperationException($"Elasticsearch rejected the traces template: {response.DebugInformation}");
            }
            catch (Exception exception) when (attempt < RetryBackoff.Length && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Failed to ensure the traces index template; retrying in {Delay}.", RetryBackoff[attempt]);
                await Task.Delay(RetryBackoff[attempt], cancellationToken);
            }
        }
    }

    /// <summary>
    ///     Ensures a live <c>traces</c> index maps the <c>embedding</c> and
    ///     <c>embedding_text</c> fields, adding them via a mapping PUT when they are
    ///     missing. dense_vector can be added to a live index without a reindex, and
    ///     the guard on the existing mapping keeps this idempotent across replays. A
    ///     missing index needs no migration — the template already carries the fields
    ///     for anything created from here on. Runs once per process.
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
                logger.LogWarning(exception, "Failed to ensure the traces vector mapping; retrying in {Delay}.", RetryBackoff[attempt]);
                await Task.Delay(RetryBackoff[attempt], cancellationToken);
            }
        }
    }

    private async Task EnsureVectorMappingOnceAsync(CancellationToken cancellationToken)
    {
        Elastic.Clients.Elasticsearch.IndexManagement.ExistsResponse exists = await client.Indices.ExistsAsync(IndexName, cancellationToken);
        if (!exists.IsValidResponse)
            throw new InvalidOperationException($"Failed to check the traces index: {exists.DebugInformation}");
        if (!exists.Exists)
            return;

        GetMappingResponse get = await client.Indices.GetMappingAsync(IndexName, cancellationToken);
        if (!get.IsValidResponse)
            throw new InvalidOperationException($"Failed to read the traces mapping: {get.DebugInformation}");
        TypeMapping? mappings = get.Mappings.GetValueOrDefault(IndexName)?.Mappings;
        if (mappings?.Properties is not null)
        {
            IDictionary<PropertyName, IProperty> properties = (IDictionary<PropertyName, IProperty>)mappings.Properties;
            if (properties.TryGetValue(EmbeddingField, out IProperty? existing))
            {
                if (existing is not DenseVectorProperty denseVector)
                    throw new InvalidOperationException(
                        $"The traces index maps {EmbeddingField} as {existing.Type}, not a dense_vector; reindex or remove the field.");

                // Fail loud at migrate time if dims disagree, instead of at every write.
                if (denseVector.Dims != EmbeddingDims)
                    throw new InvalidOperationException(
                        $"The traces index maps {EmbeddingField} with {denseVector.Dims} dims, but the configured embedding dims are {EmbeddingDims}; align Embeddings:Dims or reindex.");
                return;
            }
        }

        PutMappingResponse put = await client.Indices.PutMappingAsync<RunIndexDocument>(
            IndexName,
            descriptor => descriptor.Properties(properties => properties
                .Text(EmbeddingTextField)
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
            throw new InvalidOperationException($"Failed to add the traces vector mapping: {put.DebugInformation}");
    }

    private PutIndexTemplateRequest BuildRequest() => new(TemplateName)
    {
        IndexPatterns = new[] { "traces" },
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
                    ["session_id"] = new KeywordProperty(),
                    ["agent_id"] = new KeywordProperty(),
                    ["source"] = new KeywordProperty(),
                    ["external_id"] = new KeywordProperty(),
                    ["parent_run_id"] = new KeywordProperty(),
                    ["workspace"] = new KeywordProperty(),
                    ["persona"] = new KeywordProperty(),
                    ["model_slug"] = new KeywordProperty(),
                    ["service"] = new KeywordProperty(),
                    ["status"] = new KeywordProperty(),
                    ["pricing_status"] = new KeywordProperty(),
                    ["started_at"] = new DateProperty(),
                    ["ended_at"] = new DateProperty(),
                    ["input_tokens"] = new LongNumberProperty(),
                    ["cache_read_tokens"] = new LongNumberProperty(),
                    ["cache_write_tokens"] = new LongNumberProperty(),
                    ["output_tokens"] = new LongNumberProperty(),
                    ["cost_usd"] = new ScaledFloatNumberProperty { ScalingFactor = 1000000 },
                    ["reported_cost_usd"] = new ScaledFloatNumberProperty { ScalingFactor = 1000000 },
                    ["price_multiplier"] = new ScaledFloatNumberProperty { ScalingFactor = 1000 },
                    ["version"] = new IntegerNumberProperty(),
                    ["searchable_text"] = new TextProperty(),
                    [EmbeddingTextField] = new TextProperty(),
                    [EmbeddingField] = BuildEmbeddingProperty()
                }
            }
        }
    };

    // Backward-compatible ctor: DI supplies the configured options; callers that
    // construct directly (the Insights search tests) fall back to the same default
    // EmbeddingsOptions.Dims uses.
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
