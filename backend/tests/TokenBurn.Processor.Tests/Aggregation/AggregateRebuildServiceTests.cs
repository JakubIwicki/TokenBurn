using Microsoft.EntityFrameworkCore;
using TokenBurn.Contracts;
using TokenBurn.Processor.Aggregation;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Mocking;
using RunStatus = TokenBurn.Processor.Domain.RunStatus;

namespace TokenBurn.Processor.Tests.Aggregation;

public sealed class AggregateRebuildServiceTests : TelemetryHandlerTestBase
{
    private const int MinSize = 2;
    private static readonly DateTimeOffset DayStart = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TestBucketDay = new(2026, 8, 1);

    [Fact]
    public async Task Rebuild_ProducesOneBucketPerDayModelService()
    {
        Fixture fx = Fixture.Init(Context);
        AgentRun pricedA = SeedRun("s1", priced: 0.01m);
        AgentRun pricedB = SeedRun("s2", priced: 0.02m);
        SeedRun("s3", priced: 0.03m);
        SeedRun("s4", priced: null);
        SeedMessages(pricedA, 2);
        SeedMessages(pricedB, 1);

        int count = await fx.Sut.RebuildAsync(CancellationToken.None);

        count.Should().Be(1);
        MetricBucket row = await LoadBucketAsync(TestBucketDay, "deepseek-v4-flash", "delegate-ledger");
        row.RunCount.Should().Be(4);
        row.PricedRunCount.Should().Be(3);
        row.MessageCount.Should().Be(3);
        row.InputTokens.Should().Be(4000);
        row.CacheReadTokens.Should().Be(400);
        row.CacheWriteTokens.Should().Be(800);
        row.OutputTokens.Should().Be(1200);
        row.CostUsd.Should().Be(0.06m);

        fx.Publisher.Published.Should().ContainSingle();
        fx.Publisher.Published[0].Key.Should().Be("2026-08-01");
        fx.Publisher.Published[0].Aggregate.ModelSlug.Should().Be("deepseek-v4-flash");
        fx.Publisher.Published[0].Aggregate.RunCount.Should().Be(4);
        fx.Publisher.Published[0].Aggregate.CostUsd.Should().Be(0.06m);
    }

    [Fact]
    public async Task Rebuild_CollapsesMissingModelOrServiceIntoUnknownBucket()
    {
        Fixture fx = Fixture.Init(Context);
        SeedRun("s1", priced: null, modelSlug: null, service: null);
        SeedRun("s2", priced: null, modelSlug: "", service: "");
        SeedRun("s3", priced: null, modelSlug: "", service: null);

        await fx.Sut.RebuildAsync(CancellationToken.None);

        MetricBucket unknown = await LoadBucketAsync(TestBucketDay, MetricBucket.UnknownBucket, MetricBucket.UnknownBucket);
        unknown.RunCount.Should().Be(3);
        fx.Publisher.Published.Should().ContainSingle();
        fx.Publisher.Published[0].Aggregate.ModelSlug.Should().Be(MetricBucket.UnknownBucket);
        fx.Publisher.Published[0].Aggregate.Service.Should().Be(MetricBucket.UnknownBucket);
    }

