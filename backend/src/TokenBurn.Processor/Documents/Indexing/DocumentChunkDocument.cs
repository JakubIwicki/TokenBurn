using System.Text.Json.Serialization;

namespace TokenBurn.Processor.Documents.Indexing;

/// <summary>
///     The Elasticsearch document for one search chunk. Field names are snake_case via
///     <see cref="JsonPropertyNameAttribute" /> to match the <c>documents</c> index template
///     (the ES client's default serializer is camelCase). <see cref="Id" /> is the composite
///     <c>"{document_id}:{ordinal}"</c>, also used as the document <c>_id</c>, so re-indexing
///     overwrites the same document instead of duplicating it.
/// </summary>
public sealed class DocumentChunkDocument
{
    [JsonPropertyName("id")] public string Id { get; init; } = null!;
    [JsonPropertyName("document_id")] public long DocumentId { get; init; }
    [JsonPropertyName("uri")] public string Uri { get; init; } = null!;
    [JsonPropertyName("title")] public string Title { get; init; } = null!;
    [JsonPropertyName("source")] public string Source { get; init; } = null!;
    [JsonPropertyName("content_hash")] public string ContentHash { get; init; } = null!;
    [JsonPropertyName("indexed_at")] public DateTimeOffset IndexedAt { get; init; }
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }
    [JsonPropertyName("chunk_text")] public string ChunkText { get; init; } = null!;
    [JsonPropertyName("embedding")] public float[] Embedding { get; init; } = null!;
}
