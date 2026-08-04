using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TokenBurn.Processor.Persistence;

public sealed class PricingSeeder(TelemetryDbContext db)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        const string priceSql = """
            INSERT INTO telemetry.model_prices
                (slug, service, input_per_mtok, cache_read_per_mtok, cache_write_per_mtok,
                 output_per_mtok, context_window, effective_from, effective_to)
            VALUES (@slug, @service, @input, @cr, @cw, @output, @ctx, '-infinity'::timestamptz, NULL)
            ON CONFLICT (slug, service, effective_from) DO NOTHING
            """;
        foreach (PricingSeedData.ModelPriceSeed price in PricingSeedData.Prices)
        {
            await db.Database.ExecuteSqlRawAsync(priceSql, [
                new NpgsqlParameter("slug", price.Slug),
                new NpgsqlParameter("service", price.Service),
                new NpgsqlParameter("input", price.InputPerMtok),
                new NpgsqlParameter("cr", price.CacheReadPerMtok),
                new NpgsqlParameter("cw", price.CacheWritePerMtok),
                new NpgsqlParameter("output", price.OutputPerMtok),
                new NpgsqlParameter("ctx", (object?)price.ContextWindow ?? DBNull.Value)
            ], cancellationToken);
        }

        const string aliasSql = """
            INSERT INTO telemetry.model_aliases (alias, service, slug)
            VALUES (@alias, @service, @slug)
            ON CONFLICT (alias) DO NOTHING
            """;
        foreach (PricingSeedData.ModelAliasSeed alias in PricingSeedData.Aliases)
        {
            await db.Database.ExecuteSqlRawAsync(aliasSql, [
                new NpgsqlParameter("alias", alias.Alias),
                new NpgsqlParameter("service", alias.Service),
                new NpgsqlParameter("slug", alias.Slug)
            ], cancellationToken);
        }
    }
}
