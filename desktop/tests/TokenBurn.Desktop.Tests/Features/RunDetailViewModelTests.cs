using TokenBurn.Desktop.Core.Features.RunDetail;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Tests.Features;

public sealed class RunDetailViewModelTests
{
    private sealed class Fixture
    {
        public FakeDispatcher Dispatcher { get; } = new();
        public Mock<IInsightsApiClient> Api { get; } = new();
        public RunDetailViewModel Sut { get; }

        public Fixture()
        {
            Api.Setup(a => a.RunsDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunDetailResponse { Run = EmptyRun(), Messages = [], Findings = [] });
            Sut = new RunDetailViewModel(Dispatcher, Api.Object);
        }
    }

    private static RunSummary EmptyRun() => new()
    {
        Id = Guid.NewGuid(),
        SessionId = "s1",
        Source = "claude",
        Status = "completed",
        PricingStatus = "priced",
    };

    private static RunDetailResponse Detail(Guid id) => new()
    {
        Run = new RunSummary
        {
            Id = id,
            SessionId = "s1",
            Source = "claude",
            Status = "completed",
            PricingStatus = "priced",
        },
        Messages = [],
        Findings =
        [
            new FindingSummary
            {
                Id = Guid.NewGuid(),
                RunId = id,
                Kind = "context_replay",
                Severity = "medium",
                WastedCostUsd = 0.4,
                DetectedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
        ],
    };

    [Fact]
    public async Task Open_LoadsRunAndFindings()
    {
        var fx = new Fixture();
        var id = Guid.NewGuid();
        fx.Api.Setup(a => a.RunsDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail(id));

        await fx.Sut.OpenCommand.ExecuteAsync(id);

        fx.Sut.SelectedRunId.Should().Be(id);
        fx.Sut.Run.Should().NotBeNull();
        fx.Sut.Run!.Id.Should().Be(id);
        fx.Sut.Findings.Should().HaveCount(1);
        fx.Sut.Findings[0].Kind.Should().Be("context_replay");
        fx.Sut.IsLoading.Should().BeFalse();
        fx.Sut.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_WhenApiThrows_SetsErrorMessage()
    {
        var fx = new Fixture();
        var id = Guid.NewGuid();
        fx.Api.Setup(a => a.RunsDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        await fx.Sut.OpenCommand.ExecuteAsync(id);

        fx.Sut.ErrorMessage.Should().Be("boom");
        fx.Sut.Run.Should().BeNull();
    }

    [Fact]
    public async Task Open_WhenCancelled_LeavesRunUntouched()
    {
        var fx = new Fixture();
        var id = Guid.NewGuid();
        fx.Api.Setup(a => a.RunsDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, CancellationToken ct) =>
            {
                var tcs = new TaskCompletionSource<RunDetailResponse>();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            });

        var executing = fx.Sut.OpenCommand.ExecuteAsync(id);
        fx.Sut.OpenCommand.Cancel();
        await executing;

        fx.Sut.Run.Should().BeNull();
        fx.Sut.Findings.Should().BeEmpty();
        fx.Sut.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public void MessageFeed_IsEmptyByDesign()
    {
        var fx = new Fixture();

        fx.Sut.HasMessages.Should().BeFalse();
        fx.Sut.MessagesEmptyText.Should().Be("no messages — the transcript feed arrives in a later phase");
    }
}
