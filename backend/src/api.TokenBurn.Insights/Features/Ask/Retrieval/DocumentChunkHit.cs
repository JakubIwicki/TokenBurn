using System.Text.Json.Serialization;

namespace Api.TokenBurn.Insights.Features.Ask.Retrieval;

/// <summary>
///     A <c>documents</c> index chunk as read by ask retrieval. Field names are snake_case via
///     <see cref="JsonPropertyNameAttribute" /> to match the <c>documents</c> template (the ES
///     client's serializer is camelCase). <see cref="ChunkText" /> is the RAW chunk text —
///     redaction happens later in <see cref="Chat.ContextRedactor" />. <see cref="FusedScore" />
///     is set after RRF fusion and is not part of the stored document.
/// </summary>
public sealed class DocumentChunkHit
{
    [JsonPropertyName("id")] public string Id { get; init; } = null!;
    [JsonPropertyName("document_id")] public long DocumentId { get; init; }
    [JsonPropertyName("uri")] public string Uri { get; init; } = null!;
    [JsonPropertyName("title")] public string Title { get; init; } = null!;
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }
    [JsonPropertyName("chunk_text")] public string ChunkText { get; init; } = null!;
    public double FusedScore { get; set; }
}
