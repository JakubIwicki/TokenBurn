namespace TokenBurn.Processor.Domain;

/// <summary>
///     One deterministic chunk of a <see cref="SearchDocument" />, keyed on
///     <c>(document_id, ordinal)</c>. Chunking is deterministic, so the same document content
///     always produces the same ordinals, texts, token counts and hashes — a replay of an
///     already-applied document converges instead of duplicating rows.
/// </summary>
public sealed class SearchDocumentChunk
{
    public long StoredId { get; private init; }
    public long DocumentId { get; private init; }
    public int Ordinal { get; private init; }
    public string ChunkText { get; private init; } = null!;
    public int TokenCount { get; private init; }
    public string ContentHash { get; private init; } = null!;

    private SearchDocumentChunk() { }

    public static SearchDocumentChunk Create(long documentId, int ordinal, string chunkText, int tokenCount, string contentHash)
        => new()
        {
            DocumentId = documentId,
            Ordinal = ordinal,
            ChunkText = chunkText,
            TokenCount = tokenCount,
            ContentHash = contentHash
        };
}
