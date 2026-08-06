using System.Text.Json.Serialization;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     The ONLY two fields the embedder writes. The partial <c>_update</c> with
///     <c>doc_as_upsert</c> merges exactly this shape onto the existing traces
///     document, so indexer-owned fields are never touched (telemetry-pipeline
///     rule 8: the embedder and the indexer own disjoint field sets).
/// </summary>
public sealed class RunEmbeddingPartial
{
    [JsonPropertyName("embedding")] public float[] Embedding { get; init; } = [];
    [JsonPropertyName("embedding_text")] public string EmbeddingText { get; init; } = null!;
}
