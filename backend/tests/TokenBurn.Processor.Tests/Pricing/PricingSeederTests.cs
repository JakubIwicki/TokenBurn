using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;

namespace TokenBurn.Processor.Tests.Pricing;

public sealed class PricingSeederTests : TelemetryHandlerTestBase
{
    [Fact]
    public async Task Seeds_FivePricesAndElevenAliases()
    {
        await new PricingSeeder(Context).SeedAsync();

        (await Context.ModelPrices.CountAsync()).Should().Be(5);
        (await Context.ModelAliases.CountAsync()).Should().Be(11);
    }

    [Fact]
    public async Task Seeding_Twice_LeavesCountsUnchanged()
    {
        await new PricingSeeder(Context).SeedAsync();
        await new PricingSeeder(Context).SeedAsync();

        (await Context.ModelPrices.CountAsync()).Should().Be(5);
        (await Context.ModelAliases.CountAsync()).Should().Be(11);
    }

    [Fact]
    public async Task Seeds_DeepseekV4FlashSentinel_AsNegativeInfinity()
    {
        await new PricingSeeder(Context).SeedAsync();

        string? effectiveFrom = await ReadSentinelEffectiveFromAsync();

        effectiveFrom.Should().Be("-infinity");
    }

    [Fact]
    public async Task RejectsOverlappingPriceRow_WithExclusionViolation()
    {
        await new PricingSeeder(Context).SeedAsync();

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(InsertOverlappingPriceAsync);

        exception.SqlState.Should().Be("23P01");
    }

    private async Task<string?> ReadSentinelEffectiveFromAsync()
    {
        const string sql = """
            SELECT effective_from::text AS "Value"
            FROM telemetry.model_prices
            WHERE slug = 'deepseek-v4-flash'
            LIMIT 1
            """;
        return await Context.Database.SqlQueryRaw<string>(sql).SingleOrDefaultAsync();
    }

    private async Task InsertOverlappingPriceAsync()
    {
        const string sql = """
            INSERT INTO telemetry.model_prices
                (slug, service, input_per_mtok, cache_read_per_mtok, cache_write_per_mtok,
                 output_per_mtok, context_window, effective_from, effective_to)
            VALUES ('deepseek-v4-flash', 'deepseek', 0.01, 0.001, 0, 0.02, NULL, '2026-01-01'::timestamptz, NULL)
            """;
        await Context.Database.ExecuteSqlRawAsync(sql);
    }
}
