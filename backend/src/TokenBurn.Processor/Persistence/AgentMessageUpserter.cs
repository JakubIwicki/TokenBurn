using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence;

/// <summary>
///     Idempotent batch upsert of message rows keyed on (run_id, sequence). Rows are
///     inserted in multi-VALUES statements of at most <see cref="BatchSize" /> so a replay
///     overwrites every non-key column with the re-parsed values instead of duplicating rows.
/// </summary>
public sealed class AgentMessageUpserter(TelemetryDbContext db)
{
    private const int BatchSize = 100;
    private const string SqlPrefix = """
        INSERT INTO telemetry.agent_messages
            (id, run_id, sequence, role, content, tool_name, model_slug,
             input_tokens, cache_read_tokens, cache_write_tokens, output_tokens,
             cost_usd, occurred_at, version)
        VALUES
        """;
    private const string SqlSuffix = """
        ON CONFLICT (run_id, sequence) DO UPDATE SET
            role = EXCLUDED.role, content = EXCLUDED.content, tool_name = EXCLUDED.tool_name,
            model_slug = EXCLUDED.model_slug, input_tokens = EXCLUDED.input_tokens,
            cache_read_tokens = EXCLUDED.cache_read_tokens, cache_write_tokens = EXCLUDED.cache_write_tokens,
            output_tokens = EXCLUDED.output_tokens, cost_usd = EXCLUDED.cost_usd,
            occurred_at = EXCLUDED.occurred_at, version = EXCLUDED.version
        """;

    public async Task UpsertAsync(
        Guid runId, IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
            return;

        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        foreach (AgentMessage[] chunk in messages.Chunk(BatchSize))
            await UpsertChunkAsync(connection, runId, chunk, cancellationToken);
    }

    private static async Task UpsertChunkAsync(
        NpgsqlConnection connection, Guid runId, IReadOnlyList<AgentMessage> chunk, CancellationToken cancellationToken)
    {
        StringBuilder sql = new(SqlPrefix);
        for (int i = 0; i < chunk.Count; i++)
        {
            if (i > 0)
                sql.Append(',');
            sql.Append("(@id_").Append(i).Append(", @run_id, @sequence_").Append(i)
                .Append(", @role_").Append(i).Append(", @content_").Append(i)
                .Append(", @tool_name_").Append(i).Append(", @model_slug_").Append(i)
                .Append(", @input_tokens_").Append(i).Append(", @cache_read_tokens_").Append(i)
                .Append(", @cache_write_tokens_").Append(i).Append(", @output_tokens_").Append(i)
                .Append(", @cost_usd_").Append(i).Append(", @occurred_at_").Append(i)
                .Append(", @version_").Append(i).Append(')');
        }
        sql.Append(SqlSuffix);

        await using NpgsqlCommand command = new(sql.ToString(), connection);
        command.Parameters.AddWithValue("run_id", runId);
        for (int i = 0; i < chunk.Count; i++)
        {
            AgentMessage message = chunk[i];
            command.Parameters.AddWithValue($"id_{i}", message.Id);
            command.Parameters.AddWithValue($"sequence_{i}", message.Sequence);
            command.Parameters.AddWithValue($"role_{i}", message.Role);
            command.Parameters.AddWithValue($"content_{i}", (object?)message.Content ?? DBNull.Value);
            command.Parameters.AddWithValue($"tool_name_{i}", (object?)message.ToolName ?? DBNull.Value);
            command.Parameters.AddWithValue($"model_slug_{i}", (object?)message.ModelSlug ?? DBNull.Value);
            command.Parameters.AddWithValue($"input_tokens_{i}", message.InputTokens);
            command.Parameters.AddWithValue($"cache_read_tokens_{i}", message.CacheReadTokens);
            command.Parameters.AddWithValue($"cache_write_tokens_{i}", message.CacheWriteTokens);
            command.Parameters.AddWithValue($"output_tokens_{i}", message.OutputTokens);
            command.Parameters.AddWithValue($"cost_usd_{i}", (object?)message.CostUsd ?? DBNull.Value);
            command.Parameters.AddWithValue($"occurred_at_{i}", message.OccurredAt);
            command.Parameters.AddWithValue($"version_{i}", message.Version);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
