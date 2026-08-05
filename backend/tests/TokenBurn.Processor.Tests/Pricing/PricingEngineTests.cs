using Microsoft.EntityFrameworkCore;
using TokenBurn.Common.Primitives;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Assertions;
using TokenBurn.Testing.Common.Builders;

namespace TokenBurn.Processor.Tests.Pricing;

public sealed class PricingEngineTests : TelemetryHandlerTestBase
{
    private static readonly DateTimeOffset ResolveAsOf = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    // Shanghai 08:30 — off-peak, so the multiplier is 1.0.
    private static readonly DateTimeOffset OffPeakStart = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedEnd = new(2026, 1, 1, 0, 31, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resolves_DeepseekV4Flash_WithSeededPrices()
    {
        PricingEngine sut = await CreateSeededSutAsync();

        PriceRow row = (await sut.ResolveAsync("deepseek-v4-flash", null, ResolveAsOf, CancellationToken.None)).AssertSuccess();

        row.InputPerMtok.Should().Be(0.14m);
        row.CacheReadPerMtok.Should().Be(0.0028m);
        row.CacheWritePerMtok.Should().Be(0m);
        row.OutputPerMtok.Should().Be(0.28m);
    }

    [Fact]
    public async Task Resolves_Luna_ThroughAlias()
    {
        PricingEngine sut = await CreateSeededSutAsync();

        PriceRow row = (await sut.ResolveAsync("luna", null, ResolveAsOf, CancellationToken.None)).AssertSuccess();

        row.Slug.Should().Be("openai/gpt-5.6-luna");
        row.Service.Should().Be("openrouter-flex");
    }

    [Fact]
    public async Task Resolves_Pro_ThroughAlias()
    {
        PricingEngine sut = await CreateSeededSutAsync();

        PriceRow row = (await sut.ResolveAsync("pro", null, ResolveAsOf, CancellationToken.None)).AssertSuccess();

        row.Slug.Should().Be("deepseek-v4-flash");
        row.Service.Should().Be("deepseek");
    }

    [Fact]
    public async Task ReturnsNotFound_ForBracketSuffixedSlug_ThatIsNotARegistryKey()
    {
        PricingEngine sut = await CreateSeededSutAsync();

        Result<PriceRow> result = await sut.ResolveAsync("deepseek-v4-pro[1m]", null, ResolveAsOf, CancellationToken.None);

        result.AssertNotFound();
    }

    [Fact]
    public async Task ReturnsNotFound_ForUnknownSlug()
    {
        PricingEngine sut = await CreateSeededSutAsync();

        Result<PriceRow> result = await sut.ResolveAsync("claude-nonexistent", null, ResolveAsOf, CancellationToken.None);

        result.AssertNotFound();
    }

    [Fact]
    public async Task ReturnsNotFound_ForEmptySlug()
    {
        PricingEngine sut = await CreateSeededSutAsync();

        Result<PriceRow> result = await sut.ResolveAsync("", null, ResolveAsOf, CancellationToken.None);

        result.AssertNotFound();
    }

    [Fact]
    public async Task ResolvesVersionedPrice_NewestFirst_ByAsOf()
    {
        PricingEngine sut = await CreateSeededSutAsync();
        await InsertVersionedPriceAsync();
        var may = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);
        var july = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

        PriceRow before = (await sut.ResolveAsync("deepseek-v4-flash", null, may, CancellationToken.None)).AssertSuccess();
        PriceRow after = (await sut.ResolveAsync("deepseek-v4-flash", null, july, CancellationToken.None)).AssertSuccess();

        before.InputPerMtok.Should().Be(0.14m);
        before.CacheReadPerMtok.Should().Be(0.0028m);
        after.InputPerMtok.Should().Be(0.01m);
        after.CacheReadPerMtok.Should().Be(0.001m);
    }

    [Fact]
    public async Task Prices_CompletedRun_WithExactCostAndMultiplier()
    {
        PricingEngine sut = await CreateSeededSutAsync();
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithModelSlug("deepseek-v4-flash")
            .WithTime(OffPeakStart)
            .WithInputTokens(1_000_000).WithCacheReadTokens(500_000)
            .WithCacheWriteTokens(100_000).WithOutputTokens(200_000)
            .Completed(CompletedEnd)
            .Build();
        AgentRun persisted = Db.FindFresh<AgentRun>(run.Id)!;

        Result result = await sut.PriceRunAsync(persisted, CancellationToken.None);

        result.AssertSuccess();
        Db.SaveChanges();
        AgentRun priced = Db.FindFresh<AgentRun>(run.Id)!;
        priced.PricingStatus.Should().Be(PricingStatus.Priced);
        priced.CostUsd.Should().Be(0.1974m);
        priced.PriceMultiplier.Should().Be(1.0m);
    }

