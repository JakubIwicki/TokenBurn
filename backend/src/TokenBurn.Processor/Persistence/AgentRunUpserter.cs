using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence;

public sealed class AgentRunUpserter(TelemetryDbContext db)
{
    /// <summary>
    ///     Upserts the run and returns the STORED id — the id already in the row, not the
    ///     incoming <paramref name="run" />. Message rows must key on the stored id so a
    ///     re-import of the same (session_id, agent_id) keeps messages attached to the
    ///     original run. <c>Applied</c> is true when the INSERT or UPDATE actually landed
    ///     (RETURNING yielded a row) and false when the ended_at guard rejected the UPDATE —
    ///     in which case the stored run keeps its original pricing, so message persistence
    ///     must be skipped to preserve <c>SUM(messages.cost) = run.cost</c>.
    /// </summary>
    public async Task<(Guid StoredId, bool Applied)> UpsertAsync(AgentRun run, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO telemetry.agent_runs
                (id, session_id, agent_id, source, external_id, parent_run_id, workspace, persona,
                 model_slug, service, status, pricing_status, started_at, ended_at, input_tokens,
                 cache_read_tokens, cache_write_tokens, output_tokens, cost_usd, reported_cost_usd,
                 price_multiplier, version)
            VALUES
                (@id, @session_id, @agent_id, @source, @external_id, @parent_run_id, @workspace, @persona,
                 @model_slug, @service, @status, @pricing_status, @started_at, @ended_at, @input_tokens,
                 @cache_read_tokens, @cache_write_tokens, @output_tokens, @cost_usd, @reported_cost_usd,
                 @price_multiplier, @version)
            ON CONFLICT (session_id, agent_id) DO UPDATE SET
                source = EXCLUDED.source, external_id = EXCLUDED.external_id, parent_run_id = EXCLUDED.parent_run_id,
                workspace = EXCLUDED.workspace, persona = EXCLUDED.persona, model_slug = EXCLUDED.model_slug,
                service = EXCLUDED.service, status = EXCLUDED.status,
                pricing_status = CASE WHEN agent_runs.pricing_status = 'Priced'
                                      THEN agent_runs.pricing_status ELSE EXCLUDED.pricing_status END,
                started_at = EXCLUDED.started_at, ended_at = EXCLUDED.ended_at,
                input_tokens = EXCLUDED.input_tokens, cache_read_tokens = EXCLUDED.cache_read_tokens,
                cache_write_tokens = EXCLUDED.cache_write_tokens, output_tokens = EXCLUDED.output_tokens,
                cost_usd = CASE WHEN agent_runs.pricing_status = 'Priced'
                                THEN agent_runs.cost_usd ELSE EXCLUDED.cost_usd END,
                reported_cost_usd = EXCLUDED.reported_cost_usd,
                price_multiplier = CASE WHEN agent_runs.pricing_status = 'Priced'
                                        THEN agent_runs.price_multiplier ELSE EXCLUDED.price_multiplier END,
                version = EXCLUDED.version
            WHERE agent_runs.ended_at IS NULL
               OR (EXCLUDED.ended_at IS NOT NULL AND EXCLUDED.ended_at >= agent_runs.ended_at)
            RETURNING id
            """;

        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using (NpgsqlCommand command = new(sql, connection))
        {
            command.Parameters.AddWithValue("id", run.Id);
            command.Parameters.AddWithValue("session_id", run.SessionId);
            command.Parameters.AddWithValue("agent_id", run.AgentId);
            command.Parameters.AddWithValue("source", run.Source);
            command.Parameters.AddWithValue("external_id", (object?)run.ExternalId ?? DBNull.Value);
            command.Parameters.AddWithValue("parent_run_id", (object?)run.ParentRunId ?? DBNull.Value);
            command.Parameters.AddWithValue("workspace", (object?)run.Workspace ?? DBNull.Value);
            command.Parameters.AddWithValue("persona", (object?)run.Persona ?? DBNull.Value);
            command.Parameters.AddWithValue("model_slug", (object?)run.ModelSlug ?? DBNull.Value);
            command.Parameters.AddWithValue("service", (object?)run.Service ?? DBNull.Value);
            command.Parameters.AddWithValue("status", run.Status.ToString());
            command.Parameters.AddWithValue("pricing_status", run.PricingStatus.ToString());
            command.Parameters.AddWithValue("started_at", (object?)run.StartedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("ended_at", (object?)run.EndedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("input_tokens", (object?)run.InputTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("cache_read_tokens", (object?)run.CacheReadTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("cache_write_tokens", (object?)run.CacheWriteTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("output_tokens", (object?)run.OutputTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("cost_usd", (object?)run.CostUsd ?? DBNull.Value);
            command.Parameters.AddWithValue("reported_cost_usd", (object?)run.ReportedCostUsd ?? DBNull.Value);
            command.Parameters.AddWithValue("price_multiplier", (object?)run.PriceMultiplier ?? DBNull.Value);
            command.Parameters.AddWithValue("version", run.Version);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                return (reader.GetGuid(0), Applied: true);
        }

        // The WHERE guard rejects a replay carrying an older ended_at: the existing row is
        // untouched and RETURNING yields no row. The stored id is the only correct message key.
        const string selectSql = """
            SELECT id FROM telemetry.agent_runs WHERE session_id = @session_id AND agent_id = @agent_id
            """;
        await using NpgsqlCommand selectCommand = new(selectSql, connection);
        selectCommand.Parameters.AddWithValue("session_id", run.SessionId);
        selectCommand.Parameters.AddWithValue("agent_id", run.AgentId);
        await using NpgsqlDataReader fallbackReader = await selectCommand.ExecuteReaderAsync(cancellationToken);
        if (await fallbackReader.ReadAsync(cancellationToken))
            return (fallbackReader.GetGuid(0), Applied: false);
        throw new RunPersistenceException(
            $"Run upsert returned no stored id for session '{run.SessionId}', agent '{run.AgentId}'.");
    }
}