    [Fact]
    public async Task Rebuild_DropsDayBelowMinSize()
    {
        Fixture fx = Fixture.Init(Context);
        SeedRun("s1", priced: 0.01m);

        int count = await fx.Sut.RebuildAsync(CancellationToken.None);

        count.Should().Be(0);
        (await Context.MetricBuckets.AsNoTracking().CountAsync()).Should().Be(0);
        fx.Publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Rebuild_Twice_ConvergesToIdenticalState()
    {
        Fixture fx = Fixture.Init(Context);
        SeedRun("s1", priced: 0.01m);
        SeedRun("s2", priced: 0.02m);
        SeedRun("s3", priced: null);

        await fx.Sut.RebuildAsync(CancellationToken.None);
        List<MetricBucket> firstState = await Context.MetricBuckets.AsNoTracking().ToListAsync();
        int firstPublishCount = fx.Publisher.Published.Count;

        await fx.Sut.RebuildAsync(CancellationToken.None);

        List<MetricBucket> secondState = await Context.MetricBuckets.AsNoTracking().ToListAsync();
        secondState.Should().BeEquivalentTo(firstState);
        fx.Publisher.Published.Should().HaveCount(firstPublishCount * 2);
        fx.Publisher.Published.Take(firstPublishCount).Should().BeEquivalentTo(fx.Publisher.Published.Skip(firstPublishCount));
    }

    [Fact]
    public async Task Rebuild_DeletesBucketThatFellBelowMinSize()
    {
        Fixture fx = Fixture.Init(Context);
        SeedRun("s1", priced: 0.01m);
        SeedRun("s2", priced: 0.02m);
        SeedRun("s3", priced: 0.03m);

        await fx.Sut.RebuildAsync(CancellationToken.None);
        (await Context.MetricBuckets.AsNoTracking().CountAsync()).Should().Be(1);
        fx.Publisher.Published.Should().HaveCount(1);

        await Context.Database.ExecuteSqlRawAsync("DELETE FROM telemetry.agent_runs WHERE session_id IN ({0}, {1})", "s2", "s3");

        await fx.Sut.RebuildAsync(CancellationToken.None);

        (await Context.MetricBuckets.AsNoTracking().CountAsync()).Should().Be(0);
        fx.Publisher.Published.Should().HaveCount(1);
    }

    private async Task<MetricBucket> LoadBucketAsync(DateOnly bucketDay, string modelSlug, string service)
    {
        Context.ChangeTracker.Clear();
        return await Context.MetricBuckets.AsNoTracking()
            .SingleAsync(x => x.BucketDay == bucketDay && x.ModelSlug == modelSlug && x.Service == service);
    }

    private AgentRun SeedRun(string sessionId, decimal? priced, string? modelSlug = "deepseek-v4-flash", string? service = "delegate-ledger")
    {
        AgentRun run = AgentRun.Create(sessionId, "", "delegate-ledger", null, null,
            modelSlug, RunStatus.Completed, DayStart, DayStart.AddMinutes(1),
            inputTokens: 1000, cacheReadTokens: 100, cacheWriteTokens: 200, outputTokens: 300,
            reportedCostUsd: null, service: service);
        if (priced is not null)
            run.TryMarkPriced(priced.Value, 1.0m);
        Db.Store(run);
        return run;
    }

    private void SeedMessages(AgentRun run, int count)
    {
        for (int sequence = 0; sequence < count; sequence++)
        {
            AgentMessage message = AgentMessage.Create(run.Id, sequence, "user", null, null, run.ModelSlug,
                inputTokens: 10, cacheReadTokens: 0, cacheWriteTokens: 0, outputTokens: 5, occurredAt: DayStart);
            Db.Store(message);
        }
    }

    private sealed class Fixture
    {
        private readonly TelemetryDbContext _db;

        private Fixture(TelemetryDbContext db) { _db = db; }

        public static Fixture Init(TelemetryDbContext db) => new(db);

        public RecordingAggregatePublisher Publisher { get; } = new();

        public AggregateRebuildService Sut => new(
            _db,
            new AggregateUpserter(),
            Publisher,
            new AggregateOptions(Enabled: true, MinSize: MinSize),
            MockLogger<AggregateRebuildService>.GetSuccessful().Object);
    }

    private sealed class RecordingAggregatePublisher : IAggregatePublisher
    {
        private readonly List<(string Key, PublicAggregate Aggregate)> _published = [];

        public IReadOnlyList<(string Key, PublicAggregate Aggregate)> Published => _published;

        public Task PublishAsync(PublicAggregate aggregate, DateOnly bucketDay, CancellationToken cancellationToken)
        {
            _published.Add((bucketDay.ToString("yyyy-MM-dd"), aggregate));
            return Task.CompletedTask;
        }
    }
}
