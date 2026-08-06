using TokenBurn.Desktop.Core.Features.Findings;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Tests.Features;

public sealed class FindingsViewModelTests
{
    private sealed class Fixture
    {
        public FakeDispatcher Dispatcher { get; } = new();
        public FakeRefreshLoop Loop { get; } = new();
        public Mock<IInsightsApiClient> Api { get; } = new();
        public FindingsViewModel Sut { get; }

        public Fixture()
        {
            Api.Setup(a => a.FindingsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FindingsResponse { Findings = [], NextCursor = null });
            Sut = new FindingsViewModel(Dispatcher, Api.Object, Loop);
        }
    }

    private static FindingsResponse Findings() => new()
    {
        Findings =
        [
            new FindingSummary
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Kind = "context_replay",
                Severity = "medium",
                WastedCostUsd = 0.4,
                DetectedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                AcknowledgedAt = null,
            },
        ],
        NextCursor = "cur",
    };

    [Fact]
    public async Task Refresh_LoadsFindingRows()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.FindingsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Findings());

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.Findings.Should().HaveCount(1);
        fx.Sut.Findings[0].Kind.Should().Be("context_replay");
        fx.Sut.Findings[0].WastedCost.Should().Be("$0.40");
        fx.Sut.Findings[0].AcknowledgedAt.Should().Be("—");
        fx.Sut.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_PassesFilters()
    {
        var fx = new Fixture();
        string? seenKind = null;
        string? seenSeverity = null;
        fx.Sut.KindFilter = "context_replay";
        fx.Sut.SeverityFilter = "high";
        fx.Sut.AcknowledgedFilter = false;
        fx.Api.Setup(a => a.FindingsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((string? kind, string? severity, bool? acknowledged, string? cursor, int? limit, CancellationToken ct) =>
            {
                seenKind = kind;
                seenSeverity = severity;
                return Task.FromResult(new FindingsResponse { Findings = [], NextCursor = null });
            });

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        seenKind.Should().Be("context_replay");
        seenSeverity.Should().Be("high");
    }

    [Fact]
    public async Task Refresh_WhenApiThrows_SetsErrorMessage()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.FindingsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public async Task Refresh_WhenCancelled_LeavesFindingsUntouched()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.FindingsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((string? kind, string? severity, bool? acknowledged, string? cursor, int? limit, CancellationToken ct) =>
            {
                var tcs = new TaskCompletionSource<FindingsResponse>();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            });

        var executing = fx.Sut.RefreshCommand.ExecuteAsync(null);
        fx.Sut.RefreshCommand.Cancel();
        await executing;

        fx.Sut.Findings.Should().BeEmpty();
        fx.Sut.ErrorMessage.Should().BeEmpty();
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
