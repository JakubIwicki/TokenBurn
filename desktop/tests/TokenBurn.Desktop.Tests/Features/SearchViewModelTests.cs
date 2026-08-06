using TokenBurn.Desktop.Core.Features.Search;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Tests.Features;

public sealed class SearchViewModelTests
{
    private sealed class Fixture
    {
        public FakeDispatcher Dispatcher { get; } = new();
        public Mock<IInsightsApiClient> Api { get; } = new();
        public SearchViewModel Sut { get; }

        public Fixture()
        {
            Api.Setup(a => a.SearchAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchResponse { Total = 0, Hits = [], Highlights = [], NextCursor = null });
            Sut = new SearchViewModel(Dispatcher, Api.Object);
        }
    }

    private static SearchResponse Results() => new()
    {
        Total = 2,
        Hits =
        [
            new SearchRunHit { Id = Guid.NewGuid(), Session_id = "s1", Status = "completed", Pricing_status = "priced", Cost_usd = 1.23, Input_tokens = 1_200_000 },
            new SearchRunHit { Id = Guid.NewGuid(), Session_id = "s2", Status = "running", Pricing_status = "unpriced" },
        ],
        Highlights =
        [
            new[] { "delegated to a subagent", "priced against registry" },
            new[] { "still running" },
        ],
        NextCursor = "cur",
    };

    [Fact]
    public async Task Refresh_LoadsHitsWithRankAndDiffChrome()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.SearchAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Results());

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.Total.Should().Be(2);
        fx.Sut.Hits.Should().HaveCount(2);
        fx.Sut.Hits[0].Rank.Should().Be(1);
        fx.Sut.Hits[0].Session.Should().Be("s1");
        fx.Sut.Hits[0].Tokens.Should().Be("1.2M");
        fx.Sut.Hits[0].PricingStatus.Should().Be("priced");
        fx.Sut.Hits[0].DiffChrome.Should().ContainInOrder("+ delegated to a subagent", "+ priced against registry");
        fx.Sut.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_PassesQueryAndMode()
    {
        var fx = new Fixture();
        string? seenQuery = null;
        string? seenMode = null;
        fx.Sut.Query = "delegate";
        fx.Sut.Mode = "keyword";
        fx.Api.Setup(a => a.SearchAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset? from, DateTimeOffset? to, string? q, string? mode, string? model, string? persona, string? source, string? status, string? cursor, int? limit, CancellationToken ct) =>
            {
                seenQuery = q;
                seenMode = mode;
                return Task.FromResult(new SearchResponse { Total = 0, Hits = [], Highlights = [], NextCursor = null });
            });

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        seenQuery.Should().Be("delegate");
        seenMode.Should().Be("keyword");
    }

    [Fact]
    public async Task Refresh_WhenApiThrows_SetsErrorMessage()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.SearchAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public async Task Refresh_WhenCancelled_LeavesHitsUntouched()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.SearchAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset? from, DateTimeOffset? to, string? q, string? mode, string? model, string? persona, string? source, string? status, string? cursor, int? limit, CancellationToken ct) =>
            {
                var tcs = new TaskCompletionSource<SearchResponse>();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            });

        var executing = fx.Sut.RefreshCommand.ExecuteAsync(null);
        fx.Sut.RefreshCommand.Cancel();
        await executing;

        fx.Sut.Hits.Should().BeEmpty();
        fx.Sut.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadMore_AppendsHits()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.SearchAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset? from, DateTimeOffset? to, string? q, string? mode, string? model, string? persona, string? source, string? status, string? cursor, int? limit, CancellationToken ct) =>
                cursor is null
                    ? Task.FromResult(Results())
                    : Task.FromResult(new SearchResponse { Total = 3, Hits = [new SearchRunHit { Id = Guid.NewGuid(), Session_id = "s3", Status = "completed", Pricing_status = "priced" }], Highlights = [[]], NextCursor = null }));
        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        await fx.Sut.LoadMoreCommand.ExecuteAsync(null);

        fx.Sut.Hits.Should().HaveCount(3);
        fx.Sut.Hits[2].Rank.Should().Be(3);
        fx.Sut.HasMore.Should().BeFalse();
    }
}
