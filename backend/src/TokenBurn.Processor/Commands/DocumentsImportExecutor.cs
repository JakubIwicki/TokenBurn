using System.Globalization;
using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TokenBurn.Processor.Documents;
using TokenBurn.Processor.Documents.Indexing;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Infrastructure.Embeddings;
using TokenBurn.Processor.Persistence;

namespace TokenBurn.Processor.Commands;

/// <summary>
///     Imports a directory tree of text files into the RAG corpus (in-process, NOT via Kafka):
///     each accepted file is content-hashed and deduplicated on <c>search.documents.content_hash</c>,
///     then deterministically chunked, embedded and indexed into Elasticsearch under
///     <c>_id = "{documentId}:{ordinal}"</c>. Only genuinely unreadable files (binary, oversize)
///     are skipped; an infrastructure failure — embedding, persistence or Elasticsearch — fails
///     the run so the durable lifecycle retries it. A file whose content hash already exists is
///     reconciled against Elasticsearch (the <c>_id</c> overwrite is idempotent), so a crash
///     between the Postgres write and the ES write is repaired on the next run.
/// </summary>
public sealed class DocumentsImportExecutor(
    TextChunker chunker,
    DocumentsUpserter documentsUpserter,
    DocumentChunkUpserter chunkUpserter,
    TelemetryDbContext db,
    Lazy<IEmbeddingClient> embeddingClient,
    Lazy<ElasticsearchClient> elasticsearchClient,
    DocumentIndexTemplateInitializer templateInitializer,
    EmbeddingsOptions embeddingsOptions,
    DocumentsOptions options,
    TimeProvider timeProvider,
    ILogger<DocumentsImportExecutor> logger) : IImportCommandExecutor
{
    private const string IndexName = "documents";

    // Both infrastructure clients are Lazy, as in DocumentIndexTemplateInitializer: the imports
    // endpoint enumerates every executor to validate a command source, and a host that never
    // imports documents must still answer /api/imports without Elasticsearch or embeddings
    // configured. The clients' singleton factories throw when their config section is absent,
    // so they are resolved only when a documents command actually executes.

    public string CommandType => "documents";

    public async Task ExecuteAsync(
        ImportCommand command,
        Func<string, CancellationToken, Task> updateProgress,
        CancellationToken ct)
    {
        DocumentsPayload payload = DocumentsPayload.Parse(command.Payload);
        if (!Path.IsPathFullyQualified(payload.Path) || !Directory.Exists(payload.Path))
            throw new InvalidOperationException(
                $"Documents import path '{payload.Path}' must be a fully qualified existing directory.");

        IEmbeddingClient embeddings = embeddingClient.Value;
        ElasticsearchClient client = elasticsearchClient.Value;
        await templateInitializer.EnsureTemplateAsync(ct);
        await templateInitializer.EnsureVectorMappingAsync(ct);

        DateTimeOffset now = timeProvider.GetUtcNow();
        int filesProcessed = 0;
        int documentsUpserted = 0;
        int chunksIndexed = 0;
        int filesSkipped = 0;
        string? lastSkippedFile = null;

        foreach (string file in Directory.EnumerateFiles(payload.Path, "*", SearchOption.AllDirectories))
        {
            DateTimeOffset writtenAt = new(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
            // A file cannot legitimately carry an mtime in the future (clock skew, a write in
            // flight); skip it so a half-written file is not picked up mid-write. The optional
            // `since` filter below drives incremental imports.
            if (writtenAt > now)
                continue;
            if (payload.Since is { } since && writtenAt < since)
                continue;

            filesProcessed++;
            try
            {
                string text = ReadFile(file);
                string contentHash = ContentHasher.Compute(text);
                SearchDocument document = SearchDocument.Create(file, Path.GetFileName(file), CommandType, contentHash, now);

                long? existingId = await documentsUpserter.FindStoredIdAsync(contentHash, ct);
                if (existingId is { } storedId)
                {
                    // Identical content already in the corpus: do not re-chunk or re-embed, but
                    // reconcile the ES projection so a crash between the PG write and the ES
                    // write converges instead of leaving the chunk docs permanently missing.
                    chunksIndexed += await ReconcileIndexAsync(storedId, embeddings, client, ct);
                    continue;
                }

                IReadOnlyList<TextChunk> chunks = chunker.Chunk(text);
                if (chunks.Count == 0)
                {
                    // Whitespace-only content: the document row is the whole record — nothing to
                    // chunk, embed or index.
                    (long _, bool emptyApplied) = await documentsUpserter.UpsertAsync(document, ct);
                    if (emptyApplied)
                        documentsUpserted++;
                    continue;
                }

                // Embed BEFORE persisting, so an infra failure (embedding endpoint, dims drift)
                // leaves no committed row behind that a retry could not roll back cleanly.
                IReadOnlyDictionary<int, float[]> vectors = await EmbedAllAsync(
                    chunks.Select(chunk => (chunk.Ordinal, chunk.ChunkText)).ToArray(), embeddings, ct);

                (long newId, bool applied) = await documentsUpserter.UpsertAsync(document, ct);
                if (applied)
                {
                    SearchDocumentChunk[] rows = chunks
                        .Select(chunk => SearchDocumentChunk.Create(newId, chunk.Ordinal, chunk.ChunkText, chunk.TokenCount, chunk.ContentHash))
                        .ToArray();
                    await chunkUpserter.UpsertAsync(newId, rows, ct);
                    foreach (SearchDocumentChunk[] batch in rows.Chunk(options.EmbeddingBatchSize))
                        chunksIndexed += await IndexBatchAsync(document, batch, vectors, client, ct);
                    documentsUpserted++;
                }
                else
                {
                    // Lost the insert race to a concurrent instance with identical content: the
                    // stored rows are authoritative, so reconcile the ES projection from them.
                    chunksIndexed += await ReconcileIndexAsync(newId, embeddings, client, ct);
                }
            }
            // Only genuinely unreadable files are skippable. Everything else — EmbeddingException,
            // DocumentPersistenceException, the ES-bulk InvalidOperationException — escapes and
            // fails the run, so the import_commands lifecycle retries instead of silently
            // dropping the file.
            catch (Exception exception) when (exception is UnreadableDocumentException or IOException or UnauthorizedAccessException)
            {
                filesSkipped++;
                lastSkippedFile = file;
            }

            if (filesProcessed % 25 == 0)
                await updateProgress(SerializeProgress(filesProcessed, documentsUpserted, chunksIndexed, filesSkipped, lastSkippedFile), ct);
        }

        // Always flush the final counters: without this, an import whose file count isn't a
        // multiple of 25 never reports progress for its last (partial) batch, and a Completed
        // command whose total was under 25 files persists no progress at all.
        await updateProgress(SerializeProgress(filesProcessed, documentsUpserted, chunksIndexed, filesSkipped, lastSkippedFile), ct);

        if (filesProcessed > 0 && filesSkipped == filesProcessed)
        {
            logger.LogError(
                "Documents import failed: all {FilesProcessed} processed files were skipped (last skipped: {LastSkippedFile}).",
                filesProcessed, lastSkippedFile);
            throw new InvalidOperationException(
                $"Documents import failed: all {filesProcessed} processed files were skipped (last skipped: {lastSkippedFile}).");
        }
    }

    private string ReadFile(string file)
    {
        long length = new FileInfo(file).Length;
        if (length > options.MaxFileBytes)
            throw new UnreadableDocumentException(
                $"File '{file}' is {length} bytes, exceeding the {options.MaxFileBytes}-byte import limit.");
        string text = File.ReadAllText(file);
        // A NUL byte marks a UTF-16, compressed or otherwise non-text file; reading it as text
        // would pollute the corpus with replacement characters.
        if (text.Contains('\0'))
            throw new UnreadableDocumentException($"File '{file}' appears to be binary (contains a NUL byte).");
        return text;
    }

    private async Task<IReadOnlyDictionary<int, float[]>> EmbedAllAsync(
        IReadOnlyList<(int Ordinal, string ChunkText)> chunks, IEmbeddingClient embeddings, CancellationToken ct)
    {
        var vectors = new Dictionary<int, float[]>(chunks.Count);
        foreach ((int ordinal, string chunkText) in chunks)
        {
            // The embedding client returns one vector per call, so each chunk is embedded
            // individually; EmbeddingBatchSize bounds the Elasticsearch bulk write.
            IReadOnlyList<float> vector = await embeddings.EmbedAsync([chunkText], ct);
            if (vector.Count != embeddingsOptions.Dims)
                throw new InvalidOperationException(
                    $"Embedding for ordinal {ordinal} returned {vector.Count} dims, but the configured embedding dims are {embeddingsOptions.Dims}.");
            vectors[ordinal] = vector.ToArray();
        }
        return vectors;
    }

    private async Task<int> IndexBatchAsync(
        SearchDocument document, SearchDocumentChunk[] batch, IReadOnlyDictionary<int, float[]> vectors, ElasticsearchClient client, CancellationToken ct)
    {
        DocumentChunkDocument[] documents = batch.Select(chunk => new DocumentChunkDocument
        {
            // chunk.DocumentId is the STORED id from the upserter, not the pre-upsert document's
            // StoredId (which is still 0 until the identity column assigns it).
            Id = $"{chunk.DocumentId:D}:{chunk.Ordinal}",
            DocumentId = chunk.DocumentId,
            Uri = document.Uri,
            Title = document.Title,
            Source = document.Source,
            ContentHash = chunk.ContentHash,
            IndexedAt = document.IndexedAt,
            Ordinal = chunk.Ordinal,
            ChunkText = chunk.ChunkText,
            Embedding = vectors[chunk.Ordinal]
        }).ToArray();
        await IndexAsync(documents, client, ct);
        return documents.Length;
    }

    private async Task IndexAsync(DocumentChunkDocument[] documents, ElasticsearchClient client, CancellationToken ct)
    {
        BulkOperationsCollection operations = new();
        foreach (DocumentChunkDocument document in documents)
            operations.Add(new BulkIndexOperation<DocumentChunkDocument>(document) { Id = document.Id });

        // _id = "{documentId}:{ordinal}" makes a re-index of the same chunk an overwrite, so the
        // document count stays distinct across replays.
        BulkRequest request = new(IndexName) { Operations = operations };
        BulkResponse response = await client.BulkAsync(request, ct);
        if (!response.IsValidResponse || response.Errors)
            throw new InvalidOperationException(
                $"Failed to index {documents.Length} document chunk(s): {response.DebugInformation}");
    }

    /// <summary>
    ///     Rebuilds any chunk documents Elasticsearch is missing for a stored document. Only the
    ///     chunk rows are authoritative (deterministic chunking keeps them aligned with the ES
    ///     projection), and the <c>_id</c> overwrite keeps the rebuild idempotent. An intact ES
    ///     projection costs no embedding — the count check short-circuits the common replay case.
    /// </summary>
    private async Task<int> ReconcileIndexAsync(long storedId, IEmbeddingClient embeddings, ElasticsearchClient client, CancellationToken ct)
    {
        SearchDocument? stored = await db.SearchDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.StoredId == storedId, ct);
        List<SearchDocumentChunk> chunks = await db.SearchDocumentChunks.AsNoTracking()
            .Where(c => c.DocumentId == storedId)
            .OrderBy(c => c.Ordinal)
            .ToListAsync(ct);
        if (stored is null || chunks.Count == 0)
            return 0;

        if (await EsProjectionIntactAsync(storedId, chunks.Count, client, ct))
            return 0;

        IReadOnlyDictionary<int, float[]> vectors = await EmbedAllAsync(
            chunks.Select(chunk => (chunk.Ordinal, chunk.ChunkText)).ToArray(), embeddings, ct);
        int indexed = 0;
        foreach (SearchDocumentChunk[] batch in chunks.Chunk(options.EmbeddingBatchSize))
            indexed += await IndexBatchAsync(stored, batch, vectors, client, ct);
        return indexed;
    }

    private async Task<bool> EsProjectionIntactAsync(long storedId, int chunkCount, ElasticsearchClient client, CancellationToken ct)
    {
        var exists = await client.Indices.ExistsAsync(IndexName, ct);
        if (!exists.IsValidResponse)
            throw new InvalidOperationException($"Failed to check the documents index: {exists.DebugInformation}");
        if (!exists.Exists)
            return false;

        CountResponse count = await client.CountAsync(
            IndexName, c => c.Query(q => q.Term(t => t.Field("document_id").Value(storedId))), ct);
        if (!count.IsValidResponse)
            throw new InvalidOperationException($"Failed to count document {storedId} chunks in Elasticsearch: {count.DebugInformation}");
        return count.Count >= chunkCount;
    }

    private static string SerializeProgress(int filesProcessed, int documentsUpserted, int chunksIndexed, int filesSkipped, string? lastSkippedFile)
        => JsonSerializer.Serialize(new { progress = new { filesProcessed, documentsUpserted, chunksIndexed, filesSkipped, lastSkippedFile } });

    private sealed record DocumentsPayload(string Path, DateTimeOffset? Since)
    {
        public static DocumentsPayload Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("Import command payload must contain a JSON object with a 'path'.");

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("path", out JsonElement path) || string.IsNullOrWhiteSpace(path.GetString()))
                throw new InvalidOperationException("Import command payload is missing a non-empty 'path'.");

            DateTimeOffset? since = null;
            // A present-but-unparseable `since` must never silently degrade a filtered import to
            // a full import — fail the command so the operator sees the bad payload.
            if (root.TryGetProperty("since", out JsonElement sinceElement) && sinceElement.GetString() is { } sinceText)
            {
                if (!DateTimeOffset.TryParse(sinceText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
                    throw new InvalidOperationException($"Import command payload 'since' is not a valid timestamp: '{sinceText}'.");
                since = parsed;
            }

            return new DocumentsPayload(path.GetString()!, since);
        }
    }
}
