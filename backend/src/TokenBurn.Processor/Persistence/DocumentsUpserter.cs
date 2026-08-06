using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence;

/// <summary>
///     Inserts a search document deduplicated on its content hash, returning the STORED id —
///     the id already in the row, not any client value — and whether the insert landed.
///     <c>Applied</c> is true when the <c>INSERT ... ON CONFLICT (content_hash) DO NOTHING
///     RETURNING id</c> yielded a row and false when the content hash already exists, in which
///     case the pipeline skips re-chunking and re-embedding (the plan-mandated replay skip).
/// </summary>
public sealed class DocumentsUpserter(TelemetryDbContext db)
{
    /// <summary>
    ///     Returns the stored id for an existing content hash, or null when the content has not
    ///     been imported. The executor uses this as a pre-check so a duplicate replay skips
    ///     chunking and embedding entirely and only reconciles the Elasticsearch projection.
    /// </summary>
    public async Task<long?> FindStoredIdAsync(string contentHash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id FROM search.documents WHERE content_hash = @content_hash
            """;

        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("content_hash", contentHash);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return reader.GetInt64(0);
        return null;
    }

    public async Task<(long StoredId, bool Applied)> UpsertAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO search.documents (uri, title, source, content_hash, indexed_at)
            VALUES (@uri, @title, @source, @content_hash, @indexed_at)
            ON CONFLICT (content_hash) DO NOTHING
            RETURNING id
            """;

        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using (NpgsqlCommand command = new(sql, connection))
        {
            command.Parameters.AddWithValue("uri", document.Uri);
            command.Parameters.AddWithValue("title", document.Title);
            command.Parameters.AddWithValue("source", document.Source);
            command.Parameters.AddWithValue("content_hash", document.ContentHash);
            command.Parameters.AddWithValue("indexed_at", document.IndexedAt);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                return (reader.GetInt64(0), Applied: true);
        }

        // ON CONFLICT DO NOTHING yields no RETURNING row for a duplicate: the stored id is the
        // only correct key for a re-run that must skip re-chunk/re-embed.
        const string selectSql = """
            SELECT id FROM search.documents WHERE content_hash = @content_hash
            """;
        await using NpgsqlCommand selectCommand = new(selectSql, connection);
        selectCommand.Parameters.AddWithValue("content_hash", document.ContentHash);
        await using NpgsqlDataReader fallbackReader = await selectCommand.ExecuteReaderAsync(cancellationToken);
        if (await fallbackReader.ReadAsync(cancellationToken))
            return (fallbackReader.GetInt64(0), Applied: false);
        throw new DocumentPersistenceException(
            $"Document upsert returned no stored id for content hash '{document.ContentHash}'.");
    }
}
