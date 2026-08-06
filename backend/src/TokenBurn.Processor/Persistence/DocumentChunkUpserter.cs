using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence;

/// <summary>
///     Idempotent batch upsert of chunk rows keyed on (document_id, ordinal). Chunk rows are
///     written only for newly-applied documents, so a conflict is a partial-write repair: the
///     ON CONFLICT DO UPDATE overwrites every non-key column with the re-derived values so a
///     replay converges instead of duplicating rows.
/// </summary>
public sealed class DocumentChunkUpserter(TelemetryDbContext db)
{
    private const int BatchSize = 100;
    private const string SqlPrefix = """
        INSERT INTO search.document_chunks (document_id, ordinal, chunk_text, token_count, content_hash)
        VALUES
        """;
    private const string SqlSuffix = """
        ON CONFLICT (document_id, ordinal) DO UPDATE SET
            chunk_text = EXCLUDED.chunk_text, token_count = EXCLUDED.token_count,
            content_hash = EXCLUDED.content_hash
        """;

    public async Task UpsertAsync(
        long documentId, IReadOnlyList<SearchDocumentChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
            return;

        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        foreach (SearchDocumentChunk[] chunk in chunks.Chunk(BatchSize))
            await UpsertChunkAsync(connection, documentId, chunk, cancellationToken);
    }

    private static async Task UpsertChunkAsync(
        NpgsqlConnection connection, long documentId, IReadOnlyList<SearchDocumentChunk> chunk, CancellationToken cancellationToken)
    {
        StringBuilder sql = new(SqlPrefix);
        for (int i = 0; i < chunk.Count; i++)
        {
            if (i > 0)
                sql.Append(',');
            sql.Append("(@document_id, @ordinal_").Append(i).Append(", @chunk_text_").Append(i)
                .Append(", @token_count_").Append(i).Append(", @content_hash_").Append(i).Append(')');
        }
        sql.Append(SqlSuffix);

        await using NpgsqlCommand command = new(sql.ToString(), connection);
        command.Parameters.AddWithValue("document_id", documentId);
        for (int i = 0; i < chunk.Count; i++)
        {
            SearchDocumentChunk c = chunk[i];
            command.Parameters.AddWithValue($"ordinal_{i}", c.Ordinal);
            command.Parameters.AddWithValue($"chunk_text_{i}", c.ChunkText);
            command.Parameters.AddWithValue($"token_count_{i}", c.TokenCount);
            command.Parameters.AddWithValue($"content_hash_{i}", c.ContentHash);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
