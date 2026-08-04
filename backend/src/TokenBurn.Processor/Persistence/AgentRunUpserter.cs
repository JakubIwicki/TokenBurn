using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence;

public sealed class AgentRunUpserter(TelemetryDbContext db)
{
    public async Task UpsertAsync(AgentRun run, CancellationToken cancellationToken)
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
            """;
        await db.Database.ExecuteSqlRawAsync(sql, [
            new NpgsqlParameter("id", run.Id), new NpgsqlParameter("session_id", run.SessionId),
            new NpgsqlParameter("agent_id", run.AgentId), new NpgsqlParameter("source", run.Source),
            new NpgsqlParameter("external_id", (object?)run.ExternalId ?? DBNull.Value),
            new NpgsqlParameter("parent_run_id", (object?)run.ParentRunId ?? DBNull.Value),
            new NpgsqlParameter("workspace", (object?)run.Workspace ?? DBNull.Value),
            new NpgsqlParameter("persona", (object?)run.Persona ?? DBNull.Value),
            new NpgsqlParameter("model_slug", (object?)run.ModelSlug ?? DBNull.Value),
            new NpgsqlParameter("service", (object?)run.Service ?? DBNull.Value),
            new NpgsqlParameter("status", run.Status.ToString()), new NpgsqlParameter("pricing_status", run.PricingStatus.ToString()),
            new NpgsqlParameter("started_at", (object?)run.StartedAt ?? DBNull.Value),
            new NpgsqlParameter("ended_at", (object?)run.EndedAt ?? DBNull.Value),
            new NpgsqlParameter("input_tokens", (object?)run.InputTokens ?? DBNull.Value),
            new NpgsqlParameter("cache_read_tokens", (object?)run.CacheReadTokens ?? DBNull.Value),
            new NpgsqlParameter("cache_write_tokens", (object?)run.CacheWriteTokens ?? DBNull.Value),
            new NpgsqlParameter("output_tokens", (object?)run.OutputTokens ?? DBNull.Value),
            new NpgsqlParameter("cost_usd", (object?)run.CostUsd ?? DBNull.Value),
            new NpgsqlParameter("reported_cost_usd", (object?)run.ReportedCostUsd ?? DBNull.Value),
            new NpgsqlParameter("price_multiplier", (object?)run.PriceMultiplier ?? DBNull.Value),
            new NpgsqlParameter("version", run.Version)
        ], cancellationToken);
    }
}
