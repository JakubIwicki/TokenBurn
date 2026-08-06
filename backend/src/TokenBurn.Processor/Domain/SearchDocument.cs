namespace TokenBurn.Processor.Domain;

/// <summary>
///     A searchable document in the RAG corpus, deduplicated on its content hash. The
///     surrogate <see cref="StoredId" /> is assigned by the database identity column; the
///     natural key is <c>content_hash</c> (SHA256 over the file's UTF-8 text), so a re-import
///     of identical content is a no-op — the upserter returns the stored id with
///     Applied=false and the pipeline skips re-chunking and re-embedding. Not a
///     <see cref="TokenBurn.Common.Primitives.BaseEntity{TKey}" />: the key property is
///     deliberately named <c>StoredId</c>, mirroring the upserter's returned "stored id"
///     semantics rather than the <c>Id</c> convention used by client-assigned-key entities.
/// </summary>
public sealed class SearchDocument
{
    public long StoredId { get; private init; }
    public string Uri { get; private init; } = null!;
    public string Title { get; private init; } = null!;
    public string Source { get; private init; } = null!;
    public string ContentHash { get; private init; } = null!;
    public DateTimeOffset IndexedAt { get; private init; }

    private SearchDocument() { }

    public static SearchDocument Create(string uri, string title, string source, string contentHash, DateTimeOffset indexedAt)
        => new()
        {
            Uri = uri,
            Title = title,
            Source = source,
            ContentHash = contentHash,
            IndexedAt = indexedAt
        };
}
