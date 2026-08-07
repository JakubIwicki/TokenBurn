using TokenBurn.Desktop.Core.Features.Runs;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Tests.Features;

public sealed class RunsViewModelTests
{
    private sealed class Fixture
    {
        public FakeDispatcher Dispatcher { get; } = new();
        public FakeRefreshLoop Loop { get; } = new();
        public Mock<IInsightsApiClient> Api { get; } = new();
        public RunsViewModel Sut { get; }

        public Fixture()
        {
            Api.Setup(a => a.RunsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunsResponse { Runs = [], NextCursor = null });
            Sut = new RunsViewModel(Dispatcher, Api.Object, Loop);
        }
    }

    private static RunSummary Run(string session = "s1", double? cost = 1.23) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = session,
        Source = "claude",
        Persona = "ops",
        ModelSlug = "claude-opus-4-5",
        Status = "completed",
        PricingStatus = "priced",
        InputTokens = 1_200_000,
        CostUsd = cost,
    };

    [Fact]
    public async Task Refresh_LoadsRunRowsAndCursor()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.RunsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunsResponse { Runs = [Run("s1"), Run("s2", 4.62)], NextCursor = "cur" });

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.Runs.Should().HaveCount(2);
        fx.Sut.Runs[0].Session.Should().Be("s1");
        fx.Sut.Runs[1].Cost.Should().Be("$4.62");
        fx.Sut.Runs[0].Tokens.Should().Be("1.2M");
        fx.Sut.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_WhenSourceFilterSet_PassesSourceToApi()
    {
        var fx = new Fixture();
        string? seenSource = null;
        fx.Api.Setup(a => a.RunsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset? from, DateTimeOffset? to, string? model, string? persona, double? minCost, string? cursor, int? limit, string? source, CancellationToken ct) =>
            {
                seenSource = source;
                return Task.FromResult(new RunsResponse { Runs = [], NextCursor = null });
            });
        fx.Sut.SourceFilter = "tokenburn-self";

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        seenSource.Should().Be("tokenburn-self");
    }

    [Fact]
    public async Task Refresh_WhenApiThrows_SetsErrorMessage()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.RunsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public async Task Refresh_WhenCancelled_LeavesRowsUntouched()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.RunsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset? from, DateTimeOffset? to, string? model, string? persona, double? minCost, string? cursor, int? limit, string? source, CancellationToken ct) =>
            {
                var tcs = new TaskCompletionSource<RunsResponse>();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            });

        var executing = fx.Sut.RefreshCommand.ExecuteAsync(null);
        fx.Sut.RefreshCommand.Cancel();
        await executing;

        fx.Sut.Runs.Should().BeEmpty();
        fx.Sut.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadMore_AppendsRowsAndAdvancesCursor()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.RunsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset? from, DateTimeOffset? to, string? model, string? persona, double? minCost, string? cursor, int? limit, string? source, CancellationToken ct) =>
                cursor is null
                    ? Task.FromResult(new RunsResponse { Runs = [Run("s1")], NextCursor = "cur" })
                    : Task.FromResult(new RunsResponse { Runs = [Run("s2")], NextCursor = null }));
        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        await fx.Sut.LoadMoreCommand.ExecuteAsync(null);

        fx.Sut.Runs.Should().HaveCount(2);
        fx.Sut.HasMore.Should().BeFalse();
    }

    [Fact]
    public void LoadMore_WithoutCursor_IsNotExecutable()
    {
        var fx = new Fixture();
        fx.Sut.HasMore.Should().BeFalse();

        fx.Sut.LoadMoreCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Activate_StartsLoop()
    {
        var fx = new Fixture();

        fx.Sut.Activate();

        fx.Loop.StartCount.Should().Be(1);
    }

    [Fact]
    public void Deactivate_StopsLoop()
    {
        var fx = new Fixture();
        fx.Sut.Activate();

        fx.Sut.Deactivate();

        fx.Loop.StopCount.Should().Be(1);
    }
}
