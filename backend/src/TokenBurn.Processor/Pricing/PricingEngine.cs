using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Common.Primitives;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;

namespace TokenBurn.Processor.Pricing;

public sealed class PricingEngine(TelemetryDbContext db)
{
    public async Task<Result<PriceRow>> ResolveAsync(
        string? slug, string? service, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        string normalized = SlugResolver.Resolve(slug);
        if (normalized.Length == 0)
            return Result<PriceRow>.NotFound($"No price row for empty model slug '{slug}'.");

        Result<PriceRow> exact = await QueryPriceAsync(normalized, service, asOf, cancellationToken);
        if (exact.IsSuccess)
            return exact;

        Result<PriceRow> viaAlias = await QueryViaAliasAsync(normalized, asOf, cancellationToken);
        if (viaAlias.IsSuccess)
            return viaAlias;

        return exact;
    }

    public async Task<Result> PriceRunAsync(AgentRun run, CancellationToken cancellationToken)
    {
        if (run.EndedAt is null)
            return Result.Success();

        if (run.PricingStatus == PricingStatus.Priced)
            return Result.Success();

        if (HasNoUsage(run))
            return Result.Success();

        DateTimeOffset asOf = run.StartedAt ?? run.EndedAt!.Value;
        Result<PriceRow> resolved = await ResolveAsync(run.ModelSlug, run.Service, asOf, cancellationToken);
        if (!resolved.IsSuccess)
            return Result.Success();

        decimal multiplier = PriceMultiplier.For(asOf);
        decimal cost = CostCalculator.Compute(
            run.InputTokens, run.CacheReadTokens, run.CacheWriteTokens, run.OutputTokens,
            resolved.Value!, multiplier);
        return run.TryMarkPriced(cost, multiplier);
    }

    /// <summary>
    ///     Prices a run's retained messages against the run's OWN price row and peak
    ///     multiplier — resolved once from the run's timestamps, never per-message, so the
    ///     per-message costs sum exactly to the run cost even across a peak boundary
    ///     (CostCalculator.Compute is linear). An unpriced run (no ended_at, no resolvable
    ///     price row) leaves every message cost null and returns Success. A non-success
    ///     result means a message was already priced — a defect the caller should surface.
    /// </summary>
    public async Task<Result> PriceMessagesAsync(
        AgentRun run, IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0 || run.EndedAt is null || HasNoUsage(run))
            return Result.Success();

        DateTimeOffset asOf = run.StartedAt ?? run.EndedAt!.Value;
        Result<PriceRow> resolved = await ResolveAsync(run.ModelSlug, run.Service, asOf, cancellationToken);
        if (!resolved.IsSuccess)
            return Result.Success();

        decimal multiplier = PriceMultiplier.For(asOf);
        foreach (AgentMessage message in messages)
        {
            decimal cost = CostCalculator.Compute(
                message.InputTokens, message.CacheReadTokens, message.CacheWriteTokens,
                message.OutputTokens, resolved.Value!, multiplier);
            Result attached = message.AttachCost(cost);
            if (!attached.IsSuccess)
                return attached;
        }
        return Result.Success();
    }

    private static bool HasNoUsage(AgentRun run)
        => run.InputTokens is null or 0
            && run.CacheReadTokens is null or 0
            && run.CacheWriteTokens is null or 0
            && run.OutputTokens is null or 0
            && run.ReportedCostUsd is not null and not 0;

    private async Task<Result<PriceRow>> QueryPriceAsync(
        string slug, string? service, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        const string withService = """
            SELECT slug, service, input_per_mtok, cache_read_per_mtok, cache_write_per_mtok,
                   output_per_mtok, context_window
            FROM telemetry.model_prices
            WHERE slug = @slug AND effective_from <= @as_of
              AND (effective_to IS NULL OR effective_to > @as_of)
              AND service = @service
            ORDER BY effective_from DESC
            LIMIT 1
            """;
        const string withoutService = """
            SELECT slug, service, input_per_mtok, cache_read_per_mtok, cache_write_per_mtok,
                   output_per_mtok, context_window
            FROM telemetry.model_prices
            WHERE slug = @slug AND effective_from <= @as_of
              AND (effective_to IS NULL OR effective_to > @as_of)
            ORDER BY effective_from DESC
            LIMIT 1
            """;

        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(service is null ? withoutService : withService, connection);
        command.Parameters.AddWithValue("slug", slug);
        command.Parameters.AddWithValue("as_of", asOf);
        if (service is not null)
            command.Parameters.AddWithValue("service", service);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Result<PriceRow>.NotFound($"No price row for model slug '{slug}'.");
        return Result<PriceRow>.Success(ReadPriceRow(reader));
    }

    private async Task<Result<PriceRow>> QueryViaAliasAsync(
        string alias, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        const string aliasSql = """
            SELECT slug, service FROM telemetry.model_aliases WHERE alias = @alias LIMIT 1
            """;

        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(aliasSql, connection);
        command.Parameters.AddWithValue("alias", alias);

        string resolvedSlug;
        string resolvedService;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return Result<PriceRow>.NotFound($"No price row for model slug alias '{alias}'.");
            resolvedSlug = reader.GetString(0);
            resolvedService = reader.GetString(1);
        }

        return await QueryPriceAsync(resolvedSlug, resolvedService, asOf, cancellationToken);
    }

    private static PriceRow ReadPriceRow(NpgsqlDataReader reader)
        => new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6));
}