    [Fact]
    public async Task ReturnsSuccess_ForRunningRun_WithoutPricingIt()
    {
        PricingEngine sut = await CreateSeededSutAsync();
        AgentRun run = TestAgentRunBuilder.Init(Db).Running().WithModelSlug("deepseek-v4-flash").Build();
        AgentRun persisted = Db.FindFresh<AgentRun>(run.Id)!;

        Result result = await sut.PriceRunAsync(persisted, CancellationToken.None);

        result.AssertSuccess();
        Db.SaveChanges();
        AgentRun reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.PricingStatus.Should().Be(PricingStatus.Quarantined);
        reloaded.CostUsd.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsSuccess_AndDoesNotReprice_WhenAlreadyPriced()
    {
        PricingEngine sut = await CreateSeededSutAsync();
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithModelSlug("deepseek-v4-flash")
            .WithTime(OffPeakStart)
            .Completed(CompletedEnd)
            .Build();
        Db.FindFresh<AgentRun>(run.Id)!.TryMarkPriced(0.5m, 1.0m).AssertSuccess();
        Db.SaveChanges();
        AgentRun persisted = Db.FindFresh<AgentRun>(run.Id)!;

        Result result = await sut.PriceRunAsync(persisted, CancellationToken.None);

        result.AssertSuccess();
        Db.SaveChanges();
        AgentRun reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.PricingStatus.Should().Be(PricingStatus.Priced);
        reloaded.CostUsd.Should().Be(0.5m);
    }

    [Fact]
    public async Task KeepsQuarantined_WhenNoUsage_ButReportedCostPresent()
    {
        PricingEngine sut = await CreateSeededSutAsync();
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(0).WithCacheReadTokens(0).WithCacheWriteTokens(0).WithOutputTokens(0)
            .WithReportedCostUsd(0.01m)
            .WithTime(OffPeakStart)
            .Completed(CompletedEnd)
            .Build();
        AgentRun persisted = Db.FindFresh<AgentRun>(run.Id)!;

        Result result = await sut.PriceRunAsync(persisted, CancellationToken.None);

        result.AssertSuccess();
        Db.SaveChanges();
        AgentRun reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.PricingStatus.Should().Be(PricingStatus.Quarantined);
        reloaded.CostUsd.Should().BeNull();
    }

    [Fact]
    public async Task KeepsQuarantined_WhenSlugIsUnresolvable()
    {
        PricingEngine sut = await CreateSeededSutAsync();
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithModelSlug("deepseek-v4-pro[1m]")
            .WithTime(OffPeakStart)
            .Completed(CompletedEnd)
            .Build();
        AgentRun persisted = Db.FindFresh<AgentRun>(run.Id)!;

        Result result = await sut.PriceRunAsync(persisted, CancellationToken.None);

        result.AssertSuccess();
        Db.SaveChanges();
        AgentRun reloaded = Db.FindFresh<AgentRun>(run.Id)!;
        reloaded.PricingStatus.Should().Be(PricingStatus.Quarantined);
        reloaded.CostUsd.Should().BeNull();
    }

    [Fact]
    public async Task PricesAtZero_WhenNoUsageAndReportedCostIsZero()
    {
        PricingEngine sut = await CreateSeededSutAsync();
        AgentRun run = TestAgentRunBuilder.Init(Db)
            .WithModelSlug("deepseek-v4-flash")
            .WithInputTokens(0).WithCacheReadTokens(0).WithCacheWriteTokens(0).WithOutputTokens(0)
            .WithReportedCostUsd(0m)
            .WithTime(OffPeakStart)
            .Completed(CompletedEnd)
            .Build();
        AgentRun persisted = Db.FindFresh<AgentRun>(run.Id)!;

        Result result = await sut.PriceRunAsync(persisted, CancellationToken.None);

        result.AssertSuccess();
        Db.SaveChanges();
        AgentRun priced = Db.FindFresh<AgentRun>(run.Id)!;
        priced.PricingStatus.Should().Be(PricingStatus.Priced);
        priced.CostUsd.Should().Be(0m);
        priced.PriceMultiplier.Should().Be(1.0m);
    }

    private async Task<PricingEngine> CreateSeededSutAsync()
    {
        await new PricingSeeder(Context).SeedAsync();
        return new PricingEngine(Context);
    }

    // Versioning a row requires closing the seeded -infinity..NULL range first: the gist exclusion
    // (slug, service, tstzrange overlap) rejects any overlapping second row, and a default-price
    // substitution must never bypass it.
    private async Task InsertVersionedPriceAsync()
    {
        const string closeSeededRange = """
            UPDATE telemetry.model_prices
            SET effective_to = '2026-06-01'::timestamptz
            WHERE slug = 'deepseek-v4-flash' AND service = 'deepseek'
            """;
        await Context.Database.ExecuteSqlRawAsync(closeSeededRange);

        const string insert = """
            INSERT INTO telemetry.model_prices
                (slug, service, input_per_mtok, cache_read_per_mtok, cache_write_per_mtok,
                 output_per_mtok, context_window, effective_from, effective_to)
            VALUES ('deepseek-v4-flash', 'deepseek', 0.01, 0.001, 0, 0.02, NULL, '2026-06-01'::timestamptz, NULL)
            """;
        await Context.Database.ExecuteSqlRawAsync(insert);
    }
}
